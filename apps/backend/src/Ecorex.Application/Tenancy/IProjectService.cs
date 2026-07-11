namespace Ecorex.Application.Tenancy;

/// <summary>
/// Proyectos del tenant activo (ADR-0013): CRUD con soft-archive, ACL propio
/// (owner + miembros con CanEdit) y concurrencia optimista portable (Version).
/// </summary>
public interface IProjectService
{
    Task<IReadOnlyList<ProjectDto>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<ProjectDto?> GetAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<TaskCoreResult<ProjectDto>> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken = default);
    /// <summary>Actualiza con token de concurrencia: version vieja -> resultado Conflict.</summary>
    Task<TaskCoreResult<ProjectDto>> UpdateAsync(Guid projectId, UpdateProjectRequest request, CancellationToken cancellationToken = default);
    /// <summary>Soft-archive (o restaurar): el proyecto conserva historia.</summary>
    Task<TaskCoreResult<bool>> SetArchivedAsync(Guid projectId, bool archived, CancellationToken cancellationToken = default);

    // ACL de miembros
    Task<IReadOnlyList<ProjectMemberDto>> ListMembersAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<TaskCoreResult<ProjectMemberDto>> AddMemberAsync(Guid projectId, Guid tenantUserId, bool canEdit, CancellationToken cancellationToken = default);
    Task<TaskCoreResult<ProjectMemberDto>> SetMemberCanEditAsync(Guid projectId, Guid tenantUserId, bool canEdit, CancellationToken cancellationToken = default);
    Task<TaskCoreResult<bool>> RemoveMemberAsync(Guid projectId, Guid tenantUserId, CancellationToken cancellationToken = default);

    /// <summary>Acceso del usuario al proyecto: owner = todo; miembro = ver (+editar si CanEdit).</summary>
    Task<ProjectAccessDto> CheckAccessAsync(Guid projectId, Guid tenantUserId, CancellationToken cancellationToken = default);

    // ---- Proyectos P1: hitos ----
    Task<IReadOnlyList<ProjectMilestoneDto>> ListMilestonesAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<TaskCoreResult<ProjectMilestoneDto>> AddMilestoneAsync(Guid projectId, CreateMilestoneRequest request, CancellationToken cancellationToken = default);
    Task<TaskCoreResult<ProjectMilestoneDto>> UpdateMilestoneAsync(Guid milestoneId, UpdateMilestoneRequest request, CancellationToken cancellationToken = default);
    Task<TaskCoreResult<ProjectMilestoneDto>> SetMilestoneCompletedAsync(Guid milestoneId, bool completed, CancellationToken cancellationToken = default);
    /// <summary>Borra un hito. Falla (Invalid) si tiene actividades enlazadas (desenlazar primero).</summary>
    Task<TaskCoreResult<bool>> RemoveMilestoneAsync(Guid milestoneId, CancellationToken cancellationToken = default);
}
