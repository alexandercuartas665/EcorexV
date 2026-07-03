using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

/// <summary>Tareas de seguimiento sobre leads del tenant activo (modulo 2.5). Tenant-scoped.</summary>
public interface IFollowUpTaskService
{
    Task<IReadOnlyList<FollowUpTaskDto>> ListAsync(Guid? leadId = null, FollowUpTaskStatus? status = null, CancellationToken cancellationToken = default);

    /// <summary>Devuelve null si no hay tenant activo o el lead no existe en el tenant.</summary>
    Task<FollowUpTaskDto?> CreateAsync(CreateFollowUpTaskRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<FollowUpTaskDto?> CompleteAsync(Guid taskId, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<FollowUpTaskDto?> CancelAsync(Guid taskId, Guid actorUserId, CancellationToken cancellationToken = default);
}
