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

/// <summary>
/// Parametros del paso Llamada (contenido de ParamsJson). Dos modos:
/// - CRM (Modo null/"crm"): los 4 campos historicos crean una gestion via concepto (comportamiento actual).
/// - IA (Modo "ia"): configura una llamada con un AiAgent REUTILIZADO (por Id), un texto que se anexa a su
///   prompt, un objetivo y los formularios que puede llenar. Solo config; el motor de voz es fase siguiente.
/// </summary>
public sealed record ContactWorkflowCallParams(
    string? Comercial = null,
    string? Prioridad = null,
    string? Categoria = null,
    string? Subcategoria = null,
    // Modo de la llamada: null/"crm" = gestion CRM actual; "ia" = llamada con agente IA.
    string? Modo = null,
    // Agente IA reutilizado (AiAgent.Id); su SystemPrompt es el prompt base de la llamada.
    Guid? AgenteId = null,
    // Texto que se anexa al prompt del agente para esta llamada (producto, tono, oferta...).
    string? PromptExtra = null,
    // Nombre de ContactCallObjetivo (que se quiere que haga el agente).
    string? Objetivo = null,
    // Formularios (FormDefinition.Id) que el agente puede llenar durante la llamada.
    IReadOnlyList<Guid>? FormulariosPermitidos = null);

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
