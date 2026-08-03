using Ecorex.Application.Common;
using Ecorex.Application.Reporting.Templates;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion del catalogo de plantillas de reportes reutilizables entre tenants
/// (ADR-0062, modelo hibrido) en matriz dual PostgreSQL / SQL Server.
///
/// Lo que fijan:
/// - Una plantilla NATIVA es activable en cualquier tenant, y activarla crea una instancia
///   ReportDefinition tenant-scoped vinculada por TemplateId (snapshot del molde).
/// - AISLAMIENTO cross-tenant: activar la MISMA plantilla en dos tenants deja a cada uno con SU
///   propia instancia; el filtro global impide que un tenant vea la instancia del otro.
/// - Idempotencia: re-activar no duplica.
/// - Compatibilidad de fuente CONTENEDOR: solo se activa donde exista el contenedor requerido; en un
///   tenant sin ese contenedor se RECHAZA con mensaje claro y ni siquiera aparece como compatible.
/// - Re-sincronizar re-copia el snapshot desde la plantilla SIN perder report_definition_roles.
/// </summary>
public abstract class ReportActivationTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected ReportActivationTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task NativeTemplate_ActivatesPerTenant_AndIsIsolated()
    {
        var templateId = await SeedTemplateAsync(
            "Panel de Actividades", "panel:system-activities", ReportTemplateSourceKind.Native, null);
        var a = await SeedTenantAsync("Activacion A");
        var b = await SeedTenantAsync("Activacion B");

        // Activar la MISMA plantilla en A y en B: cada uno obtiene SU propia instancia.
        Guid reportA, reportB;
        await using (var ctx = _fixture.CreateContext(a))
        {
            var svc = new ReportActivationService(ctx, new TestTenantContext(a));
            var r = await svc.ActivateTemplateAsync(templateId);
            Assert.True(r.Ok);
            reportA = r.ReportId!.Value;

            // Idempotente: re-activar devuelve la misma instancia, no crea otra.
            var again = await svc.ActivateTemplateAsync(templateId);
            Assert.True(again.Ok);
            Assert.Equal(reportA, again.ReportId);
        }

        await using (var ctx = _fixture.CreateContext(b))
        {
            var svc = new ReportActivationService(ctx, new TestTenantContext(b));
            var r = await svc.ActivateTemplateAsync(templateId);
            Assert.True(r.Ok);
            reportB = r.ReportId!.Value;
        }

        Assert.NotEqual(reportA, reportB);

        // Aislamiento: A solo ve su instancia; la de B no aparece bajo el filtro global de A.
        await using (var ctx = _fixture.CreateContext(a))
        {
            var mine = await ctx.ReportDefinitions.Where(d => d.TemplateId == templateId).ToListAsync();
            Assert.Single(mine);
            Assert.Equal(reportA, mine[0].Id);
            Assert.Equal("panel:system-activities", mine[0].SourceKey);
            Assert.DoesNotContain(mine, d => d.Id == reportB);
        }
    }

    [Fact]
    public async Task ContainerTemplate_ActivableOnlyWhereContainerExists()
    {
        var templateId = await SeedTemplateAsync(
            "Panel OCS", "panel:ocs", ReportTemplateSourceKind.Container, "Software OCS");
        var withContainer = await SeedTenantAsync("Con OCS");
        var withoutContainer = await SeedTenantAsync("Sin OCS");
        await SeedContainerAsync(withContainer, "Software OCS");

        // Tenant CON el contenedor: se activa.
        await using (var ctx = _fixture.CreateContext(withContainer))
        {
            var svc = new ReportActivationService(ctx, new TestTenantContext(withContainer));
            var r = await svc.ActivateTemplateAsync(templateId);
            Assert.True(r.Ok);
            Assert.NotNull(r.ReportId);

            // Las plantillas son GLOBALES (la BD del fixture es compartida): se asienta sobre LA MIA.
            var activatable = await svc.ListActivatableAsync();
            var row = Assert.Single(activatable, t => t.TemplateId == templateId);
            Assert.True(row.IsCompatible);
            Assert.True(row.IsActivated);
        }

        // Tenant SIN el contenedor: se rechaza con mensaje claro y NO crea instancia.
        await using (var ctx = _fixture.CreateContext(withoutContainer))
        {
            var svc = new ReportActivationService(ctx, new TestTenantContext(withoutContainer));
            var r = await svc.ActivateTemplateAsync(templateId);
            Assert.False(r.Ok);
            Assert.NotNull(r.Message);
            Assert.Contains("Software OCS", r.Message);

            Assert.Empty(await ctx.ReportDefinitions.Where(d => d.TemplateId == templateId).ToListAsync());

            var activatable = await svc.ListActivatableAsync();
            var row = Assert.Single(activatable, t => t.TemplateId == templateId);
            Assert.False(row.IsCompatible);
            Assert.False(row.IsActivated);
        }
    }

    [Fact]
    public async Task ActivateCompatible_Sweep_ActivatesOnlyCompatible()
    {
        var native = await SeedTemplateAsync("Nativa X", "panel:system-activities", ReportTemplateSourceKind.Native, null);
        var container = await SeedTemplateAsync("Contenedor Y", "panel:ocs", ReportTemplateSourceKind.Container, "Software OCS");
        var tenant = await SeedTenantAsync("Barrido"); // sin contenedor

        await using var ctx = _fixture.CreateContext(tenant);
        var svc = new ReportActivationService(ctx, new TestTenantContext(tenant));

        // includeNative=true: activa la nativa; la de contenedor NO (falta el contenedor). La BD del
        // fixture es compartida y las plantillas son GLOBALES, asi que el conteo total no es estable:
        // se asienta sobre el vinculo de MIS plantillas en este tenant (recien creado, sin instancias).
        await svc.ActivateCompatibleAsync(includeNative: true);
        var linked = await ctx.ReportDefinitions.Where(d => d.TemplateId != null).Select(d => d.TemplateId).ToListAsync();
        Assert.Contains((Guid?)native, linked);
        Assert.DoesNotContain((Guid?)container, linked);
    }

    [Fact]
    public async Task Resync_RecopiesSnapshot_KeepingRoles()
    {
        var templateId = await SeedTemplateAsync("Con roles", "panel:system-activities", ReportTemplateSourceKind.Native, null);
        var tenant = await SeedTenantAsync("Resync");
        var rolId = await SeedRolAsync(tenant, "Administrador");

        Guid reportId;
        await using (var ctx = _fixture.CreateContext(tenant))
        {
            var svc = new ReportActivationService(ctx, new TestTenantContext(tenant));
            reportId = (await svc.ActivateTemplateAsync(templateId)).ReportId!.Value;

            // Asignacion de rol a la instancia (gobernanza doc 04).
            ctx.ReportDefinitionRoles.Add(new ReportDefinitionRole
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant,
                ReportDefinitionId = reportId,
                RolId = rolId
            });
            await ctx.SaveChangesAsync();
        }

        // Cambia el nombre de la plantilla (global) y re-sincroniza.
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            var tpl = await ctx.ReportTemplates.FirstAsync(t => t.Id == templateId);
            tpl.Name = "Con roles (v2)";
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _fixture.CreateContext(tenant))
        {
            var svc = new ReportActivationService(ctx, new TestTenantContext(tenant));
            Assert.True(await svc.ResyncFromTemplateAsync(reportId));

            var def = await ctx.ReportDefinitions.FirstAsync(d => d.Id == reportId);
            Assert.Equal("Con roles (v2)", def.Name);

            // La asignacion de rol se conserva intacta tras el resync.
            var roles = await ctx.ReportDefinitionRoles.Where(r => r.ReportDefinitionId == reportId).ToListAsync();
            Assert.Single(roles);
            Assert.Equal(rolId, roles[0].RolId);
        }
    }

    // ---- Helpers ----

    private async Task<Guid> SeedTemplateAsync(string name, string sourceKey, ReportTemplateSourceKind kind, string? containerName)
    {
        var id = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId: null);
        ctx.ReportTemplates.Add(new ReportTemplate
        {
            Id = id,
            Name = name,
            Kind = ReportTemplateKind.Panel,
            SourceKey = sourceKey,
            RequiredSourceKind = kind,
            RequiredContainerName = containerName,
            IsPublished = true
        });
        await ctx.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId: null);
        ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
        await ctx.SaveChangesAsync();
        return tenantId;
    }

    private async Task SeedContainerAsync(Guid tenantId, string containerName)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        var modelId = Guid.CreateVersion7();
        var containerId = Guid.CreateVersion7();
        ctx.DataModels.Add(new DataModel { Id = modelId, TenantId = tenantId, Name = "Modelo " + containerName });
        ctx.DataContainers.Add(new DataContainer { Id = containerId, TenantId = tenantId, ModelId = modelId, Name = containerName });
        await ctx.SaveChangesAsync();
    }

    private async Task<Guid> SeedRolAsync(Guid tenantId, string name)
    {
        var id = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId);
        ctx.Roles.Add(new Rol { Id = id, TenantId = tenantId, Name = name, IsSystem = true });
        await ctx.SaveChangesAsync();
        return id;
    }

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class ReportActivationTests_Postgres
    : ReportActivationTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public ReportActivationTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class ReportActivationTests_SqlServer
    : ReportActivationTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public ReportActivationTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture)
    {
    }
}
