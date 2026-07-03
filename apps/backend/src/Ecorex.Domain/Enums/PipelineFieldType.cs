namespace Ecorex.Domain.Enums;

/// <summary>Tipo de un campo configurable del embudo (modulo 2.1). Define como se captura/renderiza.</summary>
public enum PipelineFieldType
{
    Text,
    Number,
    Currency,
    TextArea,
    Select,
    Date,
    Phone,
    /// <summary>Campo calculado de solo lectura: suma los valores de los campos origen indicados
    /// en TotalSourceKeys (si un origen es multiple/repetido, suma todos sus registros).</summary>
    Total,
    /// <summary>Hora simple (HH:mm).</summary>
    Time,
    /// <summary>Dos horas en un solo campo: hora de salida y hora de llegada (se guardan como "salida - llegada").</summary>
    TimeRange,
    /// <summary>Separador visual (linea divisoria con titulo). No captura ningun valor.</summary>
    Separator
}
