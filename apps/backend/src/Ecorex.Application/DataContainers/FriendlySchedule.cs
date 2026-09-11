using System.Globalization;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.DataContainers;

/// <summary>
/// Programador de horario AMIGABLE para los procesos de importacion. Traduce, en AMBOS sentidos, entre
/// una eleccion humana (Manual / Diario / Semanal / Mensual / Cada N) y el almacenamiento real del motor
/// (<see cref="ImportScheduleKind"/> + IntervalMinutes/CronExpression). NO cambia el contrato ni el worker:
/// Diario/Semanal/Mensual GENERAN una expresion cron de 5 campos (guardada como Cron) y "Cada N" usa
/// Interval. La zona horaria la resuelve Cronos con la del tenant (ADR-0041); aqui solo se arma el texto.
///
/// Formato cron: "minuto hora dia-del-mes mes dia-de-semana" (5 campos). Dia-de-semana: 0=domingo .. 6=sabado.
/// El parseo inverso SOLO reconoce los patrones que este mismo builder produce; cualquier cron mas complejo
/// se considera "avanzado" (FromStorage devuelve null) y la UI cae al editor crudo, sin perder el valor.
/// </summary>
public static class FriendlySchedule
{
    /// <summary>Nombres de dia (0=domingo). ASCII a proposito (convencion del repo).</summary>
    private static readonly string[] DayNames =
        { "domingo", "lunes", "martes", "miercoles", "jueves", "viernes", "sabado" };

    /// <summary>Convierte una eleccion humana al almacenamiento real (kind + interval/cron). Valida las
    /// partes y lanza <see cref="ArgumentException"/> con un motivo legible si algo falta o esta fuera de
    /// rango (la UI valida antes, pero el helper no confia en el llamador).</summary>
    public static (ImportScheduleKind Kind, int? IntervalMinutes, string? Cron) ToStorage(FriendlyScheduleSpec spec)
    {
        switch (spec.Recurrence)
        {
            case ScheduleRecurrence.Manual:
                return (ImportScheduleKind.Manual, null, null);

            case ScheduleRecurrence.EveryN:
            {
                var n = spec.EveryValue ?? 0;
                if (n < 1) { throw new ArgumentException("El intervalo 'cada N' debe ser al menos 1."); }
                var minutes = spec.EveryUnit == ScheduleEveryUnit.Hours ? checked(n * 60) : n;
                return (ImportScheduleKind.Interval, minutes, null);
            }

            case ScheduleRecurrence.Daily:
            {
                var t = RequireTime(spec);
                return (ImportScheduleKind.Cron, null, $"{t.Minute} {t.Hour} * * *");
            }

            case ScheduleRecurrence.Weekly:
            {
                var t = RequireTime(spec);
                var days = NormalizeDays(spec.DaysOfWeek);
                if (days.Count == 0) { throw new ArgumentException("Elige al menos un dia de la semana."); }
                return (ImportScheduleKind.Cron, null, $"{t.Minute} {t.Hour} * * {string.Join(",", days)}");
            }

            case ScheduleRecurrence.Monthly:
            {
                var t = RequireTime(spec);
                var dom = spec.DayOfMonth ?? 0;
                if (dom is < 1 or > 31) { throw new ArgumentException("El dia del mes debe estar entre 1 y 31."); }
                return (ImportScheduleKind.Cron, null, $"{t.Minute} {t.Hour} {dom} * *");
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(spec), spec.Recurrence, "Recurrencia no soportada.");
        }
    }

    /// <summary>Parsea el almacenamiento real a la eleccion humana, para PRECARGAR el selector al editar.
    /// Devuelve null cuando el cron no corresponde a un patron amigable (la UI usa el modo avanzado y
    /// conserva el cron tal cual).</summary>
    public static FriendlyScheduleSpec? FromStorage(ImportScheduleKind kind, int? intervalMinutes, string? cron)
    {
        switch (kind)
        {
            case ImportScheduleKind.Manual:
                return new FriendlyScheduleSpec(ScheduleRecurrence.Manual);

            case ImportScheduleKind.Interval:
            {
                var n = intervalMinutes ?? 0;
                if (n < 1) { return null; }
                return n % 60 == 0
                    ? new FriendlyScheduleSpec(ScheduleRecurrence.EveryN, EveryValue: n / 60, EveryUnit: ScheduleEveryUnit.Hours)
                    : new FriendlyScheduleSpec(ScheduleRecurrence.EveryN, EveryValue: n, EveryUnit: ScheduleEveryUnit.Minutes);
            }

            case ImportScheduleKind.Cron:
                return ParseCron(cron);

            default:
                return null;
        }
    }

