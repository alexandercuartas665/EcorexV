using System.Globalization;
using System.Text;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed record SalonFieldDto(Guid Id, SalonFieldScope Scope, string FieldKey, string Label, SalonFieldType FieldType,
    string? Options, string? Description, int Column, int SortOrder, bool IsRequired, bool ShowOnBoard);

public sealed record CreateSalonFieldRequest(SalonFieldScope Scope, string Label, SalonFieldType FieldType,
    string? Options = null, string? Description = null, int Column = 1, bool IsRequired = false, bool ShowOnBoard = false, string? FieldKey = null);

public sealed record UpdateSalonFieldRequest(string Label, SalonFieldType FieldType,
    string? Options, string? Description, int Column, bool IsRequired, bool ShowOnBoard);

public sealed record ReorderSalonFieldsRequest(IReadOnlyList<Guid> OrderedFieldIds);

/// <summary>
/// Campos configurables del salon (capa 2): el salon agrega/edita/ordena/elimina los campos que captura
/// en sus citas y en la ficha de sus clientes, sin tocar codigo. Tenant-scoped. Sin etapas/kanban.
/// </summary>
public interface ISalonFieldService
{
    Task<IReadOnlyList<SalonFieldDto>> ListAsync(SalonFieldScope scope, CancellationToken cancellationToken = default);
    Task<SalonFieldDto?> CreateAsync(CreateSalonFieldRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<SalonFieldDto?> UpdateAsync(Guid fieldId, UpdateSalonFieldRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid fieldId, Guid actorUserId, CancellationToken cancellationToken = default);
    Task ReorderAsync(ReorderSalonFieldsRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
}

public sealed class SalonFieldService : ISalonFieldService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;

    public SalonFieldService(IApplicationDbContext db, ITenantContext tenant, IAuditWriter audit)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
    }

    public async Task<IReadOnlyList<SalonFieldDto>> ListAsync(SalonFieldScope scope, CancellationToken cancellationToken = default)
        => await _db.SalonFieldDefinitions.AsNoTracking()
            .Where(f => f.Scope == scope)
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Label)
            .Select(f => Map(f))
            .ToListAsync(cancellationToken);

    public async Task<SalonFieldDto?> CreateAsync(CreateSalonFieldRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        var label = (request.Label ?? string.Empty).Trim();
        if (label.Length == 0) { return null; }

        var key = string.IsNullOrWhiteSpace(request.FieldKey) ? Slugify(label) : Slugify(request.FieldKey);
        if (string.IsNullOrWhiteSpace(key)) { key = "campo"; }

        // FieldKey unica por (tenant, scope): si choca, agrega sufijo.
        var existing = await _db.SalonFieldDefinitions.Where(f => f.Scope == request.Scope).Select(f => f.FieldKey).ToListAsync(cancellationToken);
        var baseKey = key; var n = 2;
        while (existing.Contains(key, StringComparer.OrdinalIgnoreCase)) { key = $"{baseKey}_{n++}"; }

        var nextOrder = (await _db.SalonFieldDefinitions.Where(f => f.Scope == request.Scope).Select(f => (int?)f.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;

        var field = new SalonFieldDefinition
        {
            TenantId = tenantId,
            Scope = request.Scope,
            FieldKey = key,
            Label = label,
            FieldType = request.FieldType,
            Options = NormalizeOptions(request.Options),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Column = request.Column == 2 ? 2 : 1,
            SortOrder = nextOrder,
            IsRequired = request.IsRequired,
            ShowOnBoard = request.ShowOnBoard
        };
        _db.SalonFieldDefinitions.Add(field);
        _audit.Write(actorUserId, "salon-field.create", nameof(SalonFieldDefinition), field.Id, null, new { field.Scope, field.FieldKey, field.Label }, tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(field);
    }

    public async Task<SalonFieldDto?> UpdateAsync(Guid fieldId, UpdateSalonFieldRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var field = await _db.SalonFieldDefinitions.FirstOrDefaultAsync(f => f.Id == fieldId, cancellationToken);
        if (field is null) { return null; }
        var label = (request.Label ?? string.Empty).Trim();
        if (label.Length == 0) { return null; }

        field.Label = label;
        field.FieldType = request.FieldType;
        field.Options = NormalizeOptions(request.Options);
        field.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        field.Column = request.Column == 2 ? 2 : 1;
        field.IsRequired = request.IsRequired;
        field.ShowOnBoard = request.ShowOnBoard;
        _audit.Write(actorUserId, "salon-field.update", nameof(SalonFieldDefinition), field.Id, null, new { field.FieldKey, field.Label }, field.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(field);
    }

    public async Task<bool> DeleteAsync(Guid fieldId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var field = await _db.SalonFieldDefinitions.FirstOrDefaultAsync(f => f.Id == fieldId, cancellationToken);
        if (field is null) { return false; }
        _db.SalonFieldDefinitions.Remove(field);
        _audit.Write(actorUserId, "salon-field.delete", nameof(SalonFieldDefinition), field.Id, new { field.FieldKey, field.Label }, null, field.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task ReorderAsync(ReorderSalonFieldsRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var ids = request.OrderedFieldIds;
        if (ids.Count == 0) { return; }
        var fields = await _db.SalonFieldDefinitions.Where(f => ids.Contains(f.Id)).ToListAsync(cancellationToken);
        for (var i = 0; i < ids.Count; i++)
        {
            var f = fields.FirstOrDefault(x => x.Id == ids[i]);
            if (f is not null) { f.SortOrder = i; }
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static SalonFieldDto Map(SalonFieldDefinition f)
        => new(f.Id, f.Scope, f.FieldKey, f.Label, f.FieldType, f.Options, f.Description, f.Column, f.SortOrder, f.IsRequired, f.ShowOnBoard);

    private static string? NormalizeOptions(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return null; }
        var opts = raw.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return opts.Length == 0 ? null : string.Join("\n", opts);
    }

    // Slug estable: minusculas, sin acentos, solo letras/numeros/_.
    private static string Slugify(string s)
    {
        var n = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in n)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) { continue; }
            if (char.IsLetterOrDigit(c)) { sb.Append(c); }
            else if (c is ' ' or '-' or '_') { sb.Append('_'); }
        }
        var slug = sb.ToString().Trim('_');
        while (slug.Contains("__")) { slug = slug.Replace("__", "_"); }
        return slug;
    }
}

/// <summary>Utilidades para leer/escribir los valores de campos configurables (JSON plano clave->valor).</summary>
public static class SalonFieldJson
{
    public static IReadOnlyDictionary<string, string?> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return new Dictionary<string, string?>(); }
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new();
            var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in raw)
            {
                result[k] = v.ValueKind switch
                {
                    JsonValueKind.String => v.GetString(),
                    JsonValueKind.Number => v.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => null,
                    _ => v.GetRawText()
                };
            }
            return result;
        }
        catch { return new Dictionary<string, string?>(); }
    }

    public static string? Serialize(IReadOnlyDictionary<string, string?>? values)
    {
        if (values is null) { return null; }
        var clean = values.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value);
        return clean.Count == 0 ? null : JsonSerializer.Serialize(clean);
    }
}
