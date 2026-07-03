using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed class ActivityTypeService : IActivityTypeService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;

    public ActivityTypeService(IApplicationDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<ActivityTypeDto>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        return await _db.ActivityTypes.AsNoTracking()
            .Where(t => includeArchived || !t.IsArchived)
            .OrderBy(t => t.Category).ThenBy(t => t.SortOrder).ThenBy(t => t.Name)
            .Select(t => ToDto(t))
            .ToListAsync(cancellationToken);
    }

    public async Task<ActivityTypeDto?> GetAsync(Guid activityTypeId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.ActivityTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == activityTypeId, cancellationToken);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<TaskCoreResult<ActivityTypeDto>> CreateAsync(CreateActivityTypeRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return TaskCoreResult<ActivityTypeDto>.Invalid("No hay tenant activo.");
        }
        var category = (request.Category ?? "").Trim();
        var name = (request.Name ?? "").Trim();
        if (category.Length == 0 || name.Length == 0)
        {
            return TaskCoreResult<ActivityTypeDto>.Invalid("Categoria y nombre son obligatorios.");
        }
        // El indice unico (TenantId, Category, Name) respalda esta validacion amigable.
        if (await _db.ActivityTypes.AnyAsync(t => t.Category == category && t.Name == name, cancellationToken))
        {
            return TaskCoreResult<ActivityTypeDto>.Invalid($"Ya existe el tipo de actividad '{category}/{name}'.");
        }

        var sortOrder = request.SortOrder
            ?? (await _db.ActivityTypes.Where(t => t.Category == category)
                    .Select(t => (int?)t.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;

        var entity = new ActivityType
        {
            TenantId = tenantId,
            Category = category,
            Name = name,
            Description = Normalize(request.Description),
            SortOrder = sortOrder
        };
        _db.ActivityTypes.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<ActivityTypeDto>.Ok(ToDto(entity));
    }

    public async Task<TaskCoreResult<ActivityTypeDto>> UpdateAsync(Guid activityTypeId, UpdateActivityTypeRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await _db.ActivityTypes.FirstOrDefaultAsync(t => t.Id == activityTypeId, cancellationToken);
        if (entity is null)
        {
            return TaskCoreResult<ActivityTypeDto>.NotFound("Tipo de actividad no encontrado.");
        }
        var category = (request.Category ?? entity.Category).Trim();
        var name = (request.Name ?? entity.Name).Trim();
        if (category.Length == 0 || name.Length == 0)
        {
            return TaskCoreResult<ActivityTypeDto>.Invalid("Categoria y nombre son obligatorios.");
        }
        if (await _db.ActivityTypes.AnyAsync(
                t => t.Id != activityTypeId && t.Category == category && t.Name == name, cancellationToken))
        {
            return TaskCoreResult<ActivityTypeDto>.Invalid($"Ya existe el tipo de actividad '{category}/{name}'.");
        }

        entity.Category = category;
        entity.Name = name;
        entity.Description = Normalize(request.Description);
        entity.SortOrder = request.SortOrder;
        entity.IsArchived = request.IsArchived;
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<ActivityTypeDto>.Ok(ToDto(entity));
    }

    public async Task<TaskCoreResult<bool>> DeleteAsync(Guid activityTypeId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.ActivityTypes.FirstOrDefaultAsync(t => t.Id == activityTypeId, cancellationToken);
        if (entity is null)
        {
            return TaskCoreResult<bool>.NotFound("Tipo de actividad no encontrado.");
        }

        // Si hay tareas del tipo, la FK Restrict impediria el borrado: se archiva (soft).
        var inUse = await _db.TaskItems.AnyAsync(t => t.ActivityTypeId == activityTypeId, cancellationToken);
        if (inUse)
        {
            entity.IsArchived = true;
            await _db.SaveChangesAsync(cancellationToken);
            return TaskCoreResult<bool>.Ok(false);
        }

        _db.ActivityTypes.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<bool>.Ok(true);
    }

    private static ActivityTypeDto ToDto(ActivityType t) => new(
        t.Id, t.Category, t.Name, t.Description, t.SortOrder, t.IsArchived,
        t.WorkflowDefinitionId, t.RequiresForm);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
