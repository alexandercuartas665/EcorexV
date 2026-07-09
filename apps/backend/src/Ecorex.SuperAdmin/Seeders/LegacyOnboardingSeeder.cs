using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Ecorex.SuperAdmin.Auth;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Seeders;

/// <summary>
/// Onboarding one-shot desde la BD legacy (db3dev, SQL Server, SOLO LECTURA): crea tenants
/// cliente y da de alta a sus usuarios con la logica REAL de la app (IOnboardingService crea
/// el tenant con su menu/roles; ITenantUserService.InviteAsync crea el usuario Active con clave
/// hasheada PBKDF2). Idempotente: omite tenants/usuarios que ya existen.
///
/// Datos NO versionados (el repo es publico; hay cedulas/PII):
///   - Db3dev:Connection            -> cadena SQL Server de solo lectura a db3dev.
///   - Onboarding:Agro:Email/Name/Cedula -> el usuario semilla de agrometalicas.
/// Se dispara con la env var ECOREX_RUN_ONBOARDING=true al arrancar (ver Program.cs).
///
/// Mapeo (decidido con el usuario): SUCURSAL '01' -> tenant "BITCODE" (nuevo),
/// SUCURSAL '00136' -> tenant "SKY SYSTEM" (existente). Todos rol Owner, login por email,
/// clave = ID_USUARIO (cedula). Correos duplicados: se crea el primero y se omite el resto.
/// </summary>
public sealed class LegacyOnboardingSeeder
{
    private readonly IApplicationDbContext _db;
    private readonly IOnboardingService _onboarding;
    private readonly ITenantUserService _tenantUsers;
    private readonly IConfiguration _config;
    private readonly ILogger<LegacyOnboardingSeeder> _log;

    public LegacyOnboardingSeeder(
        IApplicationDbContext db,
        IOnboardingService onboarding,
        ITenantUserService tenantUsers,
        IConfiguration config,
        ILogger<LegacyOnboardingSeeder> log)
    {
        _db = db;
        _onboarding = onboarding;
        _tenantUsers = tenantUsers;
        _config = config;
        _log = log;
    }

    private sealed record UserRow(string Nombre, string DisplayName, string Email, string Cedula, string Sucursal);

    public sealed record Report(List<string> Creados, List<string> Omitidos, List<string> Errores);

    public async Task<Report> RunAsync(CancellationToken ct = default)
    {
        var report = new Report(new(), new(), new());

        var actor = await _db.PlatformUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.PlatformRole == PlatformRole.SuperAdmin, ct);
        if (actor is null)
        {
            report.Errores.Add("No hay Super Admin (actor) en la BD; no se puede auditar el alta.");
            return report;
        }
        var actorId = actor.Id;

        var db3conn = _config["Db3dev:Connection"] ?? Environment.GetEnvironmentVariable("DB3DEV_CONNECTION");
        if (string.IsNullOrWhiteSpace(db3conn))
        {
            report.Errores.Add("Falta Db3dev:Connection (o env DB3DEV_CONNECTION). Nada que leer.");
            return report;
        }

