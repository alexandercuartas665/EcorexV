using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Asigna un formulario dinamico a un nodo de flujo (ADR-0015): cuando una instancia activa
/// un paso de ese nodo, el detalle de la tarea ofrece el formulario y el paso no se completa
/// hasta enviarlo (FormFlowLink Pending -> Completed). Un nodo tiene a lo sumo un formulario
/// (indice unico por NodeId). TENANT-SCOPED.
/// </summary>
public class WorkflowNodeForm : TenantEntity
{
    public Guid NodeId { get; set; }
    public WorkflowNode? Node { get; set; }

    public Guid DefinitionId { get; set; }
    public FormDefinition? Definition { get; set; }
}
