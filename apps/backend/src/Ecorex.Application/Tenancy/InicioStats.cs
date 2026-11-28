using System.Globalization;
using Ecorex.Application.Common;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>Conteo de tareas de un tablero para las graficas del inicio.</summary>
public sealed record InicioBoardCountDto(string Board, int Count);

/// <summary>Serie temporal (meses o dias) con tareas nuevas y cerradas por periodo.</summary>
public sealed record InicioSerieDto(IReadOnlyList<string> Labels, IReadOnlyList<int> Nuevas, IReadOnlyList<int> Cerradas);

/// <summary>Indicadores de un alcance (mis tareas o todo el equipo).</summary>
public sealed record InicioScopeStatsDto(
    int Activas,
    int Vencidas,
    int CerradasMes,
    int CerradasMesAnterior,
    int Cerradas7Dias,
    IReadOnlyList<InicioBoardCountDto> ActivasPorTablero,
    IReadOnlyList<InicioBoardCountDto> CerradasPorTablero,
    InicioSerieDto Mensual,
    InicioSerieDto UltimosDias);

/// <summary>Indicadores del inicio: equipo (todo el tenant) y, si hay usuario, lo asignado a el.</summary>
public sealed record InicioStatsDto(InicioScopeStatsDto Equipo, InicioScopeStatsDto? Mio);

/// <summary>Fila liviana de tarea para el calculo (sin entidades ni navegaciones).</summary>
public sealed record InicioTaskRow(
    Guid? BoardId,
    Guid? AssigneeId,
    TaskItemStatus Status,
    bool IsArchived,
    DateTimeOffset? DueDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset? ColumnEnteredAt,
    bool ColumnIsDone);

/// <summary>Tablero referenciado por las filas: nombre y si quien consulta lo puede ver.</summary>
public sealed record InicioBoardInfo(string Name, bool Visible);

/// <summary>Indicadores personales y del equipo para el dashboard /inicio.</summary>
public interface IInicioStatsService
{
    /// <param name="myTenantUserId">TenantUser de quien consulta; null = solo equipo.</param>
    /// <param name="canSeeRestricted">Owner/Admin: ve el nombre de tableros restringidos a otros.</param>
    Task<InicioStatsDto> GetAsync(Guid? myTenantUserId, bool canSeeRestricted, TimeZoneInfo timeZone,
        DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}

/// <summary>
/// Lee las tareas relevantes (activas + creadas/cerradas en la ventana de 6 meses) con una sola proyeccion
/// liviana; el filtro global de EF acota al tenant. El calculo vive en <see cref="InicioStatsCalculator"/>.
/// </summary>
public sealed class InicioStatsService : IInicioStatsService
{
    private readonly IApplicationDbContext _db;

    public InicioStatsService(IApplicationDbContext db) { _db = db; }

    public async Task<InicioStatsDto> GetAsync(Guid? myTenantUserId, bool canSeeRestricted, TimeZoneInfo timeZone,
        DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        // Npgsql exige offset 0 en "timestamp with time zone".
        var from = InicioStatsCalculator.WindowStartUtc(timeZone, nowUtc.ToUniversalTime());

        var rows = await _db.TaskItems.AsNoTracking()
            .Where(t => (!t.IsArchived && (t.Status == TaskItemStatus.Pending || t.Status == TaskItemStatus.Active || t.Status == TaskItemStatus.InProgress))
                || t.CreatedAt >= from || t.ClosedAt >= from || t.ColumnEnteredAt >= from)
            .Select(t => new InicioTaskRow(
                t.BoardId, t.AssigneeTenantUserId, t.Status, t.IsArchived, t.DueDate, t.CreatedAt, t.ClosedAt,
                t.ColumnEnteredAt, t.Column != null && t.Column.IsDone))
            .ToListAsync(cancellationToken);

        var boardIds = rows.Where(r => r.BoardId.HasValue).Select(r => r.BoardId!.Value).Distinct().ToList();
        var boardRows = boardIds.Count == 0
            ? []
            : await _db.TaskBoards.AsNoTracking()
                .Where(b => boardIds.Contains(b.Id))
                .Select(b => new { b.Id, b.Name, b.AllowedUserIdsJson })
                .ToListAsync(cancellationToken);
        var boards = boardRows.ToDictionary(
            b => b.Id,
            b => new InicioBoardInfo(b.Name, canSeeRestricted || IsVisibleTo(b.AllowedUserIdsJson, myTenantUserId)));

        return InicioStatsCalculator.Compute(rows, boards, myTenantUserId, timeZone, nowUtc);
    }

