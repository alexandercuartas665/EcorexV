using Ecorex.Domain.Enums;

namespace Ecorex.Application.Organization;

/// <summary>
/// Dependencia/Cargo asignada a un nodo de flujo (fila del panel "Asignar usuarios" del
/// editor, ADR-0035). CandidateCount = TenantUserIds distintos que resolveria esta unidad.
/// </summary>
public sealed record NodePolicyDto(
    Guid PolicyId,
    Guid OrgUnitId,
    string OrgUnitName,
    OrgUnitClassifier Classifier,
    int CandidateCount);

/// <summary>Unidad candidata a asignar (Dependencia|Cargo) para el selector del editor.</summary>
public sealed record AssignableOrgUnitDto(
    Guid Id,
    string Name,
    OrgUnitClassifier Classifier,
    Guid? ParentId,
    int Depth);
