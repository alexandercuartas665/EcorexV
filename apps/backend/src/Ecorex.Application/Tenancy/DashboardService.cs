using Ecorex.Application.Common;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

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

    public async Task<TenantReportsDto> GetReportsAsync(CancellationToken cancellationToken = default)
    {
        // Proyeccion liviana de leads (todos: activos + historial); se agrega en memoria.
        var leads = await _db.Leads
            .Select(l => new LeadRow(l.StageId, l.Status, l.EstimatedValue, l.AssignedToTenantUserId,
                l.Destination, l.ArchivedAt, l.ArchiveReason, l.CreatedAt))
            .ToListAsync(cancellationToken);

        var stages = await _db.PipelineStages
            .OrderBy(s => s.SortOrder)
            .Select(s => new StageRow(s.Id, s.Name, s.IsClosedWon, s.IsClosedLost))
            .ToListAsync(cancellationToken);

        var advisors = await _db.TenantUsers
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(cancellationToken);
        var advisorName = advisors.ToDictionary(a => a.Id, a => a.Email);

        // ===== KPIs =====
        var totalLeads = leads.Count;
        var openLeads = leads.Count(l => l.Status == LeadStatus.Open && l.ArchivedAt == null);
        var wonLeads = leads.Count(l => l.Status == LeadStatus.Won);
        var lostLeads = leads.Count(l => l.Status == LeadStatus.Lost);
        var pipelineValue = leads.Where(l => l.Status == LeadStatus.Open && l.ArchivedAt == null).Sum(l => l.EstimatedValue ?? 0m);
        var wonValue = leads.Where(l => l.Status == LeadStatus.Won).Sum(l => l.EstimatedValue ?? 0m);
        var avgTicket = wonLeads > 0 ? Math.Round(wonValue / wonLeads, 0) : 0m;
        var decided = wonLeads + lostLeads;
        var winRate = decided > 0 ? Math.Round(100.0 * wonLeads / decided, 1) : 0;

        // ===== 1. Embudo (leads activos por etapa) =====
        var funnel = stages.Select(s =>
        {
            var count = leads.Count(l => l.StageId == s.Id && l.ArchivedAt == null);
            var kind = s.IsClosedWon ? "won" : s.IsClosedLost ? "lost" : "open";
            return new FunnelStageDto(s.Name, count, kind);
        }).ToList();

        // ===== 2. Rendimiento por asesor =====
        var advisorsReport = leads
            .Where(l => l.AssignedToTenantUserId is not null)
            .GroupBy(l => l.AssignedToTenantUserId!.Value)
            .Select(g =>
            {
                var won = g.Count(l => l.Status == LeadStatus.Won);
                var lost = g.Count(l => l.Status == LeadStatus.Lost);
                var open = g.Count(l => l.Status == LeadStatus.Open);
                var dec = won + lost;
                return new AdvisorPerfDto(
                    advisorName.TryGetValue(g.Key, out var n) ? n : "Asesor",
                    g.Count(), won, lost, open,
                    g.Where(l => l.Status == LeadStatus.Won).Sum(l => l.EstimatedValue ?? 0m),
                    dec > 0 ? Math.Round(100.0 * won / dec, 1) : 0);
            })
            .OrderByDescending(a => a.WonValue)
            .ToList();

        // ===== 3. Ventas por mes (ultimos 6 meses, por fecha de creacion) =====
        var now = DateTimeOffset.UtcNow;
        var monthly = new List<MonthValueDto>();
        for (var i = 5; i >= 0; i--)
        {
            var month = new DateTime(now.Year, now.Month, 1).AddMonths(-i);
            var next = month.AddMonths(1);
            var inMonth = leads.Where(l => l.CreatedAt.UtcDateTime >= month && l.CreatedAt.UtcDateTime < next).ToList();
            monthly.Add(new MonthValueDto(
                month.ToString("MMM yy", System.Globalization.CultureInfo.GetCultureInfo("es-ES")),
                inMonth.Count,
                inMonth.Sum(l => l.EstimatedValue ?? 0m)));
        }

        // ===== 4. Motivos de perdida (leads en historial) =====
        var lossReasons = leads
            .Where(l => l.ArchivedAt != null)
            .GroupBy(l => string.IsNullOrWhiteSpace(l.ArchiveReason) ? "Sin motivo" : l.ArchiveReason!)
            .Select(g => new LossReasonDto(g.Key, g.Count()))
            .OrderByDescending(r => r.Count)
            .ToList();

        // ===== 5. Destinos mas solicitados =====
        var destinations = leads
            .Where(l => !string.IsNullOrWhiteSpace(l.Destination))
            .GroupBy(l => l.Destination!.Trim())
            .Select(g => new DestinationStatDto(g.Key, g.Count(), g.Sum(l => l.EstimatedValue ?? 0m)))
            .OrderByDescending(d => d.Count)
            .Take(8)
            .ToList();

        // ===== 6. Actividad de WhatsApp =====
        var totalConversations = await _db.Conversations.CountAsync(cancellationToken);
        var convLines = await _db.Conversations
            .Select(c => new { c.Id, c.WhatsAppLineId })
            .ToListAsync(cancellationToken);
        var msgs = await _db.Messages
            .Select(m => new { m.ConversationId, m.Direction, m.SentAt })
            .ToListAsync(cancellationToken);

        var inbound = msgs.Count(m => m.Direction == MessageDirection.Inbound);
        var outbound = msgs.Count(m => m.Direction == MessageDirection.Outbound);

        // Conversaciones sin responder: ultimo entrante es posterior al ultimo saliente.
        var unanswered = 0;
        foreach (var grp in msgs.GroupBy(m => m.ConversationId))
        {
            var lastIn = grp.Where(m => m.Direction == MessageDirection.Inbound).Select(m => (DateTimeOffset?)m.SentAt).Max();
            if (lastIn is null) { continue; }
            var lastOut = grp.Where(m => m.Direction == MessageDirection.Outbound).Select(m => (DateTimeOffset?)m.SentAt).Max();
            if (lastOut is null || lastIn > lastOut) { unanswered++; }
        }

        var lines = await _db.WhatsAppLines
            .Select(l => new { l.Id, l.InstanceName, l.PhoneNumber })
            .ToListAsync(cancellationToken);
        var lineName = lines.ToDictionary(l => l.Id, l => string.IsNullOrWhiteSpace(l.PhoneNumber) ? l.InstanceName : l.PhoneNumber!);
        var convToLine = convLines.Where(c => c.WhatsAppLineId is not null).ToDictionary(c => c.Id, c => c.WhatsAppLineId!.Value);

        var lineActivity = msgs
            .Where(m => convToLine.ContainsKey(m.ConversationId))
            .GroupBy(m => convToLine[m.ConversationId])
            .Select(g => new LineActivityDto(
                lineName.TryGetValue(g.Key, out var n) ? n : "Linea",
                g.Count(m => m.Direction == MessageDirection.Inbound),
                g.Count(m => m.Direction == MessageDirection.Outbound)))
            .OrderByDescending(l => l.Inbound + l.Outbound)
            .ToList();

        return new TenantReportsDto(
            totalLeads, openLeads, wonLeads, lostLeads,
            pipelineValue, wonValue, avgTicket, winRate,
            funnel, advisorsReport, monthly, lossReasons, destinations,
            totalConversations, unanswered, inbound, outbound, lineActivity);
    }

    private sealed record LeadRow(Guid StageId, LeadStatus Status, decimal? EstimatedValue, Guid? AssignedToTenantUserId,
        string? Destination, DateTimeOffset? ArchivedAt, string? ArchiveReason, DateTimeOffset CreatedAt);
    private sealed record StageRow(Guid Id, string Name, bool IsClosedWon, bool IsClosedLost);
}
