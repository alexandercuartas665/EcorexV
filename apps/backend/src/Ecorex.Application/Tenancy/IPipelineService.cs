namespace Ecorex.Application.Tenancy;

/// <summary>Etapas y campos configurables del embudo del tenant activo (modulo 2.1). Tenant-scoped.</summary>
public interface IPipelineService
{
    /// <summary>Crea las etapas y campos por defecto (del diseno) si el tenant aun no tiene etapas.</summary>
    Task EnsureDefaultsAsync(Guid actorUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PipelineStageDto>> ListStagesAsync(CancellationToken cancellationToken = default);

    /// <summary>Devuelve null si no hay tenant activo.</summary>
    Task<PipelineStageDto?> CreateStageAsync(CreatePipelineStageRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<PipelineStageDto?> UpdateStageAsync(Guid stageId, UpdatePipelineStageRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task ReorderStagesAsync(ReorderStagesRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Elimina una etapa solo si no tiene leads. Devuelve false si tiene leads o no existe.</summary>
    Task<bool> DeleteStageAsync(Guid stageId, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PipelineFieldDto>> ListFieldsAsync(CancellationToken cancellationToken = default);
    Task<PipelineFieldDto?> CreateFieldAsync(CreatePipelineFieldRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<PipelineFieldDto?> UpdateFieldAsync(Guid fieldId, UpdatePipelineFieldRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    /// <summary>Mueve un campo configurable a otra etapa (lo coloca al final de la etapa destino).</summary>
    Task<PipelineFieldDto?> MoveFieldToStageAsync(Guid fieldId, Guid targetStageId, Guid actorUserId, CancellationToken cancellationToken = default);
    Task ReorderFieldsAsync(ReorderFieldsRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteFieldAsync(Guid fieldId, Guid actorUserId, CancellationToken cancellationToken = default);
}
