using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

// ===== DTOs de lectura para las vistas =====

public sealed record ServiceTierOption(HairLength Length, int DurationMinutes, decimal Price);
public sealed record ServiceOptionDto(Guid Id, string Name, int DurationMinutes, decimal Price, IReadOnlyList<ServiceTierOption>? Tiers = null);
public sealed record ClientSuggestionDto(Guid Id, string Name, string Phone);

public sealed record DayShiftDto(TimeOnly Start, TimeOnly End, int SlotMinutes);
public sealed record DayApptDto(Guid Id, TimeOnly StartTime, int DurationMinutes, string? ClientName, string ServicesText,
    AppointmentStatus Status, Guid? ChainId, int? ChainSequence, int? ChainTotal,
    Punctuality Punctuality = Punctuality.Unknown, IReadOnlyDictionary<string, string?>? FieldValues = null);
public sealed record DayResourceDto(Guid Id, string Name, ResourceKind Kind, string Color, bool BlockedAllDay,
    IReadOnlyList<DayShiftDto> Shifts, IReadOnlyList<DayApptDto> Appointments);
public sealed record DayKpisDto(int Total, int Unconfirmed, decimal IncomeCompleted, int NoShow);
public sealed record DayBoardDto(DateOnly Date, IReadOnlyList<DayResourceDto> Resources, DayKpisDto Kpis);

public sealed record ResourceLegendDto(Guid Id, string Name, string Color);
public sealed record WeekApptDto(Guid Id, DateOnly Date, TimeOnly StartTime, string Color, string? ClientFirstName,
    string ResourceFirstName, AppointmentStatus Status, Guid? ChainId);
public sealed record WeekDto(DateOnly WeekStart, IReadOnlyList<ResourceLegendDto> Resources, IReadOnlyList<WeekApptDto> Appointments);

public sealed record MonthDayDto(int Day, DateOnly Date, string Status, int Free, int Total);
public sealed record SlotDto(TimeOnly Time, bool Occupied, Guid? AppointmentId, string? ClientName, string? ServicesText, AppointmentStatus? Status);

public sealed record AppointmentChatDto(bool Outbound, string Body, string Time);
public sealed record AppointmentDetailDto(Guid Id, Guid ResourceId, string ResourceName, DateOnly Date, TimeOnly StartTime,
    Guid? ClientId, string? ClientName, string? ClientPhone, AppointmentStatus Status, Punctuality Punctuality, string? Notes,
    IReadOnlyList<Guid> ServiceIds, IReadOnlyList<AppointmentChatDto> Chat, Guid? ChainId, int? ChainSequence, int? ChainTotal,
    IReadOnlyDictionary<string, string?>? FieldValues = null);

// ===== DTOs de escritura (reserva) =====

public sealed record BookingChainStep(Guid ResourceId, TimeOnly StartTime);
public sealed record BookingChatLine(bool Outbound, string Body);
public sealed record BookingRequest(Guid? AppointmentId, Guid ResourceId, DateOnly Date, TimeOnly StartTime,
    string ClientName, string? ClientPhone, Guid? ClientId, IReadOnlyList<Guid> ServiceIds,
    AppointmentStatus Status, Punctuality Punctuality, string? Notes,
    IReadOnlyList<BookingChainStep> ChainSteps, IReadOnlyList<BookingChatLine> Chat, Guid? RescheduledFromId = null,
    IReadOnlyDictionary<string, string?>? FieldValues = null, HairLength? HairLength = null,
    BookingChannel Channel = BookingChannel.Reception);
public sealed record BookingResult(bool Success, Guid? AppointmentId, string? Error);
public sealed record RescheduleItemDto(Guid AppointmentId, Guid ResourceId, string ResourceName, DateOnly Date, TimeOnly StartTime,
    Guid? ClientId, string? ClientName, string? ClientPhone, string ServicesText, IReadOnlyList<Guid> ServiceIds, DateTimeOffset? CancelledAt);

