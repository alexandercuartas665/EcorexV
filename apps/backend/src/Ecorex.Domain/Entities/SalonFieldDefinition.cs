using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Definicion de un campo configurable del salon (capa 2). Entidad TENANT-SCOPED. Permite que cada
/// salon agregue los campos que quiere capturar en sus citas y/o en la ficha de sus clientes, sin
/// tocar codigo. Los VALORES se guardan como JSON (FieldValuesJson) en Appointment o Client, indexados
/// por FieldKey. No usa etapas/kanban (es independiente del pipeline del CRM).
/// </summary>
public class SalonFieldDefinition : TenantEntity
{
    /// <summary>A que entidad aplica el campo: la cita o el cliente.</summary>
    public SalonFieldScope Scope { get; set; }

    /// <summary>Clave estable del campo (slug). Unica por (tenant, scope).</summary>
    public string FieldKey { get; set; } = null!;

    /// <summary>Etiqueta visible.</summary>
    public string Label { get; set; } = null!;

    public SalonFieldType FieldType { get; set; } = SalonFieldType.Text;

    /// <summary>Opciones para el tipo Select, una por linea.</summary>
    public string? Options { get; set; }

    /// <summary>Ayuda/contexto para quien captura el dato.</summary>
    public string? Description { get; set; }

    /// <summary>Layout en el formulario: 1 (media) o 2 (completa).</summary>
    public int Column { get; set; } = 1;

    public int SortOrder { get; set; }

    /// <summary>Si el dato es obligatorio al guardar.</summary>
    public bool IsRequired { get; set; }

    /// <summary>Si el campo se muestra (compacto) en las tarjetas del tablero del dia. Solo aplica a citas.</summary>
    public bool ShowOnBoard { get; set; }
}
