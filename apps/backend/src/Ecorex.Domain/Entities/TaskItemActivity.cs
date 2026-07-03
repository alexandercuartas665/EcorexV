using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Entrada en el log de actividad de una tarea del nucleo: comentario de un usuario o
/// accion automatica del sistema (creo, cambio estado, asigno, etc.). Reutiliza el enum
/// TaskActivityType (Action/Comment) del modulo de tableros. TENANT-SCOPED.
/// </summary>
public class TaskItemActivity : TenantEntity
{
    public Guid TaskItemId { get; set; }
    public TaskItem? TaskItem { get; set; }

    public TaskActivityType Type { get; set; }

    /// <summary>PlatformUser que origino la actividad. Null si fue el sistema.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>Nombre legible del autor en el momento (capturado por si despues cambia/sale).</summary>
    public string ActorName { get; set; } = null!;

    /// <summary>El comentario, o la descripcion legible de la accion (ej. "cambio el estado a InProgress").</summary>
    public string Text { get; set; } = null!;
}
