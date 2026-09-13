namespace Ecorex.Application.Workflows;

/// <summary>
/// Construye el enlace (deep-link) absoluto a una tarea para incluirlo en una notificacion. La URL base
/// (PublicBaseUrl) se configura por entorno; si no esta configurada, devuelve null (el mensaje sale sin enlace).
/// Vive detras de una interfaz porque las notificaciones se envian desde el motor (Application) y desde
/// workers, sin NavigationManager.
/// </summary>
public interface INotifyLinkBuilder
{
    /// <summary>Enlace absoluto que abre la actividad (p.ej. https://app.../actividades?task={id}); null si no hay URL base.</summary>
    string? BuildTaskLink(Guid taskId);
}
