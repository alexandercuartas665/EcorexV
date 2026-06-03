using CubotNails.Domain.Common;
using CubotNails.Domain.Enums;

namespace CubotNails.Domain.Entities;

/// <summary>
/// Linea/instancia WhatsApp de un tenant (modulo 1.4). Entidad TENANT-SCOPED. La conexion
/// real (QR, sesion) se gestionara mediante el Evolution Connector en una fase posterior;
/// aqui se modela el ciclo de vida, el estado y la asignacion operativa a un asesor.
/// </summary>
public class WhatsAppLine : TenantEntity
{
    public string InstanceName { get; set; } = null!;
    public string? PhoneNumber { get; set; }
    public WhatsAppLineStatus Status { get; set; } = WhatsAppLineStatus.Created;
    public Guid? AssignedToTenantUserId { get; set; }
    public DateTimeOffset? LastConnectedAt { get; set; }
    public DateTimeOffset? LastStatusAt { get; set; }
}
