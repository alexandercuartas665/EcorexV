using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Paso (accion) de un <see cref="ContactWorkflow"/> (ADR-0056). Reemplaza TAR_WORKFLOW_PASOS del
/// legacy. Es un elemento de una LISTA ordenada por <see cref="Orden"/>. Solo el tipo
/// <see cref="ContactWorkflowStepType.Llamada"/> lleva campos propios, guardados en
/// <see cref="ParamsJson"/> (Comercial/Prioridad/Categoria/Subcategoria). TENANT-SCOPED.
/// </summary>
public class ContactWorkflowStep : TenantEntity
{
    public Guid ContactWorkflowId { get; set; }

    public ContactWorkflow? ContactWorkflow { get; set; }

    public ContactWorkflowStepType StepType { get; set; }

    /// <summary>Etiqueta visible del paso (por defecto el nombre de la accion).</summary>
    public string Label { get; set; } = null!;

    /// <summary>Orden secuencial dentro del workflow (0..n). Es una LISTA, no un grafo.</summary>
    public int Orden { get; set; }

    /// <summary>
    /// Parametros propios del tipo, como documento JSON (jsonb en PG, nvarchar(max) en SQL Server).
    /// Solo lo usa el tipo Llamada: { Comercial, Prioridad, Categoria, Subcategoria }. Null para el resto.
    /// </summary>
    public string? ParamsJson { get; set; }

    /// <summary>Ventanas de horario del paso (N). Cuelgan del paso (cascada al borrarlo).</summary>
    public List<ContactWorkflowSchedule> Schedules { get; set; } = new();
}
