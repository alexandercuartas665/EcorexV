using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Enlace N:N entre un FLUJO (por su <see cref="ProcessCode"/> estable, para sobrevivir al versionado
/// de <see cref="WorkflowDefinition"/>) y una etiqueta (<see cref="CardTag"/> con Scope=Flow).
/// Tenant-scoped (ADR-0097 Fase A).
/// </summary>
public class FlowTag : TenantEntity
{
    /// <summary>ProcessCode del flujo (identidad estable, no la version concreta).</summary>
    public string ProcessCode { get; set; } = null!;

    public Guid CardTagId { get; set; }
    public CardTag? CardTag { get; set; }
}
