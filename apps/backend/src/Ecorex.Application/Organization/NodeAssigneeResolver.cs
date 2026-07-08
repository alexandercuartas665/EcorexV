using Ecorex.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Organization;

/// <summary>
/// Implementacion EF de INodeAssigneeResolver (ADR-0035). Carga la lista plana de unidades
/// del organigrama y sus miembros (a traves del filtro global de tenant) y delega la logica
/// de arbol a OrgAssigneeTree (pura, testeable). No muta nada.
/// </summary>
public sealed class NodeAssigneeResolver : INodeAssigneeResolver
{
    private readonly IApplicationDbContext _db;

    public NodeAssigneeResolver(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Guid>> ResolveCandidatesAsync(
        Guid workflowNodeId, CancellationToken cancellationToken = default)
    {
        var policyUnitIds = await _db.WorkflowNodePolicies.AsNoTracking()
            .Where(p => p.WorkflowNodeId == workflowNodeId)
            .Select(p => p.OrgUnitId)
            .ToListAsync(cancellationToken);
        if (policyUnitIds.Count == 0)
        {
            return [];
        }

        // Todo el organigrama del tenant (no archivadas): el resolver camina el subarbol de
        // cada unidad de policy. Incluye archivadas NO: una unidad archivada no atiende pasos.
        var units = await _db.OrgUnits.AsNoTracking()
            .Where(u => !u.IsArchived)
            .Select(u => new OrgAssigneeTree.UnitRow(
                u.Id, u.ParentId, u.Classifier, u.ResponsibleTenantUserId, u.TenantUserId))
            .ToListAsync(cancellationToken);
        var members = await _db.OrgUnitMembers.AsNoTracking()
            .Select(m => new OrgAssigneeTree.MemberRow(m.OrgUnitId, m.TenantUserId))
            .ToListAsync(cancellationToken);

        return OrgAssigneeTree.ResolveForUnits(policyUnitIds, units, members);
    }
}
