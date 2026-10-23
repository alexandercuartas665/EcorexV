using Ecorex.Application.Common;
using Ecorex.Application.Scheduling;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.DataContainers;

/// <summary>
/// Tablas derivadas (ADR-0127). Lee el origen por lotes (tenant-scoped por el filtro global, con tope),
/// delega el calculo al motor puro <see cref="DerivedRowsBuilder"/> y escribe con el nucleo de ingesta
/// compartido <see cref="IRowIngestService"/> (Upsert por la columna clave, que CONSERVA las celdas de
/// columnas no mapeadas: por eso las columnas propias que el usuario agregue a la hija sobreviven).
/// Tras el Upsert PODA las filas cuya clave desaparecio del origen. Todo el refresco va en transaccion.
/// </summary>
public sealed class DerivedTableService : IDerivedTableService
{
    /// <summary>Tope de filas del origen que se materializan en memoria por refresco.</summary>
    public const int MaxSourceRows = 200_000;
    private const int SourceBatch = 5_000;
    private const int IngestChunk = 500;

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IRowIngestService _ingest;

    public DerivedTableService(IApplicationDbContext db, ITenantContext tenantContext, IRowIngestService ingest)
    {
        _db = db;
        _tenantContext = tenantContext;
        _ingest = ingest;
    }

    public async Task<DerivationPreviewDto> PreviewAsync(DerivationSpec spec, int take = 20, CancellationToken ct = default)
    {
        var source = await LoadSourceAsync(spec.SourceTableId, ct);
        var planned = DerivedRowsBuilder.Plan(spec, source.Columns);
        if (!planned.Ok) { throw new InvalidOperationException(planned.Error); }

        var rows = await ReadSourceRowsAsync(spec.SourceTableId, ct);
        var result = DerivedRowsBuilder.Build(spec, source.Columns, rows);
        var page = result.Rows.Take(Math.Max(1, take)).ToList();
        return new DerivationPreviewDto(result.Columns, page, result.Rows.Count, result.SourceRowsRead, result.SourceRowsAfterFilter);
    }

