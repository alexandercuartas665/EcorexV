using Ecorex.Application.Common;
using Ecorex.Application.Scheduling;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion del Motor de programaciones (modulo 000889 "Programar actividad", ola P1)
/// en matriz DUAL PostgreSQL / SQL Server: consecutivo PAC sin duplicados, persistencia de reglas y
/// canales, reemplazo total al editar, encargado opcional, activar/pausar y el test BLOQUEANTE de
/// aislamiento cross-tenant (un tenant NUNCA ve ni toca las programaciones de otro).
/// </summary>
public abstract class ScheduledJobsTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected ScheduledJobsTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Create_AssignsPacSequence_AndPersistsRulesAndChannels()
    {
        var tenantId = await SeedTenantAsync("PAC Crear");

        var first = await RunAsync(tenantId, s => s.SaveAsync(null, new SaveScheduledJobRequest(
            "Recordatorio pago proveedores", ScheduledJobType.Notification, null, null,
            Rules: new[] { WeeklyRule("Lun,Mie") },
            Channels: new[] { ScheduledJobChannelType.Email, ScheduledJobChannelType.WhatsApp })));
        Assert.True(first.IsOk, first.Error);

        // El consecutivo PAC arranca en 1 y NO se duplica bajo creaciones sucesivas.
        var second = await RunAsync(tenantId, s => s.SaveAsync(null, new SaveScheduledJobRequest(
            "Otra programacion", ScheduledJobType.Notification, null, null,
            Rules: new[] { WeeklyRule("Mar") },
            Channels: new[] { ScheduledJobChannelType.Slack })));
        Assert.True(second.IsOk, second.Error);

        var list = await RunAsync(tenantId, s => s.ListAsync());
        Assert.Equal(2, list.Count);
        Assert.Contains(list, j => j.Code == "PAC-000001");
        Assert.Contains(list, j => j.Code == "PAC-000002");

        // Reglas y canales quedan persistidos y se leen en el detalle.
        var detail = await RunAsync(tenantId, s => s.GetAsync(first.Id!.Value));
        Assert.NotNull(detail);
        var rule = Assert.Single(detail!.Rules);
        Assert.Equal(ScheduledJobFrequency.Weekly, rule.Frequency);
        Assert.Equal("Lun,Mie", rule.Weekdays);
        Assert.Equal("08:00", rule.AtTime);
        Assert.Equal(2, detail.Channels.Count);
        Assert.Contains(ScheduledJobChannelType.Email, detail.Channels);
        Assert.Contains(ScheduledJobChannelType.WhatsApp, detail.Channels);
    }

    [Fact]
    public async Task Save_Validates_NameRulesChannels_AndActivityConcept()
    {
        var tenantId = await SeedTenantAsync("PAC Validacion");
        var okRules = new[] { WeeklyRule("Lun") };
        var okChannels = new[] { ScheduledJobChannelType.Email };

        var noName = await RunAsync(tenantId, s => s.SaveAsync(null, new SaveScheduledJobRequest(
            "   ", ScheduledJobType.Notification, null, null, okRules, okChannels)));
        Assert.False(noName.IsOk);

        var noRules = await RunAsync(tenantId, s => s.SaveAsync(null, new SaveScheduledJobRequest(
            "Sin reglas", ScheduledJobType.Notification, null, null,
            Array.Empty<ScheduledJobRuleDto>(), okChannels)));
        Assert.False(noRules.IsOk);

        var noChannels = await RunAsync(tenantId, s => s.SaveAsync(null, new SaveScheduledJobRequest(
            "Sin canales", ScheduledJobType.Notification, null, null,
            okRules, Array.Empty<ScheduledJobChannelType>())));
        Assert.False(noChannels.IsOk);

        // Type=Activity EXIGE categoria + subcategoria (el concepto que se disparara).
        var activityNoConcept = await RunAsync(tenantId, s => s.SaveAsync(null, new SaveScheduledJobRequest(
            "Actividad sin concepto", ScheduledJobType.Activity, null, null, okRules, okChannels)));
        Assert.False(activityNoConcept.IsOk);
    }

    [Fact]
    public async Task Update_ReplacesRulesAndChannels_AndKeepsCode()
    {
        var tenantId = await SeedTenantAsync("PAC Editar");

        var created = await RunAsync(tenantId, s => s.SaveAsync(null, new SaveScheduledJobRequest(
            "Original", ScheduledJobType.Notification, null, null,
            Rules: new[] { WeeklyRule("Lun"), WeeklyRule("Mar") },
            Channels: new[] { ScheduledJobChannelType.Email, ScheduledJobChannelType.Slack })));
        Assert.True(created.IsOk, created.Error);
        var id = created.Id!.Value;
        var before = await RunAsync(tenantId, s => s.GetAsync(id));
        Assert.NotNull(before);

        // Editar con UNA regla y UN canal: el reemplazo es TOTAL (no quedan huerfanos).
        var updated = await RunAsync(tenantId, s => s.SaveAsync(id, new SaveScheduledJobRequest(
            "Editada", ScheduledJobType.Notification, null, null,
            Rules: new[] { MonthlyRule("Primer", "Lunes") },
            Channels: new[] { ScheduledJobChannelType.Sms },
            Version: before!.Version)));
        Assert.True(updated.IsOk, updated.Error);

        var after = await RunAsync(tenantId, s => s.GetAsync(id));
        Assert.Equal("Editada", after!.Name);
        Assert.Equal(before.Code, after.Code); // el consecutivo NO cambia al editar
        var rule = Assert.Single(after.Rules);
        Assert.Equal(ScheduledJobFrequency.Monthly, rule.Frequency);
        Assert.Equal("Primer", rule.MonthOrdinal);
        Assert.Equal(ScheduledJobChannelType.Sms, Assert.Single(after.Channels));

        // Sin filas huerfanas en las tablas hijas.
        await using var ctx = _fixture.CreateContext(tenantId);
        Assert.Equal(1, await ctx.ScheduledJobRules.CountAsync(r => r.JobId == id));
        Assert.Equal(1, await ctx.ScheduledJobChannels.CountAsync(c => c.JobId == id));
    }

    [Fact]
    public async Task Activity_KeepsConceptAndOptionalAssignee_AndToggleStatusFlips()
    {
        var tenantId = await SeedTenantAsync("PAC Actividad");
        var (categoriaId, subcategoriaId) = await SeedConceptAsync(tenantId);
        var assigneeId = await SeedTenantUserAsync(tenantId, "encargado@sky.local");

        var created = await RunAsync(tenantId, s => s.SaveAsync(null, new SaveScheduledJobRequest(
            "Visita tecnica preventiva", ScheduledJobType.Activity, categoriaId, subcategoriaId,
            Rules: new[] { MonthlyRule("Primer", "Lunes") },
            Channels: new[] { ScheduledJobChannelType.Email },
            AssigneeTenantUserId: assigneeId)));
        Assert.True(created.IsOk, created.Error);
        var id = created.Id!.Value;

        var detail = await RunAsync(tenantId, s => s.GetAsync(id));
        Assert.Equal(ScheduledJobType.Activity, detail!.Type);
        Assert.Equal(subcategoriaId, detail.SubcategoryId);   // el concepto que disparara P3
        Assert.Equal(assigneeId, detail.AssigneeTenantUserId); // encargado OPCIONAL
        Assert.Equal(ScheduledJobStatus.Active, detail.Status);

        // Activar/pausar alterna (el worker de P2 debe saltarse las pausadas).
        await RunAsync(tenantId, async s => { await s.ToggleStatusAsync(id); return 0; });
        var paused = await RunAsync(tenantId, s => s.GetAsync(id));
        Assert.Equal(ScheduledJobStatus.Paused, paused!.Status);

        await RunAsync(tenantId, async s => { await s.ToggleStatusAsync(id); return 0; });
        var active = await RunAsync(tenantId, s => s.GetAsync(id));
        Assert.Equal(ScheduledJobStatus.Active, active!.Status);
    }

    /// <summary>
    /// BLOQUEANTE (regla 1 del proyecto): el filtro global por tenant hace IMPOSIBLE por construccion
    /// que un tenant lea o modifique las programaciones de otro, en AMBOS motores.
    /// </summary>
    [Fact]
    public async Task CrossTenant_Isolation_JobsAreNeverVisibleOrMutableFromAnotherTenant()
    {
        var tenantA = await SeedTenantAsync("PAC Tenant A");
        var tenantB = await SeedTenantAsync("PAC Tenant B");

        var inA = await RunAsync(tenantA, s => s.SaveAsync(null, new SaveScheduledJobRequest(
            "Solo de A", ScheduledJobType.Notification, null, null,
            Rules: new[] { WeeklyRule("Lun") },
            Channels: new[] { ScheduledJobChannelType.Email })));
        Assert.True(inA.IsOk, inA.Error);
        var idA = inA.Id!.Value;

        // B no la ve en la lista...
        var listB = await RunAsync(tenantB, s => s.ListAsync());
        Assert.Empty(listB);

        // ...ni por id directo...
        var getFromB = await RunAsync(tenantB, s => s.GetAsync(idA));
        Assert.Null(getFromB);

        // ...ni la puede editar...
        var editFromB = await RunAsync(tenantB, s => s.SaveAsync(idA, new SaveScheduledJobRequest(
            "Secuestrada por B", ScheduledJobType.Notification, null, null,
            Rules: new[] { WeeklyRule("Vie") },
            Channels: new[] { ScheduledJobChannelType.Slack })));
        Assert.False(editFromB.IsOk);

        // ...ni borrarla ni pausarla (no-op silencioso: para B ese id no existe).
        await RunAsync(tenantB, async s => { await s.DeleteAsync(idA); return 0; });
        await RunAsync(tenantB, async s => { await s.ToggleStatusAsync(idA); return 0; });

        // A la sigue viendo intacta y activa.
        var stillInA = await RunAsync(tenantA, s => s.GetAsync(idA));
        Assert.NotNull(stillInA);
        Assert.Equal("Solo de A", stillInA!.Name);
        Assert.Equal(ScheduledJobStatus.Active, stillInA.Status);

        // Y el consecutivo de B arranca en su propio PAC-000001 (secuencias por tenant).
        var inB = await RunAsync(tenantB, s => s.SaveAsync(null, new SaveScheduledJobRequest(
            "Solo de B", ScheduledJobType.Notification, null, null,
            Rules: new[] { WeeklyRule("Lun") },
            Channels: new[] { ScheduledJobChannelType.Email })));
        Assert.True(inB.IsOk, inB.Error);
        var listBAfter = await RunAsync(tenantB, s => s.ListAsync());
        Assert.Equal("PAC-000001", Assert.Single(listBAfter).Code);
    }

    // ---- Helpers ----

    private static ScheduledJobRuleDto WeeklyRule(string weekdays) => new(
        ScheduledJobFrequency.Weekly, 1, weekdays, null, null, null,
        "08:00", false, null, null, null, null, null, null);

    private static ScheduledJobRuleDto MonthlyRule(string ordinal, string weekday) => new(
        ScheduledJobFrequency.Monthly, 1, null, ordinal, weekday, null,
        "09:00", false, null, null, null, null, null, null);

    private async Task<T> RunAsync<T>(Guid tenantId, Func<IScheduledJobService, Task<T>> action)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        var tenant = new TestTenantContext(tenantId);
        var service = new ScheduledJobService(
            ctx, new SequenceService(ctx, tenant), tenant, TimeProvider.System);
        return await action(service);
    }

    private async Task<Guid> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(null);
        ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
        await ctx.SaveChangesAsync();
        return tenantId;
    }

    private async Task<(Guid CategoriaId, Guid SubcategoriaId)> SeedConceptAsync(Guid tenantId)
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
            Nombre = "Visita tecnica"
        };
        ctx.ActividadSubcategorias.Add(sub);
        await ctx.SaveChangesAsync();
        return (categoria.Id, sub.Id);
    }

    private async Task<Guid> SeedTenantUserAsync(Guid tenantId, string email)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        // El TenantUser tiene FK a PlatformUser: hay que sembrar la cuenta de plataforma primero.
        var platform = new PlatformUser
        {
            Email = email,
            DisplayName = "Encargado Test",
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
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class ScheduledJobsTests_Postgres
    : ScheduledJobsTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public ScheduledJobsTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class ScheduledJobsTests_SqlServer
    : ScheduledJobsTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public ScheduledJobsTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture)
    {
    }
}
