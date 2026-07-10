using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.DataContainers;

/// <summary>
/// Servicio del Contenedor de datos (modelos dinamicos EAV con anidamiento). Portado de la
/// implementacion probada de CUBOT.redmanager y evolucionado al ARBOL de contenedores/filas.
///
/// Reglas clave:
/// - ListAsync devuelve SOLO contenedores raiz (ParentContainerId == null).
/// - SaveAsync respeta ParentContainerId/ParentFieldId (submodelos) y hace replace-all de columnas
///   por Id; NO permite borrar una columna que ya tiene celdas (error claro al usuario).
/// - SaveRow: upsert celda por celda, solo para columnas ESCALARES (Submodel no guarda celdas).
/// - Import/Export Excel operan sobre las columnas ESCALARES del contenedor (excluye Submodel).
/// - Tenant-scoped por el filtro global. El borrado de un contenedor arrastra su arbol (cascada BD).
/// </summary>
public sealed class DataContainerService : IDataContainerService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;

    public DataContainerService(IApplicationDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<DataContainerDto>> ListAsync(CancellationToken ct = default)
    {
        // Solo contenedores RAIZ; los submodelos no aparecen en el listado principal.
        var containers = await _db.DataContainers.AsNoTracking()
            .Where(c => c.ParentContainerId == null)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
        if (containers.Count == 0) { return Array.Empty<DataContainerDto>(); }

        var ids = containers.Select(c => c.Id).ToList();
        var colCounts = await _db.DataContainerColumns.AsNoTracking()
            .Where(c => ids.Contains(c.ContainerId))
            .GroupBy(c => c.ContainerId)
            .Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.C, ct);
        var rowCounts = await _db.DataContainerRows.AsNoTracking()
            .Where(r => ids.Contains(r.ContainerId))
            .GroupBy(r => r.ContainerId)
            .Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.C, ct);

        return containers.Select(c => new DataContainerDto(
            c.Id,
            c.Name,
            c.Description,
            c.SourceKind,
            colCounts.TryGetValue(c.Id, out var cc) ? cc : 0,
            rowCounts.TryGetValue(c.Id, out var rc) ? rc : 0,
            c.UpdatedAt ?? c.CreatedAt)).ToList();
    }

    public async Task<DataContainerDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var c = await _db.DataContainers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) { return null; }
        return await BuildDetailAsync(c, ct);
    }

    public async Task<IReadOnlyList<DataContainerDetailDto>> ListChildrenAsync(Guid parentContainerId, CancellationToken ct = default)
    {
        var children = await _db.DataContainers.AsNoTracking()
            .Where(c => c.ParentContainerId == parentContainerId)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
        if (children.Count == 0) { return Array.Empty<DataContainerDetailDto>(); }

        var result = new List<DataContainerDetailDto>(children.Count);
        foreach (var child in children)
        {
            result.Add(await BuildDetailAsync(child, ct));
        }
        return result;
    }

    public async Task<DataContainerDetailDto?> SaveAsync(SaveDataContainerRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) { return null; }
        if (req.Columns is null || req.Columns.Count == 0) { return null; }

        var joinTx = _db.HasActiveTransaction;
        var tx = joinTx ? null : await _db.BeginTransactionAsync(ct);
        try
        {
            DataContainer entity;
            if (req.Id is { } id)
            {
                var existingEntity = await _db.DataContainers.FirstOrDefaultAsync(x => x.Id == id, ct);
                if (existingEntity is null) { return null; }
                entity = existingEntity;
                entity.Name = name;
                entity.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description!.Trim();
                entity.SourceKind = req.SourceKind;
                entity.ParentContainerId = req.ParentContainerId;
                entity.ParentFieldId = req.ParentFieldId;
            }
            else
            {
                entity = new DataContainer
                {
                    TenantId = tenantId,
                    Name = name,
                    Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description!.Trim(),
                    SourceKind = req.SourceKind,
                    ParentContainerId = req.ParentContainerId,
                    ParentFieldId = req.ParentFieldId
                };
                _db.DataContainers.Add(entity);
            }

            // Columnas: replace-all por id (logica compartida con SaveTableAsync).
            await ApplyColumnsAsync(entity, req.Columns, tenantId, ct);

            await _db.SaveChangesAsync(ct);
            if (tx is not null) { await tx.CommitAsync(ct); }
            return await GetAsync(entity.Id, ct);
        }
        catch
        {
            if (tx is not null) { await tx.RollbackAsync(ct); }
            throw;
        }
        finally
        {
            if (tx is not null) { await tx.DisposeAsync(); }
        }
    }

    public async Task<DataContainerDetailDto?> SaveTableAsync(
        Guid modelId, Guid? tableId, string name, string? desc, double x, double y,
        IReadOnlyList<SaveDataColumnInput> columns, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var cleanName = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(cleanName)) { return null; }
        columns ??= Array.Empty<SaveDataColumnInput>();

        var joinTx = _db.HasActiveTransaction;
        var tx = joinTx ? null : await _db.BeginTransactionAsync(ct);
        try
        {
            DataContainer entity;
            if (tableId is { } id)
            {
                var existingEntity = await _db.DataContainers.FirstOrDefaultAsync(x => x.Id == id, ct);
                if (existingEntity is null) { return null; }
                entity = existingEntity;
                entity.Name = cleanName;
                entity.Description = string.IsNullOrWhiteSpace(desc) ? null : desc!.Trim();
                entity.ModelId = modelId;
                entity.CanvasX = x;
                entity.CanvasY = y;
            }
            else
            {
                entity = new DataContainer
                {
                    TenantId = tenantId,
                    Name = cleanName,
                    Description = string.IsNullOrWhiteSpace(desc) ? null : desc!.Trim(),
                    ModelId = modelId,
                    CanvasX = x,
                    CanvasY = y
                };
                _db.DataContainers.Add(entity);
            }

            await ApplyColumnsAsync(entity, columns, tenantId, ct);
            await _db.SaveChangesAsync(ct);
            if (tx is not null) { await tx.CommitAsync(ct); }
            return await GetAsync(entity.Id, ct);
        }
        catch
        {
            if (tx is not null) { await tx.RollbackAsync(ct); }
            throw;
        }
        finally
        {
            if (tx is not null) { await tx.DisposeAsync(); }
        }
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var entity = await _db.DataContainers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) { return false; }
        // Guard: no se puede borrar una tabla referenciada por un campo Reference/RelationMany de otra
        // tabla (FK Restrict). Se avisa con un mensaje claro (que la UI muestra).
        var referencedByName = await _db.DataContainerColumns.AsNoTracking()
            .Where(cc => cc.ReferencedContainerId == id)
            .Select(cc => _db.DataContainers.Where(o => o.Id == cc.ContainerId).Select(o => o.Name).FirstOrDefault())
            .FirstOrDefaultAsync(ct);
        if (referencedByName is not null)
        {
            throw new InvalidOperationException(
                $"No se puede eliminar esta tabla: esta referenciada por la tabla '{referencedByName}'. Quita primero esa relacion.");
        }
        // La cascada de BD borra el arbol completo (sub-contenedores, columnas, filas, celdas,
        // conectores y procesos). No se borra a mano.
        _db.DataContainers.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<DataContainerRowDto>> ListRowsAsync(Guid containerId, string? search = null, Guid? parentRowId = null, int take = 500, CancellationToken ct = default)
    {
        var rows = await _db.DataContainerRows.AsNoTracking()
            .Where(r => r.ContainerId == containerId && r.ParentRowId == parentRowId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(take)
            .ToListAsync(ct);
        if (rows.Count == 0) { return Array.Empty<DataContainerRowDto>(); }

        var rowIds = rows.Select(r => r.Id).ToList();
        var cells = await _db.DataContainerCells.AsNoTracking()
            .Where(c => rowIds.Contains(c.RowId))
            .ToListAsync(ct);
        var links = await _db.DataContainerLinks.AsNoTracking()
            .Where(l => rowIds.Contains(l.RowId))
            .ToListAsync(ct);

        var grouped = cells.GroupBy(c => c.RowId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.ColumnId, x => x.Value));
        var linksByRow = links.GroupBy(l => l.RowId)
            .ToDictionary(g => g.Key, g => GroupLinks(g.ToList()));

        var dtos = rows.Select(r => new DataContainerRowDto(
            r.Id,
            r.CreatedAt,
            grouped.TryGetValue(r.Id, out var d) ? d : new Dictionary<Guid, string?>(),
            linksByRow.TryGetValue(r.Id, out var lk) ? lk : null
        )).ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            dtos = dtos.Where(d => d.ValuesByColumnId.Values.Any(v =>
                !string.IsNullOrEmpty(v) && v!.ToLowerInvariant().Contains(s))).ToList();
        }

        return dtos;
    }

    public async Task<IReadOnlyList<RowOptionDto>> ListRowOptionsAsync(Guid containerId, int take = 500, CancellationToken ct = default)
    {
        // Columna etiqueta: la primera Text por orden; si no hay, la primera columna con celda.
        var cols = await _db.DataContainerColumns.AsNoTracking()
            .Where(c => c.ContainerId == containerId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new { c.Id, c.Type })
            .ToListAsync(ct);
        var labelColId = cols.FirstOrDefault(c => c.Type == DataContainerColumnType.Text)?.Id
            ?? cols.FirstOrDefault(c => IsCellColumn(c.Type))?.Id;

        var rowIds = await _db.DataContainerRows.AsNoTracking()
            .Where(r => r.ContainerId == containerId && r.ParentRowId == null)
            .OrderByDescending(r => r.CreatedAt)
            .Take(take)
            .Select(r => r.Id)
            .ToListAsync(ct);
        if (rowIds.Count == 0) { return Array.Empty<RowOptionDto>(); }

        var labelByRow = new Dictionary<Guid, string?>();
        if (labelColId is Guid lc)
        {
            labelByRow = await _db.DataContainerCells.AsNoTracking()
                .Where(c => c.ColumnId == lc && rowIds.Contains(c.RowId))
                .ToDictionaryAsync(c => c.RowId, c => c.Value, ct);
        }

        return rowIds.Select(id =>
        {
            var label = labelByRow.TryGetValue(id, out var v) && !string.IsNullOrWhiteSpace(v)
                ? v!.Trim()
                : $"#{id.ToString()[..8]}";
            return new RowOptionDto(id, label);
        }).ToList();
    }

    public async Task<DataContainerRowDto?> SaveRowAsync(SaveDataRowRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var container = await _db.DataContainers.FirstOrDefaultAsync(c => c.Id == req.ContainerId, ct);
        if (container is null) { return null; }

        // Clasificar columnas: las que guardan celda (escalares + Reference) y las de relacion N:N.
        var cols = await _db.DataContainerColumns.AsNoTracking()
            .Where(c => c.ContainerId == req.ContainerId)
            .Select(c => new { c.Id, c.Type })
            .ToListAsync(ct);
        var cellSet = cols.Where(c => IsCellColumn(c.Type)).Select(c => c.Id).ToHashSet();
        var relationSet = cols.Where(c => c.Type == DataContainerColumnType.RelationMany).Select(c => c.Id).ToHashSet();

        DataContainerRow row;
        if (req.RowId is { } rid)
        {
            var existing = await _db.DataContainerRows.FirstOrDefaultAsync(r => r.Id == rid, ct);
            if (existing is null) { return null; }
            row = existing;
        }
        else
        {
            row = new DataContainerRow
            {
                TenantId = tenantId,
                ContainerId = req.ContainerId,
                ParentRowId = req.ParentRowId,
                ParentFieldId = req.ParentFieldId
            };
            _db.DataContainerRows.Add(row);
        }

        // Upsert celdas (solo columnas escalares).
        var existingCells = req.RowId is { } existRid
            ? await _db.DataContainerCells.Where(c => c.RowId == existRid).ToListAsync(ct)
            : new List<DataContainerCell>();

        foreach (var kv in req.ValuesByColumnId)
        {
            if (!cellSet.Contains(kv.Key)) { continue; }
            var cell = existingCells.FirstOrDefault(c => c.ColumnId == kv.Key);
            if (cell is null)
            {
                _db.DataContainerCells.Add(new DataContainerCell
                {
                    TenantId = tenantId,
                    RowId = row.Id,
                    ColumnId = kv.Key,
                    Value = kv.Value
                });
            }
            else
            {
                cell.Value = kv.Value;
            }
        }

        // Relaciones N:N: reemplaza el set de vinculos por (columna, fila) segun LinksByColumnId.
        if (req.LinksByColumnId is not null)
        {
            foreach (var kv in req.LinksByColumnId)
            {
                if (!relationSet.Contains(kv.Key)) { continue; }
                var desired = kv.Value?.ToHashSet() ?? new HashSet<Guid>();
                var currentLinks = await _db.DataContainerLinks
                    .Where(l => l.ColumnId == kv.Key && l.RowId == row.Id).ToListAsync(ct);
                foreach (var link in currentLinks.Where(l => !desired.Contains(l.TargetRowId)))
                {
                    _db.DataContainerLinks.Remove(link);
                }
                var currentTargets = currentLinks.Select(l => l.TargetRowId).ToHashSet();
                foreach (var target in desired.Where(t => !currentTargets.Contains(t)))
                {
                    _db.DataContainerLinks.Add(new DataContainerLink
                    {
                        TenantId = tenantId,
                        ColumnId = kv.Key,
                        RowId = row.Id,
                        TargetRowId = target
                    });
                }
            }
        }

        await _db.SaveChangesAsync(ct);

        // Releer celdas + vinculos para devolver el snapshot completo.
        var allCells = await _db.DataContainerCells.AsNoTracking()
            .Where(c => c.RowId == row.Id)
            .ToListAsync(ct);
        var links = await _db.DataContainerLinks.AsNoTracking()
            .Where(l => l.RowId == row.Id)
            .ToListAsync(ct);
        return new DataContainerRowDto(row.Id, row.CreatedAt,
            allCells.ToDictionary(c => c.ColumnId, c => c.Value),
            GroupLinks(links));
    }

    public async Task<bool> DeleteRowAsync(Guid rowId, Guid actorUserId, CancellationToken ct = default)
    {
        var row = await _db.DataContainerRows.FirstOrDefaultAsync(r => r.Id == rowId, ct);
        if (row is null) { return false; }
        // Limpiar vinculos N:N donde esta fila es origen (cascada) o DESTINO (FK Restrict): hay que
        // borrarlos a mano antes de eliminar la fila para no violar la FK.
        var relatedLinks = await _db.DataContainerLinks
            .Where(l => l.RowId == rowId || l.TargetRowId == rowId).ToListAsync(ct);
        if (relatedLinks.Count > 0) { _db.DataContainerLinks.RemoveRange(relatedLinks); }
        _db.DataContainerRows.Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<DataImportResult> ImportFromExcelAsync(Guid containerId, Stream xlsxStream, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return new DataImportResult(false, 0, 0, new[] { "Sin tenant activo." });
        }
        var container = await _db.DataContainers.FirstOrDefaultAsync(c => c.Id == containerId, ct);
        if (container is null)
        {
            return new DataImportResult(false, 0, 0, new[] { "Modelo no encontrado." });
        }
        // Solo columnas ESCALARES participan del Excel (las de tipo Submodel se excluyen).
        var columns = await _db.DataContainerColumns.AsNoTracking()
            .Where(c => c.ContainerId == containerId && (
                c.Type == DataContainerColumnType.Text || c.Type == DataContainerColumnType.Number ||
                c.Type == DataContainerColumnType.Decimal || c.Type == DataContainerColumnType.Date ||
                c.Type == DataContainerColumnType.Boolean))
            .OrderBy(c => c.SortOrder)
            .ToListAsync(ct);
        if (columns.Count == 0)
        {
            return new DataImportResult(false, 0, 0, new[] { "El modelo no tiene columnas definidas." });
        }

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(xlsxStream);
        }
        catch (Exception ex)
        {
            return new DataImportResult(false, 0, 0, new[] { $"No se pudo leer el archivo Excel: {ex.Message}" });
        }

        using (workbook)
        {
            var sheet = workbook.Worksheets.FirstOrDefault();
            if (sheet is null)
            {
                return new DataImportResult(false, 0, 0, new[] { "El archivo no contiene hojas." });
            }

            var firstRow = sheet.FirstRowUsed();
            if (firstRow is null)
            {
                return new DataImportResult(false, 0, 0, new[] { "El archivo esta vacio." });
            }

            // Indexar headers por nombre normalizado -> numero de columna.
            var headerMap = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var cell in firstRow.CellsUsed())
            {
                var key = NormalizeHeader(cell.GetString());
                if (!string.IsNullOrEmpty(key) && !headerMap.ContainsKey(key))
                {
                    headerMap[key] = cell.Address.ColumnNumber;
                }
            }

            // Mapear: cada columna del modelo -> indice de columna en el Excel (o -1).
            var colMap = new Dictionary<Guid, int>();
            var missing = new List<string>();
            foreach (var col in columns)
            {
                var key = NormalizeHeader(col.Name);
                if (headerMap.TryGetValue(key, out var idx))
                {
                    colMap[col.Id] = idx;
                }
                else
                {
                    missing.Add(col.Name);
                }
            }
            if (missing.Count > 0)
            {
                return new DataImportResult(false, 0, 0, new[]
                {
                    "El Excel no contiene las siguientes columnas requeridas por el modelo: " + string.Join(", ", missing)
                });
            }

            var imported = 0;
            var failed = 0;
            var errors = new List<string>();
            var lastRow = sheet.LastRowUsed();
            var startRow = firstRow.RowNumber() + 1;
            var endRow = lastRow?.RowNumber() ?? startRow - 1;

            for (var rowNumber = startRow; rowNumber <= endRow; rowNumber++)
            {
                var xlRow = sheet.Row(rowNumber);
                if (xlRow.IsEmpty()) { continue; }

                var rowValues = new Dictionary<Guid, string?>();
                string? rowError = null;

                foreach (var col in columns)
                {
                    if (!colMap.TryGetValue(col.Id, out var colIndex)) { continue; }
                    var cell = xlRow.Cell(colIndex);
                    var raw = ExtractValue(cell, col.Type);
                    if (col.IsRequired && string.IsNullOrWhiteSpace(raw))
                    {
                        rowError = $"Fila {rowNumber}: la columna obligatoria '{col.Name}' esta vacia.";
                        break;
                    }
                    rowValues[col.Id] = raw;
                }

                if (rowError is not null)
                {
                    failed++;
                    if (errors.Count < 20) { errors.Add(rowError); }
                    continue;
                }

                var row = new DataContainerRow
                {
                    TenantId = tenantId,
                    ContainerId = containerId
                };
                _db.DataContainerRows.Add(row);
                foreach (var kv in rowValues)
                {
                    _db.DataContainerCells.Add(new DataContainerCell
                    {
                        TenantId = tenantId,
                        RowId = row.Id,
                        ColumnId = kv.Key,
                        Value = kv.Value
                    });
                }
                imported++;
            }

            await _db.SaveChangesAsync(ct);

            return new DataImportResult(imported > 0 || failed == 0, imported, failed, errors);
        }
    }

    public async Task<DataExportResult?> ExportToExcelAsync(Guid containerId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is null) { return null; }
        var container = await _db.DataContainers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == containerId, ct);
        if (container is null) { return null; }

        // Solo columnas ESCALARES se exportan (las de tipo Submodel se excluyen).
        var columns = await _db.DataContainerColumns.AsNoTracking()
            .Where(c => c.ContainerId == containerId && (
                c.Type == DataContainerColumnType.Text || c.Type == DataContainerColumnType.Number ||
                c.Type == DataContainerColumnType.Decimal || c.Type == DataContainerColumnType.Date ||
                c.Type == DataContainerColumnType.Boolean))
            .OrderBy(c => c.SortOrder)
            .ToListAsync(ct);
        var rows = await _db.DataContainerRows.AsNoTracking()
            .Where(r => r.ContainerId == containerId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);
        var rowIds = rows.Select(r => r.Id).ToList();
        var cells = rowIds.Count == 0
            ? new List<DataContainerCell>()
            : await _db.DataContainerCells.AsNoTracking()
                .Where(c => rowIds.Contains(c.RowId))
                .ToListAsync(ct);
        var cellsByRow = cells.GroupBy(c => c.RowId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.ColumnId, x => x.Value));

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(TrimSheetName(container.Name));

        // Encabezados.
        for (var i = 0; i < columns.Count; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = columns[i].Name;
            cell.Style.Font.Bold = true;
        }

        // Filas.
        for (var r = 0; r < rows.Count; r++)
        {
            cellsByRow.TryGetValue(rows[r].Id, out var byCol);
            for (var c = 0; c < columns.Count; c++)
            {
                var col = columns[c];
                var raw = byCol is not null && byCol.TryGetValue(col.Id, out var v) ? v : null;
                if (string.IsNullOrEmpty(raw)) { continue; }
                var target = sheet.Cell(r + 2, c + 1);
                // Convertir a nativo cuando el tipo del modelo lo permite, para que Excel muestre
                // el valor con el formato correcto (numero, fecha, booleano) al reabrir el archivo.
                switch (col.Type)
                {
                    case DataContainerColumnType.Number when long.TryParse(raw, out var l): target.Value = l; break;
                    case DataContainerColumnType.Decimal when decimal.TryParse(raw, CultureInfo.InvariantCulture, out var d): target.Value = d; break;
                    case DataContainerColumnType.Date when DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt): target.Value = dt; break;
                    case DataContainerColumnType.Boolean when bool.TryParse(raw, out var b): target.Value = b; break;
                    default: target.Value = raw; break;
                }
            }
        }

        if (columns.Count > 0)
        {
            sheet.Columns(1, columns.Count).AdjustToContents();
        }

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        var slug = Slugify(container.Name);
        return new DataExportResult($"{slug}.xlsx", ms.ToArray());
    }

    // ---- Helpers ----

    /// <summary>Tipos que apuntan a otra tabla independiente (guardan ReferencedContainerId).</summary>
    private static bool IsRelation(DataContainerColumnType t)
        => t is DataContainerColumnType.Reference or DataContainerColumnType.RelationMany;

    /// <summary>Replace-all de columnas por Id sobre una tabla (contenedor) ya materializada.
    /// Reusada por SaveAsync (nivel tabla clasico) y SaveTableAsync (nivel modelo). No permite
    /// borrar una columna que ya tiene celdas asociadas. No llama SaveChanges (lo hace el caller).</summary>
    private async Task ApplyColumnsAsync(
        DataContainer entity, IReadOnlyList<SaveDataColumnInput> columns, Guid tenantId, CancellationToken ct)
    {
        var existing = await _db.DataContainerColumns
            .Where(cc => cc.ContainerId == entity.Id)
            .ToListAsync(ct);
        var keepIds = columns.Where(cc => cc.Id is not null).Select(cc => cc.Id!.Value).ToHashSet();
        var toRemove = existing.Where(cc => !keepIds.Contains(cc.Id)).ToList();

        // No se permite borrar una columna que ya tiene celdas asociadas.
        if (toRemove.Count > 0)
        {
            var removeIds = toRemove.Select(cc => cc.Id).ToList();
            var hasCells = await _db.DataContainerCells.AnyAsync(cell => removeIds.Contains(cell.ColumnId), ct);
            if (hasCells)
            {
                throw new InvalidOperationException(
                    "No se puede borrar una columna que ya tiene datos. Vacia primero esa columna o elimina las filas afectadas.");
            }
            _db.DataContainerColumns.RemoveRange(toRemove);
        }

        // Update / add.
        foreach (var input in columns)
        {
            var cleanName = (input.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(cleanName)) { continue; }
            var cleanDesc = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description!.Trim();

            if (input.Id is { } cid)
            {
                var col = existing.FirstOrDefault(cc => cc.Id == cid);
                if (col is null) { continue; }
                col.Name = cleanName;
                col.Description = cleanDesc;
                col.Type = input.Type;
                col.SortOrder = input.SortOrder;
                col.IsRequired = input.IsRequired;
                col.ChildContainerId = input.Type == DataContainerColumnType.Submodel ? input.ChildContainerId : null;
                col.ReferencedContainerId = IsRelation(input.Type) ? input.ReferencedContainerId : null;
            }
            else
            {
                _db.DataContainerColumns.Add(new DataContainerColumn
                {
                    TenantId = tenantId,
                    ContainerId = entity.Id,
                    Name = cleanName,
                    Description = cleanDesc,
                    Type = input.Type,
                    SortOrder = input.SortOrder,
                    IsRequired = input.IsRequired,
                    ChildContainerId = input.Type == DataContainerColumnType.Submodel ? input.ChildContainerId : null,
                    ReferencedContainerId = IsRelation(input.Type) ? input.ReferencedContainerId : null
                });
            }
        }
    }

    /// <summary>Tipos que guardan su valor en una celda EAV (escalares + Reference, que guarda el id destino).</summary>
    private static bool IsCellColumn(DataContainerColumnType t)
        => t is DataContainerColumnType.Text or DataContainerColumnType.Number or DataContainerColumnType.Decimal
            or DataContainerColumnType.Date or DataContainerColumnType.Boolean or DataContainerColumnType.Reference;

    /// <summary>Agrupa los vinculos N:N de una fila por columna -> lista de ids destino.</summary>
    private static IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> GroupLinks(List<DataContainerLink> links)
        => links.GroupBy(l => l.ColumnId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(x => x.TargetRowId).ToList());

    private async Task<DataContainerDetailDto> BuildDetailAsync(DataContainer c, CancellationToken ct)
    {
        var cols = await _db.DataContainerColumns.AsNoTracking()
            .Where(x => x.ContainerId == c.Id)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .ToListAsync(ct);

        // Resolver nombres de contenedores enlazados: hijo (Submodel) y tabla destino (Reference/N:N).
        var linkedIds = cols.Where(x => x.ChildContainerId is not null).Select(x => x.ChildContainerId!.Value)
            .Concat(cols.Where(x => x.ReferencedContainerId is not null).Select(x => x.ReferencedContainerId!.Value))
            .Distinct().ToList();
        var linkedNames = linkedIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.DataContainers.AsNoTracking()
                .Where(x => linkedIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        return new DataContainerDetailDto(
            c.Id, c.Name, c.Description, c.SourceKind, c.ParentContainerId, c.ParentFieldId,
            cols.Select(col => MapColumn(col, linkedNames)).ToList());
    }

    private static DataContainerColumnDto MapColumn(DataContainerColumn c, IReadOnlyDictionary<Guid, string> names)
    {
        string? childName = c.ChildContainerId is { } cid && names.TryGetValue(cid, out var n) ? n : null;
        string? refName = c.ReferencedContainerId is { } rid && names.TryGetValue(rid, out var rn) ? rn : null;
        return new DataContainerColumnDto(c.Id, c.Name, c.Description, c.Type, c.SortOrder, c.IsRequired,
            c.ChildContainerId, childName, c.ReferencedContainerId, refName);
    }

    private static string TrimSheetName(string name)
    {
        // Excel limita el nombre de hoja a 31 chars y prohibe: : \ / ? * [ ]
        var cleaned = new string(name.Where(c => !"[]:\\/?*".Contains(c)).ToArray()).Trim();
        if (string.IsNullOrEmpty(cleaned)) { return "Datos"; }
        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }

    private static string Slugify(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) { sb.Append(ch); }
            else if (ch is ' ' or '-' or '_') { sb.Append('-'); }
        }
        var slug = sb.ToString().Trim('-');
        while (slug.Contains("--")) { slug = slug.Replace("--", "-"); }
        return string.IsNullOrEmpty(slug) ? "contenedor" : slug;
    }

    private static string NormalizeHeader(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return ""; }
        var lower = raw.Trim().ToLowerInvariant();
        // Remove diacritics.
        var normalized = lower.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }
        var noDiacritics = sb.ToString().Normalize(NormalizationForm.FormC);
        // Collapse whitespace.
        var collapsed = new StringBuilder(noDiacritics.Length);
        var lastWasSpace = false;
        foreach (var ch in noDiacritics)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace && collapsed.Length > 0)
                {
                    collapsed.Append(' ');
                    lastWasSpace = true;
                }
            }
            else
            {
                collapsed.Append(ch);
                lastWasSpace = false;
            }
        }
        return collapsed.ToString().Trim();
    }

    private static string? ExtractValue(IXLCell cell, DataContainerColumnType type)
    {
        if (cell is null || cell.IsEmpty()) { return null; }
        try
        {
            switch (type)
            {
                case DataContainerColumnType.Date:
                    if (cell.DataType == XLDataType.DateTime)
                    {
                        return cell.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    }
                    var raw = cell.GetString().Trim();
                    if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt1))
                    {
                        return dt1.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    }
                    if (DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt2))
                    {
                        return dt2.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    }
                    return raw;
                case DataContainerColumnType.Number:
                    if (cell.DataType == XLDataType.Number)
                    {
                        return ((long)cell.GetDouble()).ToString(CultureInfo.InvariantCulture);
                    }
                    return cell.GetString().Trim();
                case DataContainerColumnType.Decimal:
                    if (cell.DataType == XLDataType.Number)
                    {
                        return cell.GetDouble().ToString(CultureInfo.InvariantCulture);
                    }
                    return cell.GetString().Trim();
                case DataContainerColumnType.Boolean:
                    if (cell.DataType == XLDataType.Boolean)
                    {
                        return cell.GetBoolean() ? "true" : "false";
                    }
                    var bs = cell.GetString().Trim().ToLowerInvariant();
                    if (bs is "true" or "1" or "si" or "yes") { return "true"; }
                    if (bs is "false" or "0" or "no") { return "false"; }
                    return bs;
                case DataContainerColumnType.Text:
                default:
                    return cell.GetString().Trim();
            }
        }
        catch
        {
            try { return cell.GetString().Trim(); } catch { return null; }
        }
    }
}
