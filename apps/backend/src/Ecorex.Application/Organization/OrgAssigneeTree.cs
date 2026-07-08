using Ecorex.Domain.Enums;

namespace Ecorex.Application.Organization;

/// <summary>
/// Logica PURA (sin EF, testeable en unit tests) del resolver de candidatos por nodo
/// (asignacion por nodo, ADR-0035, ola F1). Dada la lista plana de unidades del organigrama
/// y sus miembros, expande una Dependencia o Cargo a los TenantUserIds que pueden atender el
/// paso: (a) los Funcionarios descendientes (recursivo por ParentId), (b) los miembros
/// (OrgUnitMember) de la unidad y sus descendientes, (c) el responsable de la unidad.
/// Distinct. La resolucion efectiva del paso y la bandeja son la ola F2.
/// </summary>
public static class OrgAssigneeTree
{
    /// <summary>Fila plana de unidad para el resolver (proyeccion de OrgUnit).</summary>
    public readonly record struct UnitRow(
        Guid Id, Guid? ParentId, OrgUnitClassifier Classifier,
        Guid? ResponsibleTenantUserId, Guid? TenantUserId);

    /// <summary>Fila plana de miembro (proyeccion de OrgUnitMember).</summary>
    public readonly record struct MemberRow(Guid OrgUnitId, Guid TenantUserId);

    /// <summary>
    /// Candidatos (TenantUserIds distintos) de UNA unidad raiz (Dependencia|Cargo) de una
    /// policy: union de funcionarios descendientes, miembros de la unidad+descendientes y
    /// responsable. Camina el subarbol con un set de visitados (tolera ciclos en datos).
    /// </summary>
    public static IReadOnlyList<Guid> ResolveForUnit(
        Guid rootUnitId, IReadOnlyList<UnitRow> units, IReadOnlyList<MemberRow> members)
    {
        var byParent = units
            .Where(u => u.ParentId is not null)
            .GroupBy(u => u.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());
        var byId = units.ToDictionary(u => u.Id);
        var membersByUnit = members
            .GroupBy(m => m.OrgUnitId)
            .ToDictionary(g => g.Key, g => g.Select(m => m.TenantUserId).ToList());

        var result = new HashSet<Guid>();
        var visited = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(rootUnitId);
        while (stack.Count > 0)
        {
            var unitId = stack.Pop();
            if (!visited.Add(unitId) || !byId.TryGetValue(unitId, out var unit))
            {
                continue;
            }
            // (a) Funcionario descendiente -> su usuario ocupante.
            if (unit.Classifier == OrgUnitClassifier.Funcionario && unit.TenantUserId is Guid occupant)
            {
                result.Add(occupant);
            }
            // (b) Miembros de la unidad.
            if (membersByUnit.TryGetValue(unitId, out var unitMembers))
            {
                foreach (var m in unitMembers)
                {
                    result.Add(m);
                }
            }
            // (c) Responsable de la unidad.
            if (unit.ResponsibleTenantUserId is Guid responsible)
            {
                result.Add(responsible);
            }
            if (byParent.TryGetValue(unitId, out var children))
            {
                foreach (var child in children)
                {
                    stack.Push(child.Id);
                }
            }
        }
        return result.ToList();
    }

    /// <summary>Candidatos distintos de VARIAS unidades de policy (union de ResolveForUnit).</summary>
    public static IReadOnlyList<Guid> ResolveForUnits(
        IEnumerable<Guid> rootUnitIds, IReadOnlyList<UnitRow> units, IReadOnlyList<MemberRow> members)
    {
        var result = new HashSet<Guid>();
        foreach (var rootId in rootUnitIds)
        {
            foreach (var candidate in ResolveForUnit(rootId, units, members))
            {
                result.Add(candidate);
            }
        }
        return result.ToList();
    }
}
