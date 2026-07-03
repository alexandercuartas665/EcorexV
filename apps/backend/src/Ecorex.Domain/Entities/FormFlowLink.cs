using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Vinculo entre una respuesta de formulario y un paso de flujo (ADR-0015): mientras este
/// Pending el paso del workflow no debe completarse manualmente; al enviar el formulario,
/// FormResponseService marca el link Completed y completa el paso via IWorkflowEngine en la
/// misma transaccion logica. Unico por (WorkflowInstanceId, WorkflowNodeId, FormResponseId).
/// TENANT-SCOPED.
/// </summary>
public class FormFlowLink : TenantEntity
{
    public Guid FormResponseId { get; set; }
    public FormResponse? FormResponse { get; set; }

    public Guid WorkflowInstanceId { get; set; }
    public WorkflowInstance? WorkflowInstance { get; set; }

    /// <summary>Nodo del flujo cuyo paso espera este formulario. FK NO ACTION.</summary>
    public Guid WorkflowNodeId { get; set; }
    public WorkflowNode? WorkflowNode { get; set; }

    public FormFlowLinkStatus Status { get; set; } = FormFlowLinkStatus.Pending;
}
