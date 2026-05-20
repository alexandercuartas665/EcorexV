using CubotTravels.Application.Common;
using CubotTravels.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CubotTravels.Application.Tenancy;

public sealed class PipelineService : IPipelineService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public PipelineService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
    }

    public async Task<IReadOnlyList<PipelineStageDto>> ListStagesAsync(CancellationToken cancellationToken = default)
    {
        return await _db.PipelineStages
            .AsNoTracking()
            .OrderBy(s => s.SortOrder)
            .Select(s => new PipelineStageDto(s.Id, s.Name, s.SortOrder, s.IsClosedWon, s.IsClosedLost))
            .ToListAsync(cancellationToken);
    }

    public async Task<PipelineStageDto?> CreateStageAsync(CreatePipelineStageRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return null;
        }

        var stage = new PipelineStage
        {
            TenantId = tenantId,
            Name = request.Name.Trim(),
            SortOrder = request.SortOrder,
            IsClosedWon = request.IsClosedWon,
            IsClosedLost = request.IsClosedLost
        };
        _db.PipelineStages.Add(stage);

        _audit.Write(actorUserId, "pipeline-stage.create", nameof(PipelineStage), stage.Id,
            previousValue: null,
            newValue: new { stage.Name, stage.SortOrder },
            tenantId: tenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return new PipelineStageDto(stage.Id, stage.Name, stage.SortOrder, stage.IsClosedWon, stage.IsClosedLost);
    }
}
