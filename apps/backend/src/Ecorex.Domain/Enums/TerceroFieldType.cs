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
    Calculated,

    /// <summary>
    /// Lista alimentada por una tabla del Contenedor de datos. La configuracion (modelo, tabla,
    /// columna a mostrar, filtros en cascada y autollenado) va serializada en <c>Options</c>, en
    /// el mismo sitio donde un Select guarda sus opciones de texto: por eso NO hizo falta ninguna
    /// columna nueva. Se guarda el Id de la FILA, no su texto, de modo que corregir el dato en el
    /// Contenedor se refleja en todos los registros que la referencian.
    /// Ver <c>Ecorex.Application.DataLookups</c>.
    /// </summary>
    Lookup
}
