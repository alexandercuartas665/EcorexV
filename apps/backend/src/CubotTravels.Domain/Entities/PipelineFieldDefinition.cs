using CubotTravels.Domain.Common;
using CubotTravels.Domain.Enums;

namespace CubotTravels.Domain.Entities;

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
}
