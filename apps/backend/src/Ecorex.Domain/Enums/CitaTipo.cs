namespace Ecorex.Domain.Enums;

/// <summary>
/// Tipo de evento/cita de la Agenda del Gestor de Clientes (000740). Cada tipo tiene su color
/// en la leyenda del calendario del prototipo (Cotizacion/Llamada/Reunion/Visita/PQR).
/// </summary>
public enum CitaTipo
{
    Cotizacion = 0,
    Llamada = 1,
    Reunion = 2,
    Visita = 3,
    Pqr = 4
}
