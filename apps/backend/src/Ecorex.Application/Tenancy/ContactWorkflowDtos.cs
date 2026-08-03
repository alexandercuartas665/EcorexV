using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Disenador de acciones de un filtro guardado (ADR-0056, Fase 1). Snapshot completo:
/// cabecera + pasos ordenados + ventanas de horario por paso. La EJECUCION es Fase 2.
/// </summary>
public sealed record ContactWorkflowDto(
    Guid Id,
    Guid TerceroFiltroId,
    string Nombre,
    bool Activo,
    IReadOnlyList<ContactWorkflowStepDto> Steps);

/// <summary>Paso (accion) del disenador. Params solo para el tipo Llamada.</summary>
public sealed record ContactWorkflowStepDto(
    Guid Id,
    ContactWorkflowStepType StepType,
    string Label,
    int Orden,
    ContactWorkflowCallParams? Call,
    IReadOnlyList<ContactWorkflowScheduleDto> Schedules);

/// <summary>Campos CRM del paso Llamada (contenido de ParamsJson).</summary>
public sealed record ContactWorkflowCallParams(
    string? Comercial = null,
    string? Prioridad = null,
    string? Categoria = null,
    string? Subcategoria = null);

/// <summary>Ventana de horario de un paso.</summary>
public sealed record ContactWorkflowScheduleDto(
    Guid Id,
    DateOnly? StartDate,
    DateOnly? EndDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string ActiveDays,
    string? TemplateId,
    Guid? AccountId,
    int? RepeatEvery,
    int? PackageSize);

// ---- Requests (alta/edicion desde el disenador) ----

/// <summary>Guardado completo del disenador (upsert). Reemplaza pasos+ventanas del filtro.</summary>
public sealed record SaveContactWorkflowRequest(
    string? Nombre,
    bool Activo,
    IReadOnlyList<SaveContactWorkflowStepRequest> Steps);

public sealed record SaveContactWorkflowStepRequest(
    ContactWorkflowStepType StepType,
    string? Label,
    ContactWorkflowCallParams? Call,
    IReadOnlyList<SaveContactWorkflowScheduleRequest> Schedules);

public sealed record SaveContactWorkflowScheduleRequest(
    DateOnly? StartDate,
    DateOnly? EndDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? ActiveDays,
    string? TemplateId,
    Guid? AccountId,
    int? RepeatEvery,
    int? PackageSize);
