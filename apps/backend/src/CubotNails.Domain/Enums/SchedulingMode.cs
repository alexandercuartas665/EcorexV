namespace CubotNails.Domain.Enums;

/// <summary>
/// Como un recurso (asesor/estacion) ofrece su disponibilidad. La defensa anti-solapamiento de la BD
/// es identica en ambos modos; lo unico que cambia es como se calculan los horarios ofrecidos.
/// </summary>
public enum SchedulingMode
{
    /// <summary>Grilla de cupos fijos cada ShiftTemplate.SlotMinutes (comportamiento clasico por turnos).</summary>
    SlotGrid = 0,

    /// <summary>Continuo: ofrece el proximo hueco donde quepa la duracion completa del servicio (por largo).</summary>
    Duration = 1
}
