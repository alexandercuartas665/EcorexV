using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Test bloqueante de aislamiento multi-tenant (hoja de ruta sec.5.3), en matriz dual
/// PostgreSQL / SQL Server (ADR-001: DAL dual). Los casos viven en esta clase base y se
/// ejecutan una vez por motor via las clases concretas de abajo. Verifica que el filtro
/// global de consulta por TenantId impide que un tenant vea datos de otro, que sin tenant
/// activo no se devuelven filas tenant-scoped (fail-closed) y que IgnoreQueryFilters
/// permite el acceso administrativo controlado. Este es el gate de merge: si alguien rompe
/// HasQueryFilter o el aislamiento, estos tests deben FALLAR en ambos motores.
/// </summary>
public abstract class TenantIsolationTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected TenantIsolationTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task TenantScopedData_IsIsolatedBetweenTenants()
    {
        var (tenantA, tenantB) = await SeedTwoTenantsAsync();

        // Con tenant A activo: solo ve datos de A.
        await using (var ctx = _fixture.CreateContext(tenantA))
        {
            var rows = await ctx.TenantConfigurations.ToListAsync();
            Assert.Single(rows);
            Assert.All(rows, r => Assert.Equal(tenantA, r.TenantId));
        }

        // Con tenant B activo: solo ve datos de B.
        await using (var ctx = _fixture.CreateContext(tenantB))
        {
            var rows = await ctx.TenantConfigurations.ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.Equal(tenantB, r.TenantId));
        }
    }

    [Fact]
    public async Task WithoutActiveTenant_TenantScopedQueriesReturnNoRows_FailClosed()
    {
        await SeedTwoTenantsAsync();

        // Sin tenant activo: cero filas tenant-scoped (fail-closed), aunque existan datos.
        await using var ctx = _fixture.CreateContext(tenantId: null);
        var rows = await ctx.TenantConfigurations.ToListAsync();
        Assert.Empty(rows);
    }

    [Fact]
    public async Task IgnoreQueryFilters_AllowsControlledAdminAccessAcrossTenants()
    {
        var (tenantA, tenantB) = await SeedTwoTenantsAsync();

        // Acceso administrativo controlado: IgnoreQueryFilters ve todos los tenants.
        // (Se acota a los tenants sembrados por este test porque el contenedor se comparte.)
        await using var ctx = _fixture.CreateContext(tenantId: null);
        var all = await ctx.TenantConfigurations
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantA || r.TenantId == tenantB)
            .ToListAsync();
        Assert.Equal(3, all.Count);
    }

    /// <summary>
    /// Siembra dos tenants nuevos (GUIDs frescos, seguros ante el contenedor compartido):
    /// A con 1 configuracion y B con 2. El interceptor estampa TenantId desde el contexto.
    /// </summary>
    private async Task<(Guid TenantA, Guid TenantB)> SeedTwoTenantsAsync()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();

        // Tenants: entidades globales (sin filtro por tenant).
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantA, Name = "Agencia A" });
            ctx.Tenants.Add(new Tenant { Id = tenantB, Name = "Agencia B" });
            await ctx.SaveChangesAsync();
        }

        // Datos tenant-scoped de A (el interceptor estampa TenantId desde el contexto).
        await using (var ctx = _fixture.CreateContext(tenantA))
        {
            ctx.TenantConfigurations.Add(new TenantConfiguration { ConfigKey = "tono", ConfigValue = "formal" });
            await ctx.SaveChangesAsync();
        }

        // Datos tenant-scoped de B.
        await using (var ctx = _fixture.CreateContext(tenantB))
        {
            ctx.TenantConfigurations.Add(new TenantConfiguration { ConfigKey = "tono", ConfigValue = "informal" });
            ctx.TenantConfigurations.Add(new TenantConfiguration { ConfigKey = "horario", ConfigValue = "8-18" });
            await ctx.SaveChangesAsync();
        }

        return (tenantA, tenantB);
    }
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class TenantIsolationTests_Postgres
    : TenantIsolationTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public TenantIsolationTests_Postgres(PostgresTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class TenantIsolationTests_SqlServer
    : TenantIsolationTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public TenantIsolationTests_SqlServer(SqlServerTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}
