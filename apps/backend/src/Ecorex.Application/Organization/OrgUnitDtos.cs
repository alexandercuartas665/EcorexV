using Ecorex.Domain.Enums;

namespace Ecorex.Application.Organization;

/// <summary>Unidad plana del organigrama (fila de detalle / edicion).</summary>
public sealed record OrgUnitDto(
    Guid Id,
    string Name,
    OrgUnitKind Kind,
    Guid? ParentId,
    string? ParentName,
    Guid? ResponsibleTenantUserId,
    string? ResponsibleName,
    string? Description,
    int SortOrder,
    bool IsArchived,
    int MemberCount,
    // Asignacion por nodo (ADR-0035): clasificador semantico + usuario ocupante (Funcionario).
    OrgUnitClassifier Classifier = OrgUnitClassifier.Dependencia,
    Guid? TenantUserId = null,
    string? OccupantName = null);

/// <summary>Nodo del arbol del organigrama (hijos ordenados por SortOrder y nombre).</summary>
public sealed record OrgUnitNodeDto(
    Guid Id,
    string Name,
    OrgUnitKind Kind,
    Guid? ParentId,
    Guid? ResponsibleTenantUserId,
    string? ResponsibleName,
    string? Description,
    int SortOrder,
    bool IsArchived,
    int MemberCount,
    IReadOnlyList<OrgUnitNodeDto> Children,
    // Asignacion por nodo (ADR-0035): clasificador semantico + usuario ocupante (Funcionario).
    OrgUnitClassifier Classifier = OrgUnitClassifier.Dependencia,
    Guid? TenantUserId = null,
    string? OccupantName = null);

/// <summary>Miembro de una unidad con los datos de presentacion del usuario.</summary>
public sealed record OrgUnitMemberDto(
    Guid Id,
    Guid OrgUnitId,
    Guid TenantUserId,
    string Email,
    string? DisplayName,
    string? Role,
    bool IsResponsible = false);

/// <summary>Alta/edicion de una unidad del organigrama.</summary>
public sealed record SaveOrgUnitRequest(
    string Name,
    OrgUnitKind Kind = OrgUnitKind.Area,
    Guid? ParentId = null,
    Guid? ResponsibleTenantUserId = null,
    string? Description = null,
    int SortOrder = 0,
    // Asignacion por nodo (ADR-0035): clasificador + usuario ocupante (solo Funcionario).
    OrgUnitClassifier Classifier = OrgUnitClassifier.Dependencia,
    Guid? TenantUserId = null);

/// <summary>KPIs de cabecera del modulo (Dependencias / Usuarios / Areas, como el prototipo).</summary>
public sealed record OrgKpisDto(int TotalUnits, int AssignedUsers, int Areas);
