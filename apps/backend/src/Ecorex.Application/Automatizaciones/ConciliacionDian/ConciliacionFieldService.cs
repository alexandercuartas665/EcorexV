using System.Text;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Automatizaciones.ConciliacionDian;

public sealed record ConciliacionFieldDefDto(Guid Id, string FieldKey, string Label, string Type, int SortOrder, IReadOnlyList<string> Options, string? OptionsJson = null, string? Description = null, bool ShowInFilter = true);

/// <summary>
/// Campos DINAMICOS de la tabla de conciliacion: definiciones por tenant (sin codigo) + sus valores por
/// renglon (EAV). La grilla los muestra como columnas editables; el motor de reglas los puede leer/escribir.
/// </summary>
public interface IConciliacionFieldService
{
    Task<IReadOnlyList<ConciliacionFieldDefDto>> ListDefsAsync(CancellationToken ct = default);
    Task<ConciliacionFieldDefDto?> SaveDefAsync(ConciliacionFieldDefDto dto, CancellationToken ct = default);
    Task<bool> DeleteDefAsync(Guid id, CancellationToken ct = default);
    Task ReorderDefsAsync(IReadOnlyList<Guid> orderedIds, CancellationToken ct = default);

    /// <summary>Valores de los campos dinamicos para un conjunto de renglones: renglonId -> (fieldDefId -> valor).</summary>
    Task<IReadOnlyDictionary<Guid, Dictionary<Guid, string?>>> GetValuesAsync(IReadOnlyList<Guid> renglonIds, CancellationToken ct = default);
    Task SetValueAsync(Guid renglonId, Guid fieldDefId, string? value, CancellationToken ct = default);

    /// <summary>
    /// Config de "fuente de datos" (Lookup a un contenedor) para una COLUMNA FIJA del sistema (p. ej. "oc",
    /// "numsol"). Se guarda como una definicion reservada con FieldKey "sys:{columnKey}" (no es una columna nueva:
    /// la grilla la consulta para convertir esa celda fija en un typeahead). json=null/"" borra la config.
    /// </summary>
    Task SaveSystemSourceAsync(string columnKey, string? optionsJson, CancellationToken ct = default);
}

