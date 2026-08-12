using System.Globalization;
using System.Text;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Directorio;

/// <summary>
/// Implementacion de ITerceroFichaService. Fichas del Directorio General configurables por tenant.
/// Aislamiento por el filtro global (nunca se filtra a mano por TenantId). Las 5 por defecto se
/// siembran idempotentemente.
/// </summary>
public sealed class TerceroFichaService : ITerceroFichaService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public TerceroFichaService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    // Fichas por defecto (del prototipo 000232): clave, titulo, descripcion, color, perfil visible.
    // Perfil null = siempre visible; "cliente"/"proveedor"/"empleado" = solo con ese perfil.
    // Perfil = lista separada por comas de perfiles que la hacen visible (vacio/null = siempre).
    // Reproduce el mapeo original: fiscal para cliente/proveedor, comercial para cliente/sospechoso.
    private static readonly (string Key, string Title, string Desc, string Color, string? Perfil)[] Defaults =
    [
        ("fiscal",    "Datos fiscales",     "Datos tributarios y de facturacion", "#B45309", "cliente,proveedor"),
        ("comercial", "Datos comerciales",  "Vendedor, origen y gestion de riesgo", "#7C3AED", "cliente,sospechoso"),
        ("cliente",   "Ficha de cliente",   "Condiciones comerciales de venta",   "#1D7A4A", "cliente"),
        ("proveedor", "Ficha de proveedor", "Condiciones de compra y pago",       "#2563EB", "proveedor"),
        ("empleado",  "Ficha de empleado",  "Vinculacion laboral",                "#BE123C", "empleado"),
    ];

    /// <summary>Construye las fichas por defecto para un tenant (usado por EnsureDefaults y el seeder).</summary>
    public static IReadOnlyList<TerceroFichaDefinition> BuildDefaultFichas(Guid tenantId)
    {
        var order = 0;
        return Defaults.Select(d => new TerceroFichaDefinition
        {
            TenantId = tenantId,
            FichaKey = d.Key,
            Title = d.Title,
            Description = d.Desc,
            Color = d.Color,
            Perfil = d.Perfil,
            SortOrder = order++,
            IsSystem = true
        }).ToList();
    }

    public async Task EnsureDefaultsAsync(CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return; }
        if (await _db.TerceroFichaDefinitions.AnyAsync(cancellationToken)) { return; }
        _db.TerceroFichaDefinitions.AddRange(BuildDefaultFichas(tenantId));
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TerceroFichaDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(cancellationToken);
        return await _db.TerceroFichaDefinitions.AsNoTracking()
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Title)
            .Select(f => new TerceroFichaDto(f.Id, f.FichaKey, f.Title, f.Description, f.Color, f.Perfil, f.SortOrder, f.IsSystem, f.IsHidden))
            .ToListAsync(cancellationToken);
    }

    public async Task<TerceroFichaDto?> CreateAsync(string title, string? color, string? perfil, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        var name = (title ?? string.Empty).Trim();
        if (name.Length is 0 or > 80) { return null; }

        var key = await UniqueKeyAsync(name, cancellationToken);
        var maxOrder = await _db.TerceroFichaDefinitions.Select(f => (int?)f.SortOrder).MaxAsync(cancellationToken) ?? -1;
        var entity = new TerceroFichaDefinition
        {
            TenantId = tenantId,
            FichaKey = key,
            Title = name,
            Color = string.IsNullOrWhiteSpace(color) ? null : color.Trim(),
            Perfil = NormalizePerfil(perfil),
            SortOrder = maxOrder + 1,
            IsSystem = false
        };
        _db.TerceroFichaDefinitions.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return new TerceroFichaDto(entity.Id, entity.FichaKey, entity.Title, entity.Description, entity.Color, entity.Perfil, entity.SortOrder, entity.IsSystem, entity.IsHidden);
    }

    public async Task<string?> UpdateAsync(Guid id, string title, string? color, string? perfil, CancellationToken cancellationToken = default)
    {
        var f = await _db.TerceroFichaDefinitions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (f is null) { return "La ficha no existe."; }
        var name = (title ?? string.Empty).Trim();
        if (name.Length is 0 or > 80) { return "El nombre es obligatorio (maximo 80)."; }
        f.Title = name;
        f.Color = string.IsNullOrWhiteSpace(color) ? null : color.Trim();
        f.Perfil = NormalizePerfil(perfil);
        await _db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> SetHiddenAsync(Guid id, bool hidden, CancellationToken cancellationToken = default)
    {
        var f = await _db.TerceroFichaDefinitions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (f is null) { return "La ficha no existe."; }
        f.IsHidden = hidden;
        await _db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var f = await _db.TerceroFichaDefinitions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (f is null) { return "La ficha no existe."; }
        if (f.IsSystem) { return "Las fichas de sistema no se pueden eliminar."; }
        var hasFields = await _db.TerceroFieldDefinitions.AnyAsync(x => x.FichaKey == f.FichaKey, cancellationToken);
        if (hasFields) { return "La ficha tiene campos: muevelos o borralos antes de eliminarla."; }
        _db.TerceroFichaDefinitions.Remove(f);
        await _db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<bool> ReorderAsync(Guid id, bool up, CancellationToken cancellationToken = default)
    {
        var all = await _db.TerceroFichaDefinitions.OrderBy(f => f.SortOrder).ThenBy(f => f.Title).ToListAsync(cancellationToken);
        var idx = all.FindIndex(f => f.Id == id);
        if (idx < 0) { return false; }
        var swap = up ? idx - 1 : idx + 1;
        if (swap < 0 || swap >= all.Count) { return false; }
        (all[idx].SortOrder, all[swap].SortOrder) = (all[swap].SortOrder, all[idx].SortOrder);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // Acepta uno o varios perfiles separados por coma; valida cada token y los rejunta. Vacio = siempre.
    private static readonly string[] ValidPerfiles = ["cliente", "proveedor", "empleado", "sospechoso"];
    private static string? NormalizePerfil(string? perfil)
    {
        if (string.IsNullOrWhiteSpace(perfil)) { return null; }
        var toks = perfil.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant()).Where(t => ValidPerfiles.Contains(t)).Distinct().ToList();
        return toks.Count == 0 ? null : string.Join(",", toks);
    }

    private async Task<string> UniqueKeyAsync(string title, CancellationToken cancellationToken)
    {
        var baseKey = Slugify(title);
        if (baseKey.Length == 0) { baseKey = "ficha"; }
        if (baseKey.Length > 34) { baseKey = baseKey[..34]; }
        var key = baseKey;
        var n = 1;
        while (await _db.TerceroFichaDefinitions.AnyAsync(f => f.FichaKey == key, cancellationToken))
        {
            n++;
            key = $"{baseKey}_{n}";
        }
        return key;
    }

    // Slug ASCII en minusculas con guion bajo (misma familia que las claves de campo).
    private static string Slugify(string text)
    {
        var norm = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(norm.Length);
        foreach (var ch in norm)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark) { continue; }
            if (char.IsLetterOrDigit(ch)) { sb.Append(char.ToLowerInvariant(ch)); }
            else if (ch is ' ' or '-' or '_' or '/') { sb.Append('_'); }
        }
        var s = sb.ToString();
        while (s.Contains("__")) { s = s.Replace("__", "_"); }
        return s.Trim('_');
    }
}
