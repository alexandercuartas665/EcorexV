using Ecorex.Domain.Enums;

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

    // ===== Ola 2: edicion de vistas y nodos (editor "Administrador de Menu") =====

    /// <summary>Renombra / redescribe una vista.</summary>
    Task<MenuConfigResult<MenuViewDto>> UpdateViewAsync(
        Guid viewId, string name, string? description, CancellationToken cancellationToken = default);

    /// <summary>
    /// Borra una vista y (en cascada) todos sus nodos, en transaccion. PROHIBIDO borrar la vista
    /// IsDefault (-> Invalid): el tenant siempre debe tener una vista por defecto.
    /// </summary>
    Task<MenuConfigResult<bool>> DeleteViewAsync(Guid viewId, CancellationToken cancellationToken = default);

    /// <summary>Marca una vista como IsDefault y desmarca las demas del tenant, en transaccion.</summary>
    Task<MenuConfigResult<bool>> SetDefaultViewAsync(Guid viewId, CancellationToken cancellationToken = default);

    /// <summary>Arbol COMPLETO de una vista (incluye invisibles) para el editor.</summary>
    Task<MenuConfigResult<MenuViewTreeDto>> GetViewTreeAsync(Guid viewId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea un nodo hijo (o raiz si parentId es null) al final del orden de sus hermanos. Valida
    /// coherencia de Kind respecto del padre (ej. una Section no cuelga de un Item).
    /// </summary>
    Task<MenuConfigResult<MenuEditorNodeDto>> CreateNodeAsync(
        Guid viewId, Guid? parentId, MenuNodeKind kind, string name,
        string? iconKey = null, string? legacyCode = null, string? route = null,
        string? description = null, string? helpText = null, MenuNodeState state = MenuNodeState.Ready,
        CancellationToken cancellationToken = default);

    /// <summary>Actualiza los campos editables de un nodo (los null en el DTO no se tocan).</summary>
    Task<MenuConfigResult<MenuEditorNodeDto>> UpdateNodeAsync(
        Guid nodeId, MenuNodeEditDto edit, CancellationToken cancellationToken = default);

    /// <summary>Alterna la visibilidad de un nodo (ojo del prototipo).</summary>
    Task<MenuConfigResult<MenuEditorNodeDto>> ToggleNodeVisibilityAsync(
        Guid nodeId, CancellationToken cancellationToken = default);

    /// <summary>Fija el estado funcional (Ready/InDevelopment/Disabled) de un nodo.</summary>
    Task<MenuConfigResult<MenuEditorNodeDto>> SetNodeStateAsync(
        Guid nodeId, MenuNodeState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reordena o reparenta un nodo. Valida que no se cree ciclo (el nuevo padre no puede ser el
    /// propio nodo ni un descendiente) y que el Kind sea coherente con el nuevo padre.
    /// </summary>
    Task<MenuConfigResult<bool>> MoveNodeAsync(
        Guid nodeId, Guid? newParentId, int newSortOrder, CancellationToken cancellationToken = default);

    /// <summary>Borra un nodo y toda su descendencia (cascada en transaccion).</summary>
    Task<MenuConfigResult<bool>> DeleteNodeAsync(Guid nodeId, CancellationToken cancellationToken = default);

    // ===== Ola 2: asignacion de usuarios a vistas =====

    /// <summary>Asigna (o desasigna con null) la vista de un usuario del tenant.</summary>
    Task<MenuConfigResult<bool>> AssignUserToViewAsync(
        Guid tenantUserId, Guid? viewId, CancellationToken cancellationToken = default);

    /// <summary>Usuarios del tenant con su vista asignada (para la pantalla de asignacion).</summary>
    Task<IReadOnlyList<TenantUserViewDto>> ListTenantUsersWithViewAsync(CancellationToken cancellationToken = default);

    // ===== Ola 2: export / import portable (System.Text.Json) =====

    /// <summary>Serializa una vista a un documento portable (sin ids de BD).</summary>
    Task<MenuConfigResult<MenuExportDocument>> ExportViewAsync(Guid viewId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea una vista nueva (nunca IsDefault) desde un documento portable, con nombre newName.
    /// Reconstruye toda la jerarquia. Transaccional.
    /// </summary>
    Task<MenuConfigResult<MenuViewDto>> ImportViewAsync(
        string json, string newName, CancellationToken cancellationToken = default);
}
