using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Registro de tiempo trabajado en una tarea (por cronometro o manual). TENANT-SCOPED.
/// Seconds valida rango (0, 86400] en el servicio.
/// </summary>
public class TaskWorkLog : TenantEntity
{
    public Guid TaskItemId { get; set; }
    public TaskItem? TaskItem { get; set; }

    /// <summary>Usuario del tenant que registro el tiempo.</summary>
    public Guid TenantUserId { get; set; }
    public TenantUser? TenantUser { get; set; }

    /// <summary>Segundos trabajados: mayor que 0 y hasta 86400 (24h).</summary>
    public int Seconds { get; set; }

    public string? Note { get; set; }

    public WorkLogKind Kind { get; set; } = WorkLogKind.Manual;

    /// <summary>Momento al que corresponde el trabajo registrado.</summary>
    public DateTimeOffset LoggedAt { get; set; }
}