/// <summary>
/// Motor de agenda: disponibilidad (turnos - excepciones - citas) para las vistas Dia/Semana/Asignacion,
/// y reserva de citas con ANTI-OVERBOOKING por SOLAPAMIENTO (exclusion constraint GiST + captura de
/// violacion 23505/23P01). Modulos 2.2/2.3.
/// </summary>
public interface IAgendaService
{
    Task<IReadOnlyList<ClientSuggestionDto>> SearchClientsAsync(string query, int take = 6, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ServiceOptionDto>> GetServiceOptionsAsync(Guid resourceId, CancellationToken cancellationToken = default);
    Task<DayBoardDto> GetDayBoardAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<WeekDto> GetWeekAsync(DateOnly weekStart, Guid? resourceFilter, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MonthDayDto>> GetMonthAvailabilityAsync(Guid resourceId, int year, int month, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SlotDto>> GetDaySlotsAsync(Guid resourceId, DateOnly date, CancellationToken cancellationToken = default);
    /// <summary>
    /// Horas de inicio LIBRES donde cabe el servicio completo (duracion por largo) sin cruzarse con otra cita,
    /// respetando el buffer y el modo de agenda del asesor (grilla o continuo por duracion). Si el largo no se
    /// conoce para un servicio que varia por largo, estima con el tier mas largo (conservador).
    /// </summary>
    Task<IReadOnlyList<TimeOnly>> GetAvailableStartsAsync(Guid resourceId, DateOnly date, IReadOnlyList<Guid> serviceIds, HairLength? hairLength, CancellationToken cancellationToken = default);
    Task<AppointmentDetailDto?> GetAppointmentAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BookingResult> SaveBookingAsync(BookingRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> CancelAppointmentAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RescheduleItemDto>> GetRescheduleQueueAsync(CancellationToken cancellationToken = default);
    Task<int> CountRescheduleQueueAsync(CancellationToken cancellationToken = default);
    Task<int> CountReschedulePendingAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<bool> DismissRescheduleAsync(Guid appointmentId, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> MarkPunctualityAsync(Guid appointmentId, Punctuality punctuality, Guid actorUserId, CancellationToken cancellationToken = default);
}

public sealed class AgendaService : IAgendaService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _clock;

    public AgendaService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit, TimeProvider clock)
    {
        _db = db; _tenantContext = tenantContext; _audit = audit; _clock = clock;
    }

    private static int SlotsOf(TimeOnly start, TimeOnly end, int slotMinutes)
    {
        if (slotMinutes <= 0) { return 0; }
        var minutes = (int)(end.ToTimeSpan() - start.ToTimeSpan()).TotalMinutes;
        return minutes > 0 ? minutes / slotMinutes : 0;
    }

    private static bool IsActiveStatus(AppointmentStatus s) => s != AppointmentStatus.Cancelled && s != AppointmentStatus.Rescheduled;

    private static string FirstName(string? name) => string.IsNullOrWhiteSpace(name) ? "?" : name.Trim().Split(' ')[0];

    public async Task<IReadOnlyList<ClientSuggestionDto>> SearchClientsAsync(string query, int take = 6, CancellationToken cancellationToken = default)
    {
        var q = (query ?? string.Empty).Trim();
        if (q.Length < 2) { return Array.Empty<ClientSuggestionDto>(); }
        var ql = q.ToLowerInvariant();
        return await _db.Clients.AsNoTracking()
            .Where(c => c.FullName.ToLower().Contains(ql) || c.Phone.Contains(q))
            .OrderBy(c => c.FullName)
            .Take(take)
            .Select(c => new ClientSuggestionDto(c.Id, c.FullName, c.Phone))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ServiceOptionDto>> GetServiceOptionsAsync(Guid resourceId, CancellationToken cancellationToken = default)
    {
        var resource = await _db.Resources.AsNoTracking().FirstOrDefaultAsync(r => r.Id == resourceId, cancellationToken);
        var links = await _db.ResourceServiceLinks.AsNoTracking().Where(l => l.ResourceId == resourceId).ToListAsync(cancellationToken);
        var services = await _db.Services.AsNoTracking().Where(s => s.IsActive).ToListAsync(cancellationToken);
        var svcById = services.ToDictionary(s => s.Id);
        var tiersBySvc = (await _db.ServicePriceTiers.AsNoTracking().ToListAsync(cancellationToken))
            .GroupBy(t => t.ServiceId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ServiceTierOption>)g.OrderBy(t => t.Length)
                .Select(t => new ServiceTierOption(t.Length, t.DurationMinutes, t.Price)).ToList());

        IReadOnlyList<ServiceTierOption>? TiersOf(Guid sid) => tiersBySvc.TryGetValue(sid, out var ts) && ts.Count > 0 ? ts : null;

        if (links.Count == 0)
        {
            // Sin servicios habilitados: ofrecer todo el catalogo activo a precio base.
            return services.OrderBy(s => s.Name)
                .Select(s => new ServiceOptionDto(s.Id, s.Name, s.DurationMinutes, s.Price, TiersOf(s.Id))).ToList();
        }

        var options = new List<ServiceOptionDto>();
        foreach (var l in links)
        {
            if (!svcById.TryGetValue(l.ServiceId, out var s)) { continue; }
            var price = resource?.Kind == ResourceKind.Image && l.PriceOverride is decimal o ? o : s.Price;
            options.Add(new ServiceOptionDto(s.Id, s.Name, s.DurationMinutes, price, TiersOf(s.Id)));
        }
        return options.OrderBy(o => o.Name).ToList();
    }

    public async Task<DayBoardDto> GetDayBoardAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var dow = date.DayOfWeek;
        var resources = await _db.Resources.AsNoTracking().Where(r => r.IsActive).OrderBy(r => r.Name).ToListAsync(cancellationToken);
        var resIds = resources.Select(r => r.Id).ToList();
        var shifts = await _db.ShiftTemplates.AsNoTracking().Where(s => s.DayOfWeek == dow && resIds.Contains(s.ResourceId)).ToListAsync(cancellationToken);
        var exceptions = await _db.ScheduleExceptions.AsNoTracking().Where(e => e.DateFrom <= date && e.DateTo >= date).ToListAsync(cancellationToken);
        var appts = await _db.Appointments.AsNoTracking()
            .Where(a => a.AppointmentDate == date && resIds.Contains(a.ResourceId) && a.Status != AppointmentStatus.Cancelled && a.Status != AppointmentStatus.Rescheduled)
            .ToListAsync(cancellationToken);
        var servicesText = await BuildServicesTextAsync(appts.Select(a => a.Id).ToList(), cancellationToken);
        var clientNames = await ClientNamesAsync(appts, cancellationToken);

        bool Blocked(Guid rid) => exceptions.Any(e => e.Scope == ExceptionScope.Global || e.ResourceId == rid);

        var resourceDtos = resources.Select(r => new DayResourceDto(
            r.Id, r.Name, r.Kind, r.Color ?? "#A03DC9", Blocked(r.Id),
            shifts.Where(s => s.ResourceId == r.Id).OrderBy(s => s.StartTime).Select(s => new DayShiftDto(s.StartTime, s.EndTime, s.SlotMinutes)).ToList(),
            appts.Where(a => a.ResourceId == r.Id).OrderBy(a => a.StartTime).Select(a => new DayApptDto(
                a.Id, a.StartTime, a.DurationMinutes,
                a.ClientId is Guid cid && clientNames.TryGetValue(cid, out var n) ? n : null,
                servicesText.TryGetValue(a.Id, out var st) ? st : "",
                a.Status, a.ChainId, a.ChainSequence, a.ChainTotal,
                a.Punctuality, SalonFieldJson.Parse(a.FieldValuesJson))).ToList()
        )).ToList();

        var kpis = new DayKpisDto(
            appts.Count,
            appts.Count(a => a.Status == AppointmentStatus.Scheduled),
            appts.Where(a => a.Status == AppointmentStatus.Completed).Sum(a => a.EstimatedValue ?? 0m),
            appts.Count(a => a.Status == AppointmentStatus.NoShow));

        return new DayBoardDto(date, resourceDtos, kpis);
    }

    public async Task<WeekDto> GetWeekAsync(DateOnly weekStart, Guid? resourceFilter, CancellationToken cancellationToken = default)
    {
        var weekEnd = weekStart.AddDays(6);
        var resources = await _db.Resources.AsNoTracking().Where(r => r.IsActive).OrderBy(r => r.Name).ToListAsync(cancellationToken);
        var visible = resourceFilter is Guid f ? resources.Where(r => r.Id == f).ToList() : resources;
        var visibleIds = visible.Select(r => r.Id).ToList();
        var byId = resources.ToDictionary(r => r.Id);

        var appts = await _db.Appointments.AsNoTracking()
            .Where(a => a.AppointmentDate >= weekStart && a.AppointmentDate <= weekEnd && visibleIds.Contains(a.ResourceId)
                        && a.Status != AppointmentStatus.Cancelled && a.Status != AppointmentStatus.Rescheduled)
            .ToListAsync(cancellationToken);
        var clientNames = await ClientNamesAsync(appts, cancellationToken);

        var apptDtos = appts.OrderBy(a => a.StartTime).Select(a =>
        {
            var res = byId.TryGetValue(a.ResourceId, out var r) ? r : null;
            return new WeekApptDto(a.Id, a.AppointmentDate, a.StartTime, res?.Color ?? "#A03DC9",
                a.ClientId is Guid cid && clientNames.TryGetValue(cid, out var n) ? FirstName(n) : "?",
                FirstName(res?.Name), a.Status, a.ChainId);
        }).ToList();

        var legend = visible.Select(r => new ResourceLegendDto(r.Id, r.Name, r.Color ?? "#A03DC9")).ToList();
        return new WeekDto(weekStart, legend, apptDtos);
    }

    public async Task<IReadOnlyList<MonthDayDto>> GetMonthAvailabilityAsync(Guid resourceId, int year, int month, CancellationToken cancellationToken = default)
    {
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var shifts = await _db.ShiftTemplates.AsNoTracking().Where(s => s.ResourceId == resourceId).ToListAsync(cancellationToken);
        var exceptions = await _db.ScheduleExceptions.AsNoTracking()
            .Where(e => (e.Scope == ExceptionScope.Global || e.ResourceId == resourceId) && e.DateFrom <= last && e.DateTo >= first)
            .ToListAsync(cancellationToken);
        var appts = await _db.Appointments.AsNoTracking()
            .Where(a => a.ResourceId == resourceId && a.AppointmentDate >= first && a.AppointmentDate <= last
                        && a.Status != AppointmentStatus.Cancelled && a.Status != AppointmentStatus.Rescheduled)
            .ToListAsync(cancellationToken);

        var result = new List<MonthDayDto>();
        for (var d = first; d <= last; d = d.AddDays(1))
        {
            var total = shifts.Where(s => s.DayOfWeek == d.DayOfWeek).Sum(s => SlotsOf(s.StartTime, s.EndTime, s.SlotMinutes));
            var blocked = exceptions.Any(e => e.DateFrom <= d && e.DateTo >= d);
            string status; int free = 0;
            if (total == 0 || blocked) { status = "closed"; }
            else
            {
                var occupied = appts.Count(a => a.AppointmentDate == d);
                free = total - occupied;
                status = free > 0 ? "available" : "full";
            }
            result.Add(new MonthDayDto(d.Day, d, status, Math.Max(0, free), total));
        }
        return result;
    }

    public async Task<IReadOnlyList<SlotDto>> GetDaySlotsAsync(Guid resourceId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var shifts = await _db.ShiftTemplates.AsNoTracking()
            .Where(s => s.ResourceId == resourceId && s.DayOfWeek == date.DayOfWeek)
            .OrderBy(s => s.StartTime).ToListAsync(cancellationToken);
        var appts = await _db.Appointments.AsNoTracking()
            .Where(a => a.ResourceId == resourceId && a.AppointmentDate == date && a.Status != AppointmentStatus.Cancelled && a.Status != AppointmentStatus.Rescheduled)
            .ToListAsync(cancellationToken);
        var servicesText = await BuildServicesTextAsync(appts.Select(a => a.Id).ToList(), cancellationToken);
        var clientNames = await ClientNamesAsync(appts, cancellationToken);

        // Una cita ocupa todo su intervalo [inicio, inicio + duracion + buffer), no solo su hora de inicio:
        // un cupo de la grilla esta ocupado si cae dentro del intervalo de alguna cita (no solo si coincide
        // el inicio). Asi un servicio de 45 min a las 9:00 marca tambien el cupo de las 9:30 como ocupado.
        Appointment? Covering(TimeOnly t) => appts.FirstOrDefault(a =>
            a.StartTime <= t && t < a.StartTime.AddMinutes(a.DurationMinutes + a.BufferMinutes));

        var slots = new List<SlotDto>();
        foreach (var sh in shifts)
        {
            var n = SlotsOf(sh.StartTime, sh.EndTime, sh.SlotMinutes);
            var t = sh.StartTime;
            for (var i = 0; i < n; i++)
            {
                var appt = Covering(t);
                if (appt is null)
                {
                    slots.Add(new SlotDto(t, false, null, null, null, null));
                }
                else
                {
                    slots.Add(new SlotDto(t, true, appt.Id,
                        appt.ClientId is Guid cid && clientNames.TryGetValue(cid, out var nm) ? nm : "?",
                        servicesText.TryGetValue(appt.Id, out var st) ? st : "", appt.Status));
                }
                t = t.AddMinutes(sh.SlotMinutes);
            }
        }
        return slots;
    }

    public async Task<IReadOnlyList<TimeOnly>> GetAvailableStartsAsync(Guid resourceId, DateOnly date, IReadOnlyList<Guid> serviceIds, HairLength? hairLength, CancellationToken cancellationToken = default)
    {
        var duration = await EstimateDurationAsync(serviceIds, hairLength, cancellationToken);
        if (duration <= 0) { duration = 1; }

        var resource = await _db.Resources.AsNoTracking().FirstOrDefaultAsync(r => r.Id == resourceId, cancellationToken);
        var buffer = resource?.BufferMinutes ?? 0;
        var mode = resource?.SchedulingMode ?? SchedulingMode.SlotGrid;
        var shifts = await _db.ShiftTemplates.AsNoTracking()
            .Where(s => s.ResourceId == resourceId && s.DayOfWeek == date.DayOfWeek)
            .OrderBy(s => s.StartTime).ToListAsync(cancellationToken);
        var appts = await _db.Appointments.AsNoTracking()
            .Where(a => a.ResourceId == resourceId && a.AppointmentDate == date && a.Status != AppointmentStatus.Cancelled && a.Status != AppointmentStatus.Rescheduled)
            .ToListAsync(cancellationToken);
        var blocks = appts.Select(a => (Start: a.StartTime, End: a.StartTime.AddMinutes(a.DurationMinutes + a.BufferMinutes))).ToList();

        var starts = new List<TimeOnly>();
        foreach (var sh in shifts)
        {
            // En modo Duration el paso es fino (hasta 15 min) para ofrecer "el proximo hueco"; en SlotGrid, la grilla.
            var step = mode == SchedulingMode.Duration
                ? Math.Clamp(sh.SlotMinutes <= 0 ? 15 : Math.Min(sh.SlotMinutes, 15), 5, 60)
                : (sh.SlotMinutes <= 0 ? 30 : sh.SlotMinutes);
            var endSpan = sh.EndTime.ToTimeSpan();
            for (var t = sh.StartTime; t.ToTimeSpan().Add(TimeSpan.FromMinutes(duration)) <= endSpan; t = t.AddMinutes(step))
            {
                var newEnd = t.AddMinutes(duration + buffer);
                var overlaps = blocks.Any(b => t < b.End && b.Start < newEnd);
                if (!overlaps) { starts.Add(t); }
            }
        }
        return starts.Distinct().OrderBy(t => t).ToList();
    }

    // Duracion total estimada de un conjunto de servicios: tier por largo si se conoce; si el servicio varia por
    // largo y no se conoce, el tier mas largo (conservador, para que el hueco ofrecido siempre alcance); si no
    // tiene tarifas por largo, la duracion base.
    private async Task<int> EstimateDurationAsync(IReadOnlyList<Guid> serviceIds, HairLength? hairLength, CancellationToken cancellationToken)
    {
        if (serviceIds is null || serviceIds.Count == 0) { return 0; }
        var baseDur = await _db.Services.AsNoTracking().Where(s => serviceIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.DurationMinutes, cancellationToken);
        var tiers = await _db.ServicePriceTiers.AsNoTracking().Where(t => serviceIds.Contains(t.ServiceId)).ToListAsync(cancellationToken);
        var tiersBySvc = tiers.GroupBy(t => t.ServiceId).ToDictionary(g => g.Key, g => g.ToList());

        var total = 0;
        foreach (var sid in serviceIds)
        {
            if (tiersBySvc.TryGetValue(sid, out var ts) && ts.Count > 0)
            {
                if (hairLength is HairLength hl && ts.FirstOrDefault(t => t.Length == hl) is { } tier) { total += tier.DurationMinutes; }
                else { total += ts.Max(t => t.DurationMinutes); }
            }
            else if (baseDur.TryGetValue(sid, out var d)) { total += d; }
        }
        return total;
    }

    public async Task<AppointmentDetailDto?> GetAppointmentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var a = await _db.Appointments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (a is null) { return null; }
        var resource = await _db.Resources.AsNoTracking().FirstOrDefaultAsync(r => r.Id == a.ResourceId, cancellationToken);
        var serviceIds = await _db.AppointmentServiceItems.AsNoTracking()
            .Where(i => i.AppointmentId == id).OrderBy(i => i.SortOrder).Select(i => i.ServiceId).ToListAsync(cancellationToken);
        var chat = await _db.AppointmentMessages.AsNoTracking()
            .Where(m => m.AppointmentId == id).OrderBy(m => m.SentAt)
            .Select(m => new AppointmentChatDto(m.Direction == MessageDirection.Outbound, m.Body, m.SentAt.ToString("HH:mm")))
            .ToListAsync(cancellationToken);
        Client? client = a.ClientId is Guid cid ? await _db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid, cancellationToken) : null;

        return new AppointmentDetailDto(a.Id, a.ResourceId, resource?.Name ?? "", a.AppointmentDate, a.StartTime,
            a.ClientId, client?.FullName, client?.Phone, a.Status, a.Punctuality, a.Notes,
            serviceIds, chat, a.ChainId, a.ChainSequence, a.ChainTotal, SalonFieldJson.Parse(a.FieldValuesJson));
    }

