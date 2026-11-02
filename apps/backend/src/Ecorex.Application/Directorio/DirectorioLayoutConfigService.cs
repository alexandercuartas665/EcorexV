using System.Linq;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Directorio;

/// <summary>
/// Layout de la zona "principal" del modal del tercero (motor Clasico): como se llena el campo
/// Sector / Industria (texto libre o lista configurable) y DONDE se intercalan los campos de la
/// ficha reservada "general" respecto de los 8 campos principales (su "ancla").
///
/// Vive en un unico <see cref="TenantConfiguration"/> (clave/valor JSON), NO en columnas: no hay
/// migracion. Es config por tenant, aislada por el filtro global. Reutiliza el motor de campos
/// configurables (<see cref="TerceroFieldDefinition"/>) para los campos nuevos; aqui solo se guarda
/// la posicion (ancla) de cada uno y el modo del Sector.
/// </summary>
public sealed record DirectorioLayoutDto(
    bool SectorIsList,
    string? SectorOptions,
    IReadOnlyDictionary<string, string> Anchors,
    IReadOnlyList<string> Order,
    IReadOnlyDictionary<string, string> Labels);

public interface IDirectorioLayoutConfigService
{
    /// <summary>Anclas validas: los 8 campos principales. Un campo "general" se pinta DESPUES del ancla.</summary>
    static readonly IReadOnlyList<string> ValidAnchors =
        ["nombre", "identificacion", "ciudad", "sector", "correo", "telefono", "vendedor", "estado"];

    Task<DirectorioLayoutDto> GetAsync(CancellationToken ct = default);
    /// <summary>Define si Sector / Industria es lista (con opciones, una por linea) o texto libre.</summary>
    Task SetSectorAsync(bool isList, string? options, CancellationToken ct = default);
    /// <summary>Fija el ancla (posicion) de un campo "general"; anchor null o invalido lo quita del mapa.</summary>
    Task SetAnchorAsync(string fieldKey, string? anchor, CancellationToken ct = default);
    /// <summary>Quita un campo del mapa de anclas (al eliminar el campo). Idempotente.</summary>
    Task RemoveAnchorAsync(string fieldKey, CancellationToken ct = default);
    /// <summary>
    /// Persiste el ORDEN unico de la zona principal: una secuencia de tokens que mezcla campos duros
    /// (clave con prefijo "#", p.ej. "#sector") y campos "general" (su clave). Es la fuente de verdad de
    /// la posicion; reemplaza al mapa de anclas. Ver <see cref="DirectorioLayoutConfigService.BuildEffectiveOrder"/>.
    /// </summary>
    Task SetOrderAsync(IReadOnlyList<string> order, CancellationToken ct = default);
    /// <summary>Renombra el titulo de un campo DURO; label vacio/null restaura el titulo por defecto.</summary>
    Task SetFixedLabelAsync(string fixedKey, string? label, CancellationToken ct = default);
}

public sealed class DirectorioLayoutConfigService : IDirectorioLayoutConfigService
{
    private const string ConfigKey = "directorio.general.layout";
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public DirectorioLayoutConfigService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    /// <summary>Prefijo reservado de los tokens de campos DUROS en la lista de orden (p.ej. "#sector").</summary>
    public const string FixedPrefix = "#";

    // Forma serializada en el TenantConfiguration (una sola fila por tenant).
    private sealed record Stored(
        bool SectorIsList = false,
        string? SectorOptions = null,
        Dictionary<string, string>? Anchors = null,
        List<string>? Order = null,
        Dictionary<string, string>? Labels = null);

    public async Task<DirectorioLayoutDto> GetAsync(CancellationToken ct = default)
    {
        var raw = await _db.TenantConfigurations.AsNoTracking()
            .Where(c => c.ConfigKey == ConfigKey)
            .Select(c => c.ConfigValue)
            .FirstOrDefaultAsync(ct);
        var s = Parse(raw);
        return new DirectorioLayoutDto(
            s.SectorIsList,
            s.SectorOptions,
            s.Anchors ?? new Dictionary<string, string>(StringComparer.Ordinal),
            s.Order ?? new List<string>(),
            s.Labels ?? new Dictionary<string, string>(StringComparer.Ordinal));
    }

    public async Task SetSectorAsync(bool isList, string? options, CancellationToken ct = default)
    {
        var s = await LoadAsync(ct);
        var cleanOptions = string.IsNullOrWhiteSpace(options) ? null : options.Trim();
        await SaveAsync(s with { SectorIsList = isList, SectorOptions = isList ? cleanOptions : null }, ct);
    }

    public async Task SetAnchorAsync(string fieldKey, string? anchor, CancellationToken ct = default)
    {
        var key = (fieldKey ?? string.Empty).Trim();
        if (key.Length == 0) { return; }
        var s = await LoadAsync(ct);
        var anchors = s.Anchors ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var a = (anchor ?? string.Empty).Trim().ToLowerInvariant();
        if (IDirectorioLayoutConfigService.ValidAnchors.Contains(a))
        {
            anchors[key] = a;
        }
        else
        {
            anchors.Remove(key);
        }
        await SaveAsync(s with { Anchors = anchors }, ct);
    }

    public async Task RemoveAnchorAsync(string fieldKey, CancellationToken ct = default)
    {
        var key = (fieldKey ?? string.Empty).Trim();
        if (key.Length == 0) { return; }
        var s = await LoadAsync(ct);
        if (s.Anchors is null || !s.Anchors.ContainsKey(key)) { return; }
        s.Anchors.Remove(key);
        await SaveAsync(s, ct);
    }

