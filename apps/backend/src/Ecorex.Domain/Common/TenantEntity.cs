namespace Ecorex.Domain.Common;

/// <summary>
/// Base de las entidades operativas que pertenecen a un tenant (agencia).
/// Llevan TenantId obligatorio y reciben el filtro global de consulta.
/// </summary>
public abstract class TenantEntity : BaseEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
}
