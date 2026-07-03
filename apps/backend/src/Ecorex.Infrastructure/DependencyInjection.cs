using Ecorex.Application.Common;
using Ecorex.Application.Common.Auth;
using Ecorex.Infrastructure.Auth;
using Ecorex.Infrastructure.Persistence;
using Ecorex.Infrastructure.Persistence.Interceptors;
using Ecorex.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ecorex.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = Environment.GetEnvironmentVariable("ECOREX_DB_CONNECTION");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Cadena de conexion 'Default' no configurada (usa ConnectionStrings:Default o ECOREX_DB_CONNECTION).");
        }

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AuditableTenantInterceptor>();

        // DAL dual (ADR-001): proveedor elegible por configuracion. Database:Provider (o la
        // variable ECOREX_DB_PROVIDER) acepta "Postgres" (default) o "SqlServer". Los
        // consumidores siguen inyectando EcorexDbContext / IApplicationDbContext sin cambios.
        var provider = configuration["Database:Provider"];
        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = Environment.GetEnvironmentVariable("ECOREX_DB_PROVIDER");
        }
        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = "Postgres";
        }

        if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            services.AddDbContext<SqlServerEcorexDbContext>((sp, options) =>
            {
                options.UseSqlServer(
                            connectionString,
                            sql => sql.MigrationsAssembly("Ecorex.Infrastructure.SqlServer"))
                       .UseSnakeCaseNamingConvention()
                       .AddInterceptors(sp.GetRequiredService<AuditableTenantInterceptor>());
            });
            // EcorexDbContext se resuelve hacia el contexto SQL Server: mismos filtros
            // multi-tenant, mismas entidades, solo cambian proveedor y migraciones.
            services.AddScoped<EcorexDbContext>(sp => sp.GetRequiredService<SqlServerEcorexDbContext>());
        }
        else if (string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase))
        {
            services.AddDbContext<EcorexDbContext>((sp, options) =>
            {
                options.UseNpgsql(connectionString)
                       .UseSnakeCaseNamingConvention()
                       .AddInterceptors(sp.GetRequiredService<AuditableTenantInterceptor>());
            });
        }
        else
        {
            throw new InvalidOperationException(
                $"Proveedor de base de datos no soportado: '{provider}' (usa 'Postgres' o 'SqlServer').");
        }

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<EcorexDbContext>());

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        // Llaves de Data Protection compartidas en la base de datos + nombre de aplicacion comun,
        // para que cualquier app (Api, SuperAdmin, Workers) descifre los secretos cifrados por otra.
        services.AddDataProtection()
            .SetApplicationName("Ecorex")
            .PersistKeysToDbContext<EcorexDbContext>();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        // Correo saliente via SMTP configurable por el Super Admin (clave cifrada).
        services.AddScoped<Application.Common.IEmailSender, Email.SmtpEmailSender>();
        services.AddHttpClient<Ecorex.Application.Admin.IWompiApiClient, Wompi.WompiApiClient>();
        services.AddHttpClient<Ecorex.Application.Admin.IEvolutionApiClient, Evolution.EvolutionApiClient>();
        services.AddHttpClient<Ecorex.Application.Tenancy.IWhatsAppCloudClient, WhatsAppCloud.WhatsAppCloudClient>();
        services.AddHttpClient<Ecorex.Application.Tenancy.IAiProviderClient, Ai.AiProviderClient>();
        services.AddHttpClient<Ecorex.Application.Auth.IGoogleOAuthClient, Auth.GoogleOAuthClient>();
        services.AddScoped<DatabaseSeeder>();

        // Comprobantes PDF (QuestPDF). Licencia Community: gratis para empresas con ingresos < USD 1M/ano.
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        services.AddScoped<Application.Common.IReceiptPdfRenderer, Pdf.QuestPdfReceiptRenderer>();
        // PDF de cotizaciones desde HTML libre (Chromium headless via PuppeteerSharp).
        services.AddScoped<Application.Common.IQuotePdfRenderer, Rendering.PuppeteerQuotePdfRenderer>();

        return services;
    }
}
