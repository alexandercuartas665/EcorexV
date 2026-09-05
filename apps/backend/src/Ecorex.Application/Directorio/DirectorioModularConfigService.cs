using System.Text;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Directorio;

/// <summary>
/// Implementacion de <see cref="IDirectorioModularConfigService"/>: CRUD de secciones/campos "mod_" del
/// 2do motor. Sobre TerceroFichaDefinition/TerceroFieldDefinition (motor Clasico los oculta por prefijo).
/// </summary>
public sealed class DirectorioModularConfigService : IDirectorioModularConfigService
{
    private readonly IApplicationDbContext _app;
    private readonly IDirectorioModularDbContext _db;
    private readonly ITenantContext _tenant;

    public DirectorioModularConfigService(IApplicationDbContext app, IDirectorioModularDbContext db, ITenantContext tenant)
    {
        _app = app;
        _db = db;
        _tenant = tenant;
    }

    private static int AnchoAColumn(string ancho) => ancho switch { "pequena" => 1, "media" => 2, _ => 3 };

    // ---- Secciones ----

    public async Task<string> CrearSeccionAsync(CancellationToken cancellationToken = default)
    {
        var prefix = DirectorioModularDefaults.SeccionPrefix;
        var fichaKey = prefix + "sec_" + Guid.NewGuid().ToString("N")[..8];
        var maxOrder = await _app.TerceroFichaDefinitions
            .Where(f => f.FichaKey.StartsWith(prefix))
            .Select(f => (int?)f.SortOrder).MaxAsync(cancellationToken) ?? 0;

        _app.TerceroFichaDefinitions.Add(new TerceroFichaDefinition
        {
            TenantId = _tenant.TenantId ?? Guid.Empty,
            FichaKey = fichaKey,
            Title = "Nueva seccion",
            Icono = "fa-folder",
            Color = "#4f46e5",
            AplicaA = "empresa,contacto",
            Areas = string.Empty,
            Protegida = false,
            SortOrder = maxOrder + 1
        });
        await _app.SaveChangesAsync(cancellationToken);
        return fichaKey;
    }

    public async Task<string?> ActualizarSeccionAsync(Guid id, string title, string? icono, string? color,
        string? descripcion, string? areas, string? aplicaA, CancellationToken cancellationToken = default)
    {
        var sec = await _app.TerceroFichaDefinitions.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (sec is null) { return "La seccion no existe."; }
        if (!sec.FichaKey.StartsWith(DirectorioModularDefaults.SeccionPrefix)) { return "Esa seccion no es del motor Modular."; }

        var nombre = (title ?? string.Empty).Trim();
        if (nombre.Length > 0) { sec.Title = nombre; }
        sec.Icono = string.IsNullOrWhiteSpace(icono) ? sec.Icono : icono;
        sec.Color = string.IsNullOrWhiteSpace(color) ? sec.Color : color;
        sec.Description = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim();
        sec.AplicaA = string.IsNullOrWhiteSpace(aplicaA) ? "empresa,contacto" : aplicaA;
        // La seccion protegida (publica) no cambia sus areas: siempre la ven todas.
        if (!sec.Protegida) { sec.Areas = areas ?? string.Empty; }

        await _app.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> BorrarSeccionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var sec = await _app.TerceroFichaDefinitions.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (sec is null) { return "La seccion no existe."; }
        if (sec.Protegida) { return "La seccion base no se puede eliminar."; }
        if (!sec.FichaKey.StartsWith(DirectorioModularDefaults.SeccionPrefix)) { return "Esa seccion no es del motor Modular."; }

        var campos = await _app.TerceroFieldDefinitions.Where(f => f.FichaKey == sec.FichaKey).ToListAsync(cancellationToken);
        _app.TerceroFieldDefinitions.RemoveRange(campos);

        // La quita de la composicion de cualquier categoria que la use.
        var usos = await _db.DirectorioCategoriaSecciones.Where(s => s.FichaKey == sec.FichaKey).ToListAsync(cancellationToken);
        _db.DirectorioCategoriaSecciones.RemoveRange(usos);

        _app.TerceroFichaDefinitions.Remove(sec);
        await _app.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<bool> MoverSeccionAsync(Guid id, int delta, CancellationToken cancellationToken = default)
    {
        var prefix = DirectorioModularDefaults.SeccionPrefix;
        var secs = await _app.TerceroFichaDefinitions
            .Where(f => f.FichaKey.StartsWith(prefix))
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Title)
            .ToListAsync(cancellationToken);
        var i = secs.FindIndex(s => s.Id == id);
        var j = i + (delta < 0 ? -1 : 1);
        if (i < 0 || j < 0 || j >= secs.Count) { return false; }
        (secs[i].SortOrder, secs[j].SortOrder) = (secs[j].SortOrder, secs[i].SortOrder);
        await _app.SaveChangesAsync(cancellationToken);
        return true;
    }

    // ---- Campos ----

