using Ecorex.Domain.Common;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Actividades;

/// <summary>
/// Implementacion de <see cref="IActivityCatalogService"/> (Configuracion de actividades: prioridades
/// 000621, estados 000653, tipos de proyecto 000690). Aislamiento por tenant via filtro global; el
/// alta estampa el TenantId del contexto. No hay borrado fisico: se archiva con IsActive=false.
/// </summary>
public sealed class ActivityCatalogService : IActivityCatalogService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public ActivityCatalogService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<ActivityCatalogEntryDto>> ListAsync(
        ActivityCatalogKind kind, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var rows = await QueryFor(kind)
            .Where(x => includeInactive || x.IsActive)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var usage = kind == ActivityCatalogKind.ProjectType
            ? await ProjectTypeUsageAsync(cancellationToken)
            : null;

        return rows.Select(x => ToDto(x, usage)).ToList();
    }

    public async Task<ActivityCatalogEntryDto?> GetAsync(
        ActivityCatalogKind kind, Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await QueryFor(kind).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity is null ? null : ToDto(entity, null);
    }

    public async Task<TaskCoreResult<ActivityCatalogEntryDto>> CreateAsync(
        ActivityCatalogKind kind, SaveActivityCatalogRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
        {
            return TaskCoreResult<ActivityCatalogEntryDto>.Invalid("No hay tenant activo.");
        }
        var error = await ValidateAsync(kind, request, id: null, cancellationToken);
        if (error is not null) { return TaskCoreResult<ActivityCatalogEntryDto>.Invalid(error); }

        var sortOrder = request.SortOrder != 0
            ? request.SortOrder
            : (await QueryFor(kind).Select(x => (int?)x.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;

        var entity = NewEntity(kind);
        ((TenantEntity)entity).TenantId = tenantId;
        Apply(kind, entity, request, sortOrder);
        AddEntity(kind, entity);
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<ActivityCatalogEntryDto>.Ok(ToDto(entity, null));
    }

    public async Task<TaskCoreResult<ActivityCatalogEntryDto>> UpdateAsync(
        ActivityCatalogKind kind, Guid id, SaveActivityCatalogRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await QueryTrackedFor(kind).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null) { return TaskCoreResult<ActivityCatalogEntryDto>.NotFound("El elemento no existe."); }
        var error = await ValidateAsync(kind, request, id, cancellationToken);
        if (error is not null) { return TaskCoreResult<ActivityCatalogEntryDto>.Invalid(error); }

        Apply(kind, entity, request, request.SortOrder != 0 ? request.SortOrder : entity.SortOrder);
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<ActivityCatalogEntryDto>.Ok(ToDto(entity, null));
    }

    public async Task<TaskCoreResult<bool>> SetActiveAsync(
        ActivityCatalogKind kind, Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var entity = await QueryTrackedFor(kind).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null) { return TaskCoreResult<bool>.NotFound("El elemento no existe."); }

        // Guard: no archivar un tipo de proyecto en uso (los otros dos no tienen FK que romper).
        if (!active && kind == ActivityCatalogKind.ProjectType)
        {
            var enUso = await _db.Projects.CountAsync(p => p.ProjectTypeId == id, cancellationToken);
            if (enUso > 0)
            {
                return TaskCoreResult<bool>.Invalid($"No se puede archivar: {enUso} proyecto(s) usan este tipo.");
            }
        }

        entity.IsActive = active;
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<bool>.Ok(true);
    }

    // ---- Helpers de despacho por kind ----

    private IQueryable<IActivityCatalogEntity> QueryFor(ActivityCatalogKind kind) => kind switch
    {
        ActivityCatalogKind.Priority => _db.ActivityPriorities.AsNoTracking(),
        ActivityCatalogKind.State => _db.ActivityStates.AsNoTracking(),
        ActivityCatalogKind.ProjectType => _db.ProjectTypes.AsNoTracking(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private IQueryable<IActivityCatalogEntity> QueryTrackedFor(ActivityCatalogKind kind) => kind switch
    {
        ActivityCatalogKind.Priority => _db.ActivityPriorities,
        ActivityCatalogKind.State => _db.ActivityStates,
        ActivityCatalogKind.ProjectType => _db.ProjectTypes,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static IActivityCatalogEntity NewEntity(ActivityCatalogKind kind) => kind switch
    {
        ActivityCatalogKind.Priority => new ActivityPriority(),
        ActivityCatalogKind.State => new ActivityState(),
        ActivityCatalogKind.ProjectType => new ProjectType(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private void AddEntity(ActivityCatalogKind kind, IActivityCatalogEntity entity)
    {
        switch (kind)
        {
            case ActivityCatalogKind.Priority: _db.ActivityPriorities.Add((ActivityPriority)entity); break;
            case ActivityCatalogKind.State: _db.ActivityStates.Add((ActivityState)entity); break;
            case ActivityCatalogKind.ProjectType: _db.ProjectTypes.Add((ProjectType)entity); break;
        }
    }

    private static void Apply(ActivityCatalogKind kind, IActivityCatalogEntity entity, SaveActivityCatalogRequest request, int sortOrder)
    {
        entity.Name = request.Name.Trim();
        entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        entity.Color = string.IsNullOrWhiteSpace(request.Color) ? null : request.Color.Trim();
        entity.SortOrder = sortOrder;
        if (kind == ActivityCatalogKind.Priority && entity is ActivityPriority prio)
        {
            prio.MappedPriority = request.MappedPriority ?? TaskPriority.Medium;
        }
    }

    private async Task<string?> ValidateAsync(
        ActivityCatalogKind kind, SaveActivityCatalogRequest request, Guid? id, CancellationToken cancellationToken)
    {
        var name = (request.Name ?? "").Trim();
        if (name.Length == 0) { return "El nombre es obligatorio."; }
        if (name.Length > 120) { return "El nombre no puede superar 120 caracteres."; }

        var dup = await QueryFor(kind)
            .AnyAsync(x => x.Name.ToLower() == name.ToLower() && (id == null || x.Id != id), cancellationToken);
        if (dup) { return $"Ya existe un elemento con el nombre '{name}'."; }
        return null;
    }

    private async Task<Dictionary<Guid, int>> ProjectTypeUsageAsync(CancellationToken cancellationToken)
        => (await _db.Projects.AsNoTracking()
                .Where(p => p.ProjectTypeId != null)
                .GroupBy(p => p.ProjectTypeId!.Value)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.Id, x => x.Count);

    private static ActivityCatalogEntryDto ToDto(IActivityCatalogEntity e, Dictionary<Guid, int>? usage)
        => new(
            e.Id, e.Name, e.Description, e.Color, e.IsActive, e.SortOrder,
            (e as ActivityPriority)?.MappedPriority,
            usage is not null && usage.TryGetValue(e.Id, out var c) ? c : 0);
}
