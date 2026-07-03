using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

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

public sealed record UpdatePipelineStageRequest(
    string Name,
    bool IsClosedWon,
    bool IsClosedLost);

/// <summary>Nuevo orden de las etapas: lista de ids en el orden deseado.</summary>
public sealed record ReorderStagesRequest(IReadOnlyList<Guid> OrderedStageIds);
public sealed record ReorderFieldsRequest(IReadOnlyList<Guid> OrderedFieldIds);

// --- Campos configurables ---
public sealed record PipelineFieldDto(
    Guid Id,
    Guid StageId,
    string FieldKey,
    string Label,
    PipelineFieldType FieldType,
    int Column,
    int SortOrder,
    string? Options,
    string? Description = null,
    bool AllowMultiple = false,
    string? RepeatWithFieldKey = null,
    bool MultiWithDetail = false,
    string? TotalSourceKeys = null,
    bool ShowInFilter = false);

public sealed record CreatePipelineFieldRequest(
    Guid StageId,
    string Label,
    PipelineFieldType FieldType,
    int Column = 1,
    string? Options = null,
    string? FieldKey = null,
    string? Description = null,
    bool AllowMultiple = false,
    string? RepeatWithFieldKey = null,
    bool MultiWithDetail = false,
    string? TotalSourceKeys = null,
    bool ShowInFilter = false);

public sealed record UpdatePipelineFieldRequest(
    string Label,
    PipelineFieldType FieldType,
    int Column,
    string? Options,
    string? Description = null,
    bool AllowMultiple = false,
    string? RepeatWithFieldKey = null,
    bool MultiWithDetail = false,
    string? TotalSourceKeys = null,
    bool ShowInFilter = false);
