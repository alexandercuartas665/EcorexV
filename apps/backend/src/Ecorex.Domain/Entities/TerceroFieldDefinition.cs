using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Definicion de un campo configurable de una FICHA del Directorio General (modulo 000232).
/// Entidad TENANT-SCOPED: cada tenant puede agregar/quitar campos y cambiarles el tipo por
/// ficha (fiscal, comercial, cliente, proveedor, empleado). Los valores por tercero se guardan
/// en <see cref="Tercero.FichasJson"/> (dict ficha -&gt; dict campo -&gt; valor) indexados por
/// <see cref="FieldKey"/>. Calcado del patron ya probado de PipelineFieldDefinition (CUBOT.travels),
/// agrupando por ficha en vez de por etapa. Multi-tenant (filtro global por reflexion).
/// </summary>
public class TerceroFieldDefinition : TenantEntity
{
    /// <summary>Ficha a la que pertenece el campo: fiscal / comercial / cliente / proveedor / empleado.</summary>
    public string FichaKey { get; set; } = null!;

    /// <summary>Clave estable del campo (no cambia), p.ej. "tipo_de_persona".</summary>
    public string FieldKey { get; set; } = null!;

    public string Label { get; set; } = null!;
    public TerceroFieldType FieldType { get; set; } = TerceroFieldType.Text;

    /// <summary>Opciones para tipo Select, separadas por salto de linea.</summary>
    public string? Options { get; set; }

    /// <summary>
    /// Ancho del campo en la rejilla de 3 columnas del modal: 1 = pequena (1/3), 2 = media (2/3),
    /// 3 = grande (ancho completo). El servicio lo acota a ese rango.
    /// </summary>
    public int Column { get; set; } = 1;
    public int SortOrder { get; set; }

    /// <summary>
    /// Descripcion/contexto del campo: para que sirve. Se muestra como ayuda al usuario y queda
    /// disponible para que un MCP / agentes de IA entiendan y llenen el campo a futuro.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>Permite capturar varios valores en este campo. Se guardan como arreglo JSON.</summary>
    public bool AllowMultiple { get; set; }

    /// <summary>
    /// Solo para <see cref="TerceroFieldType.Calculated"/>: expresion a evaluar, con los campos
    /// referenciados entre llaves. Ej: <c>ROUND(({valor_base} + {flete}) * 1.19, 2)</c>. En terceros
    /// puede referenciar campos de CUALQUIER ficha, porque todos los valores viven en el mismo
    /// FichasJson del tercero. Ver ADR-0029.
    /// </summary>
    public string? Formula { get; set; }

    /// <summary>El campo se ofrece como filtro en el listado.</summary>
    public bool ShowInFilter { get; set; }

    /// <summary>
    /// FieldKey de un campo numerico de la misma ficha: este campo se repite tantas veces como diga
    /// su valor (ej. "acompanantes" -> repetir "nombre"). Null = no se repite.
    /// </summary>
    public string? RepeatWithFieldKey { get; set; }

    /// <summary>
    /// Marca los campos sembrados por defecto (del spec del prototipo). Permite distinguir los
    /// campos de sistema de los que agrega el tenant, y re-sembrar sin duplicar.
    /// </summary>
    public bool IsSystem { get; set; }
}
