using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Cita: entidad transaccional central del salon (Modelo de Datos seccion 6). TENANT-SCOPED.
/// Anti-overbooking por SOLAPAMIENTO: exclusion constraint GiST sobre el intervalo
/// [inicio, inicio + duracion + buffer) por (tenant, recurso, fecha) entre citas NO canceladas
/// (ver configuracion y migracion en Infrastructure). Una visita puede pasar por varios asesores (cadena).
/// </summary>
public class Appointment : TenantEntity
{
    public Guid ResourceId { get; set; }
    public DateOnly AppointmentDate { get; set; }
    public TimeOnly StartTime { get; set; }
    /// <summary>Suma de las duraciones de los servicios de la cita (por largo de cabello cuando aplica).</summary>
    public int DurationMinutes { get; set; }
    /// <summary>Margen reservado tras la cita; snapshot del recurso al reservar. Entra en el anti-solapamiento.</summary>
    public int BufferMinutes { get; set; }
    public Guid? ClientId { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Scheduled;
    public Punctuality Punctuality { get; set; } = Punctuality.Unknown;

    // Cadena multi-estacion (Modulo 2.10): misma visita en 2+ asesores.
    public Guid? ChainId { get; set; }
    public int? ChainSequence { get; set; }
    public int? ChainTotal { get; set; }

    public BookingChannel Channel { get; set; } = BookingChannel.Reception;
    public decimal? EstimatedValue { get; set; }
    public string? Notes { get; set; }

    /// <summary>Valores de los campos configurables de la cita (jsonb), indexados por FieldKey.</summary>
    public string? FieldValuesJson { get; set; }

    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    /// <summary>Trazabilidad: cita origen cuando esta nace de una reprogramacion.</summary>
    public Guid? RescheduledFromId { get; set; }
}
