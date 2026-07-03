using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Infrastructure.Persistence;
using Ecorex.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Test bloqueante de aislamiento multi-tenant (hoja de ruta sec.5.3). Verifica que el filtro
/// global de consulta por TenantId impide que un tenant vea datos de otro y que, sin tenant
/// activo, no se devuelven filas tenant-scoped (fail-closed).
/// </summary>
public sealed class TenantIsolationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        await using var ctx = CreateContext(tenantId: null);
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task TenantScopedData_IsIsolatedBetweenTenants()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();

        // Tenants: entidades globales (sin filtro por tenant).
        await using (var ctx = CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantA, Name = "Agencia A" });
            ctx.Tenants.Add(new Tenant { Id = tenantB, Name = "Agencia B" });
            await ctx.SaveChangesAsync();
        }

        // Datos tenant-scoped de A (el interceptor estampa TenantId desde el contexto).
        await using (var ctx = CreateContext(tenantA))
        {
            ctx.TenantConfigurations.Add(new TenantConfiguration { ConfigKey = "tono", ConfigValue = "formal" });
            await ctx.SaveChangesAsync();
        }

        // Datos tenant-scoped de B.
        await using (var ctx = CreateContext(tenantB))
        {
            ctx.TenantConfigurations.Add(new TenantConfiguration { ConfigKey = "tono", ConfigValue = "informal" });
            ctx.TenantConfigurations.Add(new TenantConfiguration { ConfigKey = "horario", ConfigValue = "8-18" });
            await ctx.SaveChangesAsync();
        }

        // Con tenant A activo: solo ve datos de A.
        await using (var ctx = CreateContext(tenantA))
        {
            var rows = await ctx.TenantConfigurations.ToListAsync();
            Assert.Single(rows);
            Assert.All(rows, r => Assert.Equal(tenantA, r.TenantId));
        }

        // Con tenant B activo: solo ve datos de B.
        await using (var ctx = CreateContext(tenantB))
        {
            var rows = await ctx.TenantConfigurations.ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.Equal(tenantB, r.TenantId));
        }

        // Sin tenant activo: cero filas tenant-scoped (fail-closed).
        await using (var ctx = CreateContext(tenantId: null))
        {
            var rows = await ctx.TenantConfigurations.ToListAsync();
            Assert.Empty(rows);
        }

        // Acceso administrativo controlado: IgnoreQueryFilters ve todos los tenants.
        await using (var ctx = CreateContext(tenantId: null))
        {
            var all = await ctx.TenantConfigurations.IgnoreQueryFilters().ToListAsync();
            Assert.Equal(3, all.Count);
        }
    }

    private EcorexDbContext CreateContext(Guid? tenantId)
    {
        var tenantContext = new FixedTenantContext(tenantId);
        var options = new DbContextOptionsBuilder<EcorexDbContext>()
            .UseNpgsql(_db.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new AuditableTenantInterceptor(tenantContext, TimeProvider.System))
            .Options;

        return new EcorexDbContext(options, tenantContext);
    }

    private sealed class FixedTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}
