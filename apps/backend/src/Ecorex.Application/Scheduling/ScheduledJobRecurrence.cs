using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Scheduling;

/// <summary>
/// Motor de recurrencia del modulo 000889 (ola P2, doc D4): calcula la PROXIMA ejecucion de una regla.
/// Es PURO (sin BD, sin reloj propio) para ser unit-testeable y determinista.
///
/// Todo el calculo se hace en la ZONA HORARIA DEL TENANT (regla 9 del proyecto: hora del tenant + UTC) y
/// se devuelve en UTC: "todos los lunes a las 08:00" significa 08:00 EN EL TENANT, aunque cambie el offset.
/// El origen legacy (TaskProgrammer) ya importaba NodaTime con esa intencion pero nunca la cerro.
/// </summary>
public static class ScheduledJobRecurrence
{
    /// <summary>Zona por defecto cuando el tenant no la define (Colombia, sin DST).</summary>
    public const string DefaultTimeZoneId = "America/Bogota";

    /// <summary>Hora de disparo si la regla no la define.</summary>
    private static readonly TimeOnly DefaultAtTime = new(8, 0);

    /// <summary>
    /// Tope de busqueda: cubre de sobra la peor recurrencia razonable (mensual cada 12 meses).
    /// Evita bucles infinitos ante reglas que nunca casan (p.ej. semanal sin dias marcados).
    /// </summary>
    private const int HorizonDays = 800;

    /// <summary>
    /// Proxima ejecucion en UTC ESTRICTAMENTE posterior a <paramref name="afterUtc"/>, o null si la regla
    /// ya no volvera a dispararse (vigencia vencida, "Una vez" ya pasada, o regla que no casa nunca).
    /// </summary>
    public static DateTimeOffset? ComputeNextRun(ScheduledJobRule rule, DateTimeOffset afterUtc, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(timeZone);

        var afterLocal = TimeZoneInfo.ConvertTime(afterUtc, timeZone);
        var startDate = DateOnly.FromDateTime(afterLocal.DateTime);

        // Ancla de los intervalos ("cada N dias/semanas/meses" se cuenta desde aqui): la vigencia si
        // existe, si no la fecha de creacion de la regla (en hora del tenant).
        var anchor = rule.ValidFrom ?? DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(rule.CreatedAt == default ? afterUtc : rule.CreatedAt, timeZone).DateTime);

        // Nunca antes del inicio de vigencia.
        if (rule.ValidFrom is DateOnly vf && startDate < vf)
        {
            startDate = vf;
        }

        var interval = rule.IntervalNum < 1 ? 1 : rule.IntervalNum;
        var fireTimes = FireTimes(rule);
        if (fireTimes.Count == 0)
        {
            return null;
        }

