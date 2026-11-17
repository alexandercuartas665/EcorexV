using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Application.Forms.Builder;
using Ecorex.Application.Gestor;
using Ecorex.Application.Gestor.TemplateAgent;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Agente editor de plantillas de correo (ADR-0174): verificador determinista (dorados), merge de las variables
/// nuevas (incluye escape HTML y firma) y GATE humano del nucleo (una mutacion se PROPONE y no corre hasta
/// Confirmar; una irreversible exige la confirmacion explicita).
/// </summary>
public class EmailTemplateAgentTests
{
    private const string GoodBody =
        "<table role=\"presentation\" width=\"600\"><tr><td style=\"font-family:Arial\">" +
        "<p>Hola {nombre},</p><p><a href=\"https://acme.test/catalogo\">Ver catalogo</a></p>" +
        "<img src=\"https://acme.test/logo.png\" alt=\"ACME\" width=\"120\" />{firma}" +
        "<p><a href=\"{enlace_baja}\">Dejar de recibir estos correos</a></p></td></tr></table>";

    // ---------- Verificador ----------

    [Fact]
    public void Verificador_plantilla_correcta_no_tiene_errores_ni_avisos()
    {
        var r = EmailTemplateVerifier.Verify("Hola {nombre}, una propuesta para {empresa}", GoodBody);
        Assert.Equal(0, r.Errors);
        Assert.Equal(0, r.Warnings);
    }

    [Fact]
    public void Verificador_variable_desconocida_es_error_en_asunto_y_cuerpo()
    {
        var r = EmailTemplateVerifier.Verify("Hola {cliente}", GoodBody.Replace("{nombre}", "{nombre_completo}"));
        Assert.Equal(2, r.Issues.Count(i => i.Severity == "error" && i.Problem.Contains("no existe")));
    }

    [Fact]
    public void Verificador_sin_enlace_de_baja_es_error()
    {
        var r = EmailTemplateVerifier.Verify("Hola", GoodBody.Replace("{enlace_baja}", "https://acme.test/baja"));
        Assert.Contains(r.Issues, i => i.Severity == "error" && i.Problem.Contains("enlace_baja"));
    }

    [Fact]
    public void Verificador_script_eventos_y_enlaces_vacios_son_error()
    {
        var body = GoodBody + "<script>alert(1)</script><a href=\"#\">x</a><a href=\"javascript:void(0)\">y</a><div onclick=\"x()\">z</div>";
        var r = EmailTemplateVerifier.Verify("Hola", body);
        Assert.Contains(r.Issues, i => i.Severity == "error" && i.Problem.Contains("script"));
        Assert.Contains(r.Issues, i => i.Severity == "error" && i.Problem.Contains("atributos de evento"));
        Assert.Contains(r.Issues, i => i.Severity == "error" && i.Problem.Contains("vacio"));
        Assert.Contains(r.Issues, i => i.Severity == "error" && i.Problem.Contains("javascript:"));
    }

    [Fact]
    public void Verificador_avisa_img_sin_alt_http_asunto_largo_y_peso()
    {
        var heavy = GoodBody + "<img src=\"https://acme.test/a.png\"><a href=\"http://acme.test\">x</a>" + new string(' ', EmailTemplateVerifier.GmailClipBytes);
        var r = EmailTemplateVerifier.Verify(new string('a', 90), heavy);
        Assert.Equal(0, r.Errors);
        Assert.Contains(r.Issues, i => i.Where == "asunto" && i.Severity == "warning");
        Assert.Contains(r.Issues, i => i.Problem.Contains("alt"));
        Assert.Contains(r.Issues, i => i.Problem.Contains("http://"));
        Assert.Contains(r.Issues, i => i.Problem.Contains("Gmail"));
    }

    [Fact]
    public void Verificador_cuerpo_y_asunto_vacios_son_error()
    {
        var r = EmailTemplateVerifier.Verify("", "");
        Assert.Equal(2, r.Errors);
    }

    // ---------- Merge ----------

    [Fact]
    public void Merge_variables_nuevas_escapa_valores_y_firma_va_tal_cual_en_html()
    {
        var f = new EmailMergeFields("Ana <b>", "ACME", null, null, "ana@x.test",
            Vendedor: "Maria & Co", Telefono: "300", Sector: "Agro", Firma: "<b>Maria</b>", EnlaceBaja: "https://app/baja/t1");
        var html = EmailTemplateService.RenderTemplate("{nombre}|{vendedor}|{telefono}|{sector}|{firma}|{enlace_baja}|{cargo}", f, htmlEscapeValues: true);
        Assert.Equal("Ana &lt;b&gt;|Maria &amp; Co|300|Agro|<b>Maria</b>|https://app/baja/t1|", html);
    }

