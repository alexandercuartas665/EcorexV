using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Turno (cita) tomado sobre un <see cref="AgendaServicio"/> (ADR-0165 ola 4). Guarda la fecha y horas LOCALES del
/// tenant (para calcular cupos por bloque) y el instante UTC (regla 9). Ocupa uno o varios recursos a la vez
/// (<see cref="AgendaTurnoRecurso"/>). Cancelar no borra: cambia el estado y libera el cupo. Tenant-scoped +
/// soft-delete + concurrencia optimista.
/// </summary>
public sealed class AgendaTurno : TenantEntity, ISoftDeletable, IVersioned
{
    public Guid ServicioId { get; set; }
    public AgendaServicio? Servicio { get; set; }

    /// <summary>Fecha local del tenant.</summary>
    public DateOnly Fecha { get; set; }

    public TimeOnly HoraInicio { get; set; }

    public TimeOnly HoraFin { get; set; }

    /// <summary>Inicio en UTC (calculado con la zona horaria del tenant).</summary>
    public DateTimeOffset InicioUtc { get; set; }

    public DateTimeOffset FinUtc { get; set; }

    public AgendaTurnoEstado Estado { get; set; } = AgendaTurnoEstado.Reservado;

    public AgendaTurnoOrigen Origen { get; set; } = AgendaTurnoOrigen.Manual;

    /// <summary>Nombre de quien toma el turno. varchar(150).</summary>
    public string ClienteNombre { get; set; } = string.Empty;

    /// <summary>Telefono del cliente. varchar(40).</summary>
    public string? ClienteTelefono { get; set; }

    /// <summary>Contacto del Directorio, si se vinculo.</summary>
    public Guid? TerceroId { get; set; }

    /// <summary>Tarea de ECOREX creada para el turno (si el servicio tiene concepto).</summary>
    public Guid? TaskItemId { get; set; }

    /// <summary>Conversacion (WhatsApp/chat) desde la que el agente tomo el turno.</summary>
    public Guid? ConversationId { get; set; }

    /// <summary>Nota libre. varchar(500).</summary>
    public string? Nota { get; set; }

    /// <summary>Veces que se ha movido a otro horario.</summary>
    public int Reprogramaciones { get; set; }

    /// <summary>Bitacora legible del turno (reservado, reprogramado, cancelado...). Texto largo.</summary>
    public string? Historial { get; set; }

    public List<AgendaTurnoRecurso> Recursos { get; set; } = new();

    // --- IVersioned ---
    public long Version { get; set; }

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByTenantUserId { get; set; }
}

/// <summary>
/// Recurso ocupado por un turno (ADR-0165 ola 4). Repite la fecha y las horas del turno para consultar la ocupacion
/// por recurso sin cargar el turno. Se reemplaza al reprogramar.
/// </summary>
public sealed class AgendaTurnoRecurso : TenantEntity
{
    public Guid TurnoId { get; set; }
    public AgendaTurno? Turno { get; set; }

    public Guid RecursoId { get; set; }

    /// <summary>Nombre del requisito que cumple (p.ej. "Linea"). varchar(80).</summary>
    public string Requisito { get; set; } = string.Empty;

    public DateOnly Fecha { get; set; }

    public TimeOnly HoraInicio { get; set; }

    public TimeOnly HoraFin { get; set; }
}
