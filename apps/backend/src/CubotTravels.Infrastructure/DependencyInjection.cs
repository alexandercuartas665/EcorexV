using CubotTravels.Application.Common;
using CubotTravels.Application.Common.Auth;
using CubotTravels.Infrastructure.Auth;
using CubotTravels.Infrastructure.Persistence;
using CubotTravels.Infrastructure.Persistence.Interceptors;
using CubotTravels.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CubotTravels.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = Environment.GetEnvironmentVariable("CUBOT_DB_CONNECTION");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Cadena de conexion 'Default' no configurada (usa ConnectionStrings:Default o CUBOT_DB_CONNECTION).");
        }

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AuditableTenantInterceptor>();

        services.AddDbContext<CubotTravelsDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString)
                   .UseSnakeCaseNamingConvention()
                   .AddInterceptors(sp.GetRequiredService<AuditableTenantInterceptor>());
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<CubotTravelsDbContext>());

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        // Llaves de Data Protection compartidas en la base de datos + nombre de aplicacion comun,
        // para que cualquier app (Api, SuperAdmin, Workers) descifre los secretos cifrados por otra.
        services.AddDataProtection()
            .SetApplicationName("CubotTravels")
            .PersistKeysToDbContext<CubotTravelsDbContext>();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        // Correo saliente via SMTP configurable por el Super Admin (clave cifrada).
        services.AddScoped<Application.Common.IEmailSender, Email.SmtpEmailSender>();
        services.AddHttpClient<CubotTravels.Application.Admin.IWompiApiClient, Wompi.WompiApiClient>();
        services.AddHttpClient<CubotTravels.Application.Admin.IEvolutionApiClient, Evolution.EvolutionApiClient>();
        services.AddHttpClient<CubotTravels.Application.Tenancy.IAiProviderClient, Ai.AiProviderClient>();
        services.AddHttpClient<CubotTravels.Application.Auth.IGoogleOAuthClient, Auth.GoogleOAuthClient>();
        services.AddScoped<DatabaseSeeder>();

        // Comprobantes PDF (QuestPDF). Licencia Community: gratis para empresas con ingresos < USD 1M/ano.
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        services.AddScoped<Application.Common.IReceiptPdfRenderer, Pdf.QuestPdfReceiptRenderer>();

        return services;
    }
}
