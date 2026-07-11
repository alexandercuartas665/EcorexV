using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Notificacion in-app dirigida a UN usuario del tenant (Ola 7 - entrega real de notificaciones).
/// Es la "bandeja" persistente que respalda la campana del workspace: a diferencia de la traza en
/// TaskItemActivity (historia de la tarea), esta fila representa la ENTREGA a un destinatario
/// concreto, con estado leido/no leido. TENANT-SCOPED.
/// </summary>
public class Notification : TenantEntity
{
    /// <summary>TenantUser destinatario (a quien se le entrega la notificacion).</summary>
    public Guid RecipientTenantUserId { get; set; }
    public TenantUser? RecipientTenantUser { get; set; }

    public NotificationKind Kind { get; set; } = NotificationKind.General;

    public string Title { get; set; } = null!;
    public string Body { get; set; } = null!;

    /// <summary>Ruta del workspace a la que lleva la notificacion (ej. la tarea). Opcional.</summary>
    public string? LinkRoute { get; set; }

    /// <summary>Tarea relacionada (si aplica), para trazabilidad.</summary>
    public Guid? RelatedTaskItemId { get; set; }

    /// <summary>Nombre legible de quien origino la notificacion (capturado en el momento).</summary>
    public string? ActorName { get; set; }

    public bool IsRead { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}