        for (var i = 0; i <= HorizonDays; i++)
        {
            var date = startDate.AddDays(i);
            if (rule.ValidTo is DateOnly vt && date > vt)
            {
                return null;
            }
            if (!DayMatches(rule, date, anchor, interval))
            {
                // "Una vez" solo puede caer en su unico dia: si no casa, ya no hay mas.
                if (rule.Frequency == ScheduledJobFrequency.Once && date > (rule.ValidFrom ?? anchor))
                {
                    return null;
                }
                continue;
            }

            foreach (var time in fireTimes)
            {
                var utc = ToUtc(date.ToDateTime(time), timeZone);
                if (utc > afterUtc)
                {
                    return utc;
                }
            }

            // El dia casaba pero todas sus horas ya pasaron: "Una vez" se agota aqui.
            if (rule.Frequency == ScheduledJobFrequency.Once)
            {
                return null;
            }
        }
        return null;
    }

    // ---- Dias que casan ----

    private static bool DayMatches(ScheduledJobRule rule, DateOnly date, DateOnly anchor, int interval) => rule.Frequency switch
    {
        // Una vez = "fecha especifica": el dia es la fecha de inicio de vigencia.
        ScheduledJobFrequency.Once => rule.ValidFrom is DateOnly once && date == once,

        // Cada N dias desde el ancla.
        ScheduledJobFrequency.Daily => date >= anchor
            && (date.DayNumber - anchor.DayNumber) % interval == 0,

        // Dias marcados (Lun,Mie...) y cada N semanas desde la semana del ancla.
        ScheduledJobFrequency.Weekly => date >= anchor
            && ParseWeekdays(rule.Weekdays).Contains(date.DayOfWeek)
            && ((StartOfWeek(date).DayNumber - StartOfWeek(anchor).DayNumber) / 7) % interval == 0,

        // Cada N meses desde el mes del ancla, en el dia ordinal configurado.
        ScheduledJobFrequency.Monthly => date >= anchor
            && MonthsBetween(anchor, date) % interval == 0
            && MonthDayMatches(rule, date),

        _ => false,
    };

    /// <summary>Mensual: "el [Primer..Ultimo] [Lunes..Domingo | dia]" o, alternativamente, un dia del mes.</summary>
    private static bool MonthDayMatches(ScheduledJobRule rule, DateOnly date)
    {
        if (rule.DayOfMonth is int dom and > 0)
        {
            // Dia N del mes; si el mes es mas corto, cae en el ultimo dia.
            var days = DateTime.DaysInMonth(date.Year, date.Month);
            return date.Day == Math.Min(dom, days);
        }

        var ordinal = (rule.MonthOrdinal ?? "").Trim();
        var weekday = (rule.MonthWeekday ?? "").Trim();
        if (ordinal.Length == 0 || weekday.Length == 0)
        {
            return false;
        }
        var isLast = ordinal.Equals("Ultimo", StringComparison.OrdinalIgnoreCase);
        var nth = OrdinalToNumber(ordinal);

        // "dia" = dia natural del mes (el primer/segundo/.../ultimo DIA), no un dia de semana.
        if (weekday.Equals("dia", StringComparison.OrdinalIgnoreCase))
        {
            var days = DateTime.DaysInMonth(date.Year, date.Month);
            return isLast ? date.Day == days : nth > 0 && date.Day == nth;
        }

        if (ParseFullWeekday(weekday) is not DayOfWeek dow || date.DayOfWeek != dow)
        {
            return false;
        }
        if (isLast)
        {
            // Ultimo <dia de semana> del mes: no hay otro igual 7 dias despues.
            return date.AddDays(7).Month != date.Month;
        }
        // N-esima ocurrencia de ese dia de semana: dias 1-7 => 1a, 8-14 => 2a, ...
        return nth > 0 && (date.Day - 1) / 7 + 1 == nth;
    }

    // ---- Horas de disparo del dia ----

    /// <summary>
    /// Horas a las que dispara la regla dentro de un dia que casa: una sola (AtTime) o la ventana
    /// intradia "cada N horas de HH:mm a HH:mm".
    /// </summary>
    private static List<TimeOnly> FireTimes(ScheduledJobRule rule)
    {
        var times = new List<TimeOnly>();
        if (rule.RepeatIntraday
            && rule.RepeatEveryHours is int every and > 0
            && ParseTime(rule.RepeatFrom) is TimeOnly from
            && ParseTime(rule.RepeatTo) is TimeOnly to
            && to >= from)
        {
            var t = from;
            while (t <= to)
            {
                times.Add(t);
                // Corta al pasar la medianoche (la ventana intradia no cruza de dia).
                var next = t.AddHours(every, out var wrapped);
                if (wrapped != 0) { break; }
                t = next;
            }
            return times;
        }

        times.Add(ParseTime(rule.AtTime) ?? DefaultAtTime);
        return times;
    }

    // ---- Helpers ----

    /// <summary>Convierte hora LOCAL del tenant a UTC, sorteando huecos/ambiguedades de DST.</summary>
    private static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo timeZone)
    {
        var dt = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        // Hueco de DST (esa hora no existe): se corre hacia adelante hasta la primera hora valida.
        var guard = 0;
        while (timeZone.IsInvalidTime(dt) && guard++ < 180)
        {
            dt = dt.AddMinutes(1);
        }
        // Hora ambigua (se repite al atrasar el reloj): GetUtcOffset devuelve el offset estandar.
        var offset = timeZone.GetUtcOffset(dt);
        return new DateTimeOffset(dt, offset).ToUniversalTime();
    }

    private static TimeOnly? ParseTime(string? value)
        => TimeOnly.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : null;

    private static DateOnly StartOfWeek(DateOnly date)
    {
        // Semana que arranca en lunes (convencion del prototipo: Lun..Dom).
        var delta = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-delta);
    }

    private static int MonthsBetween(DateOnly from, DateOnly to)
        => (to.Year - from.Year) * 12 + (to.Month - from.Month);

    private static int OrdinalToNumber(string ordinal) => ordinal.ToLowerInvariant() switch
    {
        "primer" => 1,
        "segundo" => 2,
        "tercer" => 3,
        "cuarto" => 4,
        _ => 0, // "Ultimo" se trata aparte
    };

    /// <summary>Chips del prototipo: "Lun,Mar,Mie,Jue,Vie,Sab,Dom".</summary>
    private static HashSet<DayOfWeek> ParseWeekdays(string? csv)
    {
        var set = new HashSet<DayOfWeek>();
        if (string.IsNullOrWhiteSpace(csv)) { return set; }
        foreach (var raw in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var d = raw.ToLowerInvariant() switch
            {
                "lun" => DayOfWeek.Monday,
                "mar" => DayOfWeek.Tuesday,
                "mie" => DayOfWeek.Wednesday,
                "jue" => DayOfWeek.Thursday,
                "vie" => DayOfWeek.Friday,
                "sab" => DayOfWeek.Saturday,
                "dom" => DayOfWeek.Sunday,
                _ => (DayOfWeek?)null,
            };
            if (d is DayOfWeek dow) { set.Add(dow); }
        }
        return set;
    }

    /// <summary>Select del prototipo: "Lunes".."Domingo".</summary>
    private static DayOfWeek? ParseFullWeekday(string name) => name.ToLowerInvariant() switch
    {
        "lunes" => DayOfWeek.Monday,
        "martes" => DayOfWeek.Tuesday,
        "miercoles" => DayOfWeek.Wednesday,
        "jueves" => DayOfWeek.Thursday,
        "viernes" => DayOfWeek.Friday,
        "sabado" => DayOfWeek.Saturday,
        "domingo" => DayOfWeek.Sunday,
        _ => null,
    };

    /// <summary>Resuelve la zona del tenant (IANA); cae al default si es nula o desconocida en el host.</summary>
    public static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        foreach (var id in new[] { timeZoneId, DefaultTimeZoneId })
        {
            if (string.IsNullOrWhiteSpace(id)) { continue; }
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }
}
