using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed record ShiftDto(Guid Id, Guid ResourceId, DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime, int SlotMinutes, int Slots);
public sealed record SaveShiftRequest(DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime, int SlotMinutes);

/// <summary>Turnos base recurrentes por recurso (Modulo 2.1). Cupos = floor((End-Start)/SlotMinutes).</summary>
public interface IShiftTemplateService
{
    Task<IReadOnlyList<ShiftDto>> ListAsync(Guid resourceId, CancellationToken cancellationToken = default);
    Task<ShiftDto?> AddAsync(Guid resourceId, SaveShiftRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<ShiftDto?> UpdateAsync(Guid shiftId, SaveShiftRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid shiftId, Guid actorUserId, CancellationToken cancellationToken = default);
}

public sealed class ShiftTemplateService : IShiftTemplateService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public ShiftTemplateService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db; _tenantContext = tenantContext; _audit = audit;
    }

    /// <summary>Numero de cupos utiles de un turno (floor de la duracion entre el tamano de bloque).</summary>
    public static int SlotsOf(TimeOnly start, TimeOnly end, int slotMinutes)
    {
        if (slotMinutes <= 0) { return 0; }
        var minutes = (int)(end.ToTimeSpan() - start.ToTimeSpan()).TotalMinutes;
        return minutes > 0 ? minutes / slotMinutes : 0;
    }

    public async Task<IReadOnlyList<ShiftDto>> ListAsync(Guid resourceId, CancellationToken cancellationToken = default)
    {
        var shifts = await _db.ShiftTemplates.AsNoTracking()
            .Where(s => s.ResourceId == resourceId)
            .OrderBy(s => s.DayOfWeek).ThenBy(s => s.StartTime)
            .ToListAsync(cancellationToken);
        return shifts.Select(Map).ToList();
    }

    public async Task<ShiftDto?> AddAsync(Guid resourceId, SaveShiftRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        if (!await _db.Resources.AnyAsync(r => r.Id == resourceId, cancellationToken)) { return null; }
        if (request.EndTime <= request.StartTime || request.SlotMinutes <= 0) { return null; }

        var shift = new ShiftTemplate
        {
            TenantId = tenantId,
            ResourceId = resourceId,
            DayOfWeek = request.DayOfWeek,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            SlotMinutes = request.SlotMinutes
        };
        _db.ShiftTemplates.Add(shift);
        _audit.Write(actorUserId, "shift.create", nameof(ShiftTemplate), shift.Id, null, new { shift.DayOfWeek, shift.StartTime, shift.EndTime, shift.SlotMinutes }, tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(shift);
    }

    public async Task<ShiftDto?> UpdateAsync(Guid shiftId, SaveShiftRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var shift = await _db.ShiftTemplates.FirstOrDefaultAsync(s => s.Id == shiftId, cancellationToken);
        if (shift is null) { return null; }
        if (request.EndTime <= request.StartTime || request.SlotMinutes <= 0) { return null; }

        shift.DayOfWeek = request.DayOfWeek;
        shift.StartTime = request.StartTime;
        shift.EndTime = request.EndTime;
        shift.SlotMinutes = request.SlotMinutes;
        _audit.Write(actorUserId, "shift.update", nameof(ShiftTemplate), shift.Id, null, new { shift.DayOfWeek, shift.StartTime, shift.EndTime, shift.SlotMinutes }, shift.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(shift);
    }

    public async Task<bool> DeleteAsync(Guid shiftId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var shift = await _db.ShiftTemplates.FirstOrDefaultAsync(s => s.Id == shiftId, cancellationToken);
        if (shift is null) { return false; }
        _db.ShiftTemplates.Remove(shift);
        _audit.Write(actorUserId, "shift.delete", nameof(ShiftTemplate), shift.Id, new { shift.DayOfWeek }, null, shift.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static ShiftDto Map(ShiftTemplate s) =>
        new(s.Id, s.ResourceId, s.DayOfWeek, s.StartTime, s.EndTime, s.SlotMinutes, SlotsOf(s.StartTime, s.EndTime, s.SlotMinutes));
}