    [Fact]
    public void Merge_en_texto_plano_la_firma_va_sin_etiquetas()
    {
        var f = new EmailMergeFields("Ana", null, null, null, null, Firma: "<p>Maria<br/>Asesora</p>");
        var plain = EmailTemplateService.RenderTemplate("Saludos {firma}", f, htmlEscapeValues: false);
        Assert.DoesNotContain("<", plain);
        Assert.Contains("Maria", plain);
        Assert.Contains("Asesora", plain);
    }

    [Fact]
    public void Preheader_se_inserta_una_sola_vez_tras_body()
    {
        var once = EmailTemplateAgentToolset.WithPreheader("<html><body><p>x</p></body></html>", "Oferta");
        Assert.Contains("<body><div data-preheader=\"1\"", once);
        var twice = EmailTemplateAgentToolset.WithPreheader(once, "Otra");
        Assert.Equal(once, twice);
    }

    // ---------- Gate humano del nucleo ----------

    [Fact]
    public async Task Gate_la_mutacion_se_propone_y_solo_corre_al_confirmar()
    {
        var store = new MemStore();
        var toolset = new CountingToolset();
        var ai = new ScriptedAi(
            new AiCompletion(true, "Guardo la plantilla.", null, 10, 5, new[] { new AiToolCall("c1", "save_template", "{\"asunto\":\"Hola\",\"cuerpo_html\":\"<p>x</p>\"}") }),
            new AiCompletion(true, "Listo, guardada.", null, 10, 5, Array.Empty<AiToolCall>()));
        var svc = new TemplateAgentChatService(new PlainSecrets(), ai, store, new NoSnapshots(), new FreeUsage(), new[] { toolset });

        var start = await svc.StartAsync(TemplateAgentKind.Email, null, Guid.NewGuid());
        Assert.True(start.Ok);

        var turn = await svc.SendAsync(start.ConversationId, "Crea un correo de bienvenida", null, Guid.NewGuid());
        Assert.True(turn.Ok);
        Assert.True(turn.AwaitingConfirmation);
        Assert.Single(turn.Proposals);
        Assert.Equal(0, toolset.Executions); // NO se ejecuto: espera confirmacion

        var after = await svc.ConfirmAsync(start.ConversationId, Guid.NewGuid());
        Assert.True(after.Ok);
        Assert.Equal(1, toolset.Executions);
        Assert.True(after.TemplateChanged);
        Assert.Equal(CountingToolset.CreatedId, after.TemplateId);
    }

    [Fact]
    public async Task Gate_la_irreversible_no_corre_sin_confirmacion_explicita()
    {
        var store = new MemStore();
        var toolset = new CountingToolset();
        var ai = new ScriptedAi(
            new AiCompletion(true, "Someto a Meta.", null, 1, 1, new[] { new AiToolCall("c1", "submit_to_meta", "{}") }),
            new AiCompletion(true, "Sometida.", null, 1, 1, Array.Empty<AiToolCall>()));
        var svc = new TemplateAgentChatService(new PlainSecrets(), ai, store, new NoSnapshots(), new FreeUsage(), new[] { toolset });
        var start = await svc.StartAsync(TemplateAgentKind.Email, null, Guid.NewGuid());
        var turn = await svc.SendAsync(start.ConversationId, "Somete", null, Guid.NewGuid());
        Assert.True(Assert.Single(turn.Proposals).Irreversible);

        var plain = await svc.ConfirmAsync(start.ConversationId, Guid.NewGuid(), confirmIrreversible: false);
        Assert.False(plain.Ok);
        Assert.Equal(0, toolset.Executions);

        var explicitOk = await svc.ConfirmAsync(start.ConversationId, Guid.NewGuid(), confirmIrreversible: true);
        Assert.True(explicitOk.Ok);
        Assert.Equal(1, toolset.Executions);
    }

    // ---------- Dobles ----------

    private sealed class CountingToolset : ITemplateAgentToolset
    {
        public static readonly Guid CreatedId = Guid.NewGuid();
        public int Executions { get; private set; }
        public TemplateAgentKind Kind => TemplateAgentKind.Email;
        public IReadOnlyList<AiToolSpec> GetSpecs() => Array.Empty<AiToolSpec>();
        public IReadOnlySet<string> ReadOnlyTools { get; } = new HashSet<string> { "get_template" };
        public IReadOnlySet<string> IrreversibleTools { get; } = new HashSet<string> { "submit_to_meta" };
        public IReadOnlySet<string> TemplateMutatingTools { get; } = new HashSet<string> { "save_template" };
        public Task<string> ExecuteAsync(TemplateAgentToolContext context, string toolName, string argumentsJson, CancellationToken cancellationToken = default)
        {
            Executions++;
            context.TemplateId ??= CreatedId;
            context.TemplateChanged = true;
            return Task.FromResult("{\"ok\":true}");
        }
        public Task<TemplateVerifyResult?> VerifyAsync(Guid templateId, CancellationToken cancellationToken = default)
            => Task.FromResult<TemplateVerifyResult?>(new TemplateVerifyResult(0, 0, Array.Empty<TemplateIssue>()));
        public Task<string?> SnapshotPayloadAsync(Guid templateId, CancellationToken cancellationToken = default) => Task.FromResult<string?>("{}");
        public Task<bool> RestoreAsync(Guid templateId, string payloadJson, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<string?> GetTitleAsync(Guid templateId, CancellationToken cancellationToken = default) => Task.FromResult<string?>("Plantilla");
        public Task<string> SystemPromptAsync(string tenantName, Guid? templateId, CancellationToken cancellationToken = default) => Task.FromResult("prompt");
    }