        // 1) Leer usuarios legacy (SOLO LECTURA) de las dos sucursales objetivo.
        var rows = new List<UserRow>();
        try
        {
            await using var conn = new SqlConnection(db3conn);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT NOMBRE, ISNULL(NOMBR_PER,''), ISNULL(EMAIL,''), ISNULL(ID_USUARIO,''), SUCURSAL " +
                "FROM USUARIO WHERE SUCURSAL IN ('01','00136') ORDER BY SUCURSAL, NOMBRE";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                rows.Add(new UserRow(
                    r.GetString(0).Trim(), r.GetString(1).Trim(), r.GetString(2).Trim().ToLowerInvariant(),
                    r.GetString(3).Trim(), r.GetString(4).Trim()));
            }
        }
        catch (Exception ex)
        {
            report.Errores.Add($"Error leyendo db3dev: {ex.Message}");
            return report;
        }
        _log.LogWarning("[onboarding] leidos {N} usuarios de db3dev (sucursal 01 + 00136).", rows.Count);

        // Correo unico en TODO el lote (el login es el email): se crea el primero, se omite el resto.
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var bitcode = rows.Where(x => x.Sucursal == "01").ToList();
        var sky = rows.Where(x => x.Sucursal == "00136").ToList();

        await OnboardGroupAsync("BITCODE", bitcode, actorId, seenEmails, createTenant: true, report, ct);
        await OnboardGroupAsync("SKY SYSTEM", sky, actorId, seenEmails, createTenant: false, report, ct);

        // agrometalicas (dato directo del usuario, tambien fuera del repo).
        var agroEmail = (_config["Onboarding:Agro:Email"] ?? "").Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(agroEmail))
        {
            var agro = new List<UserRow>
            {
                new("Gustavo", _config["Onboarding:Agro:Name"] ?? agroEmail, agroEmail, _config["Onboarding:Agro:Cedula"] ?? "", "AGRO")
            };
            await OnboardGroupAsync("agrometalicas", agro, actorId, seenEmails, createTenant: true, report, ct);
        }

        _log.LogWarning("[onboarding] DONE. Creados={C} Omitidos={O} Errores={E}",
            report.Creados.Count, report.Omitidos.Count, report.Errores.Count);
        return report;
    }

    private async Task OnboardGroupAsync(
        string tenantName, List<UserRow> users, Guid actorId, HashSet<string> seenEmails,
        bool createTenant, Report report, CancellationToken ct)
    {
        // Filtra: correo presente y no duplicado en el lote.
        var valid = new List<UserRow>();
        foreach (var u in users)
        {
            if (string.IsNullOrWhiteSpace(u.Email))
            {
                report.Omitidos.Add($"{tenantName}: '{u.Nombre}' sin correo.");
                continue;
            }
            if (!seenEmails.Add(u.Email))
            {
                report.Omitidos.Add($"{tenantName}: '{u.Nombre}' correo duplicado {u.Email} (se omite).");
                continue;
            }
            valid.Add(u);
        }
        if (valid.Count == 0)
        {
            report.Omitidos.Add($"{tenantName}: sin usuarios validos.");
            return;
        }

        var existing = await _db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(t => t.Name == tenantName, ct);

        Guid tenantId;
        if (existing is not null)
        {
            tenantId = existing.Id;
        }
        else if (createTenant)
        {
            var first = valid[0];
            var outcome = await _onboarding.OnboardAsync(
                new OnboardTenantRequest(tenantName, first.Email, PasswordFor(first), NameOrNull(first.DisplayName)),
                actorId, ct);
            if (!outcome.Success || outcome.Result is null)
            {
                report.Errores.Add($"{tenantName}: no se pudo crear el tenant: {outcome.Error}");
                return;
            }
            tenantId = outcome.Result.TenantId;
            report.Creados.Add($"{tenantName}: tenant creado + Owner {first.Email}");
            valid.RemoveAt(0); // el admin ya quedo creado por OnboardAsync
        }
        else
        {
            report.Errores.Add($"{tenantName}: el tenant no existe y createTenant=false. Se omite el grupo.");
            return;
        }

        using (AmbientTenantContext.Begin(tenantId))
        {
            foreach (var u in valid)
            {
                try
                {
                    var res = await _tenantUsers.InviteAsync(
                        new InviteTenantUserRequest(u.Email, TenantRole.Owner, PasswordFor(u), NameOrNull(u.DisplayName)),
                        actorId, ct);
                    if (res is null)
                    {
                        report.Omitidos.Add($"{tenantName}: {u.Email} ya era miembro (o sin tenant activo).");
                    }
                    else
                    {
                        report.Creados.Add($"{tenantName}: {u.Email}");
                    }
                }
                catch (Exception ex)
                {
                    report.Errores.Add($"{tenantName}: {u.Email} fallo: {ex.Message}");
                }
            }
        }
    }

    // Clave = cedula (ID_USUARIO). Si por algun motivo viniera vacia o < 6, usa una temporal segura.
    private static string PasswordFor(UserRow u)
        => u.Cedula.Trim().Length >= 6 ? u.Cedula.Trim() : "Cambiar-2026*";

    private static string? NameOrNull(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
