using CubotNails.Domain.Common;
using CubotNails.Domain.Enums;

namespace CubotNails.Domain.Entities;

/// <summary>Agencia turistica cliente del SaaS. Entidad global administrada por el Super Admin.</summary>
public class Tenant : BaseEntity
{
    public string Name { get; set; } = null!;
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public string? Country { get; set; }
    public string? Currency { get; set; }

    /// <summary>Ruta del logo de la agencia (subido por el cliente), p.ej. /uploads/tenant-{id}.png.</summary>
    public string? LogoUrl { get; set; }
    public TenantStatus Status { get; set; } = TenantStatus.Trial;
    public TenantKind Kind { get; set; } = TenantKind.Standard;
}