    private sealed class MemStore : ITemplateAgentStore
    {
        private readonly Dictionary<Guid, TemplateAgentConversation> _convs = new();
        private readonly List<TemplateAgentMessage> _msgs = new();

        public Task<TemplateAgentConversation> CreateConversationAsync(TemplateAgentKind kind, Guid? templateId, AiProvider provider, string? model, string title, Guid? startedByUserId, CancellationToken cancellationToken = default)
        {
            var c = new TemplateAgentConversation { Kind = kind, TemplateId = templateId, Provider = provider, Model = model, Title = title };
            _convs[c.Id] = c;
            return Task.FromResult(c);
        }
        public Task<TemplateAgentConversation?> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken = default)
            => Task.FromResult(_convs.TryGetValue(conversationId, out var c) ? c : null);
        public Task<TemplateAgentConversation?> GetLatestConversationAsync(TemplateAgentKind kind, Guid templateId, CancellationToken cancellationToken = default)
            => Task.FromResult(_convs.Values.FirstOrDefault(c => c.Kind == kind && c.TemplateId == templateId));
        public Task SaveConversationAsync(TemplateAgentConversation conversation, CancellationToken cancellationToken = default) { _convs[conversation.Id] = conversation; return Task.CompletedTask; }
        public Task<IReadOnlyList<TemplateAgentMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TemplateAgentMessage>>(_msgs.Where(m => m.ConversationId == conversationId).OrderBy(m => m.Sequence).ToList());
        public Task<TemplateAgentMessage> AddMessageAsync(TemplateAgentMessage message, CancellationToken cancellationToken = default) { _msgs.Add(message); return Task.FromResult(message); }
        public Task SaveMessageAsync(TemplateAgentMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<FormBuilderProviderInfo?> ResolveProviderAsync(AiProvider provider, CancellationToken cancellationToken = default)
            => Task.FromResult<FormBuilderProviderInfo?>(new FormBuilderProviderInfo(true, "key", null, null));
        public Task<AiProvider?> GetTemplateBuilderProviderAsync(CancellationToken cancellationToken = default) => Task.FromResult<AiProvider?>(AiProvider.Gemini);
        public Task<string> GetTenantNameAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult("Demo");
    }

    private sealed class ScriptedAi(params AiCompletion[] script) : IAiProviderClient
    {
        private int _i;
        public Task<AiCompletion> CompleteWithToolsAsync(AiProvider provider, string apiKey, string? baseUrl, string model, string systemPrompt,
            IReadOnlyList<AiToolMessage> messages, IReadOnlyList<AiToolSpec> tools, CancellationToken cancellationToken = default)
            => Task.FromResult(script[Math.Min(_i++, script.Length - 1)]);
        public Task<AiChatResult> CompleteAsync(AiProvider provider, string apiKey, string? baseUrl, string model, string systemPrompt, IReadOnlyList<AiChatTurn> turns, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AiChatResult> CompleteVisionAsync(AiProvider provider, string apiKey, string? baseUrl, string model, string systemPrompt, IReadOnlyList<AiVisionPart> content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class PlainSecrets : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string ciphertext) => ciphertext;
    }

    private sealed class NoSnapshots : ITemplateSnapshotService
    {
        public Task SnapshotAsync(TemplateAgentKind kind, Guid templateId, string label, Guid? conversationId, Guid? actorUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<TemplateSnapshotDto>> ListAsync(TemplateAgentKind kind, Guid templateId, int take = 30, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TemplateSnapshotDto>>(Array.Empty<TemplateSnapshotDto>());
        public Task<bool> RestoreAsync(Guid snapshotId, Guid? actorUserId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class FreeUsage : IAiUsageService
    {
        public Task RecordAsync(Guid? agentId, AiProvider provider, string model, int inputTokens, int outputTokens, string source, bool success, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<AiUsageSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AiQuotaDto> GetQuotaAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AiQuotaDto(0, 0, false));
    }
}
