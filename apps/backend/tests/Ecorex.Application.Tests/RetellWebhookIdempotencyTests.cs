using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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
/// Orden e idempotencia del procesador del webhook de Retell (ADR-0190), acordado con la sesion de flujos:
/// - Si el sondeo de la UI ya dejo la llamada en Analyzed, el call_analyzed real IGUAL reanuda el paso de flujo
///   en espera (limpia AgentAttemptedAt del paso con PendingVoiceCallId).
/// - Un call_ended tardio no hace retroceder una llamada ya analizada.
/// Corre sobre EF InMemory; el IApplicationDbContext es un proxy que solo expone los DbSet del modelo de prueba.
/// </summary>
public class RetellWebhookIdempotencyTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private const string ApiKey = "key-de-prueba";
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);

    private sealed class InnerDb(DbContextOptions<InnerDb> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<VoiceCall>();
            b.Entity<RetellVoiceLine>();
            b.Entity<ContactWorkflowRun>();
            b.Entity<WorkflowStepHistory>(e => { e.Ignore(x => x.Instance); e.Ignore(x => x.Node); });
        }
    }

    /// <summary>Proxy de IApplicationDbContext: get_X de tipo DbSet&lt;T&gt; -> inner.Set&lt;T&gt;(); SaveChangesAsync -> inner.</summary>
    public class DbProxy : DispatchProxy
    {
        internal DbContext Inner = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            if (targetMethod.Name.StartsWith("get_", StringComparison.Ordinal)
                && targetMethod.ReturnType.IsGenericType
                && targetMethod.ReturnType.GetGenericTypeDefinition() == typeof(DbSet<>))
            {
                var entity = targetMethod.ReturnType.GetGenericArguments()[0];
                var set = typeof(DbContext).GetMethods()
                    .First(m => m.Name == nameof(DbContext.Set) && m.IsGenericMethod && m.GetParameters().Length == 0)
                    .MakeGenericMethod(entity);
                return set.Invoke(Inner, null);
            }
            if (targetMethod.Name == nameof(IApplicationDbContext.SaveChangesAsync))
            {
                return Inner.SaveChangesAsync(args?.Length > 0 && args[0] is CancellationToken ct ? ct : default);
            }
            throw new NotSupportedException(targetMethod.Name);
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

    /// <summary>Forms no se usa (objetivo distinto de LlenarFormulario): cualquier llamada falla.</summary>
    public class NoFormsProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => throw new NotSupportedException(targetMethod?.Name);
    }

    private static (InnerDb Inner, RetellWebhookProcessor Processor, VoiceCall Call) Build(VoiceCallStatus status)
    {
        var inner = new InnerDb(new DbContextOptionsBuilder<InnerDb>()
            .UseInMemoryDatabase("retell-idem-" + Guid.NewGuid()).Options);
        var line = new RetellVoiceLine { Id = Guid.NewGuid(), TenantId = Tenant, Name = "Linea", RetellApiKeyEncrypted = ApiKey };
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            CallId = "call_abc",
            FromNumber = "+5710000000",
            ToNumber = "+573000000000",
            RetellVoiceLineId = line.Id,
            Status = status,
        };
        inner.AddRange(line, call);
        inner.SaveChanges();

        var db = DispatchProxy.Create<IApplicationDbContext, DbProxy>();
        ((DbProxy)(object)db).Inner = inner;
        var forms = DispatchProxy.Create<IFormResponseService, NoFormsProxy>();
        var processor = new RetellWebhookProcessor(db, new PlainProtector(), forms, new FixedClock(),
            NullLogger<RetellWebhookProcessor>.Instance);
        return (inner, processor, call);
    }

    private static (string Body, string Signature) Signed(string eventType, string callJson)
    {
        var body = "{\"event\":\"" + eventType + "\",\"call\":" + callJson + "}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(ApiKey));
        var hex = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        return (body, "v=" + Now.ToUnixTimeMilliseconds() + ",d=" + hex);
    }

    [Fact]
    public async Task Analyzed_por_sondeo_y_luego_webhook_analyzed_igual_reanuda_el_paso()
    {
        var (inner, processor, call) = Build(VoiceCallStatus.Analyzed);
        var step = new WorkflowStepHistory
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            InstanceId = Guid.NewGuid(),
            NodeId = Guid.NewGuid(),
            IsCurrent = true,
            PendingVoiceCallId = call.CallId,
            AgentAttemptedAt = Now.AddMinutes(-3),
        };
        inner.Add(step);
        await inner.SaveChangesAsync();

        var (body, sig) = Signed("call_analyzed",
            "{\"call_id\":\"call_abc\",\"disconnection_reason\":\"user_hangup\",\"call_analysis\":{\"call_summary\":\"ok\"}}");
        var outcome = await processor.ProcessAsync(body, sig);

        Assert.Equal(RetellWebhookOutcome.Ok, outcome);
        var reloaded = await inner.Set<WorkflowStepHistory>().AsNoTracking().SingleAsync(s => s.Id == step.Id);
        Assert.Null(reloaded.AgentAttemptedAt);
        Assert.Equal(call.CallId, reloaded.PendingVoiceCallId);
    }

    [Fact]
    public async Task Call_ended_tardio_no_hace_retroceder_una_llamada_analizada()
    {
        var (inner, processor, call) = Build(VoiceCallStatus.Analyzed);

        var (body, sig) = Signed("call_ended",
            "{\"call_id\":\"call_abc\",\"disconnection_reason\":\"dial_no_answer\"}");
        var outcome = await processor.ProcessAsync(body, sig);

        Assert.Equal(RetellWebhookOutcome.Ok, outcome);
        var reloaded = await inner.Set<VoiceCall>().AsNoTracking().SingleAsync(v => v.Id == call.Id);
        Assert.Equal(VoiceCallStatus.Analyzed, reloaded.Status);
        Assert.Null(reloaded.DisconnectionReason);
    }

    [Fact]
    public async Task Eventos_en_orden_avanzan_el_estado()
    {
        var (inner, processor, call) = Build(VoiceCallStatus.Registered);

        var (b1, s1) = Signed("call_started", "{\"call_id\":\"call_abc\"}");
        await processor.ProcessAsync(b1, s1);
        var (b2, s2) = Signed("call_ended", "{\"call_id\":\"call_abc\",\"disconnection_reason\":\"dial_no_answer\"}");
        await processor.ProcessAsync(b2, s2);

        var reloaded = await inner.Set<VoiceCall>().AsNoTracking().SingleAsync(v => v.Id == call.Id);
        Assert.Equal(VoiceCallStatus.Ended, reloaded.Status);
        Assert.Equal("dial_no_answer", reloaded.DisconnectionReason);
    }
}