    public async Task<BookingResult> SaveBookingAsync(BookingRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return new BookingResult(false, null, "Sin salon activo."); }
        var name = (request.ClientName ?? string.Empty).Trim();
        if (name.Length == 0) { return new BookingResult(false, null, "Escribe el nombre del cliente."); }
        if (request.ServiceIds is null || request.ServiceIds.Count == 0) { return new BookingResult(false, null, "Anade al menos un servicio."); }

        var now = _clock.GetUtcNow();
        var client = await ResolveClientAsync(tenantId, request, cancellationToken);

        // Precio y duracion efectivos por servicio: tarifa por LARGO de cabello cuando el servicio la define
        // (gate: el largo es obligatorio para esos servicios), si no, precio/duracion base del recurso.
        var (lines, gateError) = await ResolveServiceLinesAsync(request.ResourceId, request.ServiceIds, request.HairLength, cancellationToken);
        if (gateError is not null) { return new BookingResult(false, null, gateError); }
        var prices = lines.ToDictionary(kv => kv.Key, kv => kv.Value.Price);
        var totalDuration = request.ServiceIds.Sum(sid => lines.TryGetValue(sid, out var ln) ? ln.DurationMinutes : 0);
        var totalValue = request.ServiceIds.Sum(sid => lines.TryGetValue(sid, out var ln) ? ln.Price : 0m);

