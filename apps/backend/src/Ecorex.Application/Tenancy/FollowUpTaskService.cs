using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed class FollowUpTaskService : IFollowUpTaskService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public FollowUpTaskService(IApplicationDbContext db, ITenantContext tenantContext, TimeProvider timeProvider)
    {
        _db = db;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<FollowUpTaskDto>> ListAsync(Guid? leadId = null, FollowUpTaskStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = _db.FollowUpTasks.AsNoTracking();
        if (leadId is Guid l)
        {
            query = query.Where(t => t.LeadId == l);
        }

        if (status is FollowUpTaskStatus s)
        {
            query = query.Where(t => t.Status == s);
        }

        return await query
            .OrderBy(t => t.DueAt)
            .Select(t => Map(t))
            .ToListAsync(cancellationToken);
    }

    public async Task<FollowUpTaskDto?> CreateAsync(CreateFollowUpTaskRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return null;
        }

        var leadExists = await _db.Leads.AnyAsync(l => l.Id == request.LeadId, cancellationToken);
        if (!leadExists)
        {
            return null;
        }

        if (request.AssignedToTenantUserId is Guid assignee
            && !await _db.TenantUsers.AnyAsync(tu => tu.Id == assignee, cancellationToken))
        {
            return null;
        }

        var task = new FollowUpTask
        {
            TenantId = tenantId,
            LeadId = request.LeadId,
            Title = request.Title.Trim(),
            Notes = request.Notes?.Trim(),
            DueAt = request.DueAt,
            Status = FollowUpTaskStatus.Pending,
            AssignedToTenantUserId = request.AssignedToTenantUserId
        };
        _db.FollowUpTasks.Add(task);

        // Deja rastro en el historial del lead.
        _db.LeadActivities.Add(new LeadActivity
        {
            TenantId = tenantId,
            LeadId = request.LeadId,
            ActivityType = "lead.followup.created",
            Description = $"Seguimiento '{task.Title}' para {task.DueAt:yyyy-MM-dd}"
        });

        await _db.SaveChangesAsync(cancellationToken);
        return Map(task);
    }

    public Task<FollowUpTaskDto?> CompleteAsync(Guid taskId, Guid actorUserId, CancellationToken cancellationToken = default)
        => TransitionAsync(taskId, FollowUpTaskStatus.Done, cancellationToken);

    public Task<FollowUpTaskDto?> CancelAsync(Guid taskId, Guid actorUserId, CancellationToken cancellationToken = default)
        => TransitionAsync(taskId, FollowUpTaskStatus.Cancelled, cancellationToken);

    private async Task<FollowUpTaskDto?> TransitionAsync(Guid taskId, FollowUpTaskStatus status, CancellationToken cancellationToken)
    {
        var task = await _db.FollowUpTasks.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
        {
            return null;
        }

        if (task.Status != status)
        {
            task.Status = status;
            task.CompletedAt = status == FollowUpTaskStatus.Done ? _timeProvider.GetUtcNow() : null;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Map(task);
    }

    private static FollowUpTaskDto Map(FollowUpTask t) =>
        new(t.Id, t.LeadId, t.Title, t.Notes, t.DueAt, t.Status, t.AssignedToTenantUserId, t.CompletedAt);
}
