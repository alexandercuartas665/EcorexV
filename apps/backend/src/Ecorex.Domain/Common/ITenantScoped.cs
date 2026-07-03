namespace Ecorex.Domain.Common;

/// <summary>
/// Marca una entidad como operativa de un tenant. El DbContext aplica un filtro global
/// de consulta por TenantId solo a las entidades que implementan esta interfaz.
/// OJO: tener una columna TenantId NO implica ser tenant-scoped; las entidades
/// administrativas del SaaS (suscripciones, pagos, logs) son globales.
/// </summary>
public interface ITenantScoped
{
    Guid TenantId { get; }
}
