namespace CubotTravels.Application.Tenancy;

/// <summary>Etapas configurables del embudo del tenant activo (modulo 2.1). Tenant-scoped.</summary>
public interface IPipelineService
{
    Task<IReadOnlyList<PipelineStageDto>> ListStagesAsync(CancellationToken cancellationToken = default);

    /// <summary>Devuelve null si no hay tenant activo.</summary>
    Task<PipelineStageDto?> CreateStageAsync(CreatePipelineStageRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
}
