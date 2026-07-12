using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Rubro del presupuesto/costos de un proyecto (legacy PROYECTOS_PRESUPUESTO + PROYECTOS_COS unificados):
/// un concepto con monto PRESUPUESTADO (planned) y monto REAL ejecutado (actual). TENANT-SCOPED.
/// </summary>
public class ProjectBudgetItem : TenantEntity
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>Categoria/rubro (ej. Materiales, Mano de obra, Servicios). Opcional.</summary>
    public string? Category { get; set; }

    /// <summary>Monto presupuestado (planificado).</summary>
    public decimal PlannedAmount { get; set; }

    /// <summary>Monto real ejecutado (costo).</summary>
    public decimal ActualAmount { get; set; }

    public string? Notes { get; set; }

    public int SortOrder { get; set; }
}
