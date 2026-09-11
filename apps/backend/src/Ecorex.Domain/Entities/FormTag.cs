using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Enlace N:N entre un FORMULARIO (por su <see cref="FormCode"/> estable) y una etiqueta
/// (<see cref="CardTag"/> con Scope=Form). Tenant-scoped (ADR-0097 Fase A).
/// </summary>
public class FormTag : TenantEntity
{
    /// <summary>Code del formulario (identidad estable, no la version concreta).</summary>
    public string FormCode { get; set; } = null!;

    public Guid CardTagId { get; set; }
    public CardTag? CardTag { get; set; }
}
