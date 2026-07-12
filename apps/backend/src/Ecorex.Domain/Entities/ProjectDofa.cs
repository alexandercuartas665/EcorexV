using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Entrada del analisis DOFA/SWOT de un proyecto (legacy PROYECTOS_DOFA): un texto en uno de los
/// cuatro cuadrantes (Fortaleza / Oportunidad / Debilidad / Amenaza). TENANT-SCOPED.
/// </summary>
public class ProjectDofa : TenantEntity
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }

    public DofaQuadrant Quadrant { get; set; }

    public string Text { get; set; } = null!;

    public int SortOrder { get; set; }
}