    public async Task<DerivedTableCreatedDto> CreateAsync(CreateDerivedTableRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            throw new InvalidOperationException("Sin tenant en contexto.");
        }
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) { throw new InvalidOperationException("El nombre de la tabla derivada es obligatorio."); }
        if (name.Length > 200) { throw new InvalidOperationException("El nombre de la tabla supera 200 caracteres."); }

        if (!await _db.DataModels.AnyAsync(m => m.Id == req.ModelId, ct))
        {
            throw new InvalidOperationException("El contenedor no existe.");
        }

        // El origen puede vivir en OTRO contenedor del tenant (extension de ADR-0127): req.ModelId es el
        // contenedor DESTINO; el tenant lo garantiza el filtro global y "un solo nivel" va por id.
        var source = await LoadSourceAsync(req.Spec.SourceTableId, ct);
        var sameModel = source.Table.ModelId == req.ModelId;
        if (await _db.DataContainers.AnyAsync(c => c.ModelId == req.ModelId && c.ParentContainerId == null && c.Name == name, ct))
        {
            throw new InvalidOperationException($"Ya existe una tabla llamada '{name}' en este contenedor.");
        }

        var planned = DerivedRowsBuilder.Plan(req.Spec, source.Columns);
        if (!planned.Ok) { throw new InvalidOperationException(planned.Error); }
        var columns = planned.Columns!;

        // Posicion en el lienzo: al lado del origen si comparten contenedor; si no, un sitio libre del
        // lienzo destino (a la derecha de su tabla mas a la derecha, o el origen del lienzo si esta vacio).
        var (canvasX, canvasY) = sameModel
            ? (source.Table.CanvasX + 260, source.Table.CanvasY)
            : await FreeCanvasSpotAsync(req.ModelId, ct);

        var joinTx = _db.HasActiveTransaction;
        var tx = joinTx ? null : await _db.BeginTransactionAsync(ct);
        try
        {
            // 1) Tabla hija en el contenedor DESTINO.
            var table = new DataContainer
            {
                TenantId = tenantId,
                ModelId = req.ModelId,
                Name = name,
                Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description!.Trim(),
                DerivedFromContainerId = source.Table.Id,
                CanvasX = canvasX,
                CanvasY = canvasY
            };
            _db.DataContainers.Add(table);

            // 2) Columnas: agrupacion (heredan tipo), clave, agregados.
            var order = 0;
            foreach (var col in columns)
            {
                _db.DataContainerColumns.Add(new DataContainerColumn
                {
                    TenantId = tenantId,
                    ContainerId = table.Id,
                    Name = col.Name,
                    Type = col.Type,
                    SortOrder = order++,
                    Description = col.Role switch
                    {
                        DerivedColumnRole.Key => "Clave del grupo (generada). No editar: es la columna clave del refresco.",
                        DerivedColumnRole.Aggregate => $"Agregado calculado desde '{source.Table.Name}'.",
                        _ => $"Agrupacion desde '{source.Table.Name}'."
                    }
                });
            }

            // 3) Conector "Desde otra tabla" con la spec; su tabla destino es la hija.
            var connector = new DataConnector
            {
                TenantId = tenantId,
                ModelId = req.ModelId,
                ContainerId = table.Id,
                Name = $"Derivada: {name}",
                Kind = ConnectorKind.FromTable,
                AuthKind = ConnectorAuthKind.None,
                MappingJson = req.Spec.ToJson(),
                IsActive = true
            };
            _db.DataConnectors.Add(connector);

            // 4) Proceso de refresco: Upsert por la clave (asi sobreviven las columnas propias de la hija).
            var keyName = columns.First(c => c.Role == DerivedColumnRole.Key).Name;
            var process = new ImportProcess
            {
                TenantId = tenantId,
                ModelId = req.ModelId,
                ConnectorId = connector.Id,
                Name = $"Refrescar {name}",
                ScheduleKind = req.ScheduleKind,
                IntervalMinutes = req.ScheduleKind == ImportScheduleKind.Interval ? req.IntervalMinutes : null,
                CronExpression = req.ScheduleKind == ImportScheduleKind.Cron && !string.IsNullOrWhiteSpace(req.CronExpression)
                    ? req.CronExpression!.Trim() : null,
                Mode = ImportRunMode.Upsert,
                KeyColumn = keyName,
                IsActive = true
            };
            // Misma regla que DataImportConfigService: la proxima ventana se calcula al guardar y un
            // horario invalido se rechaza AQUI, no mas tarde en el worker.
            var tz = ScheduledJobRecurrence.ResolveTimeZone(await _db.Tenants
                .Where(t => t.Id == tenantId).Select(t => t.TimeZoneId).FirstOrDefaultAsync(ct));
            var next = ImportRecurrence.ComputeNextRun(process, DateTimeOffset.UtcNow, tz);
            if (next.Problem == ImportScheduleProblem.Invalid)
            {
                throw new InvalidOperationException(next.Reason ?? "El horario no es valido.");
            }
            process.NextRunAt = next.NextRunAt;
            _db.ImportProcesses.Add(process);

            await _db.SaveChangesAsync(ct);
            if (tx is not null) { await tx.CommitAsync(ct); }
            return new DerivedTableCreatedDto(table.Id, connector.Id, process.Id);
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

    public async Task<DerivedRefreshOutcome> RefreshAsync(Guid connectorId, ApiImportMode mode, string? keyColumnName, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return DerivedRefreshOutcome.Fail("Sin tenant en contexto."); }

        var connector = await _db.DataConnectors.AsNoTracking().FirstOrDefaultAsync(c => c.Id == connectorId, ct);
        if (connector is null) { return DerivedRefreshOutcome.Fail("El conector ya no existe."); }
        if (connector.Kind != ConnectorKind.FromTable) { return DerivedRefreshOutcome.Fail("El conector no es de tipo 'Desde otra tabla'."); }
        if (connector.ContainerId is not Guid targetId) { return DerivedRefreshOutcome.Fail("El conector no tiene tabla derivada destino."); }

        var spec = DerivationSpec.FromJson(connector.MappingJson);
        if (spec is null) { return DerivedRefreshOutcome.Fail("La especificacion de derivacion del conector no es valida."); }

        var target = await _db.DataContainers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == targetId, ct);
        if (target is null) { return DerivedRefreshOutcome.Fail("La tabla derivada ya no existe."); }

        // Validaciones de origen: existe y NO es derivado (un solo nivel). Puede estar en otro contenedor.
        SourceSnapshot source;
        try { source = await LoadSourceAsync(spec.SourceTableId, ct); }
        catch (InvalidOperationException ex) { return DerivedRefreshOutcome.Fail(ex.Message); }

        var planned = DerivedRowsBuilder.Plan(spec, source.Columns);
        if (!planned.Ok) { return DerivedRefreshOutcome.Fail(planned.Error!); }

        // Columnas de la hija por nombre: las que la derivacion produce deben existir (el usuario pudo
        // borrarlas a mano). Las columnas PROPIAS (no producidas) no se tocan.
        var targetCols = await _db.DataContainerColumns.AsNoTracking()
            .Where(c => c.ContainerId == targetId)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);
        var byName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in targetCols) { byName[c.Name] = c.Id; }

        var mapping = new Dictionary<Guid, string>();
        foreach (var col in planned.Columns!)
        {
            if (!byName.TryGetValue(col.Name, out var id))
            {
                return DerivedRefreshOutcome.Fail($"La columna '{col.Name}' no existe en la tabla derivada '{target.Name}'. Vuelve a crearla o ajusta la derivacion.");
            }
            mapping[id] = col.Name;
        }

        var keyName = string.IsNullOrWhiteSpace(keyColumnName) ? spec.KeyColumnName : keyColumnName.Trim();
        Guid? keyColumnId = null;
        if (mode == ApiImportMode.Upsert)
        {
            if (!byName.TryGetValue(keyName, out var kid) || !mapping.ContainsKey(kid))
            {
                return DerivedRefreshOutcome.Fail($"El modo Upsert necesita la columna clave '{keyName}' en la tabla derivada.");
            }
            keyColumnId = kid;
        }

        // Calculo en memoria (motor puro) sobre el origen leido por lotes.
        var rows = await ReadSourceRowsAsync(spec.SourceTableId, ct);
        DerivedResult result;
        try { result = DerivedRowsBuilder.Build(spec, source.Columns, rows); }
        catch (InvalidOperationException ex) { return DerivedRefreshOutcome.Fail(ex.Message); }

        // Ingesta + poda, TODO en una transaccion: si algo falla a mitad, la hija queda como estaba.
        var joinTx = _db.HasActiveTransaction;
        var tx = joinTx ? null : await _db.BeginTransactionAsync(ct);
        try
        {
            var session = _ingest.CreateSession(targetId, tenantId, mapping, mode, keyColumnId);
            await session.PrepareAsync(ct);
            for (var i = 0; i < result.Rows.Count; i += IngestChunk)
            {
                var chunk = result.Rows.Skip(i).Take(IngestChunk).ToList();
                await session.IngestChunkAsync(chunk, ct);
            }

            var pruned = 0;
            if (mode == ApiImportMode.Upsert && keyColumnId is Guid kc)
            {
                pruned = await PruneAsync(targetId, kc, result.Rows.Select(r => r[keyName] ?? "").ToHashSet(StringComparer.Ordinal), ct);
            }

            if (tx is not null) { await tx.CommitAsync(ct); }
            return new DerivedRefreshOutcome(true, session.Inserted, session.Updated, session.Deleted + pruned, null);
        }
        catch (Exception ex)
        {
            if (tx is not null) { await tx.RollbackAsync(ct); }
            return DerivedRefreshOutcome.Fail($"No se pudo refrescar la tabla derivada: {ex.Message}");
        }
        finally
        {
            if (tx is not null) { await tx.DisposeAsync(); }
        }
    }

    /// <summary>Poda: borra las filas de la hija cuya clave ya no esta entre los grupos calculados (el
    /// grupo desaparecio del origen). Las filas sin celda clave tambien caen (nunca las produjo el
    /// motor). Devuelve cuantas filas se borraron.</summary>
    private async Task<int> PruneAsync(Guid targetId, Guid keyColumnId, HashSet<string> liveKeys, CancellationToken ct)
    {
        var rowIds = await _db.DataContainerRows.Where(r => r.ContainerId == targetId).Select(r => r.Id).ToListAsync(ct);
        if (rowIds.Count == 0) { return 0; }

        var keyCells = await _db.DataContainerCells.AsNoTracking()
            .Where(c => c.ColumnId == keyColumnId && rowIds.Contains(c.RowId))
            .Select(c => new { c.RowId, c.Value })
            .ToListAsync(ct);
        var keyByRow = keyCells.ToDictionary(c => c.RowId, c => c.Value ?? "");

        var dead = rowIds.Where(id => !keyByRow.TryGetValue(id, out var k) || !liveKeys.Contains(k)).ToList();
        if (dead.Count == 0) { return 0; }

        var cells = await _db.DataContainerCells.Where(c => dead.Contains(c.RowId)).ToListAsync(ct);
        if (cells.Count > 0) { _db.DataContainerCells.RemoveRange(cells); }
        var links = await _db.DataContainerLinks.Where(l => dead.Contains(l.RowId) || dead.Contains(l.TargetRowId)).ToListAsync(ct);
        if (links.Count > 0) { _db.DataContainerLinks.RemoveRange(links); }
        var rows = await _db.DataContainerRows.Where(r => dead.Contains(r.Id)).ToListAsync(ct);
        _db.DataContainerRows.RemoveRange(rows);
        await _db.SaveChangesAsync(ct);
        return dead.Count;
    }

    /// <summary>Sitio libre del lienzo de un contenedor: a la derecha de la tabla mas a la derecha
    /// (misma fila), o (40, 40) si el lienzo esta vacio.</summary>
    private async Task<(double X, double Y)> FreeCanvasSpotAsync(Guid modelId, CancellationToken ct)
    {
        var spots = await _db.DataContainers.AsNoTracking()
            .Where(c => c.ModelId == modelId && c.ParentContainerId == null)
            .Select(c => new { c.CanvasX, c.CanvasY })
            .ToListAsync(ct);
        if (spots.Count == 0) { return (40, 40); }
        var rightmost = spots.OrderByDescending(s => s.CanvasX).ThenBy(s => s.CanvasY).First();
        return (rightmost.CanvasX + 260, rightmost.CanvasY);
    }

    // ---- Lectura del origen ----

    private sealed record SourceSnapshot(DataContainer Table, IReadOnlyList<SourceColumnInfo> Columns);

    /// <summary>Carga la tabla origen y sus columnas. Lanza si no existe o si ya es derivada (un solo nivel).</summary>
    private async Task<SourceSnapshot> LoadSourceAsync(Guid sourceTableId, CancellationToken ct)
    {
        var table = await _db.DataContainers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == sourceTableId, ct);
        if (table is null) { throw new InvalidOperationException("La tabla origen no existe."); }
        if (table.DerivedFromContainerId is not null)
        {
            throw new InvalidOperationException($"'{table.Name}' ya es una tabla derivada: no se puede derivar de una derivada (un solo nivel).");
        }
        var cols = await _db.DataContainerColumns.AsNoTracking()
            .Where(c => c.ContainerId == sourceTableId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new SourceColumnInfo(c.Id, c.Name, c.Type))
            .ToListAsync(ct);
        return new SourceSnapshot(table, cols);
    }

    /// <summary>Filas RAIZ del origen (sin las de submodelos) como celdas por ColumnId, por lotes y con tope.</summary>
    private async Task<List<IReadOnlyDictionary<Guid, string?>>> ReadSourceRowsAsync(Guid sourceTableId, CancellationToken ct)
    {
        var all = new List<IReadOnlyDictionary<Guid, string?>>();
        var skip = 0;
        while (all.Count < MaxSourceRows)
        {
            var rowIds = await _db.DataContainerRows.AsNoTracking()
                .Where(r => r.ContainerId == sourceTableId && r.ParentRowId == null)
                .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
                .Skip(skip).Take(SourceBatch)
                .Select(r => r.Id)
                .ToListAsync(ct);
            if (rowIds.Count == 0) { break; }
            skip += rowIds.Count;

            var cells = await _db.DataContainerCells.AsNoTracking()
                .Where(c => rowIds.Contains(c.RowId))
                .Select(c => new { c.RowId, c.ColumnId, c.Value })
                .ToListAsync(ct);
            var byRow = cells.GroupBy(c => c.RowId)
                .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.ColumnId, x => x.Value));

            foreach (var id in rowIds)
            {
                all.Add(byRow.TryGetValue(id, out var d) ? d : new Dictionary<Guid, string?>());
                if (all.Count >= MaxSourceRows) { break; }
            }
            if (rowIds.Count < SourceBatch) { break; }
        }
        return all;
    }
}
