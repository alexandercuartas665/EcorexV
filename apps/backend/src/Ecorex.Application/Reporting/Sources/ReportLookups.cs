using Ecorex.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Reporting.Sources;

/// <summary>
/// Catalogos id -> nombre compartidos por las fuentes nativas (ADR-0158). Todas las tablas llevan el
/// filtro global tenant, asi que los diccionarios quedan acotados al tenant activo del contexto.
/// </summary>
public static class ReportLookups
{
    /// <summary>Nombre para mostrar del usuario del tenant: DisplayName de la plataforma o, si no, el correo.</summary>
    public static async Task<Dictionary<Guid, string>> UsersAsync(IApplicationDbContext db, CancellationToken ct)
    {
        var users = await (
            from u in db.TenantUsers.AsNoTracking()
            join p in db.PlatformUsers.AsNoTracking() on u.PlatformUserId equals p.Id into pj
            from p in pj.DefaultIfEmpty()
            select new { u.Id, Name = (p != null ? p.DisplayName : null) ?? u.Email })
            .ToListAsync(ct);
        return users.ToDictionary(x => x.Id, x => x.Name);
    }

    public static async Task<Dictionary<Guid, string>> BoardsAsync(IApplicationDbContext db, CancellationToken ct) =>
        (await db.TaskBoards.AsNoTracking().Select(b => new { b.Id, b.Name }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.Name);

    public static async Task<Dictionary<Guid, string>> ProjectsAsync(IApplicationDbContext db, CancellationToken ct) =>
        (await db.Projects.AsNoTracking().Select(p => new { p.Id, p.Name }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.Name);

    /// <summary>Terceros por id: nombre, NIT normalizado y vendedor (para cruzar desde tareas/horas).</summary>
    public static async Task<Dictionary<Guid, TerceroRef>> TercerosAsync(IApplicationDbContext db, CancellationToken ct) =>
        (await db.Terceros.AsNoTracking().Select(t => new { t.Id, t.Nombre, t.IdValor, t.Vendedor }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => new TerceroRef(x.Nombre, NitNormalizer.Normalize(x.IdValor), x.Vendedor));
}

public sealed record TerceroRef(string Nombre, string? Nit, string? Vendedor);

/// <summary>
/// Normaliza un NIT/identificacion para cruzar fuentes (ADR-0158 D10): quita el digito de verificacion
/// (lo que va despues del guion), espacios, puntos y cualquier caracter no alfanumerico; mayusculas.
/// "900.123.456-7" -> "900123456". Null/vacio -> null.
/// </summary>
public static class NitNormalizer
{
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var s = raw.Trim();
        var dash = s.IndexOf('-');
        if (dash > 0)
        {
            s = s[..dash];
        }

        var chars = s.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray();
        return chars.Length == 0 ? null : new string(chars);
    }
}
