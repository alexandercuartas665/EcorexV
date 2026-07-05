namespace Ecorex.Application.Tenancy;

public sealed record ActivityTypeDto(
    Guid Id, string Category, string Name, string? Description, int SortOrder, bool IsArchived,
    Guid? WorkflowDefinitionId, bool RequiresForm);

public sealed record CreateActivityTypeRequest(
    string Category, string Name, string? Description = null, int? SortOrder = null,
    Guid? WorkflowDefinitionId = null, bool RequiresForm = false);

public sealed record UpdateActivityTypeRequest(
    string Category, string Name, string? Description, int SortOrder, bool IsArchived,
    Guid? WorkflowDefinitionId = null, bool RequiresForm = false);

/// <summary>
/// Flujo PUBLICADO ofrecible como "proceso vinculado" de un concepto (modulo 000270,
/// analogo al combo DOC_PROCESOS del legacy). Solo definiciones publicadas no archivadas.
/// </summary>
public sealed record ActivityWorkflowOptionDto(Guid Id, string ProcessCode, string Name, int Version);

/// <summary>
/// Uso real del concepto: cuantas TaskItem lo referencian (analogo CANT_USADO del grid
/// Detalle legacy). OpenTasks = tareas no Done/Closed (incluye archivadas: siguen usando el tipo).
/// </summary>
public sealed record ActivityTypeUsageDto(Guid ActivityTypeId, int TotalTasks, int OpenTasks);
