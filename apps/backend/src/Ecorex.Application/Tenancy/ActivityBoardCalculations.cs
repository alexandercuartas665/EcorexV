namespace Ecorex.Application.Tenancy;

/// <summary>
/// Logica PURA de los tableros de actividades (ADR-0020), extraida para test unitario:
/// calculo de progreso del indice, porcentaje de checklist y rango de fechas de los
/// filtros hoy/manana/con-fecha. Sin dependencias de EF ni de reloj (recibe "now").
/// </summary>
public static class ActivityBoardCalculations
{
    /// <summary>
    /// Progreso % de un tablero del indice. Regla (documentada en ADR-0020): si las tareas
    /// del tablero tienen items de checklist, el progreso es items completados / totales;
    /// si NO hay checklist, cae a tareas en columna final (IsDone) / tareas totales.
    /// Sin tareas: 0.
    /// </summary>
    public static int BoardProgressPct(int checklistDone, int checklistTotal, int tasksInDoneColumns, int totalTasks)
    {
        if (checklistTotal > 0) { return Pct(checklistDone, checklistTotal); }
        if (totalTasks > 0) { return Pct(tasksInDoneColumns, totalTasks); }
        return 0;
    }

    /// <summary>Porcentaje entero 0..100 con redondeo half-away-from-zero. 0 si el total es 0.</summary>
    public static int Pct(int done, int total)
        => total <= 0 ? 0 : (int)Math.Round(done * 100.0 / total, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Rango [From, To) en UTC del filtro de fecha limite del tablero (hoy / manana /
    /// con fecha). Null = sin filtro. Decision documentada: los dias se cortan en UTC
    /// (las fechas del nucleo son DateTimeOffset UTC; el corte por zona del tenant es
    /// deuda de la ola de UI).
    /// </summary>
    public static (DateTimeOffset From, DateTimeOffset To)? DueRangeUtc(
        ActivityDueFilter due, DateTimeOffset? dueOn, DateTimeOffset nowUtc)
    {
        var today = new DateTimeOffset(nowUtc.UtcDateTime.Date, TimeSpan.Zero);
        return due switch
        {
            ActivityDueFilter.Today => (today, today.AddDays(1)),
            ActivityDueFilter.Tomorrow => (today.AddDays(1), today.AddDays(2)),
            ActivityDueFilter.OnDate when dueOn is DateTimeOffset date
                => (new DateTimeOffset(date.UtcDateTime.Date, TimeSpan.Zero),
                    new DateTimeOffset(date.UtcDateTime.Date, TimeSpan.Zero).AddDays(1)),
            _ => null
        };
    }
}

/// <summary>Iniciales para el avatar de un miembro (misma regla del kanban CRM).</summary>
public static class MemberInitials
{
    public static string From(string name)
    {
        var initials = string.Concat(
            (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2)
            .Select(p => p[0])).ToUpperInvariant();
        if (initials.Length > 0) { return initials; }
        var fallback = (name ?? "").Trim();
        return fallback.Length >= 2 ? fallback[..2].ToUpperInvariant() : fallback.ToUpperInvariant();
    }
}
