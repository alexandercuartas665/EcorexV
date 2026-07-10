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

    /// <summary>Columna del layout en el modal (1 = angosta, 2 = ancha/full).</summary>
    public int Column { get; set; } = 1;
    public int SortOrder { get; set; }

    /// <summary>Ayuda/contexto para quien captura el dato (y para agentes de IA).</summary>
    public string? Description { get; set; }

    /// <summary>Si el dato es obligatorio al guardar.</summary>
    public bool IsRequired { get; set; }

    /// <summary>Marca los campos sembrados por defecto, para distinguirlos de los del tenant.</summary>
    public bool IsSystem { get; set; }
}
