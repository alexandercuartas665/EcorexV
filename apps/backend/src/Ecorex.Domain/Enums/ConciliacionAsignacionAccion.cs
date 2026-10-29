namespace Ecorex.Domain.Enums;

/// <summary>
/// Tipo de evento en el historial de asignaciones de un renglon de la Conciliacion DIAN
/// (<see cref="Ecorex.Domain.Entities.ConciliacionDianAsignacion"/>).
/// </summary>
public enum ConciliacionAsignacionAccion
{
    /// <summary>El renglon se direcciono/asigno a un usuario.</summary>
    Asignado = 0,

    /// <summary>El documento se devolvio al conciliador (rechazado): se quito la asignacion y volvio al pool.</summary>
    Rechazado = 1
}
