namespace Ecorex.Application.MenuConfig;

/// <summary>
/// Aprovisionamiento del MENU de un tenant.
///
/// Bug que corrige: el menu solo se sembraba para el tenant DEMO (seeder), asi que un cliente creado
/// por el alta (PlatformAdmin) o por el onboarding nacia SIN NINGUNA vista de menu y sus usuarios no
/// veian nada. Ahora ambos caminos de alta siembran la vista "Completo" (por defecto, arbol canonico).
///
/// La implementacion vive en Infrastructure (DatabaseSeeder), que es la duena del arbol canonico:
/// asi el demo y el alta comparten la MISMA definicion y no derivan.
/// </summary>
public interface IMenuProvisioningService
{
    /// <summary>
    /// Siembra la vista "Completo" (IsDefault) con el arbol canonico si el tenant aun no tiene
    /// NINGUNA vista de menu. Idempotente: si ya tiene vistas, no hace nada.
    /// </summary>
    Task EnsureDefaultMenuAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
