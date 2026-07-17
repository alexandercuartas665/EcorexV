namespace Ecorex.Domain.Enums;

/// <summary>
/// Tipo de un campo configurable de una ficha del Directorio General (modulo 000232).
/// Define como se captura/renderiza el campo. Calcado del patron de campos configurables
/// del pipeline del proyecto hermano CUBOT.travels.
/// </summary>
public enum TerceroFieldType
{
    Text,
    Number,
    Currency,
    TextArea,
    Select,
    Date,
    Phone,
    /// <summary>Separador visual (linea divisoria con titulo). No captura ningun valor.</summary>
    Separator,

    /// <summary>
    /// Campo de solo lectura cuyo valor sale de evaluar <c>Formula</c> (ver ADR-0029). No se captura:
    /// se recalcula al escribir en los campos que referencia y se materializa al guardar.
    /// </summary>
    Calculated
}
