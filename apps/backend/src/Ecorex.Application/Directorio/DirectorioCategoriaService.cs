using System.Globalization;
using System.Text;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Directorio;

/// <summary>
/// Implementacion de <see cref="IDirectorioCategoriaService"/> (2do motor de contactos, Capa 8).
/// Aislamiento por el filtro global (nunca se filtra a mano por TenantId; el TenantId se estampa al
/// crear). Opera solo sobre las tablas del motor Modular (categorias, composicion y membership);
/// el motor Clasico no se toca.
/// </summary>
public sealed class DirectorioCategoriaService : IDirectorioCategoriaService
{
    private readonly IDirectorioModularDbContext _db;
    private readonly IApplicationDbContext _app;
    private readonly ITenantContext _tenant;

    public DirectorioCategoriaService(IDirectorioModularDbContext db, IApplicationDbContext app, ITenantContext tenant)
    {
        _db = db;
        _app = app;
        _tenant = tenant;
    }

    public async Task EnsureDefaultsAsync(CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return; }
        // Idempotente: si ya hay categorias Modular, no re-siembra.
        if (await _db.DirectorioCategorias.AnyAsync(cancellationToken)) { return; }

        // 1) Secciones -> TerceroFichaDefinition con clave prefijada "mod_" (separacion del Clasico).
        var secOrder = 0;
        foreach (var s in DirectorioModularDefaults.Secciones)
        {
            _app.TerceroFichaDefinitions.Add(new TerceroFichaDefinition
            {
                TenantId = tenantId,
                FichaKey = DirectorioModularDefaults.SeccionKey(s.Key),
                Title = s.Title,
                Description = s.Descripcion,
                Icono = s.Icono,
                Color = s.Color,
                AplicaA = s.AplicaA,
                Areas = s.Areas,
                Protegida = s.Protegida,
                SortOrder = secOrder++,
                IsSystem = true
            });
        }

