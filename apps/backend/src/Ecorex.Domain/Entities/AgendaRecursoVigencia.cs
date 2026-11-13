using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Agenda PROPIA de un recurso para un periodo (ADR-0165). Nace importando (copiando) una
/// <see cref="AgendaPlantilla"/>; <see cref="PlantillaOrigenId"/> es solo traza (borrar la plantilla no la toca).
/// Un recurso puede tener varias: para una fecha aplica la de <see cref="VigenteDesde"/> mas reciente que la
/// cubra (asi un "horario de temporada" se monta encima del normal). Tenant-scoped + soft-delete.
/// </summary>
public sealed class AgendaRecursoVigencia : TenantEntity, ISoftDeletable
{
    public Guid RecursoId { get; set; }
    public AgendaRecurso? Recurso { get; set; }

    /// <summary>Nombre visible (por defecto el de la plantilla origen). varchar(120).</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Primer dia en que aplica (fecha local del tenant).</summary>
    public DateOnly VigenteDesde { get; set; }

    /// <summary>Ultimo dia en que aplica (inclusive). null = sin fin.</summary>
    public DateOnly? VigenteHasta { get; set; }

    /// <summary>Plantilla de la que se importo (solo traza).</summary>
    public Guid? PlantillaOrigenId { get; set; }

    public List<AgendaRecursoFranja> Franjas { get; set; } = new();

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByTenantUserId { get; set; }
}

/// <summary>Franja de la agenda propia de un recurso (misma forma que <see cref="AgendaPlantillaFranja"/>).</summary>
public sealed class AgendaRecursoFranja : TenantEntity
{
    public Guid VigenciaId { get; set; }
    public AgendaRecursoVigencia? Vigencia { get; set; }

    /// <summary>Dia de la semana (System.DayOfWeek), persistido como int.</summary>
    public DayOfWeek DiaSemana { get; set; }

    public TimeOnly HoraInicio { get; set; }

    public TimeOnly HoraFin { get; set; }

    public int IntervaloMinutos { get; set; } = 30;

    public int Cupo { get; set; } = 1;
}
