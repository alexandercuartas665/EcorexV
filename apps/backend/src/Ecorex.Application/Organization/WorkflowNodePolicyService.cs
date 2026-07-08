using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Organization;

/// <summary>
/// Implementacion de IWorkflowNodePolicyService (ADR-0035). El aislamiento por tenant lo
/// garantiza el filtro global; el conteo de candidatos reusa INodeAssigneeResolver. Solo se
/// admiten unidades con Classifier Dependencia o Cargo (un Funcionario nunca es asignable).
/// </summary>
public sealed class WorkflowNodePolicyService : IWorkflowNodePolicyService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;

    public WorkflowNodePolicyService(IApplicationDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<NodePolicyDto>> ListNodePoliciesAsync(
        Guid nodeId, CancellationToken cancellationToken = default)
    {
        var rows = await _db.WorkflowNodePolicies.AsNoTracking()
            .Where(p => p.WorkflowNodeId == nodeId)
            .Join(_db.OrgUnits.AsNoTracking(), p => p.OrgUnitId, u => u.Id,
                (p, u) => new { p.Id, p.OrgUnitId, u.Name, u.Classifier, p.SortOrder })
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        // El conteo de candidatos por unidad es feedback del panel: se resuelve UNA vez todo
        // el nodo y luego se distribuye por unidad (barato: el arbol se recorre en memoria).
        var result = new List<NodePolicyDto>(rows.Count);
        var units = await LoadUnitRowsAsync(cancellationToken);
        var members = await LoadMemberRowsAsync(cancellationToken);
        foreach (var row in rows)
        {
            var count = OrgAssigneeTree.ResolveForUnit(row.OrgUnitId, units, members).Count;
            result.Add(new NodePolicyDto(row.Id, row.OrgUnitId, row.Name, row.Classifier, count));
        }
        return result;
    }

    public async Task<IReadOnlyList<AssignableOrgUnitDto>> ListAssignableUnitsAsync(
        CancellationToken cancellationToken = default)
    {
        var units = await _db.OrgUnits.AsNoTracking()
            .Where(u => !u.IsArchived
                && (u.Classifier == OrgUnitClassifier.Dependencia || u.Classifier == OrgUnitClassifier.Cargo))
            .Select(u => new { u.Id, u.Name, u.Classifier, u.ParentId, u.SortOrder })
            .ToListAsync(cancellationToken);

        // Ordena por arbol (dependencia -> sus cargos) para un dropdown legible con sangria.
        var byParent = units
            .GroupBy(u => u.ParentId)
            .ToDictionary(g => g.Key ?? Guid.Empty, g => g.OrderBy(u => u.SortOrder).ThenBy(u => u.Name).ToList());
        var visibleIds = units.Select(u => u.Id).ToHashSet();
        var result = new List<AssignableOrgUnitDto>();

        void Walk(Guid? parentId, int depth)
        {
            if (!byParent.TryGetValue(parentId ?? Guid.Empty, out var children))
            {
                return;
            }
            foreach (var u in children)
            {
                result.Add(new AssignableOrgUnitDto(u.Id, u.Name, u.Classifier, u.ParentId, depth));
                Walk(u.Id, depth + 1);
            }
        }

        // Raices: sin padre o con padre fuera del conjunto visible (padre archivado / Funcionario).
        foreach (var root in units
            .Where(u => u.ParentId is null || !visibleIds.Contains(u.ParentId.Value))
            .OrderBy(u => u.SortOrder).ThenBy(u => u.Name))
        {
            result.Add(new AssignableOrgUnitDto(root.Id, root.Name, root.Classifier, root.ParentId, 0));
            Walk(root.Id, 1);
        }
        return result;
    }

    public async Task<OrgResult<NodePolicyDto>> AddNodePolicyAsync(
        Guid nodeId, Guid orgUnitId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return OrgResult<NodePolicyDto>.Invalid("No hay tenant activo.");
        }
        var node = await _db.WorkflowNodes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == nodeId, cancellationToken);
        if (node is null)
        {
            return OrgResult<NodePolicyDto>.NotFound("El nodo de flujo no existe.");
        }
        var unit = await _db.OrgUnits.AsNoTracking().FirstOrDefaultAsync(u => u.Id == orgUnitId, cancellationToken);
        if (unit is null)
        {
            return OrgResult<NodePolicyDto>.NotFound("La dependencia/cargo no existe.");
        }
        if (unit.Classifier == OrgUnitClassifier.Funcionario)
        {
            return OrgResult<NodePolicyDto>.Invalid(
                "Un Funcionario no es asignable a un nodo: asigna la Dependencia o el Cargo.");
        }
        if (unit.IsArchived)
        {
            return OrgResult<NodePolicyDto>.Invalid("La dependencia/cargo esta archivada.");
        }
        if (await _db.WorkflowNodePolicies.AnyAsync(
                p => p.WorkflowNodeId == nodeId && p.OrgUnitId == orgUnitId, cancellationToken))
        {
            return OrgResult<NodePolicyDto>.Conflict("La dependencia/cargo ya esta asignada a este nodo.");
        }

        var maxOrder = await _db.WorkflowNodePolicies
            .Where(p => p.WorkflowNodeId == nodeId)
            .MaxAsync(p => (int?)p.SortOrder, cancellationToken) ?? 0;
        var policy = new WorkflowNodePolicy
        {
            TenantId = tenantId,
            WorkflowNodeId = nodeId,
            OrgUnitId = orgUnitId,
            SortOrder = maxOrder + 1
        };
        _db.WorkflowNodePolicies.Add(policy);
        await _db.SaveChangesAsync(cancellationToken);

        // Candidatos que aporta la unidad recien asignada (feedback de la fila).
        var unitCandidates = OrgAssigneeTree.ResolveForUnit(
            orgUnitId, await LoadUnitRowsAsync(cancellationToken), await LoadMemberRowsAsync(cancellationToken)).Count;
        return OrgResult<NodePolicyDto>.Ok(new NodePolicyDto(
            policy.Id, orgUnitId, unit.Name, unit.Classifier, unitCandidates));
    }

    public async Task<OrgResult<bool>> RemoveNodePolicyAsync(
        Guid policyId, CancellationToken cancellationToken = default)
    {
        var policy = await _db.WorkflowNodePolicies.FirstOrDefaultAsync(p => p.Id == policyId, cancellationToken);
        if (policy is null)
        {
            return OrgResult<bool>.NotFound("La asignacion no existe.");
        }
        _db.WorkflowNodePolicies.Remove(policy);
        await _db.SaveChangesAsync(cancellationToken);
        return OrgResult<bool>.Ok(true);
    }

    private async Task<IReadOnlyList<OrgAssigneeTree.UnitRow>> LoadUnitRowsAsync(CancellationToken cancellationToken)
        => await _db.OrgUnits.AsNoTracking()
            .Where(u => !u.IsArchived)
            .Select(u => new OrgAssigneeTree.UnitRow(
                u.Id, u.ParentId, u.Classifier, u.ResponsibleTenantUserId, u.TenantUserId))
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<OrgAssigneeTree.MemberRow>> LoadMemberRowsAsync(CancellationToken cancellationToken)
        => await _db.OrgUnitMembers.AsNoTracking()
            .Select(m => new OrgAssigneeTree.MemberRow(m.OrgUnitId, m.TenantUserId))
            .ToListAsync(cancellationToken);
}
