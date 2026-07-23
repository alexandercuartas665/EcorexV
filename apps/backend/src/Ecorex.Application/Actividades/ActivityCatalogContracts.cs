using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Actividades;

/// <summary>Catalogo configurable del modulo de actividades que administra un mismo CRUD generico.</summary>
public enum ActivityCatalogKind
{
    Priority = 0,
    State,
    ProjectType
}

/// <summary>Fila generica de un catalogo de actividades (prioridad, estado, tipo de proyecto).</summary>
public sealed record ActivityCatalogEntryDto(
    Guid Id,
    string Name,
    string? Description,
    string? Color,
    bool IsActive,
    int SortOrder,
    // Solo prioridades: a que valor del enum TaskPriority mapea esta fila.
    TaskPriority? MappedPriority,
    // Cuantos registros la referencian (para el guard de archivado y la UI). 0 si no se rastrea.
    int UsageCount);

/// <summary>Alta/edicion de un catalogo de actividades.</summary>
public sealed record SaveActivityCatalogRequest(
    string Name,
    string? Description = null,
    string? Color = null,
    int SortOrder = 0,
    // Requerido solo para prioridades.
    TaskPriority? MappedPriority = null);

/// <summary>
/// CRUD de los catalogos configurables del grupo Sistema - Actividades: prioridades (000621),
/// estados (000653) y tipos de proyecto (000690). Un mismo set de metodos genericos sirve para
/// los tres (mismo enfoque que IInventoryCatalogService). Tenant-scoped por el filtro global.
/// No se borran fisicamente: se archivan (IsActive=false); tipos de proyecto solo si no estan
/// referenciados por un proyecto.
/// </summary>
public interface IActivityCatalogService
{
    Task<IReadOnlyList<ActivityCatalogEntryDto>> ListAsync(ActivityCatalogKind kind, bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<ActivityCatalogEntryDto?> GetAsync(ActivityCatalogKind kind, Guid id, CancellationToken cancellationToken = default);
    Task<TaskCoreResult<ActivityCatalogEntryDto>> CreateAsync(ActivityCatalogKind kind, SaveActivityCatalogRequest request, CancellationToken cancellationToken = default);
    Task<TaskCoreResult<ActivityCatalogEntryDto>> UpdateAsync(ActivityCatalogKind kind, Guid id, SaveActivityCatalogRequest request, CancellationToken cancellationToken = default);
    Task<TaskCoreResult<bool>> SetActiveAsync(ActivityCatalogKind kind, Guid id, bool active, CancellationToken cancellationToken = default);
}
