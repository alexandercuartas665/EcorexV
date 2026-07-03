using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Oportunidad comercial dentro del embudo (modulo 2.2). Entidad TENANT-SCOPED.
/// </summary>
public class Lead : TenantEntity
{
    public string ContactName { get; set; } = null!;
    public string? ContactPhone { get; set; }
    public string? Destination { get; set; }
    public decimal? EstimatedValue { get; set; }
    public string? Currency { get; set; }

    public Guid StageId { get; set; }
    public PipelineStage? Stage { get; set; }

    public Guid? AssignedToTenantUserId { get; set; }

    /// <summary>Unidad / canal de negocio del lead (B2B, detal, cursos, asesoria de imagen). Opcional.</summary>
    public Guid? BusinessUnitId { get; set; }

    public LeadStatus Status { get; set; } = LeadStatus.Open;
    public string? LossReason { get; set; }
    public DateTimeOffset StageChangedAt { get; set; }

    /// <summary>Valores de los campos configurables (jsonb), indexados por FieldKey.</summary>
    public string? FieldValuesJson { get; set; }

    // ===== Historial (archivado) =====
    /// <summary>Fecha en que se envio a historial. Null = activo en el embudo.</summary>
    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>Motivo por el que se envio a historial (ej. ya compro, no interesado, duplicado).</summary>
    public string? ArchiveReason { get; set; }

    /// <summary>Observacion del asesor al enviar a historial.</summary>
    public string? ArchiveNote { get; set; }

    /// <summary>Nombre del usuario que lo envio a historial (snapshot).</summary>
    public string? ArchivedByName { get; set; }
}
