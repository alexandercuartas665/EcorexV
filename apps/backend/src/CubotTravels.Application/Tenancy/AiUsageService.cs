using CubotTravels.Application.Common;
using CubotTravels.Domain.Entities;
using CubotTravels.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotTravels.Application.Tenancy;

public sealed class AiUsageService : IAiUsageService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;

    public AiUsageService(IApplicationDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task RecordAsync(Guid? agentId, AiProvider provider, string model, int inputTokens, int outputTokens, string source, bool success, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return; }

        var total = inputTokens + outputTokens;
        var log = new AiUsageLog
        {
            TenantId = tenantId,
            AgentId = agentId,
            Provider = provider,
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = total,
            EstimatedCostUsd = AiCostEstimator.Estimate(provider, inputTokens, outputTokens),
            Source = string.IsNullOrWhiteSpace(source) ? "chat" : source,
            Success = success
        };
        _db.AiUsageLogs.Add(log);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AiUsageSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.AiUsageLogs.AsNoTracking()
            .Select(l => new { l.AgentId, l.InputTokens, l.OutputTokens, l.TotalTokens, l.EstimatedCostUsd })
            .ToListAsync(cancellationToken);

        var byAgent = rows
            .GroupBy(r => r.AgentId)
            .Select(g => new AgentUsageDto(
                g.Key,
                g.Count(),
                g.Sum(x => (long)x.InputTokens),
                g.Sum(x => (long)x.OutputTokens),
                g.Sum(x => (long)x.TotalTokens),
                g.Sum(x => x.EstimatedCostUsd)))
            .ToList();

        return new AiUsageSummaryDto(
            rows.Count,
            rows.Sum(x => (long)x.TotalTokens),
            rows.Sum(x => (long)x.InputTokens),
            rows.Sum(x => (long)x.OutputTokens),
            rows.Sum(x => x.EstimatedCostUsd),
            byAgent);
    }
}
