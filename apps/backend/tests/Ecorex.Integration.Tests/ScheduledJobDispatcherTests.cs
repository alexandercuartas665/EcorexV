using Ecorex.Application.Common;
using Ecorex.Application.Notifications;
using Ecorex.Application.Scheduling;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
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

    /// <summary>
    /// Ola P3: el tipo Actividad CREA la tarea (TaskItem, la misma del wizard de 4 pasos) consumiendo el
    /// concepto. No se duplica logica: se llama al mismo ITaskItemService.CreateAsync.
    /// </summary>
    [Fact]
    public async Task ActivityType_CreatesTheTaskFromTheConcept_WithAssignee_AndLogsItsNumber()
    {
        var tenantId = await SeedTenantAsync("Actividad P3");
        var assigneeId = await SeedTenantUserAsync(tenantId, "encargado@actividad.local");
        var subcategoriaId = await SeedConceptAsync(tenantId, tituloAuto: null);
        var window = DateTimeOffset.UtcNow.AddMinutes(-5);
        var (jobId, _) = await SeedJobAsync(tenantId, ScheduledJobType.Activity,
            ScheduledJobStatus.Active, window, assigneeId, subcategoriaId);

        Assert.Equal(1, await RunDueAsync(tenantId, DateTimeOffset.UtcNow, new FakeNotifications()));

        await using var ctx = _fixture.CreateContext(tenantId);

        // Se creo UNA tarea, con el concepto y el encargado de la programacion.
        var task = Assert.Single(await ctx.TaskItems.ToListAsync());
        Assert.Equal(subcategoriaId, task.SubcategoriaId);
        Assert.Equal(assigneeId, task.AssigneeTenantUserId);
        // Sin TituloAuto en el concepto, el titulo cae al nombre de la programacion.
        Assert.Equal("Programacion Activity", task.Title);

        // La bitacora deja el numero de la tarea creada (trazabilidad).
        var run = Assert.Single(await ctx.ScheduledJobRuns.Where(r => r.JobId == jobId).ToListAsync());
        Assert.Equal(ScheduledJobRunResult.Ok, run.Result);
        Assert.Equal(task.Number, run.CreatedEntityRef);
        Assert.Contains(task.Number, run.Detail);
    }

    [Fact]
    public async Task ActivityType_UsesTheConceptsTituloAuto_WhenItDefinesOne()
    {
        var tenantId = await SeedTenantAsync("Actividad TituloAuto");
        var subcategoriaId = await SeedConceptAsync(tenantId, tituloAuto: "Visita preventiva del mes");
        var window = DateTimeOffset.UtcNow.AddMinutes(-5);
        await SeedJobAsync(tenantId, ScheduledJobType.Activity,
            ScheduledJobStatus.Active, window, assigneeId: null, subcategoriaId: subcategoriaId);

        Assert.Equal(1, await RunDueAsync(tenantId, DateTimeOffset.UtcNow, new FakeNotifications()));

        await using var ctx = _fixture.CreateContext(tenantId);
        var task = Assert.Single(await ctx.TaskItems.ToListAsync());
        // Manda el TituloAuto del concepto, no el nombre de la programacion.
        Assert.Equal("Visita preventiva del mes", task.Title);
        // Sin encargado, la actividad nace SIN ASIGNAR (Pendiente): comportamiento por defecto de TaskItemService.
        Assert.Null(task.AssigneeTenantUserId);
        Assert.Equal(TaskItemStatus.Pending, task.Status);
    }

    [Fact]
    public async Task ActivityType_WithoutConcept_IsRecordedAsError()
    {
        var tenantId = await SeedTenantAsync("Actividad sin concepto");
        var window = DateTimeOffset.UtcNow.AddMinutes(-5);
        var (jobId, _) = await SeedJobAsync(tenantId, ScheduledJobType.Activity,
            ScheduledJobStatus.Active, window, assigneeId: null, subcategoriaId: null);

        Assert.Equal(1, await RunDueAsync(tenantId, DateTimeOffset.UtcNow, new FakeNotifications()));

        await using var ctx = _fixture.CreateContext(tenantId);
        Assert.Empty(await ctx.TaskItems.ToListAsync());
        var run = Assert.Single(await ctx.ScheduledJobRuns.Where(r => r.JobId == jobId).ToListAsync());
        Assert.Equal(ScheduledJobRunResult.Error, run.Result);
    }

    // ---- Ola P4: canales reales, reintento y dead-letter ----

    [Fact]
    public async Task Channels_AreActuallyDelivered_AndTheLogSaysSoChannelByChannel()
    {
        var tenantId = await SeedTenantAsync("Canales");
        var assigneeId = await SeedTenantUserAsync(tenantId, "encargado@canales.local");
        var window = DateTimeOffset.UtcNow.AddMinutes(-5);
        var (jobId, _) = await SeedJobAsync(tenantId, ScheduledJobType.Notification,
            ScheduledJobStatus.Active, window, assigneeId);

        var email = new FakeChannelSender(ScheduledJobChannelType.Email, delivers: true);
        var fired = await RunDueAsync(tenantId, DateTimeOffset.UtcNow, new FakeNotifications(), new[] { email });
        Assert.Equal(1, fired);

        // El canal se entrego DE VERDAD (antes solo se listaba en el detalle sin enviar nada).
        Assert.Single(email.Sent);

        await using var ctx = _fixture.CreateContext(tenantId);
        var run = Assert.Single(await ctx.ScheduledJobRuns.Where(r => r.JobId == jobId).ToListAsync());
        Assert.Equal(ScheduledJobRunResult.Ok, run.Result);
        Assert.Contains("In-app: entregada.", run.Detail);
        Assert.Contains("Email: enviado.", run.Detail);
    }

    [Fact]
    public async Task ChannelWithoutIntegration_IsReportedHonestly_NotFakedAsDelivered()
    {
        var tenantId = await SeedTenantAsync("Canal sin integracion");
        var assigneeId = await SeedTenantUserAsync(tenantId, "encargado@slack.local");
        var window = DateTimeOffset.UtcNow.AddMinutes(-5);
        // El job se siembra con canal Email; le agregamos Slack, que NO tiene sender registrado.
        var (jobId, _) = await SeedJobAsync(tenantId, ScheduledJobType.Notification,
            ScheduledJobStatus.Active, window, assigneeId);
        await using (var seed = _fixture.CreateContext(tenantId))
        {
            seed.ScheduledJobChannels.Add(new ScheduledJobChannel
            {
                TenantId = tenantId,
                JobId = jobId,
                Channel = ScheduledJobChannelType.Slack
            });
            await seed.SaveChangesAsync();
        }

        var email = new FakeChannelSender(ScheduledJobChannelType.Email, delivers: true);
        await RunDueAsync(tenantId, DateTimeOffset.UtcNow, new FakeNotifications(), new[] { email });

        await using var ctx = _fixture.CreateContext(tenantId);
        var run = Assert.Single(await ctx.ScheduledJobRuns.Where(r => r.JobId == jobId).ToListAsync());
        // Slack no se finge entregado: se dice que no hay integracion. Y NO cuenta como fallo.
        Assert.Contains("Slack: canal sin integracion", run.Detail);
        Assert.Equal(ScheduledJobRunResult.Ok, run.Result);
    }

    [Fact]
    public async Task FailedChannel_RetriesTheSameWindow_WithBackoff_AndThenDeadLetters()
    {
        var tenantId = await SeedTenantAsync("Reintento");
        var assigneeId = await SeedTenantUserAsync(tenantId, "encargado@retry.local");
        var window = DateTimeOffset.UtcNow.AddMinutes(-5);
        var (jobId, ruleId) = await SeedJobAsync(tenantId, ScheduledJobType.Notification,
            ScheduledJobStatus.Active, window, assigneeId);

        var failing = new[] { new FakeChannelSender(ScheduledJobChannelType.Email, delivers: false) };

        // Intento 1: falla -> se reprograma un REINTENTO conservando la ventana original.
        await RunDueAsync(tenantId, DateTimeOffset.UtcNow, new FakeNotifications(), failing);
        await using (var ctx1 = _fixture.CreateContext(tenantId))
        {
            var rule = await ctx1.ScheduledJobRules.FirstAsync(r => r.Id == ruleId);
            Assert.Equal(1, rule.Attempt);
            Assert.Equal(window.ToUnixTimeSeconds(), rule.PendingWindowAt!.Value.ToUnixTimeSeconds());
            Assert.True(rule.NextRunAt > DateTimeOffset.UtcNow, "el reintento se agenda hacia el futuro (backoff)");

            var run = Assert.Single(await ctx1.ScheduledJobRuns.Where(r => r.JobId == jobId).ToListAsync());
            Assert.Equal(ScheduledJobRunResult.Error, run.Result);
            Assert.Equal(1, run.Attempt);
        }

        // Intentos 2 y 3: se fuerza que el reintento este vencido. Al 3er intento -> DEAD-LETTER.
        for (var i = 0; i < 2; i++)
        {
            await using (var force = _fixture.CreateContext(tenantId))
            {
                var rule = await force.ScheduledJobRules.FirstAsync(r => r.Id == ruleId);
                rule.NextRunAt = DateTimeOffset.UtcNow.AddMinutes(-1);
                await force.SaveChangesAsync();
            }
            await RunDueAsync(tenantId, DateTimeOffset.UtcNow, new FakeNotifications(), failing);
        }

        await using var ctx = _fixture.CreateContext(tenantId);
        var runs = await ctx.ScheduledJobRuns.Where(r => r.JobId == jobId).OrderBy(r => r.Attempt).ToListAsync();
        // Cada intento dejo su propia fila (la clave de idempotencia incluye el intento).
        Assert.Equal(3, runs.Count);
        Assert.Equal(new[] { 1, 2, 3 }, runs.Select(r => r.Attempt));
        Assert.All(runs, r => Assert.Equal(ScheduledJobRunResult.Error, r.Result));
        // Todos reintentan la MISMA ventana.
        Assert.All(runs, r => Assert.Equal(window.ToUnixTimeSeconds(), r.FiredAt.ToUnixTimeSeconds()));
        Assert.Contains("dead-letter", runs.Last().Detail);

        // Tras el dead-letter la regla vuelve a su cadencia normal (no se queda atascada reintentando).
        var final = await ctx.ScheduledJobRules.FirstAsync(r => r.Id == ruleId);
        Assert.Null(final.PendingWindowAt);
        Assert.Equal(0, final.Attempt);
        Assert.True(final.NextRunAt > window);
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

    /// <summary>Dispatcher con el servicio de TAREAS real: el tipo Actividad crea una TaskItem de verdad (P3).</summary>
    private static ScheduledJobDispatcher BuildDispatcher(
        EcorexDbContext ctx, ITenantContext tenant, INotificationService notifications,
        IEnumerable<IScheduledJobChannelSender>? senders = null, TimeProvider? clock = null)
        => new(ctx, tenant, notifications,
            new TaskItemService(ctx, tenant, new SequenceService(ctx, tenant),
                new WorkflowEngine(ctx, tenant, new NoOpWorkflowRuleHook(), new NoOpTaskBroadcaster()),
                new NoOpEmailSender(), new Ecorex.Application.Organization.NodeAssigneeResolver(ctx)),
            senders ?? Array.Empty<IScheduledJobChannelSender>(),
            clock ?? TimeProvider.System,
            NullLogger<ScheduledJobDispatcher>.Instance);

    private async Task<int> RunDueAsync(
        Guid tenantId, DateTimeOffset nowUtc, FakeNotifications notifications,
        IEnumerable<IScheduledJobChannelSender>? senders = null)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        var dispatcher = BuildDispatcher(ctx, new TestTenantContext(tenantId), notifications, senders);
        return await dispatcher.RunDueForTenantAsync(nowUtc);
    }

    private async Task<IReadOnlyList<Guid>> FindDueTenantsAsync(Guid anyTenantId, DateTimeOffset nowUtc)
    {
        await using var ctx = _fixture.CreateContext(anyTenantId);
        var dispatcher = BuildDispatcher(ctx, new TestTenantContext(anyTenantId), new FakeNotifications());
        return await dispatcher.FindTenantsWithDueRulesAsync(nowUtc);
    }

    /// <summary>Sender de prueba para un canal: permite forzar entrega OK o fallo.</summary>
    private sealed class FakeChannelSender : IScheduledJobChannelSender
    {
        private readonly bool _delivers;
        public FakeChannelSender(ScheduledJobChannelType channel, bool delivers)
        {
            Channel = channel;
            _delivers = delivers;
        }
        public ScheduledJobChannelType Channel { get; }
        public List<string> Sent { get; } = new();

        public Task<ChannelSendResult> SendAsync(
            ScheduledJobRecipient recipient, string title, string body, CancellationToken cancellationToken = default)
        {
            Sent.Add(title);
            return Task.FromResult(_delivers
                ? ChannelSendResult.Ok($"{Channel}: enviado.")
                : ChannelSendResult.Fail($"{Channel}: fallo simulado."));
        }
    }

    /// <summary>Siembra el concepto (categoria -> subcategoria) que disparara el tipo Actividad.</summary>
    private async Task<Guid> SeedConceptAsync(Guid tenantId, string? tituloAuto)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        var categoria = new ActividadCategoria { TenantId = tenantId, Codigo = "CAT-OPS", Nombre = "Operaciones" };
        ctx.ActividadCategorias.Add(categoria);
        await ctx.SaveChangesAsync();
        var sub = new ActividadSubcategoria
        {
            TenantId = tenantId,
            CategoriaId = categoria.Id,
            Codigo = "CAT-OPS-01",
            Nombre = "Visita tecnica",
            TituloAuto = tituloAuto,
        };
        ctx.ActividadSubcategorias.Add(sub);
        await ctx.SaveChangesAsync();
        return sub.Id;
    }

    /// <summary>Siembra una programacion con UNA regla diaria cuya ventana (NextRunAt) se fija a mano.</summary>
    private async Task<(Guid JobId, Guid RuleId)> SeedJobAsync(
        Guid tenantId, ScheduledJobType type, ScheduledJobStatus status,
        DateTimeOffset? nextRunAt, Guid? assigneeId, Guid? subcategoriaId = null)
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
            SubcategoryId = subcategoriaId,
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
