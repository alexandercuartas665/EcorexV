using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.DataContainers;

/// <summary>
/// Nucleo de ingesta reutilizable (doc 03 s6): escribe filas en un contenedor EAV
/// (fila + celdas por columna) con los modos Append/Replace/Upsert, EXACTAMENTE como lo hacia
/// <see cref="ApiImportService"/>. El origen se abstrae como filas ya materializadas
/// (campo -> valor string), asi lo comparten el import REST (JSON) y el importador via AGENTE
/// (FetchResult). Trabaja por sesion para permitir ingesta por chunk (SaveChanges por chunk) y
/// conservar el estado de dedup del Upsert entre chunks.
/// </summary>
public interface IRowIngestService
{
    IRowIngestSession CreateSession(
        Guid containerId,
        Guid tenantId,
        IReadOnlyDictionary<Guid, string> mapping,
        ApiImportMode mode,
        Guid? keyColumnId);
}

/// <summary>Sesion de ingesta hacia un contenedor. Ver <see cref="IRowIngestService"/>.</summary>
public interface IRowIngestSession
{
    /// <summary>Prepara la sesion: Replace vacia la tabla; Upsert precarga clave->fila y celdas.</summary>
    Task PrepareAsync(CancellationToken ct);

    /// <summary>Ingiere un lote de filas (campo->valor) y hace SaveChanges. Devuelve filas no-objeto omitidas = 0 aqui.</summary>
    Task IngestChunkAsync(IReadOnlyList<IReadOnlyDictionary<string, string?>> rows, CancellationToken ct);

    int Inserted { get; }
    int Updated { get; }
    int Deleted { get; }
}

public sealed class RowIngestService : IRowIngestService
{
    private readonly IApplicationDbContext _db;

    public RowIngestService(IApplicationDbContext db) => _db = db;

    public IRowIngestSession CreateSession(
        Guid containerId, Guid tenantId, IReadOnlyDictionary<Guid, string> mapping,
        ApiImportMode mode, Guid? keyColumnId)
        => new Session(_db, containerId, tenantId, mapping, mode, keyColumnId);

    private sealed class Session : IRowIngestSession
    {
        private readonly IApplicationDbContext _db;
        private readonly Guid _containerId;
        private readonly Guid _tenantId;
        private readonly IReadOnlyDictionary<Guid, string> _mapping;
        private readonly ApiImportMode _mode;
        private readonly Guid _keyColId;
        private readonly string? _keyField;

        private Dictionary<string, Guid>? _keyToRow;
        private Dictionary<Guid, List<DataContainerCell>>? _cellsByRow;

        public int Inserted { get; private set; }
        public int Updated { get; private set; }
        public int Deleted { get; private set; }

        public Session(IApplicationDbContext db, Guid containerId, Guid tenantId,
            IReadOnlyDictionary<Guid, string> mapping, ApiImportMode mode, Guid? keyColumnId)
        {
            _db = db;
            _containerId = containerId;
            _tenantId = tenantId;
            _mapping = mapping;
            _mode = mode;
            if (mode == ApiImportMode.Upsert && keyColumnId is Guid k && mapping.TryGetValue(k, out var kf))
            {
                _keyColId = k;
                _keyField = kf;
            }
        }

        public async Task PrepareAsync(CancellationToken ct)
        {
            if (_mode == ApiImportMode.Replace)
            {
                Deleted = await DeleteAllRowsAsync(ct);
                return;
            }

            if (_mode == ApiImportMode.Upsert)
            {
                var mappedColIds = _mapping.Keys.ToList();
                var rowIds = await _db.DataContainerRows
                    .Where(r => r.ContainerId == _containerId).Select(r => r.Id).ToListAsync(ct);
                var cells = rowIds.Count == 0
                    ? new List<DataContainerCell>()
                    : await _db.DataContainerCells
                        .Where(c => rowIds.Contains(c.RowId) && mappedColIds.Contains(c.ColumnId)).ToListAsync(ct);
                _cellsByRow = cells.GroupBy(c => c.RowId).ToDictionary(g => g.Key, g => g.ToList());
                _keyToRow = new Dictionary<string, Guid>(StringComparer.Ordinal);
                foreach (var kc in cells.Where(c => c.ColumnId == _keyColId))
                {
                    _keyToRow[kc.Value ?? ""] = kc.RowId;
                }
            }
        }

        public async Task IngestChunkAsync(IReadOnlyList<IReadOnlyDictionary<string, string?>> rows, CancellationToken ct)
        {
            foreach (var src in rows)
            {
                if (_mode == ApiImportMode.Upsert)
                {
                    var keyStr = Get(src, _keyField!) ?? "";
                    if (_keyToRow!.TryGetValue(keyStr, out var existingRowId))
                    {
                        _cellsByRow!.TryGetValue(existingRowId, out var rowCells);
                        rowCells ??= new List<DataContainerCell>();
                        foreach (var (colId, field) in _mapping)
                        {
                            var value = Get(src, field);
                            var cell = rowCells.FirstOrDefault(c => c.ColumnId == colId);
                            if (cell is not null) { cell.Value = value; }
                            else
                            {
                                var nc = new DataContainerCell { TenantId = _tenantId, RowId = existingRowId, ColumnId = colId, Value = value };
                                _db.DataContainerCells.Add(nc);
                                rowCells.Add(nc);
                            }
                        }
                        _cellsByRow[existingRowId] = rowCells;
                        Updated++;
                        continue;
                    }
                    var newRow = InsertRow(src, out var newCells);
                    _keyToRow[keyStr] = newRow.Id;
                    _cellsByRow![newRow.Id] = newCells;
                    Inserted++;
                    continue;
                }

                // Append / Replace: siempre inserta.
                InsertRow(src, out _);
                Inserted++;
            }

            await _db.SaveChangesAsync(ct);
        }

        private DataContainerRow InsertRow(IReadOnlyDictionary<string, string?> src, out List<DataContainerCell> cells)
        {
            var row = new DataContainerRow { TenantId = _tenantId, ContainerId = _containerId };
            _db.DataContainerRows.Add(row);
            cells = new List<DataContainerCell>();
            foreach (var (colId, field) in _mapping)
            {
                var cell = new DataContainerCell { TenantId = _tenantId, RowId = row.Id, ColumnId = colId, Value = Get(src, field) };
                _db.DataContainerCells.Add(cell);
                cells.Add(cell);
            }
            return row;
        }

        private async Task<int> DeleteAllRowsAsync(CancellationToken ct)
        {
            var rowIds = await _db.DataContainerRows
                .Where(r => r.ContainerId == _containerId).Select(r => r.Id).ToListAsync(ct);
            if (rowIds.Count == 0) { return 0; }

            var cells = await _db.DataContainerCells.Where(c => rowIds.Contains(c.RowId)).ToListAsync(ct);
            if (cells.Count > 0) { _db.DataContainerCells.RemoveRange(cells); }

            var links = await _db.DataContainerLinks
                .Where(l => rowIds.Contains(l.RowId) || rowIds.Contains(l.TargetRowId)).ToListAsync(ct);
            if (links.Count > 0) { _db.DataContainerLinks.RemoveRange(links); }

            var rows = await _db.DataContainerRows.Where(r => rowIds.Contains(r.Id)).ToListAsync(ct);
            _db.DataContainerRows.RemoveRange(rows);

            await _db.SaveChangesAsync(ct);
            return rowIds.Count;
        }

        private static string? Get(IReadOnlyDictionary<string, string?> src, string field)
            => src.TryGetValue(field, out var v) ? v : null;
    }
}
