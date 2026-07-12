using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

public sealed record ProjectDto(
    Guid Id, string Code, string Name, string? Description, ProjectStatus Status,
    DateOnly? StartDate, DateOnly? EndDate, Guid OwnerTenantUserId, bool IsArchived,
    long Version, int TaskCount, int MemberCount);

public sealed record CreateProjectRequest(
    string Code, string Name, Guid OwnerTenantUserId, string? Description = null,
    ProjectStatus Status = ProjectStatus.Planning, DateOnly? StartDate = null, DateOnly? EndDate = null);

/// <summary>Version es el token de concurrencia optimista leido por el cliente (ADR-0013).</summary>
public sealed record UpdateProjectRequest(
    string Name, string? Description, ProjectStatus Status,
    DateOnly? StartDate, DateOnly? EndDate, Guid OwnerTenantUserId, long Version);

public sealed record ProjectMemberDto(Guid TenantUserId, string Email, bool CanEdit, bool IsOwner);

// ---- Proyectos P1: hitos ----

public sealed record ProjectMilestoneDto(
    Guid Id, Guid ProjectId, string Name, string? Description, DateOnly? DueDate,
    int SortOrder, bool IsCompleted, int TaskCount);

public sealed record CreateMilestoneRequest(string Name, DateOnly? DueDate = null, string? Description = null);

public sealed record UpdateMilestoneRequest(string Name, DateOnly? DueDate, string? Description);

// ---- Proyectos P2: presupuesto/costos ----

public sealed record ProjectBudgetItemDto(
    Guid Id, Guid ProjectId, string Name, string? Category,
    decimal PlannedAmount, decimal ActualAmount, string? Notes, int SortOrder);

public sealed record CreateBudgetItemRequest(
    string Name, decimal PlannedAmount = 0, decimal ActualAmount = 0, string? Category = null, string? Notes = null);

public sealed record UpdateBudgetItemRequest(
    string Name, decimal PlannedAmount, decimal ActualAmount, string? Category, string? Notes);

// ---- Proyectos P2: analisis DOFA/SWOT ----

public sealed record ProjectDofaDto(Guid Id, Guid ProjectId, Ecorex.Domain.Enums.DofaQuadrant Quadrant, string Text, int SortOrder);

public sealed record CreateDofaRequest(Ecorex.Domain.Enums.DofaQuadrant Quadrant, string Text);

public sealed record UpdateDofaRequest(string Text);

/// <summary>Resultado del chequeo de acceso al proyecto: owner o member ven; CanEdit permite editar.</summary>
public sealed record ProjectAccessDto(bool CanView, bool CanEdit);