        // Buffer (margen) por recurso: snapshot al reservar; entra en el intervalo del anti-solapamiento.
        var resourceIds = new List<Guid> { request.ResourceId };
        if (request.ChainSteps is { Count: > 0 }) { resourceIds.AddRange(request.ChainSteps.Select(s => s.ResourceId)); }
        var buffers = await _db.Resources.AsNoTracking().Where(r => resourceIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.BufferMinutes, cancellationToken);
        int BufferOf(Guid rid) => buffers.TryGetValue(rid, out var bm) ? bm : 0;

        if (request.AppointmentId is Guid editId)
        {
            var appt = await _db.Appointments.FirstOrDefaultAsync(x => x.Id == editId, cancellationToken);
            if (appt is null) { return new BookingResult(false, null, "La cita ya no existe."); }
            var oldStatus = appt.Status; var oldPunct = appt.Punctuality;

            appt.ClientId = client?.Id;
            appt.DurationMinutes = totalDuration;
            appt.BufferMinutes = BufferOf(appt.ResourceId);
            appt.EstimatedValue = totalValue;
            appt.Status = request.Status;
            appt.Punctuality = request.Punctuality;
            appt.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
            appt.FieldValuesJson = SalonFieldJson.Serialize(request.FieldValues);
            StampLifecycle(appt, now);

            await ReplaceServiceItemsAsync(appt.Id, tenantId, request.ServiceIds, prices, cancellationToken);
            await ReplaceChatAsync(appt.Id, tenantId, request.Chat, actorUserId, now, cancellationToken);
            if (client is not null) { ApplyClientDeltas(client, oldStatus, appt.Status, oldPunct, appt.Punctuality, now); }

            _audit.Write(actorUserId, "appointment.update", nameof(Appointment), appt.Id, null, new { appt.Status, appt.AppointmentDate, appt.StartTime }, tenantId);
            return await CommitAsync(appt.Id, cancellationToken);
        }

