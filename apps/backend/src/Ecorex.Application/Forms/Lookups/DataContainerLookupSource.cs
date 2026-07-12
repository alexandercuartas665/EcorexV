using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Forms.Lookups;

/// <summary>
/// Adaptador de lookup sobre un contenedor de datos generico (ola F1, doc 01 D4). SourceRef es el
/// id del contenedor (tabla). El valor guardado es el id de la FILA; DisplayField y el mapa de
/// autollenado usan NOMBRES de columna del contenedor. Reusa <see cref="IDataContainerService"/>
/// (tenant-scoped por el filtro global) para no re-consultar celdas a mano.
/// </summary>
public sealed class DataContainerLookupSource : IFormLookupSource
{
    // Tope de filas que se traen para paginar en memoria (los contenedores no son catalogos
    // masivos como Directorio/Inventario). Si un contenedor supera esto, la pagina se acota.
    private const int FetchCap = 500;

    private readonly IDataContainerService _containers;
    private readonly IApplicationDbContext _db;

    public DataContainerLookupSource(IDataContainerService containers, IApplicationDbContext db)
    {
        _containers = containers;
        _db = db;
    }

    public FormSourceKind Kind => FormSourceKind.DataContainer;

    public async Task<FormLookupPage> SearchAsync(FormLookupRequest request, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(request.SourceRef, out var containerId))
        {
            return new FormLookupPage(Array.Empty<FormLookupItem>(), 0, false);
        }

        var nameToCol = await GetColumnMapAsync(containerId, cancellationToken);
        if (nameToCol is null) { return new FormLookupPage(Array.Empty<FormLookupItem>(), 0, false); }

        var rows = await _containers.ListRowsAsync(containerId, request.Query, parentRowId: null, take: FetchCap, ct: cancellationToken);

        var total = rows.Count;
        var page = rows
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, 100))
            .Select(r => ToItem(r.Id, r.ValuesByColumnId, nameToCol, request.DisplayField, request.Fields))
            .ToList();

        return new FormLookupPage(page, total, request.Skip + page.Count < total);
    }

    public async Task<FormLookupItem?> ResolveAsync(string sourceRef, string value, IReadOnlyList<string> fields, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(sourceRef, out var containerId) || !Guid.TryParse(value, out var rowId))
        {
            return null;
        }

        var nameToCol = await GetColumnMapAsync(containerId, cancellationToken);
        if (nameToCol is null) { return null; }

        // Valida que la fila EXISTA, sea de ESE contenedor y del tenant (filtro global).
        var exists = await _db.DataContainerRows.AsNoTracking()
            .AnyAsync(r => r.Id == rowId && r.ContainerId == containerId, cancellationToken);
        if (!exists) { return null; }

        var cells = await _db.DataContainerCells.AsNoTracking()
            .Where(c => c.RowId == rowId)
            .Select(c => new { c.ColumnId, c.Value })
            .ToListAsync(cancellationToken);
        var values = cells.ToDictionary(c => c.ColumnId, c => c.Value);

        return ToItem(rowId, values, nameToCol, displayField: null, fields);
    }

    public async Task<IReadOnlyList<FormLookupSourceOption>> ListSourcesAsync(CancellationToken cancellationToken = default)
    {
        var all = await _containers.ListAsync(cancellationToken);
        return all.Select(c => new FormLookupSourceOption(c.Id.ToString(), c.Name)).ToList();
    }

    public async Task<IReadOnlyList<FormLookupFieldMeta>> DescribeFieldsAsync(string? sourceRef, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(sourceRef, out var containerId)) { return Array.Empty<FormLookupFieldMeta>(); }
        var detail = await _containers.GetAsync(containerId, cancellationToken);
        if (detail is null) { return Array.Empty<FormLookupFieldMeta>(); }

        var list = new List<FormLookupFieldMeta> { new("id", "Id (fila)") };
        foreach (var col in detail.Columns.OrderBy(c => c.SortOrder))
        {
            list.Add(new FormLookupFieldMeta(col.Name, col.Name));
        }
        return list;
    }

    private async Task<Dictionary<string, Guid>?> GetColumnMapAsync(Guid containerId, CancellationToken cancellationToken)
    {
        var detail = await _containers.GetAsync(containerId, cancellationToken);
        if (detail is null) { return null; }
        var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in detail.Columns) { map[col.Name] = col.Id; }
        return map;
    }

    private static FormLookupItem ToItem(
        Guid rowId, IReadOnlyDictionary<Guid, string?> valuesByCol, Dictionary<string, Guid> nameToCol,
        string? displayField, IReadOnlyList<string> fields)
    {
        string? ValueOf(string fieldName)
        {
            if (string.Equals(fieldName, "id", StringComparison.OrdinalIgnoreCase)) { return rowId.ToString(); }
            return nameToCol.TryGetValue(fieldName, out var colId) && valuesByCol.TryGetValue(colId, out var v) ? v : null;
        }

        var display = !string.IsNullOrEmpty(displayField) ? ValueOf(displayField) : null;
        if (string.IsNullOrEmpty(display))
        {
            // Fallback: primera columna con valor, o el id.
            display = valuesByCol.Values.FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? rowId.ToString();
        }

        var picked = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in fields) { picked[key] = ValueOf(key); }

        return new FormLookupItem(rowId.ToString(), display!, picked);
    }
}
