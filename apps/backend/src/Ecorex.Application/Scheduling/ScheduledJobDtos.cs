using Ecorex.Domain.Enums;

namespace Ecorex.Application.Scheduling;

// DTOs del Motor de programaciones (modulo 000889 "Programar actividad"). Ola P1: CRUD + UI.

/// <summary>Fila de la lista (prototipo: TIPO | NOMBRE | REGLA | CANALES | ESTADO).</summary>
public sealed record ScheduledJobListItemDto(
    Guid Id, string Code, ScheduledJobType Type, string Name, string? SubLabel,
    string RuleSummary, IReadOnlyList<string> Channels, ScheduledJobStatus Status,
    // Ola P2: proxima ejecucion calculada por el motor de recurrencia (null = no volvera a disparar).
    DateTimeOffset? NextRunAt = null);

/// <summary>Una regla de recurrencia (para el detalle y el guardado).</summary>
public sealed record ScheduledJobRuleDto(
    ScheduledJobFrequency Frequency, int IntervalNum, string? Weekdays,
    string? MonthOrdinal, string? MonthWeekday, int? DayOfMonth,
    string? AtTime, bool RepeatIntraday, int? RepeatEveryHours, string? RepeatFrom, string? RepeatTo,
    DateOnly? ValidFrom, DateOnly? ValidTo, string? Description);

/// <summary>Detalle completo de una programacion (para abrir el modal en edicion).</summary>
public sealed record ScheduledJobDetailDto(
    Guid Id, string Code, string Name, ScheduledJobType Type, ScheduledJobStatus Status,
    Guid? CategoryId, Guid? SubcategoryId, Guid? AssigneeTenantUserId, long Version,
    IReadOnlyList<ScheduledJobRuleDto> Rules, IReadOnlyList<ScheduledJobChannelType> Channels);

/// <summary>Datos de guardado de una programacion (crear o actualizar). Encargado OPCIONAL.</summary>
public sealed record SaveScheduledJobRequest(
    string Name, ScheduledJobType Type, Guid? CategoryId, Guid? SubcategoryId,
    IReadOnlyList<ScheduledJobRuleDto> Rules,
    IReadOnlyList<ScheduledJobChannelType> Channels,
    Guid? AssigneeTenantUserId = null, long Version = 0);

/// <summary>Resultado de guardar (Ok con Id, o Fail con mensaje de validacion/concurrencia).</summary>
public sealed record ScheduledJobSaveResult(bool IsOk, Guid? Id, string? Error)
{
    public static ScheduledJobSaveResult Ok(Guid id) => new(true, id, null);
    public static ScheduledJobSaveResult Fail(string error) => new(false, null, error);
}

/// <summary>Una fila de la bitacora de ejecucion (ola P2): lo que el origen legacy nunca integro.</summary>
public sealed record ScheduledJobRunDto(
    Guid Id, DateTimeOffset FiredAt, ScheduledJobRunResult Result, string? Detail, string? CreatedEntityRef);

/// <summary>KPIs de la bitacora (sucesores de ContarEjecutadosHoy/ContarErrores, que en el origen devolvian 0).</summary>
public sealed record ScheduledJobKpisDto(int ExecutedToday, int Errors, int ActiveJobs);

// Catalogo de conceptos (000270) para los selects Categoria/Sub-categoria del tipo Actividad.
public sealed record ScheduledJobSubcategoryDto(Guid Id, string Name);
public sealed record ScheduledJobCategoryDto(Guid Id, string Name, IReadOnlyList<ScheduledJobSubcategoryDto> Subcategories);