    /// <summary>Resumen legible de un horario (sirve para CUALQUIER valor guardado, incluido un cron crudo
    /// que no sea amigable). Sin acentos por convencion del repo.</summary>
    public static string Describe(ImportScheduleKind kind, int? intervalMinutes, string? cron)
    {
        switch (kind)
        {
            case ImportScheduleKind.Manual:
                return "Manual (solo cuando le des a 'Actualizar datos')";

            case ImportScheduleKind.Interval:
            {
                var n = intervalMinutes ?? 0;
                if (n < 1) { return "Intervalo sin definir"; }
                if (n % 60 == 0)
                {
                    var h = n / 60;
                    return h == 1 ? "Cada hora" : $"Cada {h} horas";
                }
                return n == 1 ? "Cada minuto" : $"Cada {n} minutos";
            }

            case ImportScheduleKind.Cron:
            {
                var spec = ParseCron(cron);
                if (spec is null)
                {
                    return string.IsNullOrWhiteSpace(cron) ? "Cron sin definir" : $"Segun expresion cron: {cron.Trim()}";
                }
                return DescribeSpec(spec);
            }

            default:
                return "Horario desconocido";
        }
    }

    /// <summary>Resumen legible de una eleccion humana ANTES de guardarla (para el "asi quedara" del modal).</summary>
    public static string DescribeSpec(FriendlyScheduleSpec spec)
    {
        switch (spec.Recurrence)
        {
            case ScheduleRecurrence.Manual:
                return "Manual (solo cuando le des a 'Actualizar datos')";
            case ScheduleRecurrence.EveryN:
            {
                var n = spec.EveryValue ?? 0;
                if (n < 1) { return "Intervalo sin definir"; }
                return spec.EveryUnit == ScheduleEveryUnit.Hours
                    ? (n == 1 ? "Cada hora" : $"Cada {n} horas")
                    : (n == 1 ? "Cada minuto" : $"Cada {n} minutos");
            }
            case ScheduleRecurrence.Daily:
                return spec.Time is TimeOnly td ? $"Todos los dias a las {Hm(td)}" : "Diario (falta la hora)";
            case ScheduleRecurrence.Weekly:
            {
                if (spec.Time is not TimeOnly tw) { return "Semanal (falta la hora)"; }
                var days = NormalizeDays(spec.DaysOfWeek);
                if (days.Count == 0) { return "Semanal (falta elegir dias)"; }
                if (days.Count == 7) { return $"Todos los dias a las {Hm(tw)}"; }
                return $"Cada {JoinDays(days)} a las {Hm(tw)}";
            }
            case ScheduleRecurrence.Monthly:
            {
                if (spec.Time is not TimeOnly tm) { return "Mensual (falta la hora)"; }
                var dom = spec.DayOfMonth ?? 0;
                if (dom is < 1 or > 31) { return "Mensual (falta el dia)"; }
                return $"El dia {dom} de cada mes a las {Hm(tm)}";
            }
            default:
                return "Horario desconocido";
        }
    }

    // ---- internos ----

    private static TimeOnly RequireTime(FriendlyScheduleSpec spec)
        => spec.Time ?? throw new ArgumentException("Elige una hora (HH:MM).");

    /// <summary>Dias unicos, en rango 0-6 y ordenados (para un cron y un resumen estables).</summary>
    private static List<int> NormalizeDays(IReadOnlyList<int>? days)
    {
        if (days is null) { return new List<int>(); }
        return days.Where(d => d is >= 0 and <= 6).Distinct().OrderBy(d => d).ToList();
    }

    private static FriendlyScheduleSpec? ParseCron(string? cron)
    {
        if (string.IsNullOrWhiteSpace(cron)) { return null; }
        var parts = cron.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5) { return null; }
        // minuto y hora deben ser numeros simples; mes siempre "*" en los patrones amigables.
        if (!IsInt(parts[0], 0, 59, out var minute)) { return null; }
        if (!IsInt(parts[1], 0, 23, out var hour)) { return null; }
        if (parts[3] != "*") { return null; }
        var time = new TimeOnly(hour, minute);

        var dom = parts[2];
        var dow = parts[4];

