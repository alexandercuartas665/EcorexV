namespace Ecorex.Application.Admin;

public interface IAuditAdminService
{
    Task<IReadOnlyList<AuditLogListItem>> ListRecentAsync(int take = 100, CancellationToken cancellationToken = default);
}
