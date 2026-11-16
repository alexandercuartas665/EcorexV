using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ecorex.Application.DataContainers;

/// <summary>Tipo de desencadenador, como en el Programador de tareas de Windows.</summary>
public enum TriggerTipo
{
    /// <summary>Una sola vez, en la fecha y hora de inicio.</summary>
    UnaVez,
    /// <summary>Cada N dias desde la fecha de inicio.</summary>
    Diaria,
    /// <summary>Cada N semanas, los dias elegidos.</summary>
    Semanal,
    /// <summary>Los meses elegidos, por dias del mes (1..31, ultimo) o por "el primer/segundo... lunes".</summary>
    Mensual,
}

/// <summary>
/// Desencadenador de una programacion al estilo del Programador de tareas de Windows (ADR-0171): fecha y hora
/// de inicio, recurrencia (una vez / cada N dias / cada N semanas con dias / meses con dias u ordinal),
/// repeticion dentro del dia (cada X minutos durante Y) y caducidad. Va como JSON en
/// <c>ImportProcess.TriggerJson</c>; si existe, MANDA sobre el cron/intervalo para calcular la proxima corrida
/// (<see cref="ImportRecurrence.ComputeNextRun"/>). Las horas son de RELOJ DEL TENANT (sin zona): la zona se
/// aplica al calcular.
/// </summary>
public sealed record ScheduleTrigger
{
    public TriggerTipo Tipo { get; init; } = TriggerTipo.Diaria;

    /// <summary>Fecha y hora de inicio (hora local del tenant). Nada corre antes de este instante.</summary>
    public DateTime Inicio { get; init; }

    /// <summary>Diaria: cada cuantos dias. Semanal: cada cuantas semanas.</summary>
    public int Cada { get; init; } = 1;

    /// <summary>Semanal: dias de la semana (0=domingo .. 6=sabado).</summary>
    public int[] Dias { get; init; } = Array.Empty<int>();

    /// <summary>Mensual: meses (1..12). Vacio = todos los meses.</summary>
    public int[] Meses { get; init; } = Array.Empty<int>();

    /// <summary>Mensual: true = por dias del mes (<see cref="DiasMes"/>); false = por ordinal
    /// (<see cref="Ordinal"/> + <see cref="DiaSemana"/>).</summary>
    public bool PorDiaDelMes { get; init; } = true;

    /// <summary>Mensual por dia: 1..31; <see cref="UltimoDia"/> = ultimo dia del mes.</summary>
    public int[] DiasMes { get; init; } = Array.Empty<int>();

    /// <summary>Mensual por ordinal: 1..4 = primer..cuarto; 5 = ultimo.</summary>
    public int Ordinal { get; init; } = 1;

    /// <summary>Mensual por ordinal: dia de la semana (0=domingo .. 6=sabado).</summary>
    public int DiaSemana { get; init; } = 1;

    /// <summary>Repetir la tarea cada X minutos dentro del dia (null = no repite).</summary>
    public int? RepetirCadaMin { get; init; }

    /// <summary>Durante cuantos minutos se repite desde la hora de inicio (null = hasta el final del dia).</summary>
    public int? RepetirDuranteMin { get; init; }

    /// <summary>Caduca: despues de este instante (hora local del tenant) ya no corre.</summary>
    public DateTime? Caduca { get; init; }

    /// <summary>Valor de <see cref="DiasMes"/> que significa "ultimo dia del mes".</summary>
    public const int UltimoDia = 32;

