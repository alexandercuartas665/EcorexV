using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Mensaje del chat inline de una cita (Modulo 2.11). TENANT-SCOPED. Cada cita tiene su propio hilo;
/// al reprogramar, la nueva cita hereda los mensajes de la cancelada para no perder contexto.
/// </summary>
public class AppointmentMessage : TenantEntity
{
    public Guid AppointmentId { get; set; }
    public MessageDirection Direction { get; set; }
    public string Body { get; set; } = null!;
    public DateTimeOffset SentAt { get; set; }
    public Guid? SentByTenantUserId { get; set; }
    public Guid? SourceTemplateId { get; set; }
}
