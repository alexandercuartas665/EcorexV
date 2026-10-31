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
    IReadOnlyDictionary<string, string> Anchors);

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

    // Forma serializada en el TenantConfiguration (una sola fila por tenant).
    private sealed record Stored(
        bool SectorIsList = false,
        string? SectorOptions = null,
        Dictionary<string, string>? Anchors = null);

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
            s.Anchors ?? new Dictionary<string, string>(StringComparer.Ordinal));
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
