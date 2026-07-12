using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Notifications;

/// <summary>
/// Implementacion de <see cref="INotificationService"/> sobre <see cref="IApplicationDbContext"/>.
/// Tenant-scoped por el filtro global (una consulta cross-tenant es imposible por construccion).
/// </summary>
public sealed class NotificationService : INotificationService
{
    private readonly IApplicationDbContext _db;

    public NotificationService(IApplicationDbContext db) => _db = db;

    public async Task<int> UnreadCountForPlatformUserAsync(Guid platformUserId, CancellationToken cancellationToken = default)
    {
        var tenantUserId = await ResolveTenantUserIdAsync(platformUserId, cancellationToken);
        if (tenantUserId is not Guid uid)
        {
            return 0;
        }

        return await _db.Notifications.AsNoTracking()
            .CountAsync(n => n.RecipientTenantUserId == uid && !n.IsRead, cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationDto>> ListForPlatformUserAsync(
        Guid platformUserId, int take = 30, CancellationToken cancellationToken = default)
    {
        var tenantUserId = await ResolveTenantUserIdAsync(platformUserId, cancellationToken);
        if (tenantUserId is not Guid uid)
        {
            return Array.Empty<NotificationDto>();
        }

        return await _db.Notifications.AsNoTracking()
            .Where(n => n.RecipientTenantUserId == uid)
            .OrderByDescending(n => n.CreatedAt)
            .Take(Math.Clamp(take, 1, 100))
            .Select(n => new NotificationDto(
                n.Id, n.Kind, n.Title, n.Body, n.LinkRoute, n.RelatedTaskItemId, n.ActorName, n.IsRead, n.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }
        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
        return true;
    }

    public async Task<int> MarkAllReadForPlatformUserAsync(Guid platformUserId, CancellationToken cancellationToken = default)
    {
        var tenantUserId = await ResolveTenantUserIdAsync(platformUserId, cancellationToken);
        if (tenantUserId is not Guid uid)
        {
            return 0;
        }

        var pending = await _db.Notifications
            .Where(n => n.RecipientTenantUserId == uid && !n.IsRead)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
        {
            return 0;
        }
        var now = DateTimeOffset.UtcNow;
        foreach (var n in pending)
        {
            n.IsRead = true;
            n.ReadAt = now;
        }
        await _db.SaveChangesAsync(cancellationToken);
        return pending.Count;
    }

    public async Task CreateAsync(Guid recipientTenantUserId, NotificationKind kind, string title, string body,
        string? linkRoute = null, Guid? relatedTaskItemId = null, string? actorName = null,
        CancellationToken cancellationToken = default)
    {
        // No hay stamping automatico de TenantId: se toma del destinatario (tenant-scoped, asi que
        // solo resuelve si el TenantUser pertenece al tenant actual). Si no existe, no se entrega.
        var recipientTenantId = await _db.TenantUsers.AsNoTracking()
            .Where(u => u.Id == recipientTenantUserId)
            .Select(u => (Guid?)u.TenantId)
            .FirstOrDefaultAsync(cancellationToken);
        if (recipientTenantId is not Guid tenantId)
        {
            return;
        }

        _db.Notifications.Add(new Notification
        {
            TenantId = tenantId,
            RecipientTenantUserId = recipientTenantUserId,
            Kind = kind,
            Title = title,
            Body = body,
            LinkRoute = linkRoute,
            RelatedTaskItemId = relatedTaskItemId,
            ActorName = actorName,
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Guid?> ResolveTenantUserIdAsync(Guid platformUserId, CancellationToken cancellationToken = default)
    {
        // Tenant-scoped: dentro del tenant actual, el TenantUser cuyo PlatformUserId coincide.
        var id = await _db.TenantUsers.AsNoTracking()
            .Where(u => u.PlatformUserId == platformUserId)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return id;
    }
}
