using System.Globalization;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Inventory;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Forms.Lookups;

/// <summary>
/// Adaptador de lookup sobre el Inventario de items (ola F1, doc 01 D4). El valor guardado es el
/// id del item; el catalogo esta acotado al tenant por el filtro global de EF. Expone campos
/// estandar (sku, nombre, precio, ...) y los campos dinamicos (<see cref="Item.FieldValuesJson"/>).
/// </summary>
public sealed class ItemLookupSource : IFormLookupSource
{
    private readonly IApplicationDbContext _db;
    private readonly IItemFieldService _fields;

    public ItemLookupSource(IApplicationDbContext db, IItemFieldService fields)
    {
        _db = db;
        _fields = fields;
    }

    public FormSourceKind Kind => FormSourceKind.Item;

    public async Task<FormLookupPage> SearchAsync(FormLookupRequest request, CancellationToken cancellationToken = default)
    {
        var query = ApplyFilter(_db.Items.AsNoTracking(), request.FilterJson);

        var q = request.Query?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(q))
        {
            query = query.Where(i =>
                i.Name.ToLower().Contains(q)
                || (i.Sku != null && i.Sku.ToLower().Contains(q))
                || (i.Description != null && i.Description.ToLower().Contains(q)));
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderBy(i => i.Name)
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, 100))
            .Select(i => new Row(i.Id, i.Sku, i.Name, i.Description, i.Price, i.IsActive, i.FieldValuesJson))
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => ToItem(r, request.DisplayField, request.Fields)).ToList();
        return new FormLookupPage(items, total, request.Skip + rows.Count < total);
    }

    public async Task<FormLookupItem?> ResolveAsync(string sourceRef, string value, IReadOnlyList<string> fields, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(value, out var id)) { return null; }

        var r = await _db.Items.AsNoTracking()
            .Where(i => i.Id == id)
            .Select(i => new Row(i.Id, i.Sku, i.Name, i.Description, i.Price, i.IsActive, i.FieldValuesJson))
            .FirstOrDefaultAsync(cancellationToken);

        return r is null ? null : ToItem(r, displayField: null, fields);
    }

    public Task<IReadOnlyList<FormLookupSourceOption>> ListSourcesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FormLookupSourceOption> one = new[] { new FormLookupSourceOption("", "Inventario (items)") };
        return Task.FromResult(one);
    }

    public async Task<IReadOnlyList<FormLookupFieldMeta>> DescribeFieldsAsync(string? sourceRef, CancellationToken cancellationToken = default)
    {
        var list = new List<FormLookupFieldMeta>
        {
            new("id", "Id"),
            new("sku", "SKU"),
            new("name", "Nombre"),
            new("description", "Descripcion"),
            new("price", "Precio"),
            new("active", "Activo"),
        };

        var dyn = await _fields.ListAllAsync(cancellationToken);
        foreach (var f in dyn)
        {
            if (!list.Any(x => string.Equals(x.Key, f.FieldKey, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(new FormLookupFieldMeta(f.FieldKey, string.IsNullOrWhiteSpace(f.Label) ? f.FieldKey : f.Label, IsDynamic: true));
            }
        }
        return list;
    }

    private static FormLookupItem ToItem(Row r, string? displayField, IReadOnlyList<string> fields)
    {
        var all = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = r.Id.ToString(),
            ["sku"] = r.Sku,
            ["name"] = r.Name,
            ["description"] = r.Description,
            ["price"] = r.Price?.ToString(CultureInfo.InvariantCulture),
            ["active"] = r.IsActive ? "true" : "false",
        };
        FlattenValues(r.FieldValuesJson, all);

        var display = !string.IsNullOrEmpty(displayField) && all.TryGetValue(displayField, out var d) && !string.IsNullOrEmpty(d)
            ? d! : r.Name;

        var picked = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in fields)
        {
            picked[key] = all.TryGetValue(key, out var v) ? v : null;
        }
        return new FormLookupItem(r.Id.ToString(), display, picked);
    }

    private static IQueryable<Item> ApplyFilter(IQueryable<Item> query, string? filterJson)
    {
        if (string.IsNullOrWhiteSpace(filterJson)) { return query; }
        try
        {
            using var doc = JsonDocument.Parse(filterJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { return query; }
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                switch (p.Name.ToLowerInvariant())
                {
                    case "active":
                        if (p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        {
                            var active = p.Value.GetBoolean();
                            query = query.Where(i => i.IsActive == active);
                        }
                        break;
                    case "groupid":
                        if (Guid.TryParse(p.Value.GetString(), out var gid))
                        {
                            query = query.Where(i => i.GroupId == gid);
                        }
                        break;
                    case "itemtypeid":
                        if (Guid.TryParse(p.Value.GetString(), out var tid))
                        {
                            query = query.Where(i => i.ItemTypeId == tid);
                        }
                        break;
                }
            }
        }
        catch (JsonException) { /* filtro invalido: se ignora */ }
        return query;
    }

    /// <summary>Aplana FieldValuesJson ({ campo: valor }) a campo->valor.</summary>
    private static void FlattenValues(string? json, Dictionary<string, string?> into)
    {
        if (string.IsNullOrWhiteSpace(json)) { return; }
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { return; }
            foreach (var campo in doc.RootElement.EnumerateObject())
            {
                into[campo.Name] = campo.Value.ValueKind switch
                {
                    JsonValueKind.String => campo.Value.GetString(),
                    JsonValueKind.Null => null,
                    _ => campo.Value.ToString(),
                };
            }
        }
        catch (JsonException) { /* json invalido: se ignora */ }
    }

    private sealed record Row(
        Guid Id, string? Sku, string Name, string? Description, decimal? Price, bool IsActive, string? FieldValuesJson);
}