public sealed class ConciliacionFieldService : IConciliacionFieldService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public ConciliacionFieldService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db; _tenant = tenant;
    }

    public async Task<IReadOnlyList<ConciliacionFieldDefDto>> ListDefsAsync(CancellationToken ct = default)
    {
        var rows = await _db.ConciliacionFieldDefinitions.AsNoTracking().OrderBy(d => d.SortOrder).ThenBy(d => d.Label).ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<ConciliacionFieldDefDto?> SaveDefAsync(ConciliacionFieldDefDto dto, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return null; }
        var label = (dto.Label ?? "").Trim();
        if (label.Length == 0) { return null; }

        ConciliacionFieldDefinition? e = dto.Id == Guid.Empty ? null : await _db.ConciliacionFieldDefinitions.FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
        if (e is null)
        {
            var key = await UniqueKeyAsync(Slug(label), ct);
            e = new ConciliacionFieldDefinition { TenantId = tid, FieldKey = key, SortOrder = dto.SortOrder };
            _db.ConciliacionFieldDefinitions.Add(e);
        }
        e.Label = label;
        e.Type = NormalizeType(dto.Type);
        e.SortOrder = dto.SortOrder;
        e.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        e.ShowInFilter = dto.ShowInFilter;
        e.OptionsJson = e.Type switch
        {
            // Select/MultiSelect: lista de opciones (JSON array). Lookup: DataLookupConfig JSON.
            // Calculated: la formula cruda. El resto: sin config.
            "Select" or "MultiSelect" => dto.Options is { Count: > 0 }
                ? JsonSerializer.Serialize(dto.Options.Select(o => o.Trim()).Where(o => o.Length > 0).ToList())
                : null,
            "Lookup" or "Calculated" => string.IsNullOrWhiteSpace(dto.OptionsJson) ? null : dto.OptionsJson,
            _ => null,
        };
        await _db.SaveChangesAsync(ct);
        return Map(e);
    }

    public async Task<bool> DeleteDefAsync(Guid id, CancellationToken ct = default)
    {
        var e = await _db.ConciliacionFieldDefinitions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        _db.ConciliacionFieldDefinitions.Remove(e); // cascada borra sus valores
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ReorderDefsAsync(IReadOnlyList<Guid> orderedIds, CancellationToken ct = default)
    {
        var rows = await _db.ConciliacionFieldDefinitions.ToListAsync(ct);
        for (var i = 0; i < orderedIds.Count; i++)
        {
            var e = rows.FirstOrDefault(r => r.Id == orderedIds[i]);
            if (e is not null) { e.SortOrder = i; }
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, Dictionary<Guid, string?>>> GetValuesAsync(IReadOnlyList<Guid> renglonIds, CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, Dictionary<Guid, string?>>();
        if (renglonIds.Count == 0) { return result; }
        var vals = await _db.ConciliacionFieldValues.AsNoTracking()
            .Where(v => renglonIds.Contains(v.RenglonId))
            .Select(v => new { v.RenglonId, v.FieldDefinitionId, v.Value })
            .ToListAsync(ct);
        foreach (var v in vals)
        {
            if (!result.TryGetValue(v.RenglonId, out var d)) { d = new(); result[v.RenglonId] = d; }
            d[v.FieldDefinitionId] = v.Value;
        }
        return result;
    }

    public async Task SetValueAsync(Guid renglonId, Guid fieldDefId, string? value, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return; }
        var e = await _db.ConciliacionFieldValues.FirstOrDefaultAsync(x => x.RenglonId == renglonId && x.FieldDefinitionId == fieldDefId, ct);
        if (string.IsNullOrEmpty(value))
        {
            if (e is not null) { _db.ConciliacionFieldValues.Remove(e); await _db.SaveChangesAsync(ct); }
            return;
        }
        if (e is null)
        {
            _db.ConciliacionFieldValues.Add(new ConciliacionFieldValue { TenantId = tid, RenglonId = renglonId, FieldDefinitionId = fieldDefId, Value = value });
        }
        else { e.Value = value; }
        await _db.SaveChangesAsync(ct);
    }

    public async Task SaveSystemSourceAsync(string columnKey, string? optionsJson, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return; }
        columnKey = (columnKey ?? "").Trim();
        if (columnKey.Length == 0) { return; }
        var key = "sys:" + columnKey;
        var e = await _db.ConciliacionFieldDefinitions.FirstOrDefaultAsync(x => x.FieldKey == key, ct);
        if (string.IsNullOrWhiteSpace(optionsJson))
        {
            if (e is not null) { _db.ConciliacionFieldDefinitions.Remove(e); await _db.SaveChangesAsync(ct); }
            return;
        }
        if (e is null)
        {
            e = new ConciliacionFieldDefinition { TenantId = tid, FieldKey = key, SortOrder = 0 };
            _db.ConciliacionFieldDefinitions.Add(e);
        }
        e.Label = columnKey;         // solo informativo
        e.Type = "Lookup";
        e.OptionsJson = optionsJson;
        await _db.SaveChangesAsync(ct);
    }

    private static ConciliacionFieldDefDto Map(ConciliacionFieldDefinition d)
    {
        IReadOnlyList<string> opts = Array.Empty<string>();
        // Solo Select/MultiSelect guardan una lista de opciones. Lookup (JSON {...}) y Calculated
        // (formula) usan OptionsJson crudo, que el DTO expone aparte.
        if ((d.Type == "Select" || d.Type == "MultiSelect") && !string.IsNullOrWhiteSpace(d.OptionsJson))
        {
            try { opts = JsonSerializer.Deserialize<List<string>>(d.OptionsJson) ?? new(); } catch { }
        }
        return new ConciliacionFieldDefDto(d.Id, d.FieldKey, d.Label, d.Type, d.SortOrder, opts, d.OptionsJson, d.Description, d.ShowInFilter);
    }

    private static string NormalizeType(string? t) => t switch
    {
        "Number" or "Date" or "Bool" or "Select" or "MultiSelect" or "Url" or "Lookup" or "Calculated" => t,
        _ => "Text",
    };

    private async Task<string> UniqueKeyAsync(string baseKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baseKey)) { baseKey = "campo"; }
        var key = baseKey; var n = 1;
        while (await _db.ConciliacionFieldDefinitions.AnyAsync(x => x.FieldKey == key, ct))
        {
            n++; key = baseKey + "_" + n;
        }
        return key;
    }

    private static string Slug(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) { sb.Append(ch); }
            else if (ch is ' ' or '-' or '_' or '.') { if (sb.Length > 0 && sb[^1] != '_') { sb.Append('_'); } }
        }
        var slug = sb.ToString().Trim('_');
        return slug.Length > 50 ? slug[..50] : slug;
    }
}
