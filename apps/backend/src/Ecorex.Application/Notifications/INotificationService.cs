using Ecorex.Domain.Enums;

namespace Ecorex.Application.Notifications;

/// <summary>
/// Entrega y consulta de notificaciones in-app por usuario (Ola 7 - endurecimiento). Tenant-scoped
/// por el filtro global. La ENTREGA la crean los servicios de dominio (ej. TaskItemService al
/// asignar) escribiendo filas Notification; este servicio cubre la lectura de la campana/bandeja y
/// el marcado de leidas, mas un CreateAsync para llamadores sueltos. Los metodos de lectura reciben
/// el PlatformUserId del usuario actual y resuelven su TenantUser dentro del tenant.
/// </summary>
public interface INotificationService
{
    /// <summary>Conteo de no leidas del usuario (para el badge de la campana).</summary>
    Task<int> UnreadCountForPlatformUserAsync(Guid platformUserId, CancellationToken cancellationToken = default);

    /// <summary>TenantUserId del usuario (por su PlatformUserId) en el tenant actual, o null. Lo usa la
    /// campana para unirse a su grupo SignalR y recibir el refresco en vivo.</summary>
    Task<Guid?> ResolveTenantUserIdAsync(Guid platformUserId, CancellationToken cancellationToken = default);

    /// <summary>Ultimas notificaciones del usuario (mas recientes primero).</summary>
    Task<IReadOnlyList<NotificationDto>> ListForPlatformUserAsync(
        Guid platformUserId, int take = 30, CancellationToken cancellationToken = default);

    /// <summary>Marca una notificacion como leida (solo si es del tenant actual). Devuelve false si no existe.</summary>
    Task<bool> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default);

    /// <summary>Marca todas las no leidas del usuario como leidas. Devuelve cuantas marco.</summary>
    Task<int> MarkAllReadForPlatformUserAsync(Guid platformUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea y persiste una notificacion para un TenantUser destinatario. Usado por llamadores que
    /// no participan de una transaccion de dominio (los que si -ej. asignar tarea- escriben la fila
    /// Notification en su propio SaveChanges).
    /// </summary>
    Task CreateAsync(Guid recipientTenantUserId, NotificationKind kind, string title, string body,
        string? linkRoute = null, Guid? relatedTaskItemId = null, string? actorName = null,
        CancellationToken cancellationToken = default);
}
