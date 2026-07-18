namespace Ecorex.Domain.Enums;

/// <summary>
/// Modo de seguimiento de un concepto de actividad del CRM (000125): que dispara la actividad
/// cuando se ejecuta desde el gestor de contactos.
/// </summary>
public enum ConceptoActividadMode
{
    /// <summary>No dispara proceso ni evento; es un registro simple.</summary>
    None = 0,

    /// <summary>La actividad abre un proceso de atencion (flujo de trabajo).</summary>
    AttentionProcess,

    /// <summary>La actividad genera un evento en el calendario.</summary>
    CalendarEvent,
}
