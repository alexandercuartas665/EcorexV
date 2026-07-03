using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Definicion de un campo configurable de una etapa del embudo (modulo 2.1). Entidad TENANT-SCOPED.
/// Cada agencia puede agregar/quitar campos y cambiarles el tipo; los valores por lead se guardan
/// en Lead.FieldValuesJson indexados por FieldKey.
/// </summary>
public class PipelineFieldDefinition : TenantEntity
{
    public Guid StageId { get; set; }
    public PipelineStage? Stage { get; set; }

    /// <summary>Clave estable del campo (no cambia), p.ej. "aerolinea".</summary>
    public string FieldKey { get; set; } = null!;

    public string Label { get; set; } = null!;
    public PipelineFieldType FieldType { get; set; } = PipelineFieldType.Text;

    /// <summary>Columna del layout en el modal (1 = angosta, 2 = ancha/full).</summary>
    public int Column { get; set; } = 1;
    public int SortOrder { get; set; }

    /// <summary>Opciones para tipo Select, separadas por salto de linea.</summary>
    public string? Options { get; set; }

    /// <summary>
    /// Descripcion/contexto del campo: para que sirve. Se muestra como ayuda al asesor y queda
    /// disponible para que un MCP / agentes de IA entiendan y llenen el campo a futuro.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>Permite capturar varios valores en este campo (p.ej. multiples telefonos). Se guardan como arreglo JSON.</summary>
    public bool AllowMultiple { get; set; }

    /// <summary>Si es true, el campo aparece como filtro en el tablero: un chip por cada valor distinto presente.</summary>
    public bool ShowInFilter { get; set; }

    /// <summary>
    /// Solo para campos AllowMultiple: cada valor lleva ademas un texto de detalle (descripcion).
    /// Se guarda como arreglo JSON de objetos {"d":detalle,"v":valor}. El detalle va primero.
    /// </summary>
    public bool MultiWithDetail { get; set; }

    /// <summary>
    /// Solo para FieldType=Total: lista de FieldKeys (separados por coma) de los campos numericos/moneda
    /// de la misma etapa que se suman para calcular el total. Los campos multiples suman todos sus registros.
    /// </summary>
    public string? TotalSourceKeys { get; set; }

    /// <summary>
    /// Si se indica el FieldKey de un campo numerico de la misma etapa, este campo se repite N veces
    /// segun el valor de ese campo (p.ej. "edades" se repite tantas veces como diga "ninos"). Los
    /// valores se guardan como arreglo JSON.
    /// </summary>
    public string? RepeatWithFieldKey { get; set; }
}
