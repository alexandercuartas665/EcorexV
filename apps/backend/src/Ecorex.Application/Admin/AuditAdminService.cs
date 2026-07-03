using Ecorex.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Admin;

public sealed class AuditAdminService : IAuditAdminService
{
    private readonly IApplicationDbContext _db;

    public AuditAdminService(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<AuditLogListItem>> ListRecentAsync(int take = 100, CancellationToken cancellationToken = default)
    {
        var query =
            from a in _db.SuperAdminAuditLogs.AsNoTracking()
            join t in _db.Tenants.AsNoTracking() on a.TenantId equals t.Id into tenants
            from t in tenants.DefaultIfEmpty()
            orderby a.CreatedAt descending
            select new AuditLogListItem(
                a.CreatedAt,
                a.ActorType,
                a.ActorUserId,
                t != null ? t.Name : null,
                a.ActionName,
                a.EntityName,
                a.Reason);

        return await query.Take(take).ToListAsync(cancellationToken);
    }
}
