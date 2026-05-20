namespace CubotTravels.Application.Tenancy;

public sealed record StageLeadCount(Guid StageId, string StageName, int Count);

public sealed record TenantDashboardDto(
    int TotalLeads,
    int OpenLeads,
    int WonLeads,
    int LostLeads,
    int PendingFollowUps,
    int ConnectedLines,
    int TotalConversations,
    IReadOnlyList<StageLeadCount> LeadsByStage);
