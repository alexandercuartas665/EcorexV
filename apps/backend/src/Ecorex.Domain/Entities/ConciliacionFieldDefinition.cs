using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Definicion de un campo DINAMICO (a medida) de la tabla de conciliacion DIAN, por tenant. El usuario los
/// crea sin codigo (modulo "Configurar campos") y aparecen como columnas editables en la grilla. Sus valores
/// se guardan por renglon en <see cref="ConciliacionFieldValue"/> (EAV, como el directorio). Clave logica:
/// (TenantId, FieldKey).
/// </summary>
public sealed class ConciliacionFieldDefinition : TenantEntity
{
    /// <summary>Clave estable del campo (slug, p. ej. "centro_costo"). No cambia aunque cambie la etiqueta.</summary>
    public string FieldKey { get; set; } = string.Empty;

    /// <summary>Etiqueta visible en la columna.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Tipo: "Text" | "Number" | "Date" | "Bool" | "Select".</summary>
    public string Type { get; set; } = "Text";

    /// <summary>Orden entre los campos dinamicos.</summary>
    public int SortOrder { get; set; }

    /// <summary>Opciones (JSON array de strings) cuando Type = "Select". Null para los demas.</summary>
    public string? OptionsJson { get; set; }

    /// <summary>Descripcion opcional del campo (se muestra como tooltip en el encabezado de la columna).</summary>
    public string? Description { get; set; }

    /// <summary>Si el campo se ofrece como opcion en el constructor de filtros del listado. Default true.</summary>
    public bool ShowInFilter { get; set; } = true;

    /// <summary>Si true, un valor usado en un renglon NO puede repetirse en otro del mismo documento
    /// (p. ej. una orden de compra no se reusa en otra factura). Hoy se usa en las columnas fijas oc/numsol.</summary>
    public bool UniqueValue { get; set; }
}
