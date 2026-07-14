using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Ecorex.Api.Auth;
using Ecorex.Api.Endpoints;
using Ecorex.Application;
using Ecorex.Application.Common;
using Ecorex.Application.Common.Auth;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();

// Configuracion JWT. El SigningKey no se versiona: en Development se genera una clave
// efimera por arranque si falta; en otros entornos es obligatorio.
var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();
if (string.IsNullOrWhiteSpace(jwtSettings.SigningKey))
{
    if (builder.Environment.IsDevelopment())
    {
        jwtSettings.SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }
    else
    {
        throw new InvalidOperationException("Jwt:SigningKey es obligatorio fuera de Development.");
    }
}

builder.Services.Configure<JwtSettings>(options =>
{
    options.Issuer = jwtSettings.Issuer;
    options.Audience = jwtSettings.Audience;
    options.SigningKey = jwtSettings.SigningKey;
    options.AccessTokenMinutes = jwtSettings.AccessTokenMinutes;
});

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddScoped<ITenantContext, HttpContextTenantContext>();
builder.Services.AddSingleton<Ecorex.Application.Tenancy.IAgentAssetReader, Ecorex.Api.Auth.NullAgentAssetReader>();
// La difusion en vivo de registros de formulario es SignalR y vive en la consola Blazor; este host
// no tiene hub, asi que se registra el no-op (mismo patron que NoOpTaskBroadcaster). Sin esto el
// contenedor de DI no puede construir FormResponseService y la API entera no arranca.
builder.Services.AddSingleton<Ecorex.Application.Tenancy.IFormRecordBroadcaster,
    Ecorex.Application.Tenancy.NoOpFormRecordBroadcaster>();

JwtSecurityTokenHandler.DefaultMapInboundClaims = false;
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SigningKey)),
            NameClaimType = "sub"
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SuperAdminOnly", policy =>
        policy.RequireClaim("platform_role", nameof(PlatformRole.SuperAdmin)));
    options.AddPolicy("TenantMember", policy =>
        policy.RequireClaim("tenant_id"));
    options.AddPolicy("TenantAdmin", policy =>
        policy.RequireAssertion(ctx =>
        {
            if (ctx.User.FindFirst("tenant_id") is null)
            {
                return false;
            }

            var role = ctx.User.FindFirst("tenant_role")?.Value;
            return role == nameof(TenantRole.Owner) || role == nameof(TenantRole.Admin);
        }));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Auto-migracion + seed solo en local. El flag permite desactivarlo (p.ej. en tests).
    if (app.Configuration.GetValue("Database:AutoMigrate", true))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Ecorex.Infrastructure.Persistence.EcorexDbContext>();
        await db.Database.MigrateAsync();
        var seeder = scope.ServiceProvider.GetRequiredService<Ecorex.Infrastructure.Persistence.DatabaseSeeder>();
        await seeder.SeedAsync();
        // Nucleo de tareas/proyectos demo (FASE 3, ADR-0013). Idempotente, solo Development.
        await seeder.EnsureTaskCoreDemoAsync();
    }
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapConnectEndpoints();
app.MapAdminEndpoints();
app.MapTenantEndpoints();
app.MapChatEndpoints();
app.MapWompiEndpoints();

app.Run();

public partial class Program;
