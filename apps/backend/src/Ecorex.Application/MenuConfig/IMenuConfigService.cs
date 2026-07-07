namespace Ecorex.Application.MenuConfig;

/// <summary>
/// Servicio del menu configurable por perfil (Ola 1). Resuelve el arbol de menu de un usuario
/// (segun su vista asignada o la vista IsDefault del tenant), lista vistas, crea y clona vistas
/// (util para el seed y la Ola 2). Tenant-scoped por el filtro global. Resultados tipados.
/// </summary>
public interface IMenuConfigService
{
    /// <summary>
    /// Arbol de menu resuelto para un usuario de tenant. Si menuViewId es null (o la vista no
    /// existe / no tiene nodos visibles) cae a la vista IsDefault del tenant. Devuelve null si
    /// el tenant no tiene ninguna vista con nodos visibles.
    /// </summary>
    Task<ResolvedMenuDto?> GetMenuForTenantUserAsync(Guid tenantId, Guid? menuViewId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MenuViewDto>> ListViewsAsync(CancellationToken cancellationToken = default);

    Task<MenuConfigResult<MenuViewDto>> CreateViewAsync(
        string name, string? description = null, bool isDefault = false, int sortOrder = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clona una vista (todos sus nodos, conservando la jerarquia) con un nombre nuevo. La copia
    /// nunca es IsDefault. Util para el seed (derivar "Simple" de "Completo") y la Ola 2.
    /// </summary>
    Task<MenuConfigResult<MenuViewDto>> CloneViewAsync(
        Guid sourceViewId, string newName, CancellationToken cancellationToken = default);
}
