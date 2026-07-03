namespace Ecorex.Domain.Enums;

/// <summary>Alcance de una excepcion/bloqueo de agenda (Modelo de Datos seccion 6).</summary>
public enum ExceptionScope
{
    /// <summary>Afecta a TODOS los asesores del salon (festivo, cierre del local).</summary>
    Global,
    /// <summary>Afecta a un asesor especifico (vacaciones, incapacidad, capacitacion).</summary>
    Resource
}
