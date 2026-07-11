using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
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
        if (await ValidateWorkflowAsync(request.WorkflowDefinitionId, cancellationToken) is string workflowError)
        {
            return TaskCoreResult<ActivityTypeDto>.Invalid(workflowError);
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
            SortOrder = sortOrder,
            WorkflowDefinitionId = request.WorkflowDefinitionId,
            RequiresForm = request.RequiresForm
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
        if (await ValidateWorkflowAsync(request.WorkflowDefinitionId, cancellationToken) is string workflowError)
        {
            return TaskCoreResult<ActivityTypeDto>.Invalid(workflowError);
        }

        entity.Category = category;
        entity.Name = name;
        entity.Description = Normalize(request.Description);
        entity.SortOrder = request.SortOrder;
        entity.IsArchived = request.IsArchived;
        entity.WorkflowDefinitionId = request.WorkflowDefinitionId;
        entity.RequiresForm = request.RequiresForm;
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

    // ---- Operaciones aditivas del modulo Conceptos (000270) ----

    public async Task<IReadOnlyList<ActivityWorkflowOptionDto>> ListWorkflowOptionsAsync(CancellationToken cancellationToken = default)
    {
        return await _db.WorkflowDefinitions.AsNoTracking()
            .Where(w => w.IsPublished && !w.IsArchived)
            .OrderBy(w => w.Name).ThenBy(w => w.ProcessCode)
            .Select(w => new ActivityWorkflowOptionDto(w.Id, w.ProcessCode, w.Name, w.Version))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ActivityTypeUsageDto>> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        // Solo tareas clasificadas por ActivityType (las que pivotaron a concepto tienen
        // ActivityTypeId null y no cuentan aqui).
        return await _db.TaskItems.AsNoTracking()
            .Where(t => t.ActivityTypeId != null)
            .GroupBy(t => t.ActivityTypeId!.Value)
            .Select(g => new ActivityTypeUsageDto(
                g.Key,
                g.Count(),
                g.Count(t => t.Status != TaskItemStatus.Done && t.Status != TaskItemStatus.Closed)))
            .ToListAsync(cancellationToken);
    }

    public async Task<TaskCoreResult<ActivityTypeDto>> SetArchivedAsync(Guid activityTypeId, bool archived, CancellationToken cancellationToken = default)
    {
        var entity = await _db.ActivityTypes.FirstOrDefaultAsync(t => t.Id == activityTypeId, cancellationToken);
        if (entity is null)
        {
            return TaskCoreResult<ActivityTypeDto>.NotFound("Tipo de actividad no encontrado.");
        }
        if (entity.IsArchived == archived)
        {
            return TaskCoreResult<ActivityTypeDto>.Invalid(archived
                ? "El concepto ya esta archivado."
                : "El concepto no esta archivado.");
        }
        entity.IsArchived = archived;
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<ActivityTypeDto>.Ok(ToDto(entity));
    }

    public async Task<TaskCoreResult<int>> RenameCategoryAsync(string category, string newName, CancellationToken cancellationToken = default)
    {
        var from = (category ?? "").Trim();
        var to = (newName ?? "").Trim();
        if (from.Length == 0 || to.Length == 0)
        {
            return TaskCoreResult<int>.Invalid("La categoria y el nombre nuevo son obligatorios.");
        }
        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            return TaskCoreResult<int>.Ok(0);
        }

        var types = await _db.ActivityTypes
            .Where(t => t.Category == from)
            .ToListAsync(cancellationToken);
        if (types.Count == 0)
        {
            return TaskCoreResult<int>.NotFound($"No existe la categoria '{from}'.");
        }

        // El destino no puede producir duplicados (TenantId, Category, Name).
        var movedNames = types.Select(t => t.Name).ToList();
        var collision = await _db.ActivityTypes
            .Where(t => t.Category == to && movedNames.Contains(t.Name))
            .Select(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken);
        if (collision is not null)
        {
            return TaskCoreResult<int>.Invalid(
                $"La categoria '{to}' ya tiene un concepto llamado '{collision}'.");
        }

        foreach (var type in types)
        {
            type.Category = to;
        }
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<int>.Ok(types.Count);
    }

    public async Task<TaskCoreResult<int>> SetCategoryArchivedAsync(string category, bool archived, CancellationToken cancellationToken = default)
    {
        var name = (category ?? "").Trim();
        if (name.Length == 0)
        {
            return TaskCoreResult<int>.Invalid("La categoria es obligatoria.");
        }
        var types = await _db.ActivityTypes
            .Where(t => t.Category == name)
            .ToListAsync(cancellationToken);
        if (types.Count == 0)
        {
            return TaskCoreResult<int>.NotFound($"No existe la categoria '{name}'.");
        }
        var changed = 0;
        foreach (var type in types.Where(t => t.IsArchived != archived))
        {
            type.IsArchived = archived;
            changed++;
        }
        if (changed > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        return TaskCoreResult<int>.Ok(changed);
    }

    public async Task<TaskCoreResult<ActivityTypeDto>> MoveAsync(Guid activityTypeId, bool up, CancellationToken cancellationToken = default)
    {
        var target = await _db.ActivityTypes.FirstOrDefaultAsync(t => t.Id == activityTypeId, cancellationToken);
        if (target is null)
        {
            return TaskCoreResult<ActivityTypeDto>.NotFound("Tipo de actividad no encontrado.");
        }

        // Mismo orden visible que ListAsync; normaliza empates de SortOrder al mover.
        var siblings = await _db.ActivityTypes
            .Where(t => t.Category == target.Category)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);
        var index = siblings.FindIndex(t => t.Id == activityTypeId);
        var newIndex = up ? index - 1 : index + 1;
        if (newIndex < 0 || newIndex >= siblings.Count)
        {
            return TaskCoreResult<ActivityTypeDto>.Invalid(up
                ? "El concepto ya es el primero de su categoria."
                : "El concepto ya es el ultimo de su categoria.");
        }

        (siblings[index], siblings[newIndex]) = (siblings[newIndex], siblings[index]);
        for (var i = 0; i < siblings.Count; i++)
        {
            siblings[i].SortOrder = i;
        }
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<ActivityTypeDto>.Ok(ToDto(target));
    }

    // ---- Helpers ----

    /// <summary>Mensaje de error si el flujo vinculado no existe o no esta publicado; null si es valido.</summary>
    private async Task<string?> ValidateWorkflowAsync(Guid? workflowDefinitionId, CancellationToken cancellationToken)
    {
        if (workflowDefinitionId is not Guid id)
        {
            return null;
        }
        var exists = await _db.WorkflowDefinitions
            .AnyAsync(w => w.Id == id && w.IsPublished && !w.IsArchived, cancellationToken);
        return exists ? null : "El proceso vinculado no existe o no esta publicado.";
    }

    private static ActivityTypeDto ToDto(ActivityType t) => new(
        t.Id, t.Category, t.Name, t.Description, t.SortOrder, t.IsArchived,
        t.WorkflowDefinitionId, t.RequiresForm);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
