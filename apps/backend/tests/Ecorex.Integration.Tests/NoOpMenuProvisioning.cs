using Ecorex.Application.MenuConfig;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Doble no-op de <see cref="IMenuProvisioningService"/> para los tests que construyen
/// <c>TenantAdminService</c> a mano y NO ejercitan el aprovisionamiento del menu (perfil de
/// empresa, cambio de estado, listado de usuarios).
///
/// OJO: no usar en un test que verifique que un tenant NUEVO nace con su vista de menu; ahi hay
/// que inyectar la implementacion real (<c>DatabaseSeeder</c>), o el test pasaria en falso.
/// </summary>
public sealed class NoOpMenuProvisioning : IMenuProvisioningService
{
    public Task EnsureDefaultMenuAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