        // Diario: dia-del-mes y dia-de-semana comodin.
        if (dom == "*" && dow == "*")
        {
            return new FriendlyScheduleSpec(ScheduleRecurrence.Daily, Time: time);
        }
        // Semanal: dia-del-mes comodin, dia-de-semana = lista de 0-6.
        if (dom == "*" && dow != "*")
        {
            var days = ParseDayList(dow);
            return days is null ? null : new FriendlyScheduleSpec(ScheduleRecurrence.Weekly, Time: time, DaysOfWeek: days);
        }
        // Mensual: dia-del-mes numerico, dia-de-semana comodin.
        if (dow == "*" && IsInt(dom, 1, 31, out var day))
        {
            return new FriendlyScheduleSpec(ScheduleRecurrence.Monthly, Time: time, DayOfMonth: day);
        }
        return null;
    }

    /// <summary>Parsea el campo dia-de-semana de un cron a dias 0-6 (0=domingo). Acepta:
    /// numeros sueltos ("1"), LISTAS ("1,2,3,4,5,6"), RANGOS ("1-6") y combinaciones ("1-5,0"),
    /// con el domingo como 0 o 7 (7 se normaliza a 0). Asi "1-6" y "1,2,3,4,5,6" resultan IGUALES.
    /// Devuelve null si algo no es representable (rango invertido, texto no numerico, etc.).</summary>
    private static List<int>? ParseDayList(string csv)
    {
        var result = new List<int>();
        foreach (var token in csv.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var dash = token.IndexOf('-');
            if (dash > 0)
            {
                // Rango a-b inclusive (ej. 1-6). Ambos extremos 0-7; se expande y se normaliza cada valor.
                if (!IsInt(token[..dash], 0, 7, out var a) || !IsInt(token[(dash + 1)..], 0, 7, out var b) || a > b)
                {
                    return null;
                }
                for (var d = a; d <= b; d++)
                {
                    var nd = d == 7 ? 0 : d;   // 7 = domingo -> 0
                    if (!result.Contains(nd)) { result.Add(nd); }
                }
            }
            else
            {
                if (!IsInt(token, 0, 7, out var d)) { return null; }
                if (d == 7) { d = 0; }         // cron admite 7 como domingo; normalizamos a 0.
                if (!result.Contains(d)) { result.Add(d); }
            }
        }
        if (result.Count == 0) { return null; }
        result.Sort();
        return result;
    }

    private static bool IsInt(string s, int min, int max, out int value)
        => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;

    private static string Hm(TimeOnly t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string JoinDays(List<int> days)
    {
        var names = days.Select(d => DayNames[d]).ToList();
        if (names.Count == 1) { return names[0]; }
        return string.Join(", ", names.Take(names.Count - 1)) + " y " + names[^1];
    }
}

/// <summary>Tipo de recurrencia elegible en el programador amigable.</summary>
public enum ScheduleRecurrence
{
    /// <summary>Sin horario (solo bajo demanda). Mapea a <see cref="ImportScheduleKind.Manual"/>.</summary>
    Manual,
    /// <summary>Todos los dias a una hora. Genera cron.</summary>
    Daily,
    /// <summary>Uno o varios dias de la semana a una hora. Genera cron.</summary>
    Weekly,
    /// <summary>Un dia del mes a una hora. Genera cron.</summary>
    Monthly,
    /// <summary>Cada N minutos u horas. Mapea a <see cref="ImportScheduleKind.Interval"/>.</summary>
    EveryN
}

/// <summary>Unidad del intervalo en la recurrencia "Cada N".</summary>
public enum ScheduleEveryUnit
{
    Minutes,
    Hours
}

/// <summary>Eleccion humana de horario. Para Daily/Weekly/Monthly se usa <see cref="Time"/>; Weekly usa
/// <see cref="DaysOfWeek"/> (0=domingo..6=sabado); Monthly usa <see cref="DayOfMonth"/> (1-31); EveryN usa
/// <see cref="EveryValue"/> + <see cref="EveryUnit"/>.</summary>
public sealed record FriendlyScheduleSpec(
    ScheduleRecurrence Recurrence,
    TimeOnly? Time = null,
    IReadOnlyList<int>? DaysOfWeek = null,
    int? DayOfMonth = null,
    int? EveryValue = null,
    ScheduleEveryUnit EveryUnit = ScheduleEveryUnit.Minutes);
