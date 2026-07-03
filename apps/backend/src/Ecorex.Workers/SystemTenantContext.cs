using Ecorex.Application.Common;

namespace Ecorex.Workers;

/// <summary>
/// ITenantContext para procesos de fondo: no hay usuario ni tenant en sesion. Los workers operan
/// sobre entidades globales (suscripciones, pagos) y, cuando tocan datos tenant-scoped, lo hacen
/// con IgnoreQueryFilters y TenantId explicito.
/// </summary>
public sealed class SystemTenantContext : ITenantContext
{
    public Guid? TenantId => null;
    public Guid? UserId => null;
}