    public async Task<string?> CrearCampoAsync(string fichaKey, string label, TerceroFieldType tipo, string ancho,
        string? opciones, bool requerido, string? descripcion, CancellationToken cancellationToken = default)
    {
        var key = (fichaKey ?? string.Empty).Trim();
        if (!key.StartsWith(DirectorioModularDefaults.SeccionPrefix)) { return "La seccion no es del motor Modular."; }
        var etiqueta = (label ?? string.Empty).Trim();
        if (etiqueta.Length == 0) { return "La etiqueta del campo es obligatoria."; }
        if (!await _app.TerceroFichaDefinitions.AnyAsync(f => f.FichaKey == key, cancellationToken)) { return "La seccion no existe."; }

        var existentes = await _app.TerceroFieldDefinitions
            .Where(f => f.FichaKey == key).Select(f => f.FieldKey).ToListAsync(cancellationToken);
        var fieldKey = UniqueKey(Slug(etiqueta), existentes);
        var maxOrder = await _app.TerceroFieldDefinitions
            .Where(f => f.FichaKey == key).Select(f => (int?)f.SortOrder).MaxAsync(cancellationToken) ?? 0;

        _app.TerceroFieldDefinitions.Add(new TerceroFieldDefinition
        {
            TenantId = _tenant.TenantId ?? Guid.Empty,
            FichaKey = key,
            FieldKey = fieldKey,
            Label = etiqueta,
            FieldType = tipo,
            Column = AnchoAColumn(ancho),
            Options = NormalizeOptions(tipo, opciones),
            RequeridoEn = requerido ? "empresa,contacto" : null,
            Description = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim(),
            SortOrder = maxOrder + 1,
            IsSystem = false
        });
        await _app.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> ActualizarCampoAsync(Guid id, string label, TerceroFieldType tipo, string ancho,
        string? opciones, bool requerido, string? descripcion, CancellationToken cancellationToken = default)
    {
        var f = await _app.TerceroFieldDefinitions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (f is null) { return "El campo no existe."; }
        if (!f.FichaKey.StartsWith(DirectorioModularDefaults.SeccionPrefix)) { return "Ese campo no es del motor Modular."; }
        var etiqueta = (label ?? string.Empty).Trim();
        if (etiqueta.Length == 0) { return "La etiqueta del campo es obligatoria."; }

        f.Label = etiqueta;
        f.FieldType = tipo;
        f.Column = AnchoAColumn(ancho);
        f.Options = NormalizeOptions(tipo, opciones);
        f.RequeridoEn = requerido ? "empresa,contacto" : null;
        f.Description = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim();
        await _app.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> BorrarCampoAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var f = await _app.TerceroFieldDefinitions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (f is null) { return "El campo no existe."; }
        if (f.IsSystem) { return "Un campo de sistema no se puede eliminar."; }
        if (!f.FichaKey.StartsWith(DirectorioModularDefaults.SeccionPrefix)) { return "Ese campo no es del motor Modular."; }
        _app.TerceroFieldDefinitions.Remove(f);
        await _app.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<bool> MoverCampoAsync(Guid id, int delta, CancellationToken cancellationToken = default)
    {
        var f = await _app.TerceroFieldDefinitions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (f is null) { return false; }
        var campos = await _app.TerceroFieldDefinitions
            .Where(x => x.FichaKey == f.FichaKey).OrderBy(x => x.SortOrder).ToListAsync(cancellationToken);
        var i = campos.FindIndex(x => x.Id == id);
        var j = i + (delta < 0 ? -1 : 1);
        if (i < 0 || j < 0 || j >= campos.Count) { return false; }
        (campos[i].SortOrder, campos[j].SortOrder) = (campos[j].SortOrder, campos[i].SortOrder);
        await _app.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> CambiarAnchoAsync(Guid id, string ancho, CancellationToken cancellationToken = default)
    {
        var f = await _app.TerceroFieldDefinitions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (f is null) { return false; }
        f.Column = AnchoAColumn(ancho);
        await _app.SaveChangesAsync(cancellationToken);
        return true;
    }

    // ---- helpers ----

    /// <summary>Las listas guardan opciones (una por linea); la Tabla guarda su definicion de columnas (JSON,
    /// tal cual); los demas tipos no llevan Options.</summary>
    private static string? NormalizeOptions(TerceroFieldType tipo, string? opciones)
    {
        if (tipo == TerceroFieldType.Table)
        {
            return string.IsNullOrWhiteSpace(opciones) ? null : opciones.Trim();
        }
        if (tipo != TerceroFieldType.Select && tipo != TerceroFieldType.MultiSelect) { return null; }
        if (string.IsNullOrWhiteSpace(opciones)) { return null; }
        var lineas = opciones.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lineas.Length == 0 ? null : string.Join('\n', lineas);
    }

    private static string Slug(string label)
    {
        var sb = new StringBuilder(label.Length);
        foreach (var ch in label.ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') { sb.Append(ch); }
            else if (ch is ' ' or '-' or '_' or '.') { sb.Append('_'); }
            // acentos y otros se omiten (ASCII-only en las claves)
        }
        var s = sb.ToString().Trim('_');
        while (s.Contains("__")) { s = s.Replace("__", "_"); }
        return s.Length == 0 ? "campo" : s;
    }

    private static string UniqueKey(string baseKey, IReadOnlyCollection<string> existentes)
    {
        if (!existentes.Contains(baseKey)) { return baseKey; }
        for (var n = 2; n < 1000; n++)
        {
            var k = baseKey + "_" + n;
            if (!existentes.Contains(k)) { return k; }
        }
        return baseKey + "_" + Guid.NewGuid().ToString("N")[..6];
    }
}
