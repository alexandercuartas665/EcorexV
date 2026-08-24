using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Nota colaborativa dejada sobre un NODO de una INSTANCIA de flujo (ADR-0071). A diferencia de:
/// - <see cref="WorkflowNode.Note"/> (nota de CONFIGURACION del editor, por definicion), y
/// - <see cref="WorkflowStepHistory.ApprovalComment"/> (anotacion del CIERRE de un paso),
/// esta es un recado del EQUIPO: cualquier miembro con acceso a la tarea puede dejar notas en
/// cualquier nodo -- incluidos pasos FUTUROS de los que no es encargado -- para avisar algo a
/// quien atienda ese paso. Se muestra en el menu del nodo dentro del diagrama de la tarea.
/// Append-only (no se edita ni borra desde la UI). TENANT-SCOPED.
/// </summary>
public class WorkflowNodeNote : TenantEntity
{
    public Guid InstanceId { get; set; }
    public WorkflowInstance? Instance { get; set; }

    public Guid NodeId { get; set; }
    public WorkflowNode? Node { get; set; }

    /// <summary>Autor de la nota (TenantUser). Nullable por robustez del historial (append-only).</summary>
    public Guid? AuthorTenantUserId { get; set; }

    /// <summary>Nombre para mostrar del autor, capturado al crear la nota.</summary>
    public string AuthorName { get; set; } = "Usuario";

    public string Text { get; set; } = null!;
}
