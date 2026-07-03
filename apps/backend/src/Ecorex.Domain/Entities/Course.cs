using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Curso eventual del salon (Capa 2 - configuracion). Se parametriza el dia, el cupo de personas,
/// el detalle y el valor. Las inscripciones (CourseRegistration) registran a las personas y su pago.
/// TENANT-SCOPED.
/// </summary>
public class Course : TenantEntity
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    /// <summary>Dia del curso (local del salon).</summary>
    public DateOnly Date { get; set; }
    /// <summary>Hora de inicio (opcional).</summary>
    public TimeOnly? StartTime { get; set; }
    /// <summary>Cupo maximo de personas.</summary>
    public int Capacity { get; set; }
    /// <summary>Valor del curso.</summary>
    public decimal? Price { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Archivado: el curso ya paso o se retiro; sale de la vista principal y el agente no lo ofrece.</summary>
    public bool IsArchived { get; set; }
}

/// <summary>Inscripcion de una persona a un curso, con su estado de pago. TENANT-SCOPED.</summary>
public class CourseRegistration : TenantEntity
{
    public Guid CourseId { get; set; }
    public Course? Course { get; set; }
    public string PersonName { get; set; } = null!;
    public string? Phone { get; set; }
    /// <summary>Estado de pago: pagado o no.</summary>
    public bool IsPaid { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
}
