using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Regla de recurrencia de una programacion (1:N con <see cref="ScheduledJob"/>). Describe CUANDO
/// dispara: frecuencia + intervalo + dias/ordinal + repeticion intradia + vigencia. Sucesor de
/// PROG_ACTIVIDADES_R (origen), enriquecido a las frecuencias del prototipo. Las horas se guardan
/// como texto "HH:mm" (formato del prototipo); las fechas de vigencia como DateOnly. El calculo de
/// la proxima ejecucion (timezone-aware por tenant) lo hace el worker en P2 (rellena NextRunAt).
/// </summary>
public class ScheduledJobRule : TenantEntity
{
    public Guid JobId { get; set; }
    public ScheduledJob Job { get; set; } = null!;

    /// <summary>Orden de la regla dentro de la programacion.</summary>
    public int SortOrder { get; set; }

    public ScheduledJobFrequency Frequency { get; set; } = ScheduledJobFrequency.Weekly;

    /// <summary>"cada N" {dias/semanas/meses} segun la frecuencia.</summary>
    public int IntervalNum { get; set; } = 1;

    /// <summary>Weekly: dias seleccionados como CSV corto ("Lun,Mar,Mie"). Null si no aplica.</summary>
    public string? Weekdays { get; set; }

    /// <summary>Monthly: ordinal ("Primer".."Cuarto","Ultimo"). Null si no aplica.</summary>
    public string? MonthOrdinal { get; set; }

    /// <summary>Monthly: dia de semana ("Lunes".."Domingo","dia"). Null si no aplica.</summary>
    public string? MonthWeekday { get; set; }

    /// <summary>Monthly: alternativa "dia N del mes" (del doc; sin UI en el prototipo). Null si se usa ordinal.</summary>
    public int? DayOfMonth { get; set; }

    /// <summary>Hora de disparo "HH:mm" (cuando NO hay repeticion intradia).</summary>
    public string? AtTime { get; set; }

    /// <summary>Repeticion intradia: "cada N horas de HH:mm a HH:mm".</summary>
    public bool RepeatIntraday { get; set; }

    public int? RepeatEveryHours { get; set; }

    /// <summary>Ventana intradia inicio "HH:mm".</summary>
    public string? RepeatFrom { get; set; }

    /// <summary>Ventana intradia fin "HH:mm".</summary>
    public string? RepeatTo { get; set; }

    /// <summary>Vigencia desde (inclusive). Null = desde ya.</summary>
    public DateOnly? ValidFrom { get; set; }

    /// <summary>Vigencia hasta (inclusive). Null = sin fin.</summary>
    public DateOnly? ValidTo { get; set; }

    public string? Description { get; set; }

    /// <summary>Proxima ejecucion calculada por el worker (P2). Null hasta que el motor la fije.</summary>
    public DateTimeOffset? NextRunAt { get; set; }

    // ---- Reintento y dead-letter (ola P4, doc D5) ----

    /// <summary>
    /// Ventana que quedo FALLIDA y se esta reintentando. Mientras no sea null, <see cref="NextRunAt"/> es
    /// el instante del REINTENTO (no una ventana nueva): asi el disparo conserva su identidad original
    /// (fired_at) y el reintento no "inventa" una ventana que nunca toco. Null = operacion normal.
    /// </summary>
    public DateTimeOffset? PendingWindowAt { get; set; }

    /// <summary>Intentos ya realizados sobre <see cref="PendingWindowAt"/>. 0 cuando no hay reintento en curso.</summary>
    public int Attempt { get; set; }
}
