using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Servicio agendable (ADR-0165 ola 3): lo que se reserva. Define cuanto dura, con cuanta anticipacion se puede
/// tomar y que recursos ocupa A LA VEZ mediante sus <see cref="AgendaServicioRequisito"/> (p.ej. una linea de
/// revision + un inspector). Tenant-scoped + soft-delete + concurrencia optimista.
/// </summary>
public sealed class AgendaServicio : TenantEntity, ISoftDeletable, IVersioned
{
    /// <summary>Nombre visible. varchar(150).</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Descripcion opcional. varchar(500).</summary>
    public string? Descripcion { get; set; }

    /// <summary>Duracion del servicio en minutos (5 a 720).</summary>
    public int DuracionMinutos { get; set; } = 30;

    /// <summary>Minimo de minutos entre "ahora" y el inicio del turno (0 = se puede tomar ya).</summary>
    public int AnticipacionMinMinutos { get; set; }

    /// <summary>Maximo de dias hacia adelante en que se puede reservar.</summary>
    public int AnticipacionMaxDias { get; set; } = 60;

    /// <summary>Si el agente de IA puede reservar este servicio (herramientas del grupo "agendas").</summary>
    public bool AgentePuedeReservar { get; set; }

    /// <summary>
    /// Concepto (ActividadSubcategoria) de la TAREA que nace con cada turno: hereda su tablero, columna, flujo y
    /// formulario. null = el turno no crea tarea.
    /// </summary>
    public Guid? ConceptoId { get; set; }

    public bool Activo { get; set; } = true;

    public List<AgendaServicioRequisito> Requisitos { get; set; } = new();

    // --- IVersioned ---
    public long Version { get; set; }

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByTenantUserId { get; set; }
}

/// <summary>
/// Requisito de un servicio (ADR-0165 ola 3): "un recurso de este grupo". Al reservar, cada requisito se cumple
/// con UNO de los recursos elegibles que este libre durante todo el turno. Los recursos elegibles se guardan como
/// lista JSON de ids (se validan contra el tenant al guardar y al reservar).
/// </summary>
public sealed class AgendaServicioRequisito : TenantEntity
{
    public Guid ServicioId { get; set; }
    public AgendaServicio? Servicio { get; set; }

    /// <summary>Nombre del requisito (p.ej. "Linea", "Inspector", "Medico"). varchar(80).</summary>
    public string Nombre { get; set; } = string.Empty;

    public int Orden { get; set; }

    /// <summary>Ids de los recursos que pueden cumplir el requisito, en JSON.</summary>
    public string RecursoIdsJson { get; set; } = "[]";
}
