using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Asignacion por nodo (ADR-0035, ola F1): que Dependencia o Cargo del organigrama atiende
/// un paso Task del flujo. NO se asignan usuarios directos: el resolver expande la unidad a
/// los TenantUserIds candidatos (Funcionarios descendientes + miembros + responsable). Solo
/// se admiten OrgUnit con Classifier Dependencia o Cargo (un Funcionario NUNCA es asignable;
/// se valida en el servicio). FK al nodo en cascada; a la unidad NO ACTION. Unico por
/// (WorkflowNodeId, OrgUnitId). El motor de ejecucion (bandeja/atender) es la ola F2.
/// TENANT-SCOPED.
/// </summary>
public class WorkflowNodePolicy : TenantEntity
{
    public Guid WorkflowNodeId { get; set; }
    public WorkflowNode? WorkflowNode { get; set; }

    public Guid OrgUnitId { get; set; }
    public OrgUnit? OrgUnit { get; set; }

    /// <summary>Orden de la unidad entre las asignadas al nodo.</summary>
    public int SortOrder { get; set; }
}