    // Misma regla que los tableros restringidos (ADR de tableros por usuario): lista vacia = todos lo ven.
    private static bool IsVisibleTo(string? allowedUserIdsJson, Guid? me)
    {
        if (string.IsNullOrWhiteSpace(allowedUserIdsJson)) { return true; }
        try
        {
            var ids = System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(allowedUserIdsJson) ?? [];
            return ids.Count == 0 || (me is Guid m && ids.Contains(m));
        }
        catch (System.Text.Json.JsonException)
        {
            return true;
        }
    }
}

/// <summary>
/// Calculo puro de los indicadores del inicio (testeable sin BD). Reglas:
/// - Cerrada = esta en una columna de cierre (IsDone) o su estado es Done/Closed. Fecha de cierre =
///   ClosedAt, o la entrada a la columna actual (las cerradas se quedan en la columna de cierre).
/// - Activa = no cerrada, no archivada y en Pending/Active/InProgress. Vencida = activa con DueDate pasada.
/// - "Mio" = asignada a quien consulta. Meses y dias en la zona horaria del tenant.
/// - En el alcance equipo, un tablero restringido que quien consulta no ve sale como "Otros tableros".
/// </summary>
public static class InicioStatsCalculator
{
    public const int Months = 6;
    public const int Days = 7;
    public const int TopBoards = 8;
    public const string SinTablero = "Sin tablero";
    public const string OtrosTableros = "Otros tableros";

    private static readonly CultureInfo Es = new("es-CO");

    /// <summary>Inicio (UTC) de la ventana: dia 1 del mes de hace 5 meses, a medianoche local.</summary>
    public static DateTimeOffset WindowStartUtc(TimeZoneInfo tz, DateTimeOffset nowUtc)
    {
        var local = TimeZoneInfo.ConvertTime(nowUtc, tz).DateTime;
        var first = new DateTime(local.Year, local.Month, 1).AddMonths(-(Months - 1));
        return LocalMidnightToUtc(first, tz);
    }

    public static InicioStatsDto Compute(IReadOnlyList<InicioTaskRow> rows, IReadOnlyDictionary<Guid, InicioBoardInfo> boards,
        Guid? me, TimeZoneInfo tz, DateTimeOffset nowUtc)
    {
        var equipo = ComputeScope(rows, boards, tz, nowUtc, personal: false);
        InicioScopeStatsDto? mio = null;
        if (me is Guid m)
        {
            mio = ComputeScope(rows.Where(r => r.AssigneeId == m).ToList(), boards, tz, nowUtc, personal: true);
        }
        return new InicioStatsDto(equipo, mio);
    }

    public static bool IsClosed(InicioTaskRow r) => r.ColumnIsDone || r.Status is TaskItemStatus.Done or TaskItemStatus.Closed;

    public static DateTimeOffset? CloseDate(InicioTaskRow r) => IsClosed(r) ? r.ClosedAt ?? r.ColumnEnteredAt : null;

    public static bool IsActive(InicioTaskRow r) => !IsClosed(r) && !r.IsArchived
        && r.Status is TaskItemStatus.Pending or TaskItemStatus.Active or TaskItemStatus.InProgress;

