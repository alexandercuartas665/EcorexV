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
    Separator
}