        // ----- Nueva cita (+ cadena multi-estacion) -----
        Guid? chainId = null; int chainTotal = 1 + (request.ChainSteps?.Count ?? 0);
        if (request.ChainSteps is { Count: > 0 }) { chainId = Guid.CreateVersion7(); }

        var main = new Appointment
        {
            TenantId = tenantId, ResourceId = request.ResourceId, AppointmentDate = request.Date, StartTime = request.StartTime,
            DurationMinutes = totalDuration, BufferMinutes = BufferOf(request.ResourceId),
            ClientId = client?.Id, Status = request.Status, Punctuality = request.Punctuality,
            Channel = request.Channel, EstimatedValue = totalValue,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            FieldValuesJson = SalonFieldJson.Serialize(request.FieldValues),
            ChainId = chainId, ChainSequence = chainId is null ? null : 1, ChainTotal = chainId is null ? null : chainTotal,
            RescheduledFromId = request.RescheduledFromId
        };
        StampLifecycle(main, now);
        _db.Appointments.Add(main);
        AddServiceItems(main.Id, tenantId, request.ServiceIds, prices);
        AddChat(main.Id, tenantId, request.Chat, actorUserId, now);
        if (client is not null) { ApplyClientDeltas(client, null, main.Status, Punctuality.Unknown, main.Punctuality, now); }

