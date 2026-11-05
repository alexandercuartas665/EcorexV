using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>Implementacion de <see cref="ITenantOperatingCalendarService"/>. Tenant-scoped por el filtro global
/// del DbContext; en el alta fija el TenantId del contexto.</summary>
public sealed class TenantOperatingCalendarService : ITenantOperatingCalendarService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public TenantOperatingCalendarService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<TenantOperatingDayDto>> ListAsync(int? year = null, CancellationToken cancellationToken = default)
    {
        var query = _db.TenantOperatingDays.AsNoTracking();
        if (year is int y)
        {
            var from = new DateOnly(y, 1, 1);
            var to = new DateOnly(y, 12, 31);
            query = query.Where(d => d.Date >= from && d.Date <= to);
        }
        return await query
            .OrderBy(d => d.Date)
            .Select(d => new TenantOperatingDayDto(d.Id, d.Date, d.Reason))
            .ToListAsync(cancellationToken);
    }

    public async Task<TenantOperatingDayDto> AddAsync(DateOnly date, string? reason, CancellationToken cancellationToken = default)
    {
        var cleanReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        var existing = await _db.TenantOperatingDays.FirstOrDefaultAsync(d => d.Date == date, cancellationToken);
        if (existing is not null)
        {
            existing.Reason = cleanReason;   // idempotente por fecha: actualiza el motivo
            await _db.SaveChangesAsync(cancellationToken);
            return new TenantOperatingDayDto(existing.Id, existing.Date, existing.Reason);
        }
        var day = new TenantOperatingDay
        {
            TenantId = _tenant.TenantId ?? Guid.Empty,
            Date = date,
            Reason = cleanReason
        };
        _db.TenantOperatingDays.Add(day);
        await _db.SaveChangesAsync(cancellationToken);
        return new TenantOperatingDayDto(day.Id, day.Date, day.Reason);
    }

    public async Task<int> SeedColombianHolidaysAsync(int year, CancellationToken cancellationToken = default)
    {
        var holidays = ColombianHolidays.ForYear(year);
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);

        // Fechas ya marcadas en el anio: no se duplican (idempotente por fecha).
        var existing = await _db.TenantOperatingDays
            .Where(d => d.Date >= from && d.Date <= to)
            .Select(d => d.Date)
            .ToListAsync(cancellationToken);
        var have = existing.ToHashSet();

        var added = 0;
        foreach (var h in holidays)
        {
            if (have.Add(h.Date))
            {
                _db.TenantOperatingDays.Add(new TenantOperatingDay
                {
                    TenantId = _tenant.TenantId ?? Guid.Empty,
                    Date = h.Date,
                    Reason = h.Name
                });
                added++;
            }
        }

        if (added > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        return added;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var day = await _db.TenantOperatingDays.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (day is null) { return false; }
        _db.TenantOperatingDays.Remove(day);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
