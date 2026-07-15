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

        // Relaciones = aristas del ER (entidad DataModelRelation), contadas por modelo.
        var relationCountByModel = (await _db.DataModelRelations.AsNoTracking()
                .Where(r => modelIds.Contains(r.ModelId))
                .GroupBy(r => r.ModelId)
                .Select(g => new { ModelId = g.Key, Count = g.Count() })
                .ToListAsync(ct))
            .ToDictionary(x => x.ModelId, x => x.Count);

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

        // Relaciones = aristas del ER (entidad DataModelRelation) del modelo.
        var relations = await _db.DataModelRelations.AsNoTracking()
            .Where(r => r.ModelId == id)
            .OrderBy(r => r.Name)
            .Select(r => new ModelRelationDto(r.Id, r.FromTableId, r.ToTableId, r.Kind, r.Name))
            .ToListAsync(ct);

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

        // Las relaciones ya NO viven en columnas (son DataModelRelation); la tabla solo lleva campos escalares/Submodel.
        // Delega el upsert de la tabla (columnas + ModelId + CanvasX/Y) al servicio de tablas.
        var detail = await _containers.SaveTableAsync(
            req.ModelId, req.TableId, req.Name, req.Description, req.CanvasX, req.CanvasY, columns, actorUserId, ct);
        if (detail is null) { return null; }

        return new ModelTableDto(detail.Id, detail.Name, detail.Description, req.CanvasX, req.CanvasY, detail.Columns);
    }

    public async Task<bool> DeleteTableAsync(Guid tableId, Guid actorUserId, CancellationToken ct = default)
    {
        // Primero limpia las relaciones (aristas) donde la tabla sea origen o destino (FK Restrict);
        // luego delega el borrado de la tabla (cascada BD de columnas/filas/celdas).
        var rels = await _db.DataModelRelations
            .Where(r => r.FromTableId == tableId || r.ToTableId == tableId)
            .ToListAsync(ct);
        if (rels.Count > 0)
        {
            _db.DataModelRelations.RemoveRange(rels);
            await _db.SaveChangesAsync(ct);
        }
        return await _containers.DeleteAsync(tableId, actorUserId, ct);
    }

    public async Task<ModelRelationDto?> AddRelationAsync(SaveModelRelationRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        if (req.FromTableId == req.ToTableId)
        {
            throw new InvalidOperationException("Una relacion debe unir dos tablas distintas.");
        }
        // Ambas tablas deben pertenecer al MISMO contenedor (modelo).
        var tablesOfModel = await _db.DataContainers.AsNoTracking()
            .Where(t => t.ModelId == req.ModelId && (t.Id == req.FromTableId || t.Id == req.ToTableId))
            .Select(t => t.Id)
            .ToListAsync(ct);
        if (tablesOfModel.Count != 2)
        {
            throw new InvalidOperationException("Las relaciones solo pueden enlazar dos tablas del mismo contenedor.");
        }
        // Evitar duplicar la misma arista (mismo origen/destino/cardinalidad).
        var dup = await _db.DataModelRelations.AnyAsync(r => r.ModelId == req.ModelId
            && r.FromTableId == req.FromTableId && r.ToTableId == req.ToTableId && r.Kind == req.Kind, ct);
        if (dup)
        {
            throw new InvalidOperationException("Ya existe una relacion igual entre esas dos tablas.");
        }

        var rel = new DataModelRelation
        {
            TenantId = tenantId,
            ModelId = req.ModelId,
            FromTableId = req.FromTableId,
            ToTableId = req.ToTableId,
            Kind = req.Kind,
            Name = string.IsNullOrWhiteSpace(req.Name) ? null : req.Name!.Trim()
        };
        _db.DataModelRelations.Add(rel);
        await _db.SaveChangesAsync(ct);
        return new ModelRelationDto(rel.Id, rel.FromTableId, rel.ToTableId, rel.Kind, rel.Name);
    }

    public async Task<bool> DeleteRelationAsync(Guid relationId, Guid actorUserId, CancellationToken ct = default)
    {
        var rel = await _db.DataModelRelations.FirstOrDefaultAsync(r => r.Id == relationId, ct);
        if (rel is null) { return false; }
        _db.DataModelRelations.Remove(rel);
        await _db.SaveChangesAsync(ct);
        return true;
    }

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

        var tableCount = await _db.DataContainers.AsNoTracking().CountAsync(c => c.ModelId == modelId, ct);
        var relationCount = await _db.DataModelRelations.AsNoTracking().CountAsync(r => r.ModelId == modelId, ct);

        return new DataModelDto(model.Id, model.Name, model.Description, tableCount, relationCount,
            model.UpdatedAt ?? model.CreatedAt);
    }
}
