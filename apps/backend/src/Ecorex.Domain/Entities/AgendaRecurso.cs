using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Recurso agendable (ADR-0165): lo que tiene agenda. Generaliza al "profesional" del proyecto hermano Visal:
/// una persona, un grupo, un espacio, una maquina, un vehiculo o un punto de atencion. Su agenda propia vive en
/// <see cref="AgendaRecursoVigencia"/> (copias editables de una <see cref="AgendaPlantilla"/>) y sus cierres en
/// <see cref="AgendaNovedad"/>. Tenant-scoped + soft-delete + concurrencia optimista.
/// </summary>
public sealed class AgendaRecurso : TenantEntity, ISoftDeletable, IVersioned
{
    /// <summary>Nombre visible. varchar(150).</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Descripcion opcional para el operador. varchar(500).</summary>
    public string? Descripcion { get; set; }

    public Guid TipoRecursoId { get; set; }
    public AgendaTipoRecurso? TipoRecurso { get; set; }

    /// <summary>Sede (Entidad de tipo Sede) donde opera. Opcional.</summary>
    public Guid? SedeId { get; set; }

    /// <summary>Usuario del tenant al que apunta, cuando el tipo se vincula a Usuario.</summary>
    public Guid? TenantUserId { get; set; }

    /// <summary>Si el agente de IA podra reservar turnos en este recurso (lo usa la ola de turnos).</summary>
    public bool AgenteHabilitado { get; set; }

    public bool Activo { get; set; } = true;

    public List<AgendaRecursoVigencia> Vigencias { get; set; } = new();

    // --- IVersioned ---
    public long Version { get; set; }

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByTenantUserId { get; set; }
}
