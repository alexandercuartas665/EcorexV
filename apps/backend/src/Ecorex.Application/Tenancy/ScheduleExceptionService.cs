using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed record ScheduleExceptionDto(Guid Id, ExceptionScope Scope, Guid? ResourceId, string? ResourceName, DateOnly DateFrom, DateOnly DateTo, ExceptionReason Reason, string? Note);
public sealed record SaveExceptionRequest(ExceptionScope Scope, Guid? ResourceId, DateOnly DateFrom, DateOnly DateTo, ExceptionReason Reason, string? Note);

/// <summary>Excepciones/bloqueos de agenda, globales del salon o por asesor (Modulo 2.4).</summary>
public interface IScheduleExceptionService
{
    Task<IReadOnlyList<ScheduleExceptionDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<ScheduleExceptionDto?> CreateAsync(SaveExceptionRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
}

public sealed class ScheduleExceptionService : IScheduleExceptionService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public ScheduleExceptionService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db; _tenantContext = tenantContext; _audit = audit;
    }

    public async Task<IReadOnlyList<ScheduleExceptionDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var exceptions = await _db.ScheduleExceptions.AsNoTracking()
            .OrderByDescending(e => e.DateFrom)
            .ToListAsync(cancellationToken);
        var resourceIds = exceptions.Where(e => e.ResourceId.HasValue).Select(e => e.ResourceId!.Value).Distinct().ToList();
        var names = await _db.Resources.AsNoTracking()
            .Where(r => resourceIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);
        return exceptions.Select(e => new ScheduleExceptionDto(
            e.Id, e.Scope, e.ResourceId,
            e.ResourceId is Guid rid && names.TryGetValue(rid, out var n) ? n : null,
            e.DateFrom, e.DateTo, e.Reason, e.Note)).ToList();
    }

    public async Task<ScheduleExceptionDto?> CreateAsync(SaveExceptionRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        if (request.DateTo < request.DateFrom) { return null; }

        var scope = request.Scope;
        var resourceId = scope == ExceptionScope.Resource ? request.ResourceId : null;
        if (scope == ExceptionScope.Resource && resourceId is null) { return null; }
        if (resourceId is Guid rid && !await _db.Resources.AnyAsync(r => r.Id == rid, cancellationToken)) { return null; }

        var entity = new ScheduleException
        {
            TenantId = tenantId,
            Scope = scope,
            ResourceId = resourceId,
            DateFrom = request.DateFrom,
            DateTo = request.DateTo,
            Reason = request.Reason,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()
        };
        _db.ScheduleExceptions.Add(entity);
        _audit.Write(actorUserId, "exception.create", nameof(ScheduleException), entity.Id, null, new { entity.Scope, entity.DateFrom, entity.DateTo }, tenantId);
        await _db.SaveChangesAsync(cancellationToken);

        var name = resourceId is Guid id2
            ? await _db.Resources.Where(r => r.Id == id2).Select(r => r.Name).FirstOrDefaultAsync(cancellationToken)
            : null;
        return new ScheduleExceptionDto(entity.Id, entity.Scope, entity.ResourceId, name, entity.DateFrom, entity.DateTo, entity.Reason, entity.Note);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.ScheduleExceptions.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entity is null) { return false; }
        _db.ScheduleExceptions.Remove(entity);
        _audit.Write(actorUserId, "exception.delete", nameof(ScheduleException), entity.Id, new { entity.Scope, entity.DateFrom }, null, entity.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
