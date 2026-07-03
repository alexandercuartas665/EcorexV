namespace Ecorex.Application.Tenancy;

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

// ===== Reportes del modulo de Metricas =====
public sealed record FunnelStageDto(string StageName, int Count, string Kind); // Kind: open / won / lost
public sealed record AdvisorPerfDto(string Name, int Assigned, int Won, int Lost, int Open, decimal WonValue, double ConversionPct);
public sealed record MonthValueDto(string Label, int Leads, decimal Value);
public sealed record LossReasonDto(string Reason, int Count);
public sealed record DestinationStatDto(string Destination, int Count, decimal Value);
public sealed record LineActivityDto(string Line, int Inbound, int Outbound);

public sealed record TenantReportsDto(
    int TotalLeads,
    int OpenLeads,
    int WonLeads,
    int LostLeads,
    decimal PipelineValue,
    decimal WonValue,
    decimal AvgTicket,
    double WinRate,
    IReadOnlyList<FunnelStageDto> Funnel,
    IReadOnlyList<AdvisorPerfDto> Advisors,
    IReadOnlyList<MonthValueDto> Monthly,
    IReadOnlyList<LossReasonDto> LossReasons,
    IReadOnlyList<DestinationStatDto> Destinations,
    int TotalConversations,
    int UnansweredConversations,
    int InboundMessages,
    int OutboundMessages,
    IReadOnlyList<LineActivityDto> LineActivity);
