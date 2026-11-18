using Ecorex.Application.Common;
using Ecorex.Application.Gestor;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Matriz dual PostgreSQL / SQL Server de los eventos de entrega de SendGrid (ADR-0188):
/// (a) el estado de la ejecucion solo SUBE (un "processed" tardio no tapa un "delivered") y guarda apertura/clic;
/// (b) un rebote definitivo pasa el correo a la lista de bajas (un bloqueo temporal no);
/// (c) un evento con ecx_run de OTRO tenant no toca nada del ambiente (aislamiento);
/// (d) el token de la URL resuelve al tenant duenno, y group_resubscribe solo levanta bajas del proveedor.
/// </summary>
public abstract class EmailDeliveryEventsTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected EmailDeliveryEventsTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Apply_RaisesStatusOnly_AndRecordsOpenAndClick()
    {
        var tenantId = await SeedTenantAsync("Entrega A");
        var runId = await SeedRunAsync(tenantId, "q-abc");
        await using var ctx = _fixture.CreateContext(tenantId);
        var svc = Service(ctx, tenantId);
        var t0 = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

        await svc.ApplyAsync(
        [
            Ev("delivered", t0.AddSeconds(5), tenantId, runId),
            Ev("processed", t0, tenantId, runId),            // llega tarde: no baja el estado
            Ev("click", t0.AddMinutes(3), tenantId, runId),  // clic implica apertura
            Ev("open", t0.AddMinutes(9), tenantId, runId),   // apertura posterior: no baja de Clic
        ], ownAccount: true);

        var run = await ctx.ContactWorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == runId);
        Assert.Equal(EmailDeliveryStatus.Clicked, run.DeliveryStatus);
        Assert.Equal(t0.AddMinutes(3), run.OpenedAtUtc);
        Assert.Equal(t0.AddMinutes(3), run.ClickedAtUtc);
        Assert.Equal(ContactWorkflowRunStatus.Sent, run.Status);
    }

    [Fact]
    public async Task Apply_HardBounceSuppresses_BlockedDoesNot_AndMatchesByMessageId()
    {
        var tenantId = await SeedTenantAsync("Entrega B");
        var runId = await SeedRunAsync(tenantId, "q-xyz");
        await using var ctx = _fixture.CreateContext(tenantId);
        var svc = Service(ctx, tenantId);
        var now = DateTimeOffset.UtcNow;

        // Sin ecx_run: se correlaciona por el id del SMTP (prefijo de sg_message_id = ExternalRef).
        await svc.ApplyAsync(
        [
            new SendGridEvent("bounce", "malo@destino.test", now, tenantId, null, "q-xyz", "550 5.1.1 user unknown", "bounce"),
            new SendGridEvent("bounce", "lleno@destino.test", now, tenantId, null, null, "421 try later", "blocked"),
        ], ownAccount: true);

        var run = await ctx.ContactWorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == runId);
        Assert.Equal(EmailDeliveryStatus.Bounced, run.DeliveryStatus);
        Assert.Contains("user unknown", run.DeliveryDetail);

        var bajas = await ctx.EmailUnsubscribes.AsNoTracking().ToListAsync();
        var baja = Assert.Single(bajas);
        Assert.Equal("malo@destino.test", baja.Email);
        Assert.Equal(SendGridEvents.OrigenRebote, baja.Origen);

        // Re-entrega del mismo lote (SendGrid reintenta): idempotente, no duplica la baja.
        await svc.ApplyAsync(
        [
            new SendGridEvent("bounce", "malo@destino.test", now, tenantId, null, "q-xyz", "550 5.1.1 user unknown", "bounce"),
        ], ownAccount: true);
        Assert.Single(await ctx.EmailUnsubscribes.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Apply_EventsOfAnotherTenant_DoNotTouchTheAmbient()
    {
        var tenantA = await SeedTenantAsync("Entrega C1");
        var tenantB = await SeedTenantAsync("Entrega C2");
        var runOfB = await SeedRunAsync(tenantB, "q-b");

        // Ambiente = A, pero los eventos dicen ser de B (o apuntan a una ejecucion de B).
        await using (var ctxA = _fixture.CreateContext(tenantA))
        {
            var svcA = Service(ctxA, tenantA);
            var res = await svcA.ApplyAsync(
            [
                Ev("delivered", DateTimeOffset.UtcNow, tenantB, runOfB),
                new SendGridEvent("spamreport", "x@destino.test", DateTimeOffset.UtcNow, tenantB, runOfB, null, null, null),
                // Sin tenant pero con la ejecucion de B: el filtro global de A no la encuentra.
                Ev("open", DateTimeOffset.UtcNow, null, runOfB),
            ], ownAccount: true);
            Assert.Equal(0, res.RunsUpdated);
            Assert.Empty(await ctxA.EmailUnsubscribes.AsNoTracking().ToListAsync());
        }

        await using var ctxB = _fixture.CreateContext(tenantB);
        var run = await ctxB.ContactWorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == runOfB);
        Assert.Equal(EmailDeliveryStatus.None, run.DeliveryStatus);
        Assert.Null(run.OpenedAtUtc);
    }

    [Fact]
    public async Task WebhookToken_ResolvesOwner_AndResubscribeOnlyLiftsProviderBajas()
    {
        var tenantId = await SeedTenantAsync("Entrega D");
        await using var ctx = _fixture.CreateContext(tenantId);
        ctx.TenantEmailConfigs.Add(new TenantEmailConfig
        {
            TenantId = tenantId,
            SmtpHost = "smtp.sendgrid.net",
            FromEmail = "comercial@empresa.test",
            IsEnabled = true
        });
        ctx.EmailUnsubscribes.Add(new EmailUnsubscribe { TenantId = tenantId, Email = "enlace@destino.test", Origen = "enlace" });
        await ctx.SaveChangesAsync();
        var svc = Service(ctx, tenantId);

        var info = await svc.GetTenantWebhookAsync(create: true);
        Assert.True(info.HasToken);
        Assert.StartsWith("https://app.test" + EmailDeliveryEventService.WebhookPath, info.Url);
        var token = info.Url![(info.Url.LastIndexOf('/') + 1)..];

        // Resolver es cross-tenant: un contexto sin tenant encuentra al duenno por el token.
        await using (var anon = _fixture.CreateContext(tenantId: null))
        {
            var scope = await Service(anon, null).ResolveScopeAsync(token);
            Assert.True(scope.Found);
            Assert.Equal(tenantId, scope.TenantId);
            Assert.False((await Service(anon, null).ResolveScopeAsync("no-existe")).Found);
        }

        // Regenerar revoca el token anterior.
        var again = await svc.GetTenantWebhookAsync(regenerate: true);
        Assert.NotEqual(info.Url, again.Url);
        await using (var anon = _fixture.CreateContext(tenantId: null))
        {
            Assert.False((await Service(anon, null).ResolveScopeAsync(token)).Found);
        }

        var now = DateTimeOffset.UtcNow;
        await svc.ApplyAsync(
        [
            new SendGridEvent("unsubscribe", "prov@destino.test", now, tenantId, null, null, null, null),
        ], ownAccount: true);
        Assert.Equal(2, await ctx.EmailUnsubscribes.CountAsync());

        // Re-suscripcion: levanta la baja del proveedor, nunca la de nuestro enlace.
        await svc.ApplyAsync(
        [
            new SendGridEvent("group_resubscribe", "prov@destino.test", now.AddMinutes(1), tenantId, null, null, null, null),
            new SendGridEvent("group_resubscribe", "enlace@destino.test", now.AddMinutes(1), tenantId, null, null, null, null),
        ], ownAccount: true);
        var left = await ctx.EmailUnsubscribes.AsNoTracking().Select(u => u.Email).ToListAsync();
        Assert.Equal(new[] { "enlace@destino.test" }, left);
    }

    // ---- helpers ----

    private static SendGridEvent Ev(string evt, DateTimeOffset at, Guid? tenantId, Guid runId)
        => new(evt, "contacto@destino.test", at, tenantId, runId, null, null, null);

    private static EmailDeliveryEventService Service(EcorexDbContext ctx, Guid? tenantId)
        => new(ctx, new TestTenantContext(tenantId), new TestLinks());

    private async Task<Guid> SeedRunAsync(Guid tenantId, string externalRef)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        var filtro = new TerceroFiltro { TenantId = tenantId, Nombre = "Filtro " + externalRef };
        var wf = new ContactWorkflow { TenantId = tenantId, TerceroFiltroId = filtro.Id, Nombre = "Campana " + externalRef };
        var step = new ContactWorkflowStep
        {
            TenantId = tenantId,
            ContactWorkflowId = wf.Id,
            StepType = ContactWorkflowStepType.Email,
            Label = "E-mail",
            Orden = 1
        };
        var run = new ContactWorkflowRun
        {
            TenantId = tenantId,
            ContactWorkflowStepId = step.Id,
            ContactWorkflowScheduleId = Guid.CreateVersion7(),
            TerceroId = Guid.CreateVersion7(),
            WindowDate = new DateOnly(2026, 11, 18),
            DispatchedAtUtc = DateTimeOffset.UtcNow,
            Status = ContactWorkflowRunStatus.Sent,
            Channel = "email",
            ExternalRef = externalRef
        };
        ctx.TerceroFiltros.Add(filtro);
        ctx.ContactWorkflows.Add(wf);
        ctx.ContactWorkflowSteps.Add(step);
        ctx.ContactWorkflowRuns.Add(run);
        await ctx.SaveChangesAsync();
        return run.Id;
    }

    private async Task<Guid> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId: null);
        ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
        await ctx.SaveChangesAsync();
        return tenantId;
    }

    private sealed class TestTenantContext(Guid? tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId => null;
    }

    private sealed class TestLinks : INotifyLinkBuilder
    {
        public string? BuildTaskLink(Guid taskId) => null;
        public string? BuildDecisionLink(string token) => null;
        public string? BuildSignatureLink(string token) => null;
        public string? ToAbsoluteUrl(string? pathOrUrl) => pathOrUrl is null ? null : "https://app.test" + pathOrUrl;
    }
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class EmailDeliveryEventsTests_Postgres
    : EmailDeliveryEventsTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public EmailDeliveryEventsTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture) { }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class EmailDeliveryEventsTests_SqlServer
    : EmailDeliveryEventsTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public EmailDeliveryEventsTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture) { }
}
