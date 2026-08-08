using Ecorex.Domain.Enums;

namespace Ecorex.SuperAdmin.Components.Shared.Tasks;

/// <summary>
/// Helpers de presentacion de los campos personalizados de la tarea por tablero (ADR-0065).
/// Etiquetas del tipo, ancho de la rejilla e input HTML por tipo, compartidos por el
/// configurador (ActivityBoardDetail) y el editor de valor (TaskDetailModal). Reusa el enum
/// TerceroFieldType del Directorio General para no duplicar la logica de tipos.
/// </summary>
public static class TaskFieldUi
{
    /// <summary>Etiqueta en espanol del tipo de campo.</summary>
    public static string TypeLabel(TerceroFieldType type) => type switch
    {
        TerceroFieldType.Text => "Texto",
        TerceroFieldType.TextArea => "Texto largo",
        TerceroFieldType.Number => "Numero",
        TerceroFieldType.Currency => "Monto",
        TerceroFieldType.Select => "Lista",
        TerceroFieldType.Date => "Fecha",
        TerceroFieldType.Phone => "Telefono",
        TerceroFieldType.Separator => "Separador",
        TerceroFieldType.Calculated => "Calculado",
        TerceroFieldType.Lookup => "Lista del Contenedor",
        _ => type.ToString()
    };

    /// <summary>Ancho legible: 1 = pequena, 2 = media, 3 = grande (completo).</summary>
    public static string ColumnLabel(int column) => column switch
    {
        >= 3 => "grande",
        2 => "media",
        _ => "pequena"
    };

    /// <summary>Clase Bootstrap de ancho sobre la rejilla de 12: 1 = 1/3, 2 = 2/3, 3 = fila entera.</summary>
    public static string ColClass(int column) => column switch
    {
        >= 3 => "col-12",
        2 => "col-md-8",
        _ => "col-md-4"
    };

    /// <summary>Tipo de &lt;input&gt; HTML para el editor de valor por tipo.</summary>
    public static string InputType(TerceroFieldType type) => type switch
    {
        TerceroFieldType.Number or TerceroFieldType.Currency => "number",
        TerceroFieldType.Date => "date",
        TerceroFieldType.Phone => "tel",
        _ => "text"
    };

    /// <summary>Opciones de un Select (una por linea).</summary>
    public static string[] SplitOptions(string? options)
        => string.IsNullOrWhiteSpace(options)
            ? []
            : options.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
