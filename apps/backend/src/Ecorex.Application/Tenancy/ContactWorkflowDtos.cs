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
    IReadOnlyList<ContactWorkflowScheduleDto> Schedules,
    // Paso WhatsApp: linea + contenido (texto libre o plantilla aprobada). Null en otros tipos.
    ContactWorkflowWhatsAppParams? WhatsApp = null);

/// <summary>
/// Parametros del paso WhatsApp (contenido de ParamsJson, 2026-11-15). El contenido depende del proveedor
/// de la linea: Evolution/Emulator envian <see cref="Texto"/> libre con variables {nombre} {empresa} {cargo}
/// {ciudad} {correo}; YCloud/Cloud exigen una plantilla APROBADA (<see cref="PlantillaId"/>), porque fuera de
/// la ventana de 24 h Meta rechaza el texto libre. <see cref="Variables"/> mapea cada token de la plantilla a
/// un campo del contacto (<see cref="ContactWorkflowWhatsAppFields"/>) o a un texto fijo ("txt:...").
/// </summary>
public sealed record ContactWorkflowWhatsAppParams(
    Guid? LineaId = null,
    string? Texto = null,
    Guid? PlantillaId = null,
    IReadOnlyDictionary<string, string>? Variables = null);

/// <summary>Campos del contacto que alimentan las variables de una plantilla de WhatsApp.</summary>
public static class ContactWorkflowWhatsAppFields
{
    /// <summary>Prefijo de un valor FIJO en el mapeo (ej. "txt:Promocion de noviembre").</summary>
    public const string FixedPrefix = "txt:";

    public static readonly IReadOnlyList<(string Key, string Label)> All = new[]
    {
        ("nombre", "Nombre del contacto"),
        ("empresa", "Empresa"),
        ("cargo", "Cargo"),
        ("ciudad", "Ciudad"),
        ("correo", "Correo"),
        ("telefono", "Telefono"),
    };

    /// <summary>Campo sugerido para un token de plantilla (por nombre; "cliente" = nombre). "" si no hay.</summary>
    public static string DefaultFor(string? token)
    {
        var t = (token ?? "").Trim().ToLowerInvariant();
        return t switch
        {
            "nombre" or "cliente" or "contacto" or "name" or "1" => "nombre",
            "empresa" or "compania" or "company" => "empresa",
            "cargo" => "cargo",
            "ciudad" or "city" => "ciudad",
            "correo" or "email" => "correo",
            "telefono" or "celular" or "phone" => "telefono",
            _ => ""
        };
    }

    /// <summary>Valor de un origen del mapeo para el contacto (campo o texto fijo). Null si queda vacio.</summary>
    public static string? Resolve(string? source, string? nombre, string? empresa, string? cargo,
        string? ciudad, string? correo, string? telefono)
    {
        if (string.IsNullOrWhiteSpace(source)) { return null; }
        if (source.StartsWith(FixedPrefix, StringComparison.Ordinal))
        {
            var fixedValue = source[FixedPrefix.Length..].Trim();
            return fixedValue.Length == 0 ? null : fixedValue;
        }
        var value = source.Trim().ToLowerInvariant() switch
        {
            "nombre" => nombre,
            "empresa" => empresa,
            "cargo" => cargo,
            "ciudad" => ciudad,
            "correo" => correo,
            "telefono" => telefono,
            _ => null
        };
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

/// <summary>Una ejecucion del motor para el panel "Ultimas ejecuciones" del disenador (hora local del tenant).</summary>
public sealed record ContactWorkflowRunDto(
    DateTime FechaLocal,
    string Contacto,
    string Paso,
    string Canal,
    ContactWorkflowRunStatus Estado,
    int Intentos,
    string? Error);

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
    IReadOnlyList<SaveContactWorkflowScheduleRequest> Schedules,
    ContactWorkflowWhatsAppParams? WhatsApp = null,
    // Id del paso existente: se ACTUALIZA en sitio (conserva su historial de ejecuciones y el dedupe del dia).
    // Null = paso nuevo.
    Guid? Id = null);

public sealed record SaveContactWorkflowScheduleRequest(
    DateOnly? StartDate,
    DateOnly? EndDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? ActiveDays,
    string? TemplateId,
    Guid? AccountId,
    int? RepeatEvery,
    int? PackageSize,
    // Id de la ventana existente (forma parte de la llave de dedupe). Null = ventana nueva.
    Guid? Id = null);
