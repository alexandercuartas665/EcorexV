using Ecorex.Application.Common;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ecorex.Infrastructure.SqlServer;

/// <summary>
/// Factory para herramientas de diseno (dotnet ef) del proveedor SQL Server. Permite crear
/// el contexto sin levantar la aplicacion. La cadena real se toma de la variable de entorno
/// ECOREX_SQLSERVER_DB_CONNECTION (dedicada al motor para no chocar con ECOREX_DB_CONNECTION,
/// que suele apuntar a Postgres en dev); el fallback es solo un default local de desarrollo,
/// igual que hace EcorexDbContextFactory con Postgres.
/// TODO(KeyVault): en entornos reales la cadena debe venir de un secreto gestionado
/// (Azure Key Vault / variables de la plataforma), nunca versionada.
/// </summary>
public sealed class SqlServerEcorexDbContextFactory : IDesignTimeDbContextFactory<SqlServerEcorexDbContext>
{
    public SqlServerEcorexDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ECOREX_SQLSERVER_DB_CONNECTION")
            ?? "Server=localhost,1443;Database=ecorex_dev;User Id=sa;Password=EcorexDev2026sql;TrustServerCertificate=true";

        var options = new DbContextOptionsBuilder<SqlServerEcorexDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly("Ecorex.Infrastructure.SqlServer"))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new SqlServerEcorexDbContext(options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
        public Guid? UserId => null;
    }
}
