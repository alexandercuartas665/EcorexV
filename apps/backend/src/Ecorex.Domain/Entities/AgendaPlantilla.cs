using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Agenda BASE del tenant (ADR-0165): plantilla semanal reutilizable, independiente de recursos y servicios.
/// Se IMPORTA (copia) a cada recurso como una <see cref="AgendaRecursoVigencia"/>; despues la copia se edita
/// sin tocar la plantilla. Ej: "Jornada oficina L-V", "Turnos de planta 2x8". Tenant-scoped + soft-delete +
/// concurrencia optimista.
/// </summary>
public sealed class AgendaPlantilla : TenantEntity, ISoftDeletable, IVersioned
{
    /// <summary>Nombre visible. Unico por tenant entre los no borrados. varchar(120).</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Descripcion opcional. varchar(300).</summary>
    public string? Descripcion { get; set; }

    /// <summary>Inactiva = no se ofrece para importar, pero se conserva.</summary>
    public bool Activa { get; set; } = true;

    public List<AgendaPlantillaFranja> Franjas { get; set; } = new();

    // --- IVersioned ---
    public long Version { get; set; }

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByTenantUserId { get; set; }
}

/// <summary>
/// Franja recurrente de una agenda base para un dia de la semana (ADR-0165). Un dia puede tener varias
/// (manana + tarde). El intervalo es el tamano del bloque; el cupo es cuantos turnos caben a la vez en cada
/// bloque (aforo de una sala, asesores de una linea). Cupos de la franja = bloques * cupo.
/// </summary>
public sealed class AgendaPlantillaFranja : TenantEntity
{
    public Guid PlantillaId { get; set; }
    public AgendaPlantilla? Plantilla { get; set; }

    /// <summary>Dia de la semana (System.DayOfWeek: 0 = domingo ... 6 = sabado), persistido como int.</summary>
    public DayOfWeek DiaSemana { get; set; }

    public TimeOnly HoraInicio { get; set; }

    public TimeOnly HoraFin { get; set; }

    /// <summary>Tamano del bloque en minutos.</summary>
    public int IntervaloMinutos { get; set; } = 30;

    /// <summary>Turnos simultaneos por bloque (1 = uno a la vez).</summary>
    public int Cupo { get; set; } = 1;
}
