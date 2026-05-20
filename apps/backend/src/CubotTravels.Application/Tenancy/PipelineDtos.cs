namespace CubotTravels.Application.Tenancy;

public sealed record PipelineStageDto(
    Guid Id,
    string Name,
    int SortOrder,
    bool IsClosedWon,
    bool IsClosedLost);

public sealed record CreatePipelineStageRequest(
    string Name,
    int SortOrder,
    bool IsClosedWon = false,
    bool IsClosedLost = false);
