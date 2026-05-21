using CubotTravels.Domain.Common;
using CubotTravels.Domain.Enums;

namespace CubotTravels.Domain.Entities;

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
    public LeadStatus Status { get; set; } = LeadStatus.Open;
    public string? LossReason { get; set; }
    public DateTimeOffset StageChangedAt { get; set; }

    /// <summary>Valores de los campos configurables (jsonb), indexados por FieldKey.</summary>
    public string? FieldValuesJson { get; set; }
}
