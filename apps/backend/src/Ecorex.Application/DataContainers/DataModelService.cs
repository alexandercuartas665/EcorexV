using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.DataContainers;

/// <summary>
/// Servicio del Contenedor (DataModel): un contenedor agrupa VARIAS tablas (DataContainer) con
/// relaciones internas entre ellas (aristas del lienzo ER) mas su configuracion de importacion.
/// El nivel TABLA (columnas/relaciones Reference/RelationMany/Submodel) se delega en el
/// <see cref="IDataContainerService"/> para NO duplicar la maquinaria EAV: aqui solo se gobierna
/// la cabecera del contenedor, el conjunto de sus tablas y las aristas entre ellas. Tenant-scoped
/// por el filtro global; el borrado del contenedor arrastra sus tablas (cascada BD).
/// </summary>
public sealed class DataModelService : IDataModelService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IDataContainerService _containers;

    public DataModelService(IApplicationDbContext db, ITenantContext tenantContext, IDataContainerService containers)
    {
        _db = db;
        _tenantContext = tenantContext;
        _containers = containers;
    }

    public async Task<IReadOnlyList<DataModelDto>> ListAsync(CancellationToken ct = default)
    {
        var models = await _db.DataModels.AsNoTracking()
            .OrderBy(m => m.Name)
            .ToListAsync(ct);
        if (models.Count == 0) { return Array.Empty<DataModelDto>(); }

        var modelIds = models.Select(m => m.Id).ToList();
        var tables = await _db.DataContainers.AsNoTracking()
            .Where(c => c.ModelId != null && modelIds.Contains(c.ModelId!.Value))
            .Select(c => new { c.Id, ModelId = c.ModelId!.Value })
            .ToListAsync(ct);

        var tableCountByModel = tables.GroupBy(t => t.ModelId).ToDictionary(g => g.Key, g => g.Count());
        var modelByTable = tables.ToDictionary(t => t.Id, t => t.ModelId);

        var tableIds = tables.Select(t => t.Id).ToList();
        var relationContainerIds = tableIds.Count == 0
            ? new List<Guid>()
            : await _db.DataContainerColumns.AsNoTracking()
                .Where(c => tableIds.Contains(c.ContainerId) &&
                    (c.Type == DataContainerColumnType.Reference || c.Type == DataContainerColumnType.RelationMany))
                .Select(c => c.ContainerId)
                .ToListAsync(ct);

        var relationCountByModel = new Dictionary<Guid, int>();
        foreach (var containerId in relationContainerIds)
        {
            if (!modelByTable.TryGetValue(containerId, out var mId)) { continue; }
            relationCountByModel[mId] = relationCountByModel.TryGetValue(mId, out var n) ? n + 1 : 1;
        }

        return models.Select(m => new DataModelDto(
            m.Id,
            m.Name,
            m.Description,
            tableCountByModel.TryGetValue(m.Id, out var tc) ? tc : 0,
            relationCountByModel.TryGetValue(m.Id, out var rc) ? rc : 0,
            m.UpdatedAt ?? m.CreatedAt)).ToList();
    }

    public async Task<DataModelDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var model = await _db.DataModels.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (model is null) { return null; }

        var tables = await _db.DataContainers.AsNoTracking()
            .Where(c => c.ModelId == id)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        var tableDtos = new List<ModelTableDto>(tables.Count);
        foreach (var t in tables)
        {
            // Reusa el detalle de tabla (resuelve ChildContainerName/ReferencedContainerName).
            var detail = await _containers.GetAsync(t.Id, ct);
            var columns = detail?.Columns ?? Array.Empty<DataContainerColumnDto>();
            tableDtos.Add(new ModelTableDto(t.Id, t.Name, t.Description, t.CanvasX, t.CanvasY, columns));
        }

        // Relaciones: por cada campo Reference/RelationMany de una tabla del modelo cuyo destino es
        // OTRA tabla del mismo modelo, se emite una arista del lienzo.
        var tableIdSet = tables.Select(t => t.Id).ToHashSet();
        var tableIds = tableIdSet.ToList();
        var relationCols = tableIds.Count == 0
            ? new List<DataContainerColumn>()
            : await _db.DataContainerColumns.AsNoTracking()
                .Where(c => tableIds.Contains(c.ContainerId) &&
                    (c.Type == DataContainerColumnType.Reference || c.Type == DataContainerColumnType.RelationMany) &&
                    c.ReferencedContainerId != null)
                .ToListAsync(ct);

        var relations = relationCols
            .Where(c => tableIdSet.Contains(c.ReferencedContainerId!.Value))
            .Select(c => new ModelRelationDto(c.Id, c.ContainerId, c.Name, c.ReferencedContainerId!.Value, c.Type))
            .ToList();

        return new DataModelDetailDto(model.Id, model.Name, model.Description, tableDtos, relations);
    }

    public async Task<DataModelDto?> SaveModelAsync(SaveDataModelRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) { return null; }

        DataModel entity;
        if (req.Id is { } id)
        {
            var existing = await _db.DataModels.FirstOrDefaultAsync(m => m.Id == id, ct);
            if (existing is null) { return null; }
            // Nombre unico por tenant (excluyendo el propio).
            if (await _db.DataModels.AnyAsync(m => m.Id != id && m.Name == name, ct))
            {
                throw new InvalidOperationException($"Ya existe un contenedor llamado '{name}'.");
            }
            existing.Name = name;
            existing.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description!.Trim();
            entity = existing;
        }
        else
        {
            if (await _db.DataModels.AnyAsync(m => m.Name == name, ct))
            {
                throw new InvalidOperationException($"Ya existe un contenedor llamado '{name}'.");
            }
            entity = new DataModel
            {
                TenantId = tenantId,
                Name = name,
                Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description!.Trim()
            };
            _db.DataModels.Add(entity);
        }

        await _db.SaveChangesAsync(ct);
        return await BuildModelDtoAsync(entity.Id, ct);
    }

    public async Task<bool> DeleteModelAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var entity = await _db.DataModels.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (entity is null) { return false; }
        // La cascada de BD borra las tablas del contenedor (con sus columnas/filas/celdas), los
        // conectores, el destino y los procesos. No se borra a mano.
        _db.DataModels.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<ModelTableDto?> SaveTableAsync(SaveModelTableRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is null) { return null; }
        // El contenedor debe existir para poder anclar la tabla.
        if (!await _db.DataModels.AnyAsync(m => m.Id == req.ModelId, ct)) { return null; }

        var columns = req.Columns ?? Array.Empty<SaveDataColumnInput>();

        // Las relaciones (Reference/RelationMany) solo pueden apuntar a tablas del MISMO contenedor.
        var referencedIds = columns
            .Where(c => (c.Type == DataContainerColumnType.Reference || c.Type == DataContainerColumnType.RelationMany)
                && c.ReferencedContainerId is not null)
            .Select(c => c.ReferencedContainerId!.Value)
            .Distinct()
            .ToList();
        if (referencedIds.Count > 0)
        {
            var validTargets = await _db.DataContainers.AsNoTracking()
                .Where(t => t.ModelId == req.ModelId && referencedIds.Contains(t.Id))
                .Select(t => t.Id)
                .ToListAsync(ct);
            var invalid = referencedIds.Except(validTargets).ToList();
            if (invalid.Count > 0)
            {
                throw new InvalidOperationException(
                    "Una relacion apunta a una tabla que no pertenece a este contenedor. Las relaciones solo pueden enlazar tablas del mismo contenedor.");
            }
        }

        // Delega el upsert de la tabla (columnas + ModelId + CanvasX/Y) al servicio de tablas.
        var detail = await _containers.SaveTableAsync(
            req.ModelId, req.TableId, req.Name, req.Description, req.CanvasX, req.CanvasY, columns, actorUserId, ct);
        if (detail is null) { return null; }

        return new ModelTableDto(detail.Id, detail.Name, detail.Description, req.CanvasX, req.CanvasY, detail.Columns);
    }

    public Task<bool> DeleteTableAsync(Guid tableId, Guid actorUserId, CancellationToken ct = default)
        // Reusa el borrado de tabla (guarda la FK Restrict de tablas referenciadas + cascada BD).
        => _containers.DeleteAsync(tableId, actorUserId, ct);

    public async Task<bool> UpdateTablePositionAsync(UpdateTablePositionRequest req, CancellationToken ct = default)
    {
        var table = await _db.DataContainers.FirstOrDefaultAsync(t => t.Id == req.TableId, ct);
        if (table is null) { return false; }
        table.CanvasX = req.CanvasX;
        table.CanvasY = req.CanvasY;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ---- Helpers ----

    private async Task<DataModelDto?> BuildModelDtoAsync(Guid modelId, CancellationToken ct)
    {
        var model = await _db.DataModels.AsNoTracking().FirstOrDefaultAsync(m => m.Id == modelId, ct);
        if (model is null) { return null; }

        var tableIds = await _db.DataContainers.AsNoTracking()
            .Where(c => c.ModelId == modelId)
            .Select(c => c.Id)
            .ToListAsync(ct);
        var relationCount = tableIds.Count == 0
            ? 0
            : await _db.DataContainerColumns.AsNoTracking()
                .CountAsync(c => tableIds.Contains(c.ContainerId) &&
                    (c.Type == DataContainerColumnType.Reference || c.Type == DataContainerColumnType.RelationMany), ct);

        return new DataModelDto(model.Id, model.Name, model.Description, tableIds.Count, relationCount,
            model.UpdatedAt ?? model.CreatedAt);
    }
}
