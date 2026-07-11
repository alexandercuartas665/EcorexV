namespace Ecorex.Domain.Enums;

/// <summary>
/// Tipo de una notificacion in-app (Ola 7 - entrega real de notificaciones). Se persiste como
/// string (ver ConfigureConventions), no como entero.
/// </summary>
public enum NotificationKind
{
    /// <summary>Le asignaron una tarea al destinatario.</summary>
    TaskAssigned = 0,

    /// <summary>Aviso dirigido a un destinatario configurado en el concepto de la actividad.</summary>
    ConceptNotice = 1,

    /// <summary>Notificacion generica del sistema.</summary>
    General = 2,
}
