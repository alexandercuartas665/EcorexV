using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Novedad que cierra la agenda de un recurso en un rango de fechas (ADR-0165): vacaciones, incapacidad,
/// mantenimiento... Si <see cref="HoraDesde"/>/<see cref="HoraHasta"/> son null cubre los dias completos; si no,
/// solo ese tramo horario de cada dia. Rango inclusivo [FechaDesde, FechaHasta]. Tenant-scoped + soft-delete.
/// </summary>
public sealed class AgendaNovedad : TenantEntity, ISoftDeletable
{
    public Guid RecursoId { get; set; }
    public AgendaRecurso? Recurso { get; set; }

    public AgendaNovedadTipo Tipo { get; set; } = AgendaNovedadTipo.Vacaciones;

    public DateOnly FechaDesde { get; set; }

    public DateOnly FechaHasta { get; set; }

    public TimeOnly? HoraDesde { get; set; }

    public TimeOnly? HoraHasta { get; set; }

    /// <summary>Motivo opcional. varchar(300).</summary>
    public string? Nota { get; set; }

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByTenantUserId { get; set; }
}
