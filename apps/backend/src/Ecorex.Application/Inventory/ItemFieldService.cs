using System.Globalization;
using System.Text;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Inventory;

/// <summary>
/// Implementacion de IItemFieldService (campos configurables del item POR tipo, 000066).
/// Aislamiento por tenant via filtro global (nunca se filtra a mano por TenantId); el alta
/// estampa el TenantId del contexto. Calcado de TerceroFieldService, agrupando por ItemType en
/// vez de por ficha. La clave (slug) es unica por (tenant, tipo).
/// </summary>
public sealed class ItemFieldService : IItemFieldService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public ItemFieldService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<ItemFieldDto>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await _db.ItemFieldDefinitions
            .AsNoTracking()
            .OrderBy(f => f.ItemTypeId).ThenBy(f => f.SortOrder)
            .Select(f => new ItemFieldDto(f.Id, f.ItemTypeId, f.FieldKey, f.Label, f.FieldType, f.Column, f.SortOrder, f.Options, f.Description, f.IsRequired, f.IsSystem))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ItemFieldDto>> ListByTypeAsync(Guid itemTypeId, CancellationToken cancellationToken = default) =>
        await _db.ItemFieldDefinitions
            .AsNoTracking()
            .Where(f => f.ItemTypeId == itemTypeId)
            .OrderBy(f => f.SortOrder)
            .Select(f => new ItemFieldDto(f.Id, f.ItemTypeId, f.FieldKey, f.Label, f.FieldType, f.Column, f.SortOrder, f.Options, f.Description, f.IsRequired, f.IsSystem))
            .ToListAsync(cancellationToken);

    public async Task<ItemFieldDto?> CreateFieldAsync(CreateItemFieldRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
        {
            return null;
        }
        // El tipo debe existir en el tenant (el filtro global lo restringe al tenant activo).
        if (!await _db.ItemTypes.AnyAsync(t => t.Id == request.ItemTypeId, cancellationToken))
        {
            return null;
        }
        var label = (request.Label ?? string.Empty).Trim();
        if (label.Length == 0)
        {
            return null;
        }

        var key = string.IsNullOrWhiteSpace(request.FieldKey) ? Slugify(label) : request.FieldKey.Trim();
        var existingKeys = await _db.ItemFieldDefinitions
            .Where(f => f.ItemTypeId == request.ItemTypeId).Select(f => f.FieldKey).ToListAsync(cancellationToken);
        key = EnsureUniqueKey(key, existingKeys);

        var maxOrder = await _db.ItemFieldDefinitions
            .Where(f => f.ItemTypeId == request.ItemTypeId).Select(f => (int?)f.SortOrder).MaxAsync(cancellationToken) ?? -1;
        var field = new ItemFieldDefinition
        {
            TenantId = tenantId,
            ItemTypeId = request.ItemTypeId,
            FieldKey = key,
            Label = label,
            FieldType = request.FieldType,
            Column = Math.Clamp(request.Column, 1, 2),
            SortOrder = maxOrder + 1,
            Options = string.IsNullOrWhiteSpace(request.Options) ? null : request.Options.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            IsRequired = request.IsRequired,
            IsSystem = false
        };
        _db.ItemFieldDefinitions.Add(field);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(field);
    }

    public async Task<ItemFieldDto?> UpdateFieldAsync(Guid fieldId, UpdateItemFieldRequest request, CancellationToken cancellationToken = default)
    {
        var field = await _db.ItemFieldDefinitions.FirstOrDefaultAsync(f => f.Id == fieldId, cancellationToken);
        if (field is null)
        {
            return null;
        }
        var label = (request.Label ?? string.Empty).Trim();
        if (label.Length == 0)
        {
            return null;
        }
        field.Label = label;
        field.FieldType = request.FieldType;
        field.Column = Math.Clamp(request.Column, 1, 2);
        field.Options = string.IsNullOrWhiteSpace(request.Options) ? null : request.Options.Trim();
        field.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        field.IsRequired = request.IsRequired;
        await _db.SaveChangesAsync(cancellationToken);
        return Map(field);
    }

    public async Task<bool> DeleteFieldAsync(Guid fieldId, CancellationToken cancellationToken = default)
    {
        var field = await _db.ItemFieldDefinitions.FirstOrDefaultAsync(f => f.Id == fieldId, cancellationToken);
        if (field is null)
        {
            return false;
        }
        _db.ItemFieldDefinitions.Remove(field);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static ItemFieldDto Map(ItemFieldDefinition f) =>
        new(f.Id, f.ItemTypeId, f.FieldKey, f.Label, f.FieldType, f.Column, f.SortOrder, f.Options, f.Description, f.IsRequired, f.IsSystem);

    private static string EnsureUniqueKey(string key, IReadOnlyCollection<string> existing)
    {
        if (!existing.Contains(key))
        {
            return key;
        }
        var i = 2;
        while (existing.Contains($"{key}{i}"))
        {
            i++;
        }
        return $"{key}{i}";
    }

    private static string Slugify(string label)
    {
        var normalized = label.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) { continue; }
            if (char.IsLetterOrDigit(c)) { sb.Append(c); }
            else if (sb.Length > 0 && sb[^1] != '_') { sb.Append('_'); }
        }
        var slug = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(slug) ? "campo" : slug;
    }
}
