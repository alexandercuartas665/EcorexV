namespace Ecorex.Application.Workflows;

/// <summary>
/// Calculo PURO de vencimientos por plazo (Fase 1 - plazos de flujo). Trabaja en hora LOCAL del tenant: el
/// motor convierte ahora(UTC)->local, calcula aqui, y persiste UTC. Los DIAS respetan el modo (calendario suma
/// dias corridos; habil salta fines de semana + dias no operativos del tenant, conservando la hora del dia);
/// horas y minutos son SIEMPRE tiempo real. Sin BD ni zona horaria adentro: recibe los dias no operativos y el
/// fin de semana como conjuntos en fechas/dias locales. Testeable en aislamiento.
/// </summary>
public static class StepDeadlineCalculator
{
    /// <summary>Fin de semana por defecto (sabado y domingo). Configurable por tenant en una fase posterior.</summary>
    public static readonly IReadOnlySet<DayOfWeek> DefaultWeekend =
        new HashSet<DayOfWeek> { DayOfWeek.Saturday, DayOfWeek.Sunday };

    private static readonly IReadOnlySet<DateOnly> NoDays = new HashSet<DateOnly>();

    /// <summary>Suma UN plazo a un inicio local. nonWorkingDays/weekend en fechas y dias LOCALES del tenant.</summary>
    public static DateTime AddPlazo(DateTime startLocal, StepSla sla,
        IReadOnlySet<DateOnly>? nonWorkingDays = null, IReadOnlySet<DayOfWeek>? weekend = null)
    {
        if (sla.IsEmpty) { return startLocal; }
        var result = startLocal;
        if (sla.Days > 0)
        {
            result = sla.DayMode == StepSlaDayMode.Business
                ? AddBusinessDays(result, sla.Days, nonWorkingDays ?? NoDays, weekend ?? DefaultWeekend)
                : result.AddDays(sla.Days);
        }
        return result.AddHours(sla.Hours).AddMinutes(sla.Minutes);
    }

    /// <summary>Encadena varios plazos desde un inicio local (para el estimado total del flujo / la fecha final
    /// que rueda): cada plazo se suma en secuencia respetando su propio modo. Los plazos vacios se ignoran.</summary>
    public static DateTime AddPlazos(DateTime startLocal, IEnumerable<StepSla> slas,
        IReadOnlySet<DateOnly>? nonWorkingDays = null, IReadOnlySet<DayOfWeek>? weekend = null)
    {
        var result = startLocal;
        foreach (var sla in slas)
        {
            if (!sla.IsEmpty) { result = AddPlazo(result, sla, nonWorkingDays, weekend); }
        }
        return result;
    }

    /// <summary>Avanza N dias habiles conservando la hora del dia (salta fines de semana y dias no operativos).</summary>
    private static DateTime AddBusinessDays(DateTime start, int businessDays,
        IReadOnlySet<DateOnly> nonWorking, IReadOnlySet<DayOfWeek> weekend)
    {
        var date = start;
        var added = 0;
        // Cota de seguridad: nunca mas de ~40 anios de saltos (evita bucle infinito si todo es no operativo).
        var guard = 0;
        while (added < businessDays && guard++ < 15000)
        {
            date = date.AddDays(1);
            if (IsWorkingDay(date, nonWorking, weekend)) { added++; }
        }
        return date;
    }

    /// <summary>True si el dia local es operativo (ni fin de semana ni dia no operativo del tenant).</summary>
    public static bool IsWorkingDay(DateTime dateLocal, IReadOnlySet<DateOnly> nonWorking, IReadOnlySet<DayOfWeek> weekend)
        => !weekend.Contains(dateLocal.DayOfWeek) && !nonWorking.Contains(DateOnly.FromDateTime(dateLocal));
}
