using System.Text;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Requerimientos;

/// <summary>Definicion de un campo dinamico de Requerimientos (Ola 12). Scope = "Padre" | "Hija".</summary>
public sealed record RequerimientoFieldDefDto(
    Guid Id, string FieldKey, string Label, string Scope, string Type, int SortOrder,
    IReadOnlyList<string> Options, string? Description = null, bool ShowInFilter = true);

/// <summary>
/// Campos DINAMICOS (a medida) del modulo Requerimientos y Compras: definiciones por tenant (sin codigo) +
/// sus valores por dueno (EAV). El dueno es la necesidad (Scope=Padre) o la linea de planificacion
/// (Scope=Hija). La grilla muestra los de Padre como columnas; el modal de Planificar muestra los de Hija.
/// </summary>
public interface IRequerimientoFieldService
{
    /// <summary>Lista las definiciones; si <paramref name="scope"/> no es null, solo las de ese ambito.</summary>
    Task<IReadOnlyList<RequerimientoFieldDefDto>> ListDefsAsync(string? scope = null, CancellationToken ct = default);
    Task<RequerimientoFieldDefDto?> SaveDefAsync(RequerimientoFieldDefDto dto, CancellationToken ct = default);
    Task<bool> DeleteDefAsync(Guid id, CancellationToken ct = default);
    Task ReorderDefsAsync(IReadOnlyList<Guid> orderedIds, CancellationToken ct = default);

    /// <summary>Valores para un conjunto de duenos: ownerId -> (fieldDefId -> valor).</summary>
    Task<IReadOnlyDictionary<Guid, Dictionary<Guid, string?>>> GetValuesAsync(IReadOnlyList<Guid> ownerIds, CancellationToken ct = default);
    Task SetValueAsync(Guid ownerId, Guid fieldDefId, string? value, CancellationToken ct = default);
}

public sealed class RequerimientoFieldService : IRequerimientoFieldService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public RequerimientoFieldService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db; _tenant = tenant;
    }

    public async Task<IReadOnlyList<RequerimientoFieldDefDto>> ListDefsAsync(string? scope = null, CancellationToken ct = default)
    {
        var q = _db.RequerimientoFieldDefinitions.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(scope)) { var s = NormalizeScope(scope); q = q.Where(d => d.Scope == s); }
        var rows = await q.OrderBy(d => d.Scope).ThenBy(d => d.SortOrder).ThenBy(d => d.Label).ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<RequerimientoFieldDefDto?> SaveDefAsync(RequerimientoFieldDefDto dto, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return null; }
        var label = (dto.Label ?? "").Trim();
        if (label.Length == 0) { return null; }
        var scope = NormalizeScope(dto.Scope);

        RequerimientoFieldDefinition? e = dto.Id == Guid.Empty ? null : await _db.RequerimientoFieldDefinitions.FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
        if (e is null)
        {
            var key = await UniqueKeyAsync(Slug(label), scope, ct);
            e = new RequerimientoFieldDefinition { TenantId = tid, FieldKey = key, Scope = scope, SortOrder = dto.SortOrder };
            _db.RequerimientoFieldDefinitions.Add(e);
        }
        e.Label = label;
        e.Scope = scope; // el ambito solo se fija al crear en la practica, pero se respeta si viene
        e.Type = NormalizeType(dto.Type);
        e.SortOrder = dto.SortOrder;
        e.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        e.ShowInFilter = dto.ShowInFilter;
        e.OptionsJson = e.Type == "Select" && dto.Options is { Count: > 0 }
            ? JsonSerializer.Serialize(dto.Options.Select(o => o.Trim()).Where(o => o.Length > 0).ToList())
            : null;
        await _db.SaveChangesAsync(ct);
        return Map(e);
    }

    public async Task<bool> DeleteDefAsync(Guid id, CancellationToken ct = default)
    {
        var e = await _db.RequerimientoFieldDefinitions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        _db.RequerimientoFieldDefinitions.Remove(e); // cascada borra sus valores
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ReorderDefsAsync(IReadOnlyList<Guid> orderedIds, CancellationToken ct = default)
    {
        var rows = await _db.RequerimientoFieldDefinitions.ToListAsync(ct);
        for (var i = 0; i < orderedIds.Count; i++)
        {
            var e = rows.FirstOrDefault(r => r.Id == orderedIds[i]);
            if (e is not null) { e.SortOrder = i; }
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, Dictionary<Guid, string?>>> GetValuesAsync(IReadOnlyList<Guid> ownerIds, CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, Dictionary<Guid, string?>>();
        if (ownerIds.Count == 0) { return result; }
        var vals = await _db.RequerimientoFieldValues.AsNoTracking()
            .Where(v => ownerIds.Contains(v.OwnerId))
            .Select(v => new { v.OwnerId, v.FieldDefinitionId, v.Value })
            .ToListAsync(ct);
        foreach (var v in vals)
        {
            if (!result.TryGetValue(v.OwnerId, out var d)) { d = new(); result[v.OwnerId] = d; }
            d[v.FieldDefinitionId] = v.Value;
        }
        return result;
    }

    public async Task SetValueAsync(Guid ownerId, Guid fieldDefId, string? value, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return; }
        var e = await _db.RequerimientoFieldValues.FirstOrDefaultAsync(x => x.OwnerId == ownerId && x.FieldDefinitionId == fieldDefId, ct);
        if (string.IsNullOrEmpty(value))
        {
            if (e is not null) { _db.RequerimientoFieldValues.Remove(e); await _db.SaveChangesAsync(ct); }
            return;
        }
        if (e is null)
        {
            _db.RequerimientoFieldValues.Add(new RequerimientoFieldValue { TenantId = tid, OwnerId = ownerId, FieldDefinitionId = fieldDefId, Value = value });
        }
        else { e.Value = value; }
        await _db.SaveChangesAsync(ct);
    }

    private static RequerimientoFieldDefDto Map(RequerimientoFieldDefinition d)
    {
        IReadOnlyList<string> opts = Array.Empty<string>();
        if (d.Type == "Select" && !string.IsNullOrWhiteSpace(d.OptionsJson))
        {
            try { opts = JsonSerializer.Deserialize<List<string>>(d.OptionsJson) ?? new(); } catch { }
        }
        return new RequerimientoFieldDefDto(d.Id, d.FieldKey, d.Label, d.Scope, d.Type, d.SortOrder, opts, d.Description, d.ShowInFilter);
    }

    private static string NormalizeScope(string? s) => string.Equals(s, "Hija", StringComparison.OrdinalIgnoreCase) ? "Hija" : "Padre";

    private static string NormalizeType(string? t) => t switch
    {
        "Number" or "Date" or "Bool" or "Select" or "Url" => t,
        _ => "Text",
    };

    private async Task<string> UniqueKeyAsync(string baseKey, string scope, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baseKey)) { baseKey = "campo"; }
        var key = baseKey; var n = 1;
        while (await _db.RequerimientoFieldDefinitions.AnyAsync(x => x.Scope == scope && x.FieldKey == key, ct))
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
