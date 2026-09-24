using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Definicion de un campo configurable de un ITEM de inventario (000066). El campo puede ser
/// GENERAL (aplica a TODOS los items del tenant, <see cref="ItemTypeId"/> == null; equivale a la
/// ficha "base" del Directorio) o POR TIPO (<see cref="ItemType"/>: producto, servicio, insumo...;
/// se muestra solo cuando el item es de ese tipo). Entidad TENANT-SCOPED: cada tenant agrega/quita
/// los campos que quiere capturar en la ficha de sus items, sin tocar codigo. Los VALORES por item
/// se guardan en <see cref="Item.FieldValuesJson"/> (dict FieldKey -&gt; valor). Calcado del patron
/// probado de <see cref="TerceroFieldDefinition"/> (general + por ficha).
/// </summary>
public class ItemFieldDefinition : TenantEntity
{
    /// <summary>
    /// Tipo de item dueno del campo, o NULL para un campo GENERAL que aplica a todos los items del
    /// tenant sin importar su tipo (como la ficha "base" del Directorio).
    /// </summary>
    public Guid? ItemTypeId { get; set; }
    public ItemType? ItemType { get; set; }

    /// <summary>Clave estable del campo (slug). Unica por (tenant, tipo); general = tipo null.</summary>
    public string FieldKey { get; set; } = null!;

    /// <summary>Etiqueta visible.</summary>
    public string Label { get; set; } = null!;

    public TerceroFieldType FieldType { get; set; } = TerceroFieldType.Text;

    /// <summary>Opciones para el tipo Select, una por linea.</summary>
    public string? Options { get; set; }

    /// <summary>
    /// Ancho del campo en la rejilla de 3 columnas del modal: 1 = pequena (1/3), 2 = media (2/3),
    /// 3 = grande (ancho completo). El servicio lo acota a ese rango.
    /// </summary>
    public int Column { get; set; } = 1;
    public int SortOrder { get; set; }

    /// <summary>Ayuda/contexto para quien captura el dato (y para agentes de IA).</summary>
    public string? Description { get; set; }

    /// <summary>Si el dato es obligatorio al guardar.</summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Solo para <see cref="TerceroFieldType.Calculated"/>: expresion a evaluar, con los campos
    /// referenciados entre llaves. Ej: <c>{costo} * (1 + {margen} / 100)</c>. Un campo GENERAL solo
    /// puede referenciar otros generales; un campo POR TIPO puede referenciar los generales y los del
    /// MISMO tipo (los unicos que existen a la vez en un item de ese tipo). Ver ADR-0029.
    /// </summary>
    public string? Formula { get; set; }

    /// <summary>El campo se ofrece como filtro en el listado de items.</summary>
    public bool ShowInFilter { get; set; }

    /// <summary>
    /// FieldKey de un campo numerico del mismo tipo: este campo se repite tantas veces como diga su
    /// valor. Null = no se repite.
    /// </summary>
    public string? RepeatWithFieldKey { get; set; }

    /// <summary>Marca los campos sembrados por defecto, para distinguirlos de los del tenant.</summary>
    public bool IsSystem { get; set; }
}
