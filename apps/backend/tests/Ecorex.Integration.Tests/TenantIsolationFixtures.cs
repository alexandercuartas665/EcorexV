using Ecorex.Application.Common;
using Ecorex.Infrastructure.Persistence;
using Ecorex.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Fixture base para los tests de aislamiento multi-tenant en matriz dual (ADR-001).
/// Cada fixture concreto levanta un contenedor efimero de su motor, aplica las migraciones
/// del proveedor y fabrica contextos con el mismo interceptor de auditoria/tenant que usa
/// la aplicacion. El contenedor se comparte entre los tests de la clase (IClassFixture).
/// </summary>
public abstract class TenantIsolationDbFixture : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await StartContainerAsync();
        await using var ctx = CreateContext(tenantId: null);
        await ctx.Database.MigrateAsync();
    }

    public abstract Task DisposeAsync();

    protected abstract Task StartContainerAsync();

    /// <summary>
    /// Crea un contexto EF con el tenant activo indicado (null = sin tenant, fail-closed).
    /// Espeja la configuracion de DependencyInjection: proveedor + snake_case + interceptor.
    /// </summary>
    public abstract EcorexDbContext CreateContext(Guid? tenantId);

    protected sealed class FixedTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Fixture PostgreSQL: contenedor efimero postgres:16-alpine via Testcontainers.</summary>
public sealed class PostgresTenantIsolationFixture : TenantIsolationDbFixture
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    protected override Task StartContainerAsync() => _db.StartAsync();

    public override async Task DisposeAsync() => await _db.DisposeAsync();

    public override EcorexDbContext CreateContext(Guid? tenantId)
    {
        var tenantContext = new FixedTenantContext(tenantId);
        var options = new DbContextOptionsBuilder<EcorexDbContext>()
            .UseNpgsql(_db.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new AuditableTenantInterceptor(tenantContext, TimeProvider.System))
            .Options;

        return new EcorexDbContext(options, tenantContext);
    }
}

/// <summary>
/// Fixture SQL Server: contenedor efimero mcr.microsoft.com/mssql/server:2022-latest via
/// Testcontainers.MsSql. Usa SqlServerEcorexDbContext con MigrationsAssembly propio
/// (Ecorex.Infrastructure.SqlServer), igual que DependencyInjection en produccion.
/// </summary>
public sealed class SqlServerTenantIsolationFixture : TenantIsolationDbFixture
{
    private readonly MsSqlContainer _db = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    protected override Task StartContainerAsync() => _db.StartAsync();

    public override async Task DisposeAsync() => await _db.DisposeAsync();

    public override EcorexDbContext CreateContext(Guid? tenantId)
    {
        var tenantContext = new FixedTenantContext(tenantId);
        var options = new DbContextOptionsBuilder<SqlServerEcorexDbContext>()
            .UseSqlServer(
                _db.GetConnectionString(),
                sql => sql.MigrationsAssembly("Ecorex.Infrastructure.SqlServer"))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new AuditableTenantInterceptor(tenantContext, TimeProvider.System))
            .Options;

        return new SqlServerEcorexDbContext(options, tenantContext);
    }
}
