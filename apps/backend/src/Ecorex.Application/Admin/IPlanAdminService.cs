namespace Ecorex.Application.Admin;

public interface IPlanAdminService
{
    Task<PlanDetail> CreateAsync(CreatePlanRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<PlanDetail?> UpdateAsync(Guid id, CreatePlanRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlanDetail>> ListAsync(CancellationToken cancellationToken = default);
    Task<PlanDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PlanDetail?> SetActiveAsync(Guid id, bool isActive, Guid actorUserId, CancellationToken cancellationToken = default);
}
