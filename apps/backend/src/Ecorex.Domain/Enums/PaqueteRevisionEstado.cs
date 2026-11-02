namespace Ecorex.Domain.Enums;

/// <summary>
/// Revision humana (QA) de un paquete separado por el Analizador. El usuario abre el resultado y marca
/// cada soporte como Correcto o Incorrecto; por defecto queda Pendiente hasta que alguien lo revise.
/// </summary>
public enum PaqueteRevisionEstado
{
    /// <summary>Aun no revisado por una persona.</summary>
    Pendiente = 0,

    /// <summary>El usuario confirmo que la separacion y la metadata del paquete son correctas.</summary>
    Correcto = 1,

    /// <summary>El usuario marco el paquete como incorrecto (mal cortado o mala metadata).</summary>
    Incorrecto = 2
}