        // 2) Campos -> TerceroFieldDefinition (FichaKey = clave de seccion prefijada).
        var fieldOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var c in DirectorioModularDefaults.Campos)
        {
            var fk = DirectorioModularDefaults.SeccionKey(c.Seccion);
            var so = fieldOrder.TryGetValue(fk, out var v) ? v : 0;
            fieldOrder[fk] = so + 1;
            _app.TerceroFieldDefinitions.Add(new TerceroFieldDefinition
            {
                TenantId = tenantId,
                FichaKey = fk,
                FieldKey = c.Key,
                Label = c.Label,
                FieldType = c.Type,
                Column = c.Column,
                Options = c.Options,
                Description = c.Descripcion,
                RequeridoEn = c.RequeridoEn,
                ReadOnly = c.ReadOnly,
                ShowInFilter = c.ShowInFilter,
                SortOrder = so,
                IsSystem = true
            });
        }

        // 3) Categorias + composicion (tablas propias del motor Modular).
        var catOrder = 0;
        foreach (var cat in DirectorioModularDefaults.Categorias)
        {
            _db.DirectorioCategorias.Add(new DirectorioCategoria
            {
                TenantId = tenantId,
                CategoriaKey = cat.Key,
                Title = cat.Title,
                Icono = cat.Icono,
                Color = cat.Color,
                Areas = cat.Areas,
                Protegido = cat.Protegido,
                HomologaSeccion = cat.HomologaSeccion is null ? null : DirectorioModularDefaults.SeccionKey(cat.HomologaSeccion),
                SortOrder = catOrder++,
                IsSystem = true
            });
            var compOrder = 0;
            foreach (var sk in cat.Secciones)
            {
                _db.DirectorioCategoriaSecciones.Add(new DirectorioCategoriaSeccion
                {
                    TenantId = tenantId,
                    CategoriaKey = cat.Key,
                    FichaKey = DirectorioModularDefaults.SeccionKey(sk),
                    Orden = compOrder++
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DirectorioCategoriaDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(cancellationToken);
        return await _db.DirectorioCategorias.AsNoTracking()
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Title)
            .Select(c => Map(c))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, int>> CountByCategoriaAsync(CancellationToken cancellationToken = default)
    {
        var pares = await _db.TerceroCategorias.AsNoTracking()
            .GroupBy(t => t.CategoriaKey)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return pares.ToDictionary(p => p.Key, p => p.Count, StringComparer.Ordinal);
    }

    public async Task<IReadOnlyDictionary<Guid, List<string>>> MembershipsAsync(CancellationToken cancellationToken = default)
    {
        var pares = await _db.TerceroCategorias.AsNoTracking()
            .Select(t => new { t.TerceroId, t.CategoriaKey })
            .ToListAsync(cancellationToken);
        return pares.GroupBy(p => p.TerceroId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.CategoriaKey).ToList());
    }

    public async Task<DirectorioCategoriaDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var c = await _db.DirectorioCategorias.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return c is null ? null : Map(c);
    }

    public async Task<IReadOnlyList<DirectorioCategoriaSeccionDto>> GetSeccionesAsync(string categoriaKey, CancellationToken cancellationToken = default)
    {
        var key = (categoriaKey ?? string.Empty).Trim();
        return await _db.DirectorioCategoriaSecciones.AsNoTracking()
            .Where(s => s.CategoriaKey == key)
            .OrderBy(s => s.Orden)
            .Select(s => new DirectorioCategoriaSeccionDto(s.FichaKey, s.Orden))
            .ToListAsync(cancellationToken);
    }

    public async Task<DirectorioCategoriaDto?> CreateAsync(CreateDirectorioCategoriaRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        var name = (request.Title ?? string.Empty).Trim();
        if (name.Length is 0 or > 80) { return null; }

        var key = await UniqueKeyAsync(name, cancellationToken);
        var maxOrder = await _db.DirectorioCategorias.Select(c => (int?)c.SortOrder).MaxAsync(cancellationToken) ?? -1;
        var entity = new DirectorioCategoria
        {
            TenantId = tenantId,
            CategoriaKey = key,
            Title = name,
            Icono = Clean(request.Icono),
            Color = Clean(request.Color),
            Areas = NormalizeAreas(request.Areas),
            Description = Clean(request.Description),
            SortOrder = maxOrder + 1,
            IsSystem = false
        };
        _db.DirectorioCategorias.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<string?> UpdateAsync(Guid id, UpdateDirectorioCategoriaRequest request, CancellationToken cancellationToken = default)
    {
        var c = await _db.DirectorioCategorias.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (c is null) { return "La categoria no existe."; }
        var name = (request.Title ?? string.Empty).Trim();
        if (name.Length is 0 or > 80) { return "El nombre es obligatorio (maximo 80)."; }

        c.Title = name;
        c.Icono = Clean(request.Icono);
        c.Color = Clean(request.Color);
        c.Areas = NormalizeAreas(request.Areas);
        c.Description = Clean(request.Description);
        c.HomologaSeccion = Clean(request.HomologaSeccion);
        c.IsHidden = request.IsHidden;
        await _db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var c = await _db.DirectorioCategorias.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (c is null) { return "La categoria no existe."; }
        if (c.Protegido) { return "Esta categoria esta protegida y no se puede eliminar."; }
        if (c.IsSystem) { return "Las categorias de sistema no se pueden eliminar."; }
        var total = await _db.DirectorioCategorias.CountAsync(cancellationToken);
        if (total <= 1) { return "Debe existir al menos una categoria."; }

        // Se lleva su composicion de secciones; las pertenencias de terceros a esta categoria tambien.
        var secciones = await _db.DirectorioCategoriaSecciones.Where(s => s.CategoriaKey == c.CategoriaKey).ToListAsync(cancellationToken);
        _db.DirectorioCategoriaSecciones.RemoveRange(secciones);
        var membresias = await _db.TerceroCategorias.Where(t => t.CategoriaKey == c.CategoriaKey).ToListAsync(cancellationToken);
        _db.TerceroCategorias.RemoveRange(membresias);
        _db.DirectorioCategorias.Remove(c);
        await _db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> SetSeccionesAsync(string categoriaKey, IReadOnlyList<string> fichaKeysEnOrden, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return "No hay tenant activo."; }
        var key = (categoriaKey ?? string.Empty).Trim();
        var existeCategoria = await _db.DirectorioCategorias.AnyAsync(c => c.CategoriaKey == key, cancellationToken);
        if (!existeCategoria) { return "La categoria no existe."; }

        var actuales = await _db.DirectorioCategoriaSecciones.Where(s => s.CategoriaKey == key).ToListAsync(cancellationToken);
        _db.DirectorioCategoriaSecciones.RemoveRange(actuales);

        var orden = 0;
        var vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fk in fichaKeysEnOrden ?? Array.Empty<string>())
        {
            var f = (fk ?? string.Empty).Trim();
            if (f.Length == 0 || !vistas.Add(f)) { continue; }
            _db.DirectorioCategoriaSecciones.Add(new DirectorioCategoriaSeccion
            {
                TenantId = tenantId,
                CategoriaKey = key,
                FichaKey = f,
                Orden = orden++
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<bool> ReorderAsync(Guid id, bool up, CancellationToken cancellationToken = default)
    {
        var all = await _db.DirectorioCategorias.OrderBy(c => c.SortOrder).ThenBy(c => c.Title).ToListAsync(cancellationToken);
        var idx = all.FindIndex(c => c.Id == id);
        if (idx < 0) { return false; }
        var swap = up ? idx - 1 : idx + 1;
        if (swap < 0 || swap >= all.Count) { return false; }
        (all[idx].SortOrder, all[swap].SortOrder) = (all[swap].SortOrder, all[idx].SortOrder);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<string?> AsignarTerceroAsync(Guid terceroId, string categoriaKey, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return "No hay tenant activo."; }
        var key = (categoriaKey ?? string.Empty).Trim();
        if (!await _app.Terceros.AnyAsync(t => t.Id == terceroId, cancellationToken)) { return "El tercero no existe."; }
        if (!await _db.DirectorioCategorias.AnyAsync(c => c.CategoriaKey == key, cancellationToken)) { return "La categoria no existe."; }

        var yaEsta = await _db.TerceroCategorias.AnyAsync(t => t.TerceroId == terceroId && t.CategoriaKey == key, cancellationToken);
        if (yaEsta) { return null; } // idempotente

        _db.TerceroCategorias.Add(new TerceroCategoria { TenantId = tenantId, TerceroId = terceroId, CategoriaKey = key });
        await _db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> QuitarTerceroAsync(Guid terceroId, string categoriaKey, CancellationToken cancellationToken = default)
    {
        var key = (categoriaKey ?? string.Empty).Trim();
        var row = await _db.TerceroCategorias.FirstOrDefaultAsync(t => t.TerceroId == terceroId && t.CategoriaKey == key, cancellationToken);
        if (row is null) { return null; } // idempotente
        _db.TerceroCategorias.Remove(row);
        await _db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<IReadOnlyList<string>> CategoriasDeTerceroAsync(Guid terceroId, CancellationToken cancellationToken = default)
    {
        return await _db.TerceroCategorias.AsNoTracking()
            .Where(t => t.TerceroId == terceroId)
            .Select(t => t.CategoriaKey)
            .ToListAsync(cancellationToken);
    }

    // ---- helpers ----

    private static DirectorioCategoriaDto Map(DirectorioCategoria c) => new(
        c.Id, c.CategoriaKey, c.Title, c.Description, c.Icono, c.Color, c.Areas,
        c.Protegido, c.HomologaSeccion, c.SortOrder, c.IsHidden, c.IsSystem);

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Normaliza el CSV de areas: minusculas, sin vacios, sin duplicados. Null si queda vacio.
    /// No valida contra un catalogo fijo: las areas del producto salen de la seguridad de la plataforma.</summary>
    private static string? NormalizeAreas(string? areas)
    {
        if (string.IsNullOrWhiteSpace(areas)) { return null; }
        var toks = areas.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant()).Distinct().ToList();
        return toks.Count == 0 ? null : string.Join(",", toks);
    }

    private async Task<string> UniqueKeyAsync(string title, CancellationToken cancellationToken)
    {
        var baseKey = Slugify(title);
        if (baseKey.Length == 0) { baseKey = "categoria"; }
        if (baseKey.Length > 34) { baseKey = baseKey[..34]; }
        var key = baseKey;
        var n = 1;
        while (await _db.DirectorioCategorias.AnyAsync(c => c.CategoriaKey == key, cancellationToken))
        {
            n++;
            key = $"{baseKey}_{n}";
        }
        return key;
    }

    // Slug ASCII en minusculas con guion bajo (misma familia que las claves de ficha/campo).
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
