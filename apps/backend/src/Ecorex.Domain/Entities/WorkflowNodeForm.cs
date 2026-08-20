using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Asigna un formulario dinamico a un nodo de flujo (ADR-0015): cuando una instancia activa
/// un paso de ese nodo, el detalle de la tarea ofrece el/los formulario(s) y el paso NO se
/// completa hasta enviar TODOS (FormFlowLink Pending -> Completed por cada uno). Un nodo puede
/// tener VARIOS formularios (unico por {NodeId, DefinitionId}: no se repite el mismo). TENANT-SCOPED.
/// </summary>
public class WorkflowNodeForm : TenantEntity
{
    public Guid NodeId { get; set; }
    public WorkflowNode? Node { get; set; }

    public Guid DefinitionId { get; set; }
    public FormDefinition? Definition { get; set; }

    /// <summary>Orden del formulario dentro del nodo (0..n). Fija el orden de presentacion.</summary>
    public int SortOrder { get; set; }
}
