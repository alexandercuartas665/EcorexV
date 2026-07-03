using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Etapa configurable del embudo comercial de un tenant (modulo 2.1). Entidad TENANT-SCOPED.
/// IsClosedWon/IsClosedLost marcan etapas terminales que cierran el lead.
/// </summary>
public class PipelineStage : TenantEntity
{
    public string Name { get; set; } = null!;
    public int SortOrder { get; set; }
    public bool IsClosedWon { get; set; }
    public bool IsClosedLost { get; set; }
}
