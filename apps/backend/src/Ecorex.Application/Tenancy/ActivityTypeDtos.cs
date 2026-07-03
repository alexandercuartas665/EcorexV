namespace Ecorex.Application.Tenancy;

public sealed record ActivityTypeDto(
    Guid Id, string Category, string Name, string? Description, int SortOrder, bool IsArchived,
    Guid? WorkflowDefinitionId, bool RequiresForm);

public sealed record CreateActivityTypeRequest(string Category, string Name, string? Description = null, int? SortOrder = null);

public sealed record UpdateActivityTypeRequest(string Category, string Name, string? Description, int SortOrder, bool IsArchived);
