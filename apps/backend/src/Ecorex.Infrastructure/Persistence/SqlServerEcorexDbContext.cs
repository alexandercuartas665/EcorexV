using Ecorex.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Infrastructure.Persistence;

/// <summary>
/// Contexto por proveedor para SQL Server (ADR-001: DAL dual PostgreSQL/SQL Server).
/// Existe UNICAMENTE para separar las migraciones por motor: sus migraciones viven en el
/// ensamblado Ecorex.Infrastructure.SqlServer (ver MigrationsAssembly en DependencyInjection
/// y en SqlServerEcorexDbContextFactory). No agrega ni cambia configuracion de modelo:
/// las diferencias por proveedor se resuelven en EcorexDbContext.OnModelCreating via
/// Database.IsNpgsql(), y el HasQueryFilter global multi-tenant se hereda intacto.
/// Los consumidores siguen inyectando EcorexDbContext / IApplicationDbContext; cuando
/// Database:Provider=SqlServer, el contenedor resuelve EcorexDbContext hacia esta clase.
/// La clase vive en este ensamblado (y no en Ecorex.Infrastructure.SqlServer) porque el
/// registro en DependencyInjection necesita el tipo en compilacion y el proyecto de
/// migraciones referencia a este (patron multi-provider de la documentacion de EF Core).
/// </summary>
public sealed class SqlServerEcorexDbContext : EcorexDbContext
{
    public SqlServerEcorexDbContext(DbContextOptions<SqlServerEcorexDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext)
    {
    }
}
