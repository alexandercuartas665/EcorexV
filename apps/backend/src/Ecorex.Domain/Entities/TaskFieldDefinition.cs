using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Definicion de un campo configurable de una TAREA, agrupado POR TABLERO
/// (<see cref="BoardId"/>: el tablero de actividades donde vive la tarea). Entidad
/// TENANT-SCOPED: cada tenant agrega/quita los campos que quiere capturar en el detalle de
/// las tareas de UN tablero, sin tocar codigo, y esos campos SOLO existen en ese tablero (no
/// se ven en otro). Los VALORES por tarea se guardan en <see cref="TaskItem.CustomFieldsJson"/>
/// (dict FieldKey -&gt; valor, un solo nivel: la tarea ya sabe su tablero). Calcado del patron
/// probado de <see cref="ItemFieldDefinition"/>, agrupando por tablero en vez de por tipo de item.
/// Reusa <see cref="TerceroFieldType"/> para no duplicar la logica de tipos (incluye Calculated
/// y Lookup del Contenedor de datos).
/// </summary>
public class TaskFieldDefinition : TenantEntity
{
    /// <summary>Tablero de actividades dueno del campo. Los campos son POR tablero.</summary>
    public Guid BoardId { get; set; }
    public TaskBoard? Board { get; set; }

    /// <summary>Clave estable del campo (slug). Unica por (tenant, tablero).</summary>
    public string FieldKey { get; set; } = null!;

    /// <summary>Etiqueta visible.</summary>
    public string Label { get; set; } = null!;

    public TerceroFieldType FieldType { get; set; } = TerceroFieldType.Text;

    /// <summary>Opciones para el tipo Select (una por linea) o config JSON del tipo Lookup.</summary>
    public string? Options { get; set; }

    /// <summary>
    /// Ancho del campo en la rejilla de 3 columnas del modal: 1 = pequena (1/3), 2 = media (2/3),
    /// 3 = grande (ancho completo). El servicio lo acota a ese rango.
    /// </summary>
    public int Column { get; set; } = 1;
    public int SortOrder { get; set; }

    /// <summary>Ayuda/contexto para quien captura el dato (y para agentes de IA).</summary>
    public string? Description { get; set; }

    /// <summary>Permite capturar varios valores en este campo. Se guardan como arreglo JSON.</summary>
    public bool AllowMultiple { get; set; }

    /// <summary>
    /// Solo para <see cref="TerceroFieldType.Calculated"/>: expresion a evaluar, con los campos
    /// referenciados entre llaves. Ej: <c>{horas} * {tarifa}</c>. En tareas solo puede referenciar
    /// campos del MISMO tablero: una tarea vive en un unico tablero, asi que los de otro tablero no
    /// existirian al evaluar. Ver ADR-0029.
    /// </summary>
    public string? Formula { get; set; }

    /// <summary>
    /// FieldKey de un campo numerico del mismo tablero: este campo se repite tantas veces como diga
    /// su valor. Null = no se repite. (Reservado; la captura repetible aun no se cablea en la UI.)
    /// </summary>
    public string? RepeatWithFieldKey { get; set; }

    /// <summary>Marca los campos sembrados por defecto, para distinguirlos de los del tenant.</summary>
    public bool IsSystem { get; set; }
}