    private static InicioScopeStatsDto ComputeScope(IReadOnlyList<InicioTaskRow> rows, IReadOnlyDictionary<Guid, InicioBoardInfo> boards,
        TimeZoneInfo tz, DateTimeOffset nowUtc, bool personal)
    {
        var today = TimeZoneInfo.ConvertTime(nowUtc, tz).Date;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var windowStart = monthStart.AddMonths(-(Months - 1));
        var dayStart = today.AddDays(-(Days - 1));

        DateTime Local(DateTimeOffset d) => TimeZoneInfo.ConvertTime(d, tz).DateTime;

        var active = rows.Where(IsActive).ToList();
        var vencidas = active.Count(r => r.DueDate is { } due && due < nowUtc);

        var closed = rows
            .Select(r => (Row: r, When: CloseDate(r)))
            .Where(x => x.When.HasValue)
            .Select(x => (x.Row, Day: Local(x.When!.Value).Date))
            .Where(x => x.Day >= windowStart && x.Day <= today)
            .ToList();
        var created = rows
            .Select(r => Local(r.CreatedAt).Date)
            .Where(d => d >= windowStart && d <= today)
            .ToList();

        var cerradasMes = closed.Count(x => x.Day >= monthStart);
        var cerradasMesAnterior = closed.Count(x => x.Day >= monthStart.AddMonths(-1) && x.Day < monthStart);
        var cerradas7 = closed.Count(x => x.Day >= dayStart);

        // Serie mensual: 6 meses terminando en el actual.
        var mLabels = new List<string>();
        var mNuevas = new List<int>();
        var mCerradas = new List<int>();
        for (var i = 0; i < Months; i++)
        {
            var start = windowStart.AddMonths(i);
            var end = start.AddMonths(1);
            mLabels.Add(MonthLabel(start));
            mNuevas.Add(created.Count(d => d >= start && d < end));
            mCerradas.Add(closed.Count(x => x.Day >= start && x.Day < end));
        }

        // Serie diaria: 7 dias terminando hoy.
        var dLabels = new List<string>();
        var dNuevas = new List<int>();
        var dCerradas = new List<int>();
        for (var i = 0; i < Days; i++)
        {
            var day = dayStart.AddDays(i);
            dLabels.Add(DayLabel(day));
            dNuevas.Add(created.Count(d => d == day));
            dCerradas.Add(closed.Count(x => x.Day == day));
        }

        return new InicioScopeStatsDto(
            active.Count, vencidas, cerradasMes, cerradasMesAnterior, cerradas7,
            ByBoard(active.Select(r => r.BoardId), boards, personal),
            ByBoard(closed.Select(x => x.Row.BoardId), boards, personal),
            new InicioSerieDto(mLabels, mNuevas, mCerradas),
            new InicioSerieDto(dLabels, dNuevas, dCerradas));
    }

    private static IReadOnlyList<InicioBoardCountDto> ByBoard(IEnumerable<Guid?> boardIds, IReadOnlyDictionary<Guid, InicioBoardInfo> boards, bool personal)
    {
        var counts = boardIds
            .Select(id => BoardLabel(id, boards, personal))
            .GroupBy(n => n)
            .Select(g => new InicioBoardCountDto(g.Key, g.Count()))
            .OrderByDescending(x => x.Count).ThenBy(x => x.Board, StringComparer.Create(Es, true))
            .ToList();
        if (counts.Count <= TopBoards) { return counts; }

        // Top N y el resto agrupado (sumando a un "Otros tableros" que ya existiera).
        var top = counts.Where(c => c.Board != OtrosTableros).Take(TopBoards - 1).ToList();
        var rest = counts.Except(top).Sum(c => c.Count);
        top.Add(new InicioBoardCountDto(OtrosTableros, rest));
        return top;
    }

    private static string BoardLabel(Guid? id, IReadOnlyDictionary<Guid, InicioBoardInfo> boards, bool personal)
    {
        if (id is not Guid bid) { return SinTablero; }
        if (!boards.TryGetValue(bid, out var b)) { return OtrosTableros; }
        // Lo propio siempre se nombra (la tarea es de quien consulta); en equipo se respeta la restriccion.
        return personal || b.Visible ? b.Name : OtrosTableros;
    }

    private static string MonthLabel(DateTime d)
    {
        var s = d.ToString("MMM", Es).TrimEnd('.');
        s = char.ToUpper(s[0], Es) + s[1..];
        return d.Month == 1 ? $"{s} {d:yy}" : s;
    }

    private static string DayLabel(DateTime d)
    {
        var s = d.ToString("ddd", Es).TrimEnd('.');
        return $"{char.ToUpper(s[0], Es)}{s[1..]} {d.Day}";
    }

    private static DateTimeOffset LocalMidnightToUtc(DateTime localMidnight, TimeZoneInfo tz)
    {
        var unspecified = DateTime.SpecifyKind(localMidnight, DateTimeKind.Unspecified);
        var offset = tz.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset).ToUniversalTime();
    }
}
