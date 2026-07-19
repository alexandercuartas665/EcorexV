namespace Ecorex.Domain.Enums;

/// <summary>
/// Naturaleza de una etapa configurable del pipeline de oportunidades (000740). La usan los KPIs
/// (valor del pipeline abierto, ganadas, perdidas) y define si la oportunidad esta cerrada. Se
/// persiste como string (seguro entre motores al agregar valores al final).
/// </summary>
public enum OportunidadEstadoTipo
{
    /// <summary>Oportunidad en curso: cuenta para el pipeline abierto.</summary>
    Abierta = 0,
    /// <summary>Cerrada con exito.</summary>
    Ganada = 1,
    /// <summary>Cerrada sin exito.</summary>
    Perdida = 2
}
