namespace Ecorex.Domain.Enums;

/// <summary>
/// Agregado de una columna de tabla (GridDetail) en Formularios avanzados, ola F2 (doc 01 D5).
/// Define la fila de totales al pie y el roll-up al encabezado. Se persiste como string para
/// portabilidad DAL dual.
/// </summary>
public enum FormAggregate
{
    /// <summary>Sin agregado (default).</summary>
    None = 0,

    /// <summary>Suma de la columna.</summary>
    Sum,

    /// <summary>Conteo de filas con valor.</summary>
    Count,

    /// <summary>Promedio de la columna.</summary>
    Avg,

    /// <summary>Minimo de la columna.</summary>
    Min,

    /// <summary>Maximo de la columna.</summary>
    Max
}