    public async Task SetOrderAsync(IReadOnlyList<string> order, CancellationToken ct = default)
    {
        var s = await LoadAsync(ct);
        var clean = (order ?? Array.Empty<string>())
            .Select(t => (t ?? string.Empty).Trim())
            .Where(t => t.Length > 0)
            .ToList();
        await SaveAsync(s with { Order = clean }, ct);
    }

    public async Task SetFixedLabelAsync(string fixedKey, string? label, CancellationToken ct = default)
    {
        var key = (fixedKey ?? string.Empty).Trim().ToLowerInvariant();
        if (!IDirectorioLayoutConfigService.ValidAnchors.Contains(key)) { return; }
        var s = await LoadAsync(ct);
        var labels = s.Labels ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var clean = (label ?? string.Empty).Trim();
        if (clean.Length == 0) { labels.Remove(key); } else { labels[key] = clean; }
        await SaveAsync(s with { Labels = labels }, ct);
    }

    /// <summary>
    /// Construye el ORDEN efectivo de la zona principal: la secuencia de tokens (duros "#clave" +
    /// campos "general") que se debe pintar. Es una funcion pura (sin BD), compartida por el modal del
    /// tercero y el configurador.
    /// <para>
    /// Si <paramref name="storedOrder"/> trae datos, se respeta (quitando tokens que ya no existen) y se
    /// completan los que falten: los duros en su posicion canonica, los "general" tras su ancla. Si viene
    /// vacio (tenants ADR-0141 que nunca reordenaron), se reconstruye el layout ACTUAL a partir de las
    /// anclas, de modo que el resultado es identico al que ya veian.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> BuildEffectiveOrder(
        IReadOnlyList<string>? storedOrder,
        IReadOnlyDictionary<string, string>? anchors,
        IEnumerable<string> customFieldKeysInSortOrder)
    {
        anchors ??= new Dictionary<string, string>(StringComparer.Ordinal);
        var customList = customFieldKeysInSortOrder?
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .ToList() ?? new List<string>();
        var customSet = new HashSet<string>(customList, StringComparer.Ordinal);

        bool IsKnown(string tok)
            => tok.StartsWith(FixedPrefix, StringComparison.Ordinal)
                ? IDirectorioLayoutConfigService.ValidAnchors.Contains(tok[FixedPrefix.Length..])
                : customSet.Contains(tok);

        string AnchorOf(string customKey)
        {
            var a = anchors!.TryGetValue(customKey, out var v) && !string.IsNullOrWhiteSpace(v) ? v : "sector";
            return IDirectorioLayoutConfigService.ValidAnchors.Contains(a) ? a : "sector";
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (storedOrder is { Count: > 0 })
        {
            // 1) Tokens guardados que siguen existiendo (sin duplicados).
            foreach (var raw in storedOrder)
            {
                var tok = (raw ?? string.Empty).Trim();
                if (tok.Length > 0 && IsKnown(tok) && seen.Add(tok)) { result.Add(tok); }
            }
            // 2) Duros faltantes, en su posicion canonica relativa.
            var insertPos = 0;
            foreach (var k in IDirectorioLayoutConfigService.ValidAnchors)
            {
                var tok = FixedPrefix + k;
                var idx = result.IndexOf(tok);
                if (idx >= 0) { insertPos = idx + 1; }
                else { result.Insert(insertPos, tok); seen.Add(tok); insertPos++; }
            }
            // 3) Campos "general" faltantes, tras su ancla (o al final).
            foreach (var ck in customList)
            {
                if (seen.Contains(ck)) { continue; }
                var anchorTok = FixedPrefix + AnchorOf(ck);
                var ai = result.IndexOf(anchorTok);
                if (ai >= 0) { result.Insert(ai + 1, ck); } else { result.Add(ck); }
                seen.Add(ck);
            }
        }
        else
        {
            // Sin orden guardado: reconstruye el layout ACTUAL desde las anclas (resultado identico).
            foreach (var k in IDirectorioLayoutConfigService.ValidAnchors)
            {
                result.Add(FixedPrefix + k);
                foreach (var ck in customList.Where(c => AnchorOf(c) == k))
                {
                    result.Add(ck);
                }
            }
        }

        return result;
    }

    // ---- helpers ----

    private async Task<Stored> LoadAsync(CancellationToken ct)
    {
        var raw = await _db.TenantConfigurations.AsNoTracking()
            .Where(c => c.ConfigKey == ConfigKey)
            .Select(c => c.ConfigValue)
            .FirstOrDefaultAsync(ct);
        return Parse(raw);
    }

    private async Task SaveAsync(Stored s, CancellationToken ct)
    {
        if (_tenant.TenantId is not Guid tid) { return; }
        var json = JsonSerializer.Serialize(s);
        var row = await _db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == ConfigKey, ct);
        if (row is null)
        {
            _db.TenantConfigurations.Add(new TenantConfiguration { TenantId = tid, ConfigKey = ConfigKey, ConfigValue = json });
        }
        else
        {
            row.ConfigValue = json;
        }
        await _db.SaveChangesAsync(ct);
    }

    private static Stored Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return new Stored(); }
        try
        {
            return JsonSerializer.Deserialize<Stored>(raw) ?? new Stored();
        }
        catch (JsonException)
        {
            return new Stored();
        }
    }
}
