using Ecorex.Application.Common;

namespace Ecorex.Infrastructure.Persistence;

/// <summary>
/// Implementacion de <see cref="IApplicationDbContextFactory"/>. Se registra SCOPED (ver
/// DependencyInjection) con una fabrica que construye el contexto del proveedor ACTIVO
/// (EcorexDbContext para Postgres, SqlServerEcorexDbContext para SQL Server) usando las
/// <c>DbContextOptions</c> del scope y el <c>ITenantContext</c> del circuito. Asi cada
/// contexto creado es una instancia NUEVA (sin colisiones de concurrencia) pero con el MISMO
/// filtro global multi-tenant que el contexto compartido.
/// </summary>
public sealed class ApplicationDbContextFactory : IApplicationDbContextFactory
{
    private readonly Func<IScopedDbContext> _create;

    public ApplicationDbContextFactory(Func<IScopedDbContext> create) => _create = create;

    public IScopedDbContext Create() => _create();
}
