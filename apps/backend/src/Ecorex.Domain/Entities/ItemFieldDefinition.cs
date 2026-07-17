using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Definicion de un campo configurable de un ITEM de inventario (000066), agrupado POR TIPO
/// (<see cref="ItemType"/>: producto, servicio, insumo...). Entidad TENANT-SCOPED: cada tenant
/// agrega/quita los campos que quiere capturar en la ficha de sus items, sin tocar codigo, y
/// esos campos se muestran solo cuando el item es del tipo dueno del campo. Los VALORES por item
/// se guardan en <see cref="Item.FieldValuesJson"/> (dict FieldKey -&gt; valor). Calcado del patron
/// probado de <see cref="TerceroFieldDefinition"/>, agrupando por tipo en vez de por ficha.
/// </summary>
public class ItemFieldDefinition : TenantEntity
{
    /// <summary>Tipo de item dueno del campo. Los campos son POR tipo (producto/servicio/insumo).</summary>
    public Guid ItemTypeId { get; set; }
    public ItemType? ItemType { get; set; }

    /// <summary>Clave estable del campo (slug). Unica por (tenant, tipo).</summary>
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
    /// referenciados entre llaves. Ej: <c>{costo} * (1 + {margen} / 100)</c>. En items solo puede
    /// referenciar campos del MISMO tipo de item: un item tiene un unico tipo, asi que los de otro
    /// tipo no existirian al evaluar. Ver ADR-0029.
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
