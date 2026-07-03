namespace Ecorex.Domain.Enums;

/// <summary>Ciclo de vida de una cita (Modelo de Datos seccion 6).</summary>
public enum AppointmentStatus
{
    Scheduled,
    Confirmed,
    Completed,
    NoShow,
    Cancelled,
    Rescheduled
}
