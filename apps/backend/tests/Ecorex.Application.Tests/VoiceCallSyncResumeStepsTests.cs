using System.Reflection;
using Ecorex.Application.Common;
using Ecorex.Application.Voice;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Rescate de pasos de FLUJO en espera de una llamada (VoiceCallSyncService.ResumeStalledFlowStepsAsync, ADR-0190,
/// acordado con la sesion de flujos): si la llamada ya termino (Analyzed, o terminal sin contacto) y el webhook no
/// reanudo el paso, se limpia AgentAttemptedAt (solo si el ultimo intento tiene &gt;= 5 min) y se conserva
/// PendingVoiceCallId. EF InMemory; Retell y el procesador no se usan (proxies que fallan si se llaman).
/// </summary>
public class VoiceCallSyncResumeStepsTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);

    private sealed class InnerDb(DbContextOptions<InnerDb> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<VoiceCall>();
            b.Entity<RetellVoiceLine>();
            b.Entity<WorkflowStepHistory>(e => { e.Ignore(x => x.Instance); e.Ignore(x => x.Node); });
        }
    }

    public class Unused : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => throw new NotSupportedException(targetMethod?.Name);
    }

    private sealed class PlainProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string ciphertext) => ciphertext;
    }

    private static (InnerDb Inner, VoiceCallSyncService Sync) Build()
    {
        var inner = new InnerDb(new DbContextOptionsBuilder<InnerDb>()
            .UseInMemoryDatabase("voice-resume-" + Guid.NewGuid()).Options);
        var db = DispatchProxy.Create<IApplicationDbContext, RetellWebhookIdempotencyTests.DbProxy>();
        ((RetellWebhookIdempotencyTests.DbProxy)(object)db).Inner = inner;
        var sync = new VoiceCallSyncService(db, new PlainProtector(),
            DispatchProxy.Create<IRetellApiClient, Unused>(),
            DispatchProxy.Create<IRetellWebhookProcessor, Unused>(),
            NullLogger<VoiceCallSyncService>.Instance);
        return (inner, sync);
    }

    private static VoiceCall Call(string callId, VoiceCallStatus status, string? reason = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Tenant,
        CallId = callId,
        FromNumber = "+5710000000",
        ToNumber = "+573000000000",
        Status = status,
        DisconnectionReason = reason,
    };

    private static WorkflowStepHistory Step(string? pendingCallId, DateTimeOffset? attemptedAt) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Tenant,
        InstanceId = Guid.NewGuid(),
        NodeId = Guid.NewGuid(),
        IsCurrent = true,
        PendingVoiceCallId = pendingCallId,
        AgentAttemptedAt = attemptedAt,
    };

    private static async Task<WorkflowStepHistory> Reload(InnerDb inner, Guid id)
        => await inner.Set<WorkflowStepHistory>().AsNoTracking().SingleAsync(s => s.Id == id);

    [Fact]
    public async Task Analyzed_sin_webhook_y_paso_con_intento_viejo_se_reanuda()
    {
        var (inner, sync) = Build();
        var step = Step("call_1", Now.AddMinutes(-10));
        inner.AddRange(Call("call_1", VoiceCallStatus.Analyzed), step);
        await inner.SaveChangesAsync();

        var resumed = await sync.ResumeStalledFlowStepsAsync(Now);

        Assert.Equal(1, resumed);
        var r = await Reload(inner, step.Id);
        Assert.Null(r.AgentAttemptedAt);
        Assert.Equal("call_1", r.PendingVoiceCallId);
    }

    [Fact]
    public async Task Analyzed_con_intento_reciente_no_se_toca()
    {
        var (inner, sync) = Build();
        var attempted = Now.AddMinutes(-2);
        var step = Step("call_1", attempted);
        inner.AddRange(Call("call_1", VoiceCallStatus.Analyzed), step);
        await inner.SaveChangesAsync();

        Assert.Equal(0, await sync.ResumeStalledFlowStepsAsync(Now));
        Assert.Equal(attempted, (await Reload(inner, step.Id)).AgentAttemptedAt);
    }

    [Fact]
    public async Task Paso_sin_llamada_pendiente_no_se_toca()
    {
        var (inner, sync) = Build();
        var attempted = Now.AddMinutes(-30);
        var step = Step(null, attempted);
        inner.AddRange(Call("call_1", VoiceCallStatus.Analyzed), step);
        await inner.SaveChangesAsync();

        Assert.Equal(0, await sync.ResumeStalledFlowStepsAsync(Now));
        Assert.Equal(attempted, (await Reload(inner, step.Id)).AgentAttemptedAt);
    }

    [Fact]
    public async Task Ended_sin_contacto_se_reanuda_y_ended_con_contacto_espera_el_analisis()
    {
        var (inner, sync) = Build();
        var noAnswer = Step("call_na", Now.AddMinutes(-10));
        var talked = Step("call_ok", Now.AddMinutes(-10));
        inner.AddRange(
            Call("call_na", VoiceCallStatus.Ended, "dial_no_answer"),
            Call("call_ok", VoiceCallStatus.Ended, "user_hangup"),
            noAnswer, talked);
        await inner.SaveChangesAsync();

        Assert.Equal(1, await sync.ResumeStalledFlowStepsAsync(Now));
        Assert.Null((await Reload(inner, noAnswer.Id)).AgentAttemptedAt);
        Assert.NotNull((await Reload(inner, talked.Id)).AgentAttemptedAt);
    }

    [Fact]
    public async Task Llamada_en_curso_no_reanuda_y_el_tenant_igual_se_descubre()
    {
        var (inner, sync) = Build();
        var step = Step("call_live", Now.AddMinutes(-10));
        var live = Call("call_live", VoiceCallStatus.Ongoing);
        live.CreatedAt = Now.AddMinutes(-10);
        inner.AddRange(live, step);
        await inner.SaveChangesAsync();

        Assert.Equal(0, await sync.ResumeStalledFlowStepsAsync(Now));
        Assert.Contains(Tenant, await sync.FindTenantsWithPendingCallsAsync(Now));
    }
}
