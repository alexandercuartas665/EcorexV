using Ecorex.Application.Common;
using Ecorex.Application.Notifications;
using Ecorex.Application.Scheduling;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion del RUNNER del Motor de programaciones (000889, ola P2) en matriz DUAL
/// PostgreSQL / SQL Server: dispara solo las ACTIVAS vencidas, escribe la bitacora (lo que el origen
/// dejo en placeholders), avanza NextRunAt, es IDEMPOTENTE (una ventana = un disparo) y respeta el
/// aislamiento por tenant.
/// </summary>
public abstract class ScheduledJobDispatcherTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected ScheduledJobDispatcherTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Due_ActiveNotification_Fires_WritesRunOk_NotifiesAssignee_AndAdvancesNextRun()
    {
        var tenantId = await SeedTenantAsync("Disparo OK");
        var assigneeId = await SeedTenantUserAsync(tenantId, "encargado@disparo.local");
        var window = DateTimeOffset.UtcNow.AddMinutes(-5); // ventana ya vencida
        var (jobId, ruleId) = await SeedJobAsync(tenantId, ScheduledJobType.Notification,
            ScheduledJobStatus.Active, window, assigneeId);

        var notifications = new FakeNotifications();
        var fired = await RunDueAsync(tenantId, DateTimeOffset.UtcNow, notifications);
        Assert.Equal(1, fired);

        await using var ctx = _fixture.CreateContext(tenantId);
        var run = Assert.Single(await ctx.ScheduledJobRuns.Where(r => r.JobId == jobId).ToListAsync());
        Assert.Equal(ScheduledJobRunResult.Ok, run.Result);
        Assert.Equal(ruleId, run.RuleId);
        // La bitacora guarda la VENTANA disparada, no el "ahora" del worker (clave de la idempotencia).
        Assert.Equal(window.ToUnixTimeSeconds(), run.FiredAt.ToUnixTimeSeconds());

        // Se entrego la notificacion in-app al encargado.
        var sent = Assert.Single(notifications.Sent);
        Assert.Equal(assigneeId, sent.Recipient);

        // Y la regla quedo reprogramada hacia el futuro.
        var rule = await ctx.ScheduledJobRules.FirstAsync(r => r.Id == ruleId);
        Assert.NotNull(rule.NextRunAt);
        Assert.True(rule.NextRunAt > window, "NextRunAt debe avanzar mas alla de la ventana disparada.");
    }

    [Fact]
    public async Task Paused_Job_IsNeverFired()
    {
        var tenantId = await SeedTenantAsync("Pausada");
        var window = DateTimeOffset.UtcNow.AddMinutes(-5);
        var (jobId, _) = await SeedJobAsync(tenantId, ScheduledJobType.Notification,
            ScheduledJobStatus.Paused, window, assigneeId: null);

        var fired = await RunDueAsync(tenantId, DateTimeOffset.UtcNow, new FakeNotifications());
        Assert.Equal(0, fired);

        await using var ctx = _fixture.CreateContext(tenantId);
        Assert.Empty(await ctx.ScheduledJobRuns.Where(r => r.JobId == jobId).ToListAsync());

        // Tampoco aparece en el barrido de plataforma.
        var tenants = await FindDueTenantsAsync(tenantId, DateTimeOffset.UtcNow);
        Assert.DoesNotContain(tenantId, tenants);
    }

    [Fact]
    public async Task Idempotent_SecondPassDoesNotRefireTheSameWindow()
    {
        var tenantId = await SeedTenantAsync("Idempotencia");
        var window = DateTimeOffset.UtcNow.AddMinutes(-5);
        var (jobId, _) = await SeedJobAsync(tenantId, ScheduledJobType.Notification,
            ScheduledJobStatus.Active, window, assigneeId: null);

        var now = DateTimeOffset.UtcNow;
        Assert.Equal(1, await RunDueAsync(tenantId, now, new FakeNotifications()));
        // Segunda pasada inmediata: la ventana ya avanzo al futuro -> no hay nada vencido.
        Assert.Equal(0, await RunDueAsync(tenantId, now, new FakeNotifications()));

        await using var ctx = _fixture.CreateContext(tenantId);
        Assert.Single(await ctx.ScheduledJobRuns.Where(r => r.JobId == jobId).ToListAsync());
    }

    [Fact]
    public async Task ActivityType_IsSkipped_UntilP3()
    {
        var tenantId = await SeedTenantAsync("Actividad P3");
        var window = DateTimeOffset.UtcNow.AddMinutes(-5);
        var (jobId, _) = await SeedJobAsync(tenantId, ScheduledJobType.Activity,
            ScheduledJobStatus.Active, window, assigneeId: null);

        var notifications = new FakeNotifications();
        Assert.Equal(1, await RunDueAsync(tenantId, DateTimeOffset.UtcNow, notifications));

        await using var ctx = _fixture.CreateContext(tenantId);
        var run = Assert.Single(await ctx.ScheduledJobRuns.Where(r => r.JobId == jobId).ToListAsync());
        Assert.Equal(ScheduledJobRunResult.Skipped, run.Result);
        Assert.Contains("P3", run.Detail);
        Assert.Empty(notifications.Sent); // no es una notificacion
    }

    [Fact]
    public async Task UnscheduledRule_IsSelfHealed_InsteadOfStayingDeadForever()
    {
        // Una regla sin NextRunAt (p.ej. creada antes de que existiera el motor) NUNCA disparia:
        // el motor debe reprogramarla sola en su barrido.
        var tenantId = await SeedTenantAsync("Auto-reparacion");
        var (jobId, ruleId) = await SeedJobAsync(tenantId, ScheduledJobType.Notification,
            ScheduledJobStatus.Active, nextRunAt: null, assigneeId: null);

        // El barrido de plataforma DEBE visitar al tenant aunque no tenga nada "vencido".
        var tenants = await FindDueTenantsAsync(tenantId, DateTimeOffset.UtcNow);
        Assert.Contains(tenantId, tenants);

        await RunDueAsync(tenantId, DateTimeOffset.UtcNow, new FakeNotifications());

        await using var ctx = _fixture.CreateContext(tenantId);
        var rule = await ctx.ScheduledJobRules.FirstAsync(r => r.Id == ruleId);
        Assert.NotNull(rule.NextRunAt); // quedo programada hacia el futuro
        Assert.True(rule.NextRunAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task PlatformScan_FindsOnlyTenantsWithDueActiveRules()
    {
        var withDue = await SeedTenantAsync("Con vencidas");
        var withFuture = await SeedTenantAsync("Sin vencidas");

        await SeedJobAsync(withDue, ScheduledJobType.Notification, ScheduledJobStatus.Active,
            DateTimeOffset.UtcNow.AddMinutes(-5), assigneeId: null);
        await SeedJobAsync(withFuture, ScheduledJobType.Notification, ScheduledJobStatus.Active,
            DateTimeOffset.UtcNow.AddDays(3), assigneeId: null);

        var tenants = await FindDueTenantsAsync(withDue, DateTimeOffset.UtcNow);
        Assert.Contains(withDue, tenants);
        Assert.DoesNotContain(withFuture, tenants);
    }

    // ---- Helpers ----

    private async Task<int> RunDueAsync(Guid tenantId, DateTimeOffset nowUtc, FakeNotifications notifications)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        var dispatcher = new ScheduledJobDispatcher(
            ctx, new TestTenantContext(tenantId), notifications,
            NullLogger<ScheduledJobDispatcher>.Instance);
        return await dispatcher.RunDueForTenantAsync(nowUtc);
    }

    private async Task<IReadOnlyList<Guid>> FindDueTenantsAsync(Guid anyTenantId, DateTimeOffset nowUtc)
    {
        await using var ctx = _fixture.CreateContext(anyTenantId);
        var dispatcher = new ScheduledJobDispatcher(
            ctx, new TestTenantContext(anyTenantId), new FakeNotifications(),
            NullLogger<ScheduledJobDispatcher>.Instance);
        return await dispatcher.FindTenantsWithDueRulesAsync(nowUtc);
    }

    /// <summary>Siembra una programacion con UNA regla diaria cuya ventana (NextRunAt) se fija a mano.</summary>
    private async Task<(Guid JobId, Guid RuleId)> SeedJobAsync(
        Guid tenantId, ScheduledJobType type, ScheduledJobStatus status,
        DateTimeOffset? nextRunAt, Guid? assigneeId)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        var job = new ScheduledJob
        {
            TenantId = tenantId,
            Code = "PAC-" + Guid.NewGuid().ToString("N")[..6],
            Name = "Programacion " + type,
            Type = type,
            Status = status,
            AssigneeTenantUserId = assigneeId,
        };
        job.Rules.Add(new ScheduledJobRule
        {
            TenantId = tenantId,
            Frequency = ScheduledJobFrequency.Daily,
            IntervalNum = 1,
            AtTime = "08:00",
            SortOrder = 0,
            NextRunAt = nextRunAt,
        });
        job.Channels.Add(new ScheduledJobChannel { TenantId = tenantId, Channel = ScheduledJobChannelType.Email });
        ctx.ScheduledJobs.Add(job);
        await ctx.SaveChangesAsync();
        return (job.Id, job.Rules[0].Id);
    }

    private async Task<Guid> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(null);
        ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name, TimeZoneId = "America/Bogota" });
        await ctx.SaveChangesAsync();
        return tenantId;
    }

    private async Task<Guid> SeedTenantUserAsync(Guid tenantId, string email)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        var platform = new PlatformUser
        {
            Email = email,
            DisplayName = "Encargado",
            EmailVerified = true,
            Status = PlatformUserStatus.Active
        };
        ctx.PlatformUsers.Add(platform);
        await ctx.SaveChangesAsync();

        var user = new TenantUser
        {
            TenantId = tenantId,
            PlatformUserId = platform.Id,
            Email = email,
            TenantRole = TenantRole.Advisor
        };
        ctx.TenantUsers.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }

    /// <summary>Captura las entregas in-app para poder afirmarlas sin depender del servicio real.</summary>
    private sealed class FakeNotifications : INotificationService
    {
        public List<(Guid Recipient, string Title)> Sent { get; } = new();

        public Task CreateAsync(Guid recipientTenantUserId, NotificationKind kind, string title, string body,
            string? linkRoute = null, Guid? relatedTaskItemId = null, string? actorName = null,
            CancellationToken cancellationToken = default)
        {
            Sent.Add((recipientTenantUserId, title));
            return Task.CompletedTask;
        }

        public Task<int> UnreadCountForPlatformUserAsync(Guid platformUserId, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
        public Task<Guid?> ResolveTenantUserIdAsync(Guid platformUserId, CancellationToken cancellationToken = default)
            => Task.FromResult<Guid?>(null);
        public Task<IReadOnlyList<NotificationDto>> ListForPlatformUserAsync(
            Guid platformUserId, int take = 20, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<NotificationDto>>(Array.Empty<NotificationDto>());
        public Task<bool> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
        public Task<int> MarkAllReadForPlatformUserAsync(Guid platformUserId, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class ScheduledJobDispatcherTests_Postgres
    : ScheduledJobDispatcherTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public ScheduledJobDispatcherTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class ScheduledJobDispatcherTests_SqlServer
    : ScheduledJobDispatcherTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public ScheduledJobDispatcherTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture)
    {
    }
}