        // Reprogramacion: la cita original pasa a Rescheduled y sale de la bandeja de reprogramaciones.
        if (request.RescheduledFromId is Guid origId)
        {
            var orig = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == origId, cancellationToken);
            if (orig is not null) { orig.Status = AppointmentStatus.Rescheduled; orig.CancelledAt ??= now; }
        }

        if (chainId is { } cid)
        {
            var seq = 2;
            foreach (var step in request.ChainSteps!)
            {
                var stepAppt = new Appointment
                {
                    TenantId = tenantId, ResourceId = step.ResourceId, AppointmentDate = request.Date, StartTime = step.StartTime,
                    DurationMinutes = totalDuration, BufferMinutes = BufferOf(step.ResourceId),
                    ClientId = client?.Id, Status = AppointmentStatus.Scheduled,
                    Channel = BookingChannel.Reception, EstimatedValue = totalValue,
                    Notes = $"Cadena: paso {seq}", ChainId = cid, ChainSequence = seq, ChainTotal = chainTotal
                };
                StampLifecycle(stepAppt, now);
                _db.Appointments.Add(stepAppt);
                AddServiceItems(stepAppt.Id, tenantId, request.ServiceIds, prices);
                seq++;
            }
        }

        _audit.Write(actorUserId, "appointment.create", nameof(Appointment), main.Id, null, new { main.AppointmentDate, main.StartTime, chained = chainId != null }, tenantId);
        return await CommitAsync(main.Id, cancellationToken);
    }

    public async Task<bool> CancelAppointmentAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var appt = await _db.Appointments.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (appt is null) { return false; }
        appt.Status = AppointmentStatus.Cancelled;
        appt.CancelledAt = _clock.GetUtcNow();
        _audit.Write(actorUserId, "appointment.cancel", nameof(Appointment), appt.Id, null, null, appt.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // ===== Reprogramaciones (bandeja de citas canceladas que el cliente pidio reprogramar) =====

    public async Task<int> CountRescheduleQueueAsync(CancellationToken cancellationToken = default)
        => await _db.Appointments.AsNoTracking().CountAsync(a => a.Status == AppointmentStatus.Cancelled, cancellationToken);

    // Variante para usar desde un scope/DbContext aislado (ej. badge del NavMenu): no depende del
    // ITenantContext del circuito; filtra por el tenantId explicito ignorando el query filter global.
    public async Task<int> CountReschedulePendingAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => await _db.Appointments.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(a => a.TenantId == tenantId && a.Status == AppointmentStatus.Cancelled, cancellationToken);

    public async Task<IReadOnlyList<RescheduleItemDto>> GetRescheduleQueueAsync(CancellationToken cancellationToken = default)
    {
        var appts = await _db.Appointments.AsNoTracking()
            .Where(a => a.Status == AppointmentStatus.Cancelled)
            .OrderByDescending(a => a.CancelledAt).ThenByDescending(a => a.AppointmentDate)
            .ToListAsync(cancellationToken);
        if (appts.Count == 0) { return Array.Empty<RescheduleItemDto>(); }

        var apptIds = appts.Select(a => a.Id).ToList();
        var items = await _db.AppointmentServiceItems.AsNoTracking()
            .Where(i => apptIds.Contains(i.AppointmentId)).OrderBy(i => i.SortOrder).ToListAsync(cancellationToken);
        var serviceIdsByAppt = items.GroupBy(i => i.AppointmentId).ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(i => i.ServiceId).ToList());
        var servicesText = await BuildServicesTextAsync(apptIds, cancellationToken);
        var clientNames = await ClientNamesAsync(appts, cancellationToken);
        var clientPhones = await ClientPhonesAsync(appts, cancellationToken);
        var resourceNames = await _db.Resources.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);

        return appts.Select(a => new RescheduleItemDto(
            a.Id, a.ResourceId, resourceNames.TryGetValue(a.ResourceId, out var rn) ? rn : "?",
            a.AppointmentDate, a.StartTime,
            a.ClientId, a.ClientId is Guid cid && clientNames.TryGetValue(cid, out var n) ? n : null,
            a.ClientId is Guid cid2 && clientPhones.TryGetValue(cid2, out var ph) ? ph : null,
            servicesText.TryGetValue(a.Id, out var st) ? st : "",
            serviceIdsByAppt.TryGetValue(a.Id, out var sids) ? sids : Array.Empty<Guid>(),
            a.CancelledAt)).ToList();
    }

    public async Task<bool> DismissRescheduleAsync(Guid appointmentId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var appt = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId && a.Status == AppointmentStatus.Cancelled, cancellationToken);
        if (appt is null) { return false; }
        appt.Status = AppointmentStatus.Rescheduled; // sale de la bandeja sin reprogramar
        _audit.Write(actorUserId, "appointment.reschedule-dismiss", nameof(Appointment), appt.Id, null, null, appt.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> MarkPunctualityAsync(Guid appointmentId, Punctuality punctuality, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var appt = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);
        if (appt is null) { return false; }
        var old = appt.Punctuality;
        if (old == punctuality) { return true; }
        appt.Punctuality = punctuality;
        if (appt.ClientId is Guid cid)
        {
            var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == cid, cancellationToken);
            if (client is not null) { ApplyClientDeltas(client, appt.Status, appt.Status, old, punctuality, _clock.GetUtcNow()); }
        }
        _audit.Write(actorUserId, "appointment.punctuality", nameof(Appointment), appt.Id, null, new { punctuality }, appt.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // ===== Helpers =====

    private async Task<BookingResult> CommitAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return new BookingResult(true, appointmentId, null);
        }
        catch (DbUpdateException ex) when (IsSlotConflict(ex))
        {
            // Otro actor (recepcion / cliente / IA) ocupo (o solapo) el cupo entre la lectura y el guardado.
            return new BookingResult(false, null, "Ese horario acaba de ocuparse o se cruza con otra cita, elige otro.");
        }
    }

    private static bool IsSlotConflict(DbUpdateException ex)
    {
        // Evita acoplar la capa Application a Npgsql: lee SqlState por reflexion.
        // 23505 = unique_violation; 23P01 = exclusion_violation (el constraint de no-solapamiento).
        var sqlState = ex.InnerException?.GetType().GetProperty("SqlState")?.GetValue(ex.InnerException) as string;
        return sqlState is "23505" or "23P01";
    }

    private async Task<Client?> ResolveClientAsync(Guid tenantId, BookingRequest request, CancellationToken cancellationToken)
    {
        var name = request.ClientName.Trim();
        var phone = string.IsNullOrWhiteSpace(request.ClientPhone) ? null : request.ClientPhone.Trim();

        if (request.ClientId is Guid cid)
        {
            var existing = await _db.Clients.FirstOrDefaultAsync(c => c.Id == cid, cancellationToken);
            if (existing is not null) { return existing; }
        }
        if (phone is not null)
        {
            var byPhone = await _db.Clients.FirstOrDefaultAsync(c => c.Phone == phone, cancellationToken);
            if (byPhone is not null) { return byPhone; }
        }
        var byName = await _db.Clients.FirstOrDefaultAsync(c => c.FullName == name, cancellationToken);
        if (byName is not null) { return byName; }

        var created = new Client { TenantId = tenantId, FullName = name, Phone = phone ?? string.Empty };
        _db.Clients.Add(created);
        return created;
    }

    private sealed record ServiceLine(int DurationMinutes, decimal Price);

    /// <summary>
    /// Resuelve precio y duracion por servicio. Si el servicio define tarifas por largo (ServicePriceTier),
    /// el largo es OBLIGATORIO (decision ADR-0009): sin largo devuelve un mensaje de gate y no se reserva;
    /// con largo usa el tier correspondiente (o el base si ese largo no tiene tier). Sin tarifas por largo,
    /// usa el precio base (con override del asesor de imagen) y la duracion base.
    /// </summary>
    private async Task<(Dictionary<Guid, ServiceLine> Lines, string? GateError)> ResolveServiceLinesAsync(
        Guid resourceId, IReadOnlyList<Guid> serviceIds, HairLength? hairLength, CancellationToken cancellationToken)
    {
        var resource = await _db.Resources.AsNoTracking().FirstOrDefaultAsync(r => r.Id == resourceId, cancellationToken);
        var links = await _db.ResourceServiceLinks.AsNoTracking().Where(l => l.ResourceId == resourceId).ToListAsync(cancellationToken);
        var linkBySvc = links.ToDictionary(l => l.ServiceId);
        var services = await _db.Services.AsNoTracking().Where(s => serviceIds.Contains(s.Id)).ToListAsync(cancellationToken);
        var tiers = await _db.ServicePriceTiers.AsNoTracking().Where(t => serviceIds.Contains(t.ServiceId)).ToListAsync(cancellationToken);
        var tiersBySvc = tiers.GroupBy(t => t.ServiceId).ToDictionary(g => g.Key, g => g.ToList());

        var lines = new Dictionary<Guid, ServiceLine>();
        foreach (var s in services)
        {
            if (tiersBySvc.TryGetValue(s.Id, out var svcTiers) && svcTiers.Count > 0)
            {
                if (hairLength is not HairLength hl)
                {
                    return (lines, $"El servicio \"{s.Name}\" cambia de precio y duracion segun el largo del cabello. Clasifica el largo de la clienta (foto) antes de reservar.");
                }
                var tier = svcTiers.FirstOrDefault(t => t.Length == hl);
                if (tier is not null)
                {
                    lines[s.Id] = new ServiceLine(tier.DurationMinutes, tier.Price);
                    continue;
                }
                // Ese largo no tiene tier definido: cae al precio/duracion base.
            }
            var price = resource?.Kind == ResourceKind.Image && linkBySvc.TryGetValue(s.Id, out var l) && l.PriceOverride is decimal o ? o : s.Price;
            lines[s.Id] = new ServiceLine(s.DurationMinutes, price);
        }
        return (lines, null);
    }

    private void AddServiceItems(Guid appointmentId, Guid tenantId, IReadOnlyList<Guid> serviceIds, IReadOnlyDictionary<Guid, decimal> prices)
    {
        var order = 0;
        foreach (var sid in serviceIds)
        {
            _db.AppointmentServiceItems.Add(new AppointmentServiceItem
            {
                TenantId = tenantId, AppointmentId = appointmentId, ServiceId = sid,
                SortOrder = order++, PriceSnapshot = prices.TryGetValue(sid, out var p) ? p : 0m
            });
        }
    }

    private async Task ReplaceServiceItemsAsync(Guid appointmentId, Guid tenantId, IReadOnlyList<Guid> serviceIds, IReadOnlyDictionary<Guid, decimal> prices, CancellationToken cancellationToken)
    {
        var existing = await _db.AppointmentServiceItems.Where(i => i.AppointmentId == appointmentId).ToListAsync(cancellationToken);
        _db.AppointmentServiceItems.RemoveRange(existing);
        AddServiceItems(appointmentId, tenantId, serviceIds, prices);
    }

    private void AddChat(Guid appointmentId, Guid tenantId, IReadOnlyList<BookingChatLine>? chat, Guid actorUserId, DateTimeOffset now)
    {
        if (chat is null) { return; }
        var i = 0;
        foreach (var line in chat)
        {
            _db.AppointmentMessages.Add(new AppointmentMessage
            {
                TenantId = tenantId, AppointmentId = appointmentId,
                Direction = line.Outbound ? MessageDirection.Outbound : MessageDirection.Inbound,
                Body = line.Body, SentAt = now.AddSeconds(i++),
                SentByTenantUserId = line.Outbound ? actorUserId : null
            });
        }
    }

    private async Task ReplaceChatAsync(Guid appointmentId, Guid tenantId, IReadOnlyList<BookingChatLine>? chat, Guid actorUserId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await _db.AppointmentMessages.Where(m => m.AppointmentId == appointmentId).ToListAsync(cancellationToken);
        _db.AppointmentMessages.RemoveRange(existing);
        AddChat(appointmentId, tenantId, chat, actorUserId, now);
    }

    private static void StampLifecycle(Appointment a, DateTimeOffset now)
    {
        if (a.Status == AppointmentStatus.Confirmed && a.ConfirmedAt is null) { a.ConfirmedAt = now; }
        if (a.Status == AppointmentStatus.Completed && a.CompletedAt is null) { a.CompletedAt = now; }
        if (a.Status == AppointmentStatus.Cancelled && a.CancelledAt is null) { a.CancelledAt = now; }
    }

    private static void ApplyClientDeltas(Client client, AppointmentStatus? oldStatus, AppointmentStatus newStatus, Punctuality oldPunct, Punctuality newPunct, DateTimeOffset now)
    {
        if (newStatus == AppointmentStatus.Completed && oldStatus != AppointmentStatus.Completed) { client.VisitCount++; client.LastVisitAt = now; }
        if (newStatus == AppointmentStatus.NoShow && oldStatus != AppointmentStatus.NoShow) { client.NoShowCount++; }
        if (oldPunct != newPunct)
        {
            if (oldPunct is Punctuality.Early or Punctuality.OnTime) { client.OnTimeCount = Math.Max(0, client.OnTimeCount - 1); }
            if (oldPunct == Punctuality.Late) { client.LateCount = Math.Max(0, client.LateCount - 1); }
            if (newPunct is Punctuality.Early or Punctuality.OnTime) { client.OnTimeCount++; }
            if (newPunct == Punctuality.Late) { client.LateCount++; }
        }
    }

    private async Task<Dictionary<Guid, string>> BuildServicesTextAsync(List<Guid> appointmentIds, CancellationToken cancellationToken)
    {
        if (appointmentIds.Count == 0) { return new(); }
        var items = await _db.AppointmentServiceItems.AsNoTracking()
            .Where(i => appointmentIds.Contains(i.AppointmentId)).OrderBy(i => i.SortOrder).ToListAsync(cancellationToken);
        var names = await _db.Services.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
        return items.GroupBy(i => i.AppointmentId)
            .ToDictionary(g => g.Key, g => string.Join(" + ", g.Select(i => names.TryGetValue(i.ServiceId, out var n) ? n : "")));
    }

    private async Task<Dictionary<Guid, string>> ClientNamesAsync(List<Appointment> appts, CancellationToken cancellationToken)
    {
        var ids = appts.Where(a => a.ClientId.HasValue).Select(a => a.ClientId!.Value).Distinct().ToList();
        if (ids.Count == 0) { return new(); }
        return await _db.Clients.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.FullName, cancellationToken);
    }

    private async Task<Dictionary<Guid, string>> ClientPhonesAsync(List<Appointment> appts, CancellationToken cancellationToken)
    {
        var ids = appts.Where(a => a.ClientId.HasValue).Select(a => a.ClientId!.Value).Distinct().ToList();
        if (ids.Count == 0) { return new(); }
        return await _db.Clients.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Phone, cancellationToken);
    }
}
