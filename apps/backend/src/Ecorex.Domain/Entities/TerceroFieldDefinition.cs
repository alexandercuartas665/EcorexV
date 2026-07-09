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

    /// <summary>Columna del layout en el modal (1 = angosta, 2 = ancha/full).</summary>
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
    /// Marca los campos sembrados por defecto (del spec del prototipo). Permite distinguir los
    /// campos de sistema de los que agrega el tenant, y re-sembrar sin duplicar.
    /// </summary>
    public bool IsSystem { get; set; }
}
