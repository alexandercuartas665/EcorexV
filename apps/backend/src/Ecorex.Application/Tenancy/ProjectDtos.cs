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

/// <summary>Resultado del chequeo de acceso al proyecto: owner o member ven; CanEdit permite editar.</summary>
public sealed record ProjectAccessDto(bool CanView, bool CanEdit);