    private static readonly string[] DayNames = { "domingo", "lunes", "martes", "miercoles", "jueves", "viernes", "sabado" };
    private static readonly string[] MonthNames =
        { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };
    private static readonly string[] Ordinals = { "", "primer", "segundo", "tercer", "cuarto", "ultimo" };

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static ScheduleTrigger? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return null; }
        try { return JsonSerializer.Deserialize<ScheduleTrigger>(json, Json); }
        catch (JsonException) { return null; }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Motivo por el que el desencadenador no es valido, o null si lo es.</summary>
    public string? Validate()
    {
        if (Inicio == default) { return "Indica la fecha y hora de inicio."; }
        switch (Tipo)
        {
            case TriggerTipo.Diaria when Cada is < 1 or > 365:
                return "\"Repetir cada\" debe estar entre 1 y 365 dias.";
            case TriggerTipo.Semanal when Cada is < 1 or > 52:
                return "\"Repetir cada\" debe estar entre 1 y 52 semanas.";
            case TriggerTipo.Semanal when !Dias.Any(d => d is >= 0 and <= 6):
                return "Elige al menos un dia de la semana.";
            case TriggerTipo.Mensual when Meses.Any(m => m is < 1 or > 12):
                return "Mes fuera de rango.";
            case TriggerTipo.Mensual when PorDiaDelMes && !DiasMes.Any(d => d is >= 1 and <= UltimoDia):
                return "Elige al menos un dia del mes.";
            case TriggerTipo.Mensual when !PorDiaDelMes && (Ordinal is < 1 or > 5 || DiaSemana is < 0 or > 6):
                return "Elige la semana (primer..ultimo) y el dia.";
        }
        if (RepetirCadaMin is int rc && rc is < 5 or > 720)
        {
            return "\"Repetir cada\" debe estar entre 5 minutos y 12 horas.";
        }
        if (RepetirDuranteMin is int rd && (rd < 1 || rd > 1440))
        {
            return "\"Durante\" debe estar entre 1 minuto y 24 horas.";
        }
        if (Caduca is DateTime c && c <= Inicio) { return "La fecha de caducidad debe ser posterior al inicio."; }
        return null;
    }

    /// <summary>Proxima ejecucion estrictamente posterior a <paramref name="afterUtc"/>, en UTC; null si ya no
    /// hay mas (una vez ya paso, o caduco).</summary>
    public DateTimeOffset? NextAfter(DateTimeOffset afterUtc, TimeZoneInfo tz)
    {
        var afterLocal = TimeZoneInfo.ConvertTime(afterUtc, tz).DateTime;
        var day = (afterLocal.Date > Inicio.Date ? afterLocal.Date : Inicio.Date);
        // Techo: lo bastante largo para "cada 52 semanas" o "el 29 de febrero" (8 anos cubre bisiestos).
        var limit = day.AddYears(Tipo == TriggerTipo.Mensual ? 8 : 2);
        for (; day <= limit; day = day.AddDays(1))
        {
            if (Caduca is DateTime cad && day > cad) { return null; }
            if (Tipo == TriggerTipo.UnaVez && day > Inicio.Date) { return null; }
            if (!DayMatches(day)) { continue; }
            foreach (var t in TimesOf(day))
            {
                if (t <= afterLocal || t < Inicio) { continue; }
                if (Caduca is DateTime c2 && t > c2) { return null; }
                return ToUtc(t, tz);
            }
        }
        return null;
    }

    /// <summary>Las proximas <paramref name="count"/> ejecuciones en hora local del tenant (para el resumen).</summary>
    public IReadOnlyList<DateTime> NextLocal(DateTimeOffset afterUtc, TimeZoneInfo tz, int count)
    {
        var list = new List<DateTime>();
        var cursor = afterUtc;
        while (list.Count < count && NextAfter(cursor, tz) is DateTimeOffset n)
        {
            list.Add(TimeZoneInfo.ConvertTime(n, tz).DateTime);
            cursor = n;
        }
        return list;
    }

    /// <summary>Cron de 5 campos equivalente (o la mejor aproximacion) para lectores antiguos que no conocen
    /// el desencadenador. El calculo real usa <see cref="NextAfter"/>.</summary>
    public string ToCronApprox()
    {
        var m = Inicio.Minute;
        var h = Inicio.Hour;
        return Tipo switch
        {
            TriggerTipo.UnaVez => $"{m} {h} {Inicio.Day} {Inicio.Month} *",
            TriggerTipo.Semanal => $"{m} {h} * * {string.Join(",", Dias.Where(d => d is >= 0 and <= 6).Distinct().Order())}",
            TriggerTipo.Mensual when PorDiaDelMes && DiasMes.All(d => d is >= 1 and <= 31) =>
                $"{m} {h} {string.Join(",", DiasMes.Distinct().Order())} {(Meses.Length == 0 ? "*" : string.Join(",", Meses.Distinct().Order()))} *",
            _ => $"{m} {h} * * *",
        };
    }

    /// <summary>Resumen humano, sin acentos (convencion del repo).</summary>
    public string Describe()
    {
        var hora = Inicio.ToString("HH:mm", CultureInfo.InvariantCulture);
        var fecha = Inicio.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var s = Tipo switch
        {
            TriggerTipo.UnaVez => $"Una vez, el {fecha} a las {hora}",
            TriggerTipo.Diaria => (Cada == 1 ? "Todos los dias" : $"Cada {Cada} dias") + $" a las {hora}",
            TriggerTipo.Semanal => (Cada == 1 ? "Cada semana" : $"Cada {Cada} semanas")
                + $" el {Join(Dias.Where(d => d is >= 0 and <= 6).Distinct().OrderBy(d => d == 0 ? 7 : d).Select(d => DayNames[d]))} a las {hora}",
            TriggerTipo.Mensual => DescribeMensual() + $" a las {hora}",
            _ => "Horario desconocido",
        };
        if (RepetirCadaMin is int rc)
        {
            s += $", repite cada {Dur(rc)} " + (RepetirDuranteMin is int rd ? $"durante {Dur(rd)}" : "hasta el final del dia");
        }
        if (Tipo != TriggerTipo.UnaVez) { s += $" (desde el {fecha}"; s += Caduca is DateTime c ? $" hasta el {c.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)})" : ")"; }
        return s;
    }

    // ---- internos ----

    private string DescribeMensual()
    {
        var meses = Meses.Length == 0 || Meses.Distinct().Count() == 12
            ? "de cada mes"
            : "de " + Join(Meses.Where(x => x is >= 1 and <= 12).Distinct().Order().Select(x => MonthNames[x - 1]));
        if (PorDiaDelMes)
        {
            var dias = DiasMes.Where(d => d is >= 1 and <= UltimoDia).Distinct().Order()
                .Select(d => d == UltimoDia ? "ultimo dia" : d.ToString(CultureInfo.InvariantCulture)).ToList();
            return (dias.Count == 1 && dias[0] == "ultimo dia" ? "El ultimo dia" : $"El dia {Join(dias)}") + $" {meses}";
        }
        return $"El {Ordinals[Math.Clamp(Ordinal, 1, 5)]} {DayNames[Math.Clamp(DiaSemana, 0, 6)]} {meses}";
    }

    private bool DayMatches(DateTime day)
    {
        switch (Tipo)
        {
            case TriggerTipo.UnaVez:
                return day == Inicio.Date;
            case TriggerTipo.Diaria:
                return (day - Inicio.Date).Days % Math.Max(1, Cada) == 0;
            case TriggerTipo.Semanal:
                {
                    if (!Dias.Contains((int)day.DayOfWeek)) { return false; }
                    // Semanas que empiezan el lunes; la semana del inicio es la 0.
                    var semanas = (MondayOf(day) - MondayOf(Inicio.Date)).Days / 7;
                    return semanas % Math.Max(1, Cada) == 0;
                }
            case TriggerTipo.Mensual:
                {
                    if (Meses.Length > 0 && !Meses.Contains(day.Month)) { return false; }
                    var dim = DateTime.DaysInMonth(day.Year, day.Month);
                    if (PorDiaDelMes)
                    {
                        return DiasMes.Contains(day.Day) || (DiasMes.Contains(UltimoDia) && day.Day == dim);
                    }
                    if ((int)day.DayOfWeek != DiaSemana) { return false; }
                    return Ordinal == 5 ? day.Day + 7 > dim : (day.Day - 1) / 7 + 1 == Ordinal;
                }
            default:
                return false;
        }
    }

    /// <summary>Horas del dia en que corre: la de inicio y, si repite, cada X minutos durante Y (sin pasar de
    /// la medianoche).</summary>
    private IEnumerable<DateTime> TimesOf(DateTime day)
    {
        var t0 = day + Inicio.TimeOfDay;
        yield return t0;
        if (RepetirCadaMin is not int rc || rc < 1) { yield break; }
        var fin = RepetirDuranteMin is int rd ? t0.AddMinutes(rd) : day.AddDays(1);
        if (fin > day.AddDays(1)) { fin = day.AddDays(1); }
        for (var t = t0.AddMinutes(rc); t < fin; t = t.AddMinutes(rc)) { yield return t; }
    }

    private static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo tz)
    {
        var l = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        // Hora que no existe (salto de verano): se corre una hora adelante.
        if (tz.IsInvalidTime(l)) { l = l.AddHours(1); }
        var offset = tz.IsAmbiguousTime(l) ? tz.GetAmbiguousTimeOffsets(l).Max() : tz.GetUtcOffset(l);
        return new DateTimeOffset(l, offset).ToUniversalTime();
    }

    private static DateTime MondayOf(DateTime d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    private static string Dur(int min) => min % 60 == 0
        ? (min == 60 ? "1 hora" : $"{min / 60} horas")
        : $"{min} minutos";

    private static string Join(IEnumerable<string> items)
    {
        var l = items.ToList();
        if (l.Count == 0) { return "(ninguno)"; }
        if (l.Count == 1) { return l[0]; }
        return string.Join(", ", l.Take(l.Count - 1)) + " y " + l[^1];
    }
}
