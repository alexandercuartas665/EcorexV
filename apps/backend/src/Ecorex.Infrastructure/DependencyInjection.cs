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
        // Consola SQL admin (000077): ejecuta SQL crudo + audita en sql_console_logs.
        services.AddScoped<Ecorex.Application.Admin.ISqlConsoleService, Sql.SqlConsoleService>();
        services.AddHttpClient<Ecorex.Application.Admin.IWompiApiClient, Wompi.WompiApiClient>();
        services.AddHttpClient<Ecorex.Application.Admin.IEvolutionApiClient, Evolution.EvolutionApiClient>();
        services.AddHttpClient<Ecorex.Application.Tenancy.IWhatsAppCloudClient, WhatsAppCloud.WhatsAppCloudClient>();
        services.AddHttpClient<Ecorex.Application.Tenancy.IYCloudApiClient, YCloud.YCloudApiClient>();
        services.AddHttpClient<Ecorex.Application.Tenancy.IAiProviderClient, Ai.AiProviderClient>();
        services.AddHttpClient<Ecorex.Application.Auth.IGoogleOAuthClient, Auth.GoogleOAuthClient>();
        // Importacion manual desde API REST del Contenedor de datos (disparo por el usuario).
        services.AddHttpClient<Ecorex.Application.DataContainers.IApiImportService, Ecorex.Application.DataContainers.ApiImportService>(
                static client => client.Timeout = TimeSpan.FromSeconds(35));
        // Ejecutor de extraccion de datos (modulo 000730, ADR-0025): limites por defecto
        // SEGUROS (sin loopback, 15s, 2 MB, redirecciones re-validadas). La app host puede
        // re-registrar ScrapeGuardOptions DESPUES de AddInfrastructure para habilitar el
        // endpoint demo local SOLO en Development (la ultima registracion singleton gana).
        services.AddSingleton(new Ecorex.Application.Scraping.ScrapeGuardOptions());
        services.AddHttpClient<Ecorex.Application.Scraping.IScrapeFetcher, Ecorex.Application.Scraping.ScrapeHttpFetcher>(
                static client =>
                {
                    // Margen sobre el timeout logico del fetcher (15s), que es el que manda.
                    client.Timeout = TimeSpan.FromSeconds(30);
                })
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
            {
                // Las redirecciones se siguen A MANO en ScrapeHttpFetcher para re-validar
                // cada salto contra el guard SSRF. Sin cookies ni credenciales de ambiente.
                AllowAutoRedirect = false,
                UseCookies = false,
                Credentials = null
            });
        services.AddScoped<DatabaseSeeder>();
        // El seeder es el dueno del arbol canonico del menu: se expone como IMenuProvisioningService
        // para que el ALTA DE TENANTS (PlatformAdmin y onboarding) siembre la vista "Completo" y
        // ningun cliente nazca sin menu. Misma instancia scoped -> misma transaccion del alta.
        services.AddScoped<Ecorex.Application.MenuConfig.IMenuProvisioningService>(
            sp => sp.GetRequiredService<DatabaseSeeder>());

        // Comprobantes PDF (QuestPDF). Licencia Community: gratis para empresas con ingresos < USD 1M/ano.
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        services.AddScoped<Application.Common.IReceiptPdfRenderer, Pdf.QuestPdfReceiptRenderer>();
        // PDF de cotizaciones desde HTML libre (Chromium headless via PuppeteerSharp).
        services.AddScoped<Application.Common.IQuotePdfRenderer, Rendering.PuppeteerQuotePdfRenderer>();

        return services;
    }
}
