using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Definicion de un campo DINAMICO de la <see cref="Entidad"/>, a nivel de tenant: cada tenant
/// agrega/quita los campos extra que quiere capturar en la ficha de sus entidades (ej. codigos
/// locales como DIVIPOLA, o cualquier dato propio), sin tocar codigo. Los VALORES por entidad se
/// guardan en <see cref="Entidad.FieldValuesJson"/> (dict FieldKey-&gt;valor). Calca el patron
/// probado de ItemFieldDefinition/TerceroFieldDefinition, pero sin agrupar por tipo (los campos
/// aplican a todas las entidades del tenant). Unica por (TenantId, FieldKey).
/// </summary>
public class EntidadFieldDefinition : TenantEntity
{
    /// <summary>Clave estable del campo (slug). Unica por tenant.</summary>
    public string FieldKey { get; set; } = null!;

    /// <summary>Etiqueta visible.</summary>
    public string Label { get; set; } = null!;

    public TerceroFieldType FieldType { get; set; } = TerceroFieldType.Text;

    /// <summary>Opciones para el tipo Select, una por linea.</summary>
    public string? Options { get; set; }

    /// <summary>Columna del layout (1 = angosta, 2 = ancha/full).</summary>
    public int Column { get; set; } = 1;
    public int SortOrder { get; set; }

    /// <summary>Ayuda/contexto para quien captura el dato.</summary>
    public string? Description { get; set; }

    public bool IsRequired { get; set; }

    /// <summary>Campos sembrados por defecto, para distinguirlos de los del tenant.</summary>
    public bool IsSystem { get; set; }
}
