using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.DataLookups;

/// <summary>
/// Implementacion del motor de listas. La busqueda se delega en
/// <see cref="IDataContainerService.ListRowsPagedAsync"/>, que ya resuelve texto libre + filtros
/// por columna EN EL SERVIDOR y de forma compatible con PostgreSQL y SQL Server; aqui solo se
/// arma la etiqueta y se recortan las columnas pedidas.
/// </summary>
public sealed class DataLookupService : IDataLookupService
{
    private readonly IApplicationDbContext _db;
    private readonly IDataContainerService _containers;

    public DataLookupService(IApplicationDbContext db, IDataContainerService containers)
    {
        _db = db;
        _containers = containers;
    }

    public async Task<IReadOnlyList<LookupModelDto>> ListModelsAsync(CancellationToken cancellationToken = default)
        => await _db.DataModels.AsNoTracking()
            .OrderBy(m => m.Name)
            .Select(m => new LookupModelDto(m.Id, m.Name, m.Tables.Count))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LookupTableDto>> ListTablesAsync(
        Guid? modelId = null, CancellationToken cancellationToken = default)
    {
        // Solo tablas RAIZ: las anidadas (submodelo) se llenan desde su tabla padre, no se
        // eligen sueltas como origen de una lista.
        var q = _db.DataContainers.AsNoTracking().Where(c => c.ParentContainerId == null);
        if (modelId is Guid mid) { q = q.Where(c => c.ModelId == mid); }
        return await q
            .OrderBy(c => c.Name)
            .Select(c => new LookupTableDto(c.Id, c.Name, c.ModelId, c.Model!.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LookupColumnDto>> ListColumnsAsync(
        Guid tableId, CancellationToken cancellationToken = default)
        => await _db.DataContainerColumns.AsNoTracking()
            .Where(c => c.ContainerId == tableId)
            .OrderBy(c => c.SortOrder)
            .Select(c => new LookupColumnDto(c.Id, c.Name, c.Type, c.SortOrder))
            .ToListAsync(cancellationToken);

    public async Task<DataLookupPageDto> SearchAsync(
        DataLookupQuery query, CancellationToken cancellationToken = default)
    {
        var displayId = await ResolveDisplayColumnAsync(query.TableId, query.DisplayColumnId, cancellationToken);

        var page = await _containers.ListRowsPagedAsync(new DataRowQuery(
            ContainerId: query.TableId,
            Search: query.Search,
            Filters: query.Filters,
            SortColumnId: displayId,
            SortDescending: false,
            Page: query.Page < 1 ? 1 : query.Page,
            PageSize: query.PageSize), cancellationToken);

        var wanted = ColumnsToKeep(displayId, query.ExtraColumnIds);
        var rows = page.Rows.Select(r => ToRow(r.Id, r.ValuesByColumnId, displayId, wanted)).ToList();
        return new DataLookupPageDto(rows, page.Total, page.Page, page.PageSize);
    }

    public async Task<IReadOnlyList<DataLookupRowDto>> ResolveAsync(
        Guid tableId,
        IReadOnlyList<Guid> rowIds,
        Guid? displayColumnId = null,
        IReadOnlyList<Guid>? extraColumnIds = null,
        CancellationToken cancellationToken = default)
    {
        if (rowIds.Count == 0) { return []; }
        var ids = rowIds.Distinct().ToList();
        var displayId = await ResolveDisplayColumnAsync(tableId, displayColumnId, cancellationToken);
        var wanted = ColumnsToKeep(displayId, extraColumnIds);

        // Se traen solo las celdas de las columnas que interesan: una fila puede tener decenas.
        var celdas = await _db.DataContainerCells.AsNoTracking()
            .Where(c => ids.Contains(c.RowId)
                && c.Row!.ContainerId == tableId
                && (wanted == null || wanted.Contains(c.ColumnId)))
            .Select(c => new { c.RowId, c.ColumnId, c.Value })
            .ToListAsync(cancellationToken);

        // Se respeta el orden en que vinieron los ids (el consumidor suele querer ese orden).
        var porFila = celdas.GroupBy(c => c.RowId)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<Guid, string?>)g.ToDictionary(x => x.ColumnId, x => x.Value));

        var vivas = await _db.DataContainerRows.AsNoTracking()
            .Where(r => ids.Contains(r.Id) && r.ContainerId == tableId)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);
        var existentes = vivas.ToHashSet();

        return ids.Where(existentes.Contains)
            .Select(id => ToRow(
                id,
                porFila.TryGetValue(id, out var vals) ? vals : new Dictionary<Guid, string?>(),
                displayId,
                wanted))
            .ToList();
    }

    public async Task<DataLookupPageDto> SearchForFieldAsync(
        DataLookupConfig config,
        IReadOnlyDictionary<string, string?>? formValues,
        string? search = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var resolucion = DataLookupBinder.ResolveFilters(config, formValues);
        if (resolucion.Blocked)
        {
            // Falta el campo padre: no se consulta nada. La UI distingue este caso por Total = 0
            // con la pagina pedida, y muestra "elige primero X" en vez de "sin resultados".
            return new DataLookupPageDto([], 0, page, pageSize);
        }

        return await SearchAsync(new DataLookupQuery(
            config.TableId,
            Search: search,
            DisplayColumnId: config.DisplayColumnId,
            Filters: resolucion.Filters.Count == 0 ? null : resolucion.Filters,
            ExtraColumnIds: DataLookupBinder.ColumnsNeeded(config),
            Page: page,
            PageSize: pageSize), cancellationToken);
    }

    /// <summary>
    /// Columna que se muestra. Si no se configuro una, se cae a la MISMA heuristica que ya usa el
    /// Contenedor de datos (primera columna de texto por orden) para que la etiqueta no cambie
    /// segun por donde se mire la tabla.
    /// </summary>
    private async Task<Guid?> ResolveDisplayColumnAsync(
        Guid tableId, Guid? preferida, CancellationToken cancellationToken)
    {
        if (preferida is Guid p)
        {
            // Se valida que siga existiendo: una columna borrada no debe romper el selector.
            var vive = await _db.DataContainerColumns.AsNoTracking()
                .AnyAsync(c => c.Id == p && c.ContainerId == tableId, cancellationToken);
            if (vive) { return p; }
        }

        var columnas = await _db.DataContainerColumns.AsNoTracking()
            .Where(c => c.ContainerId == tableId)
            .OrderBy(c => c.SortOrder)
            .Select(c => new { c.Id, c.Type })
            .ToListAsync(cancellationToken);

        var texto = columnas.FirstOrDefault(c => c.Type == DataContainerColumnType.Text);
        if (texto is not null) { return texto.Id; }
        var primera = columnas.FirstOrDefault(c => c.Type != DataContainerColumnType.Submodel);
        return primera?.Id;
    }

    /// <summary>null = traer todas las columnas (no se pidio recorte).</summary>
    private static HashSet<Guid>? ColumnsToKeep(Guid? displayId, IReadOnlyList<Guid>? extra)
    {
        if (extra is null) { return null; }
        var set = new HashSet<Guid>(extra);
        if (displayId is Guid d) { set.Add(d); }
        return set;
    }

    private static DataLookupRowDto ToRow(
        Guid rowId,
        IReadOnlyDictionary<Guid, string?> values,
        Guid? displayId,
        HashSet<Guid>? wanted)
    {
        var recortados = wanted is null
            ? values
            : values.Where(kv => wanted.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);

        var etiqueta = displayId is Guid d && recortados.TryGetValue(d, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v!
            : $"#{rowId.ToString()[..8]}";

        return new DataLookupRowDto(rowId, etiqueta, recortados);
    }
}
