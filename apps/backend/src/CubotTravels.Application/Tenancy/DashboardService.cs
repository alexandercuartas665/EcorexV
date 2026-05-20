using CubotTravels.Application.Common;
using CubotTravels.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotTravels.Application.Tenancy;

public sealed class DashboardService : IDashboardService
{
    private readonly IApplicationDbContext _db;

    public DashboardService(IApplicationDbContext db) => _db = db;

    public async Task<TenantDashboardDto> GetAsync(CancellationToken cancellationToken = default)
    {
        // Todas las consultas quedan acotadas por el filtro global de tenant.
        var totalLeads = await _db.Leads.CountAsync(cancellationToken);
        var openLeads = await _db.Leads.CountAsync(l => l.Status == LeadStatus.Open, cancellationToken);
        var wonLeads = await _db.Leads.CountAsync(l => l.Status == LeadStatus.Won, cancellationToken);
        var lostLeads = await _db.Leads.CountAsync(l => l.Status == LeadStatus.Lost, cancellationToken);
        var pendingFollowUps = await _db.FollowUpTasks.CountAsync(t => t.Status == FollowUpTaskStatus.Pending, cancellationToken);
        var connectedLines = await _db.WhatsAppLines.CountAsync(l => l.Status == WhatsAppLineStatus.Connected, cancellationToken);
        var conversations = await _db.Conversations.CountAsync(cancellationToken);

        var byStage = await _db.PipelineStages
            .OrderBy(s => s.SortOrder)
            .Select(s => new StageLeadCount(s.Id, s.Name, _db.Leads.Count(l => l.StageId == s.Id)))
            .ToListAsync(cancellationToken);

        return new TenantDashboardDto(
            totalLeads, openLeads, wonLeads, lostLeads,
            pendingFollowUps, connectedLines, conversations, byStage);
    }
}
