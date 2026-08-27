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

    /// <summary>
    /// OBLIGATORIO para cerrar/decidir (ADR-0077): si es true, el paso de este nodo NO se puede cerrar
    /// (ni, en una compuerta atendida, elegir ruta) hasta ENVIAR este formulario. Si es false (por defecto),
    /// el formulario es opcional y el paso se puede cerrar directo (comportamiento de ADR-0070). Es una marca
    /// por nodo: cada flujo decide en cuales pasos el formulario es requisito de cierre.
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// CARGA AUTOMATICA al llegar al paso: si es true (por defecto), al activarse el paso de este nodo el
    /// formulario se materializa solo (se crea el borrador y aparece "Pendiente" en la tarea, como siempre).
    /// Si es false, el formulario NO se crea automaticamente al llegar; queda disponible para agregarlo a mano
    /// ("+ Agregar formulario") cuando se necesite. Marca por (nodo, formulario), hermana de <see cref="IsRequired"/>.
    /// </summary>
    public bool AutoCreateOnArrival { get; set; } = true;
}
