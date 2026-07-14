using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Canal de transmision de una programacion (N por job). El origen no los modelaba explicitos;
/// el destino los normaliza (unico por job+canal). Tenant-scoped por herencia.
/// </summary>
public class ScheduledJobChannel : TenantEntity
{
    public Guid JobId { get; set; }
    public ScheduledJob Job { get; set; } = null!;

    public ScheduledJobChannelType Channel { get; set; }
}
