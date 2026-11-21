using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Forms;
using Ecorex.Application.Voice;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Volcado de respuestas de una llamada (RetellWebhookProcessor, ADR-0190 fase B), acordado con la sesion de flujos:
/// - Llamada del MOTOR DE CONTACTOS (ContactWorkflowRunId): va a "TERCERO:{id}", traduce opciones y MEZCLA con lo previo.
/// - Llamada de FLUJO (sin run): sigue escribiendo en "voz:{callId}" con el comportamiento de siempre.
/// - Run inexistente: no se escribe nada. Whitelist dura: solo los formularios permitidos de la llamada.
/// </summary>
public class RetellContactFormDumpTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private const string ApiKey = "key-de-prueba";
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid FormId = Guid.NewGuid();
    private static readonly Guid OtherFormId = Guid.NewGuid();

    private sealed class InnerDb(DbContextOptions<InnerDb> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<VoiceCall>();
            b.Entity<RetellVoiceLine>();
            b.Entity<ContactWorkflowRun>();
            b.Entity<WorkflowStepHistory>(e => { e.Ignore(x => x.Instance); e.Ignore(x => x.Node); });
            b.Entity<FormQuestion>(e => { e.Ignore(x => x.Definition); e.Ignore(x => x.Container); });
            b.Entity<Tercero>(e => { e.Ignore(x => x.Contactos); e.Ignore(x => x.Categorias); });
        }
    }

    /// <summary>Doble de IFormResponseService: un borrador por (formulario, referencia); SaveAsync REEMPLAZA como el real.</summary>
    public class FormsProxy : DispatchProxy
    {
        internal readonly Dictionary<(Guid Def, string Ref), (Guid Id, Dictionary<string, FormFieldValue> Data)> Drafts = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(IFormResponseService.GetOrCreateDraftAsync):
                    {
                        var key = ((Guid)args![0]!, (string)args[1]!);
                        if (!Drafts.TryGetValue(key, out var d))
                        {
                            d = (Guid.NewGuid(), new Dictionary<string, FormFieldValue>());
                            Drafts[key] = d;
                        }
                        return Task.FromResult(FormResult<FormResponseDto>.Ok(new FormResponseDto(
                            d.Id, key.Item1, key.Item2, FormResponseStatus.Draft, d.Data, null, null, 1)));
                    }
                case nameof(IFormResponseService.SaveAsync):
                    {
                        var id = (Guid)args![0]!;
                        var data = (IReadOnlyDictionary<string, FormFieldValue>)args[1]!;
                        var key = Drafts.First(kv => kv.Value.Id == id).Key;
                        Drafts[key] = (id, new Dictionary<string, FormFieldValue>(data));
                        return Task.FromResult(FormResult<FormResponseDto>.Ok(new FormResponseDto(
                            id, key.Def, key.Ref, FormResponseStatus.Draft, data, null, null, 2)));
                    }
                default:
                    throw new NotSupportedException(targetMethod?.Name);
            }
        }
    }

    private sealed class PlainProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string ciphertext) => ciphertext;
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed record Ctx(InnerDb Inner, RetellWebhookProcessor Processor, FormsProxy Forms, Guid TerceroId);

    private static Ctx Build(Guid? runId, bool createRun = true, string objetivo = "Personalizado")
    {
        var inner = new InnerDb(new DbContextOptionsBuilder<InnerDb>()
            .UseInMemoryDatabase("retell-dump-" + Guid.NewGuid()).Options);
        var line = new RetellVoiceLine { Id = Guid.NewGuid(), TenantId = Tenant, Name = "Linea", RetellApiKeyEncrypted = ApiKey };
        var tercero = new Tercero { Id = Guid.NewGuid(), TenantId = Tenant, Nombre = "ACME" };
        inner.AddRange(line, tercero,
            new VoiceCall
            {
                Id = Guid.NewGuid(),
                TenantId = Tenant,
                CallId = "call_abc",
                FromNumber = "+5710000000",
                ToNumber = "+573000000000",
                RetellVoiceLineId = line.Id,
                Status = VoiceCallStatus.Ended,
                Objetivo = objetivo,
                FormulariosPermitidosJson = JsonSerializer.Serialize(new[] { FormId }),
                ContactWorkflowRunId = runId,
            },
            new FormQuestion
            {
                Id = Guid.NewGuid(),
                TenantId = Tenant,
                DefinitionId = FormId,
                FieldCode = "canal",
                Label = "Canal preferido",
                ControlType = FormControlType.Select,
                SortOrder = 1,
                OptionsJson = "[{\"id\":\"wa\",\"label\":\"WhatsApp\"},{\"id\":\"mail\",\"label\":\"Correo\"}]"
            },
            new FormQuestion
            {
                Id = Guid.NewGuid(),
                TenantId = Tenant,
                DefinitionId = FormId,
                FieldCode = "opinion",
                Label = "Opinion",
                ControlType = FormControlType.TextArea,
                SortOrder = 2
            },
            new FormQuestion
            {
                Id = Guid.NewGuid(),
                TenantId = Tenant,
                DefinitionId = OtherFormId,
                FieldCode = "canal",
                Label = "Canal",
                ControlType = FormControlType.Text,
                SortOrder = 1
            });
        if (runId is Guid rid && createRun)
        {
            inner.Add(new ContactWorkflowRun
            {
                Id = rid,
                TenantId = Tenant,
                ContactWorkflowStepId = Guid.NewGuid(),
                ContactWorkflowScheduleId = Guid.NewGuid(),
                TerceroId = tercero.Id,
                WindowDate = new DateOnly(2026, 11, 21),
                DispatchedAtUtc = Now.AddMinutes(-5),
                Status = ContactWorkflowRunStatus.Sent,
                Channel = "voz-ia",
                ExternalRef = "call_abc",
            });
        }
        inner.SaveChanges();

        var db = DispatchProxy.Create<IApplicationDbContext, RetellWebhookIdempotencyTests.DbProxy>();
        ((RetellWebhookIdempotencyTests.DbProxy)(object)db).Inner = inner;
        var forms = DispatchProxy.Create<IFormResponseService, FormsProxy>();
        var processor = new RetellWebhookProcessor(db, new PlainProtector(), forms, new FixedClock(),
            NullLogger<RetellWebhookProcessor>.Instance);
        return new Ctx(inner, processor, (FormsProxy)(object)forms, tercero.Id);
    }

    private static async Task<RetellWebhookOutcome> SendAnalyzedAsync(RetellWebhookProcessor p, string customJson)
    {
        var body = "{\"event\":\"call_analyzed\",\"call\":{\"call_id\":\"call_abc\",\"disconnection_reason\":\"user_hangup\","
            + "\"call_analysis\":{\"call_summary\":\"ok\",\"custom_analysis_data\":" + customJson + "}}}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(ApiKey));
        var hex = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        return await p.ProcessAsync(body, "v=" + Now.ToUnixTimeMilliseconds() + ",d=" + hex);
    }

    [Fact]
    public async Task Llamada_del_motor_va_al_formulario_del_contacto_traducida_y_mezclada()
    {
        var ctx = Build(runId: Guid.NewGuid());
        var reference = "TERCERO:" + ctx.TerceroId.ToString("D");
        // Dato previo del contacto que la llamada NO trae: debe conservarse.
        var prevId = Guid.NewGuid();
        ctx.Forms.Drafts[(FormId, reference)] = (prevId, new Dictionary<string, FormFieldValue>
        {
            ["opinion"] = new("Regular (2025)", "TextArea"),
            ["canal"] = new("mail", "Select"),
        });

        var outcome = await SendAnalyzedAsync(ctx.Processor, "{\"canal\":\"whatsapp\",\"opinion\":\"\"}");

        Assert.Equal(RetellWebhookOutcome.Ok, outcome);
        var data = ctx.Forms.Drafts[(FormId, reference)].Data;
        Assert.Equal("wa", data["canal"].Value);                 // etiqueta -> id de opcion
        Assert.Equal("Regular (2025)", data["opinion"].Value);    // vacio no pisa lo previo
        Assert.DoesNotContain(ctx.Forms.Drafts.Keys, k => k.Ref.StartsWith("voz:", StringComparison.Ordinal));
        Assert.DoesNotContain(ctx.Forms.Drafts.Keys, k => k.Def == OtherFormId); // whitelist dura
    }

    [Fact]
    public async Task Opcion_que_no_casa_no_se_guarda_cruda()
    {
        var ctx = Build(runId: Guid.NewGuid());
        await SendAnalyzedAsync(ctx.Processor, "{\"canal\":\"Telegram\",\"opinion\":\"Muy bueno\"}");
        var data = ctx.Forms.Drafts[(FormId, "TERCERO:" + ctx.TerceroId.ToString("D"))].Data;
        Assert.False(data.ContainsKey("canal"));
        Assert.Equal("Muy bueno", data["opinion"].Value);
    }

    [Fact]
    public async Task Run_inexistente_no_escribe_nada()
    {
        var ctx = Build(runId: Guid.NewGuid(), createRun: false);
        await SendAnalyzedAsync(ctx.Processor, "{\"canal\":\"WhatsApp\"}");
        Assert.Empty(ctx.Forms.Drafts);
    }

    [Fact]
    public async Task Llamada_de_flujo_sigue_escribiendo_en_voz_callid_como_siempre()
    {
        var ctx = Build(runId: null, objetivo: nameof(ContactCallObjetivo.LlenarFormulario));
        await SendAnalyzedAsync(ctx.Processor, "{\"canal\":\"WhatsApp\",\"opinion\":\"Bien\"}");

        var draft = Assert.Single(ctx.Forms.Drafts);
        Assert.Equal((FormId, "voz:call_abc"), draft.Key);
        // Camino de siempre: valores crudos tal cual (sin traduccion de opciones).
        Assert.Equal("WhatsApp", draft.Value.Data["canal"].Value);
        Assert.Equal("Bien", draft.Value.Data["opinion"].Value);
    }

    [Fact]
    public async Task Llamada_de_flujo_sin_objetivo_llenar_formulario_no_vuelca()
    {
        var ctx = Build(runId: null, objetivo: "Personalizado");
        await SendAnalyzedAsync(ctx.Processor, "{\"canal\":\"WhatsApp\"}");
        Assert.Empty(ctx.Forms.Drafts);
    }
}
