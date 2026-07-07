namespace Ecorex.Application.Roles;

/// <summary>
/// Gestion de roles de permisos dinamicos del tenant activo (Ola B1, ADR-0032). Tenant-scoped
/// (filtro global + estampado en alta), auditado y transaccional en las operaciones multi-tabla.
/// La APLICACION del set efectivo en el backend (enforcement) es Ola B2: aqui solo se define la
/// matriz y se deja lista la resolucion de permisos efectivos (ResolveEffectivePermissionsAsync).
/// </summary>
public interface IRolService
{
    Task<IReadOnlyList<RolDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<RolDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Crea (id null) o edita un rol. No permite duplicar nombre; el rol de sistema no se renombra.</summary>
    Task<RolResult<RolDto>> SaveAsync(
        Guid? id, string name, string? description, bool isActive, Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Borra un rol. Bloquea (Invalid) si es de sistema o si tiene usuarios asignados.</summary>
    Task<RolResult<bool>> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Guarda la matriz de permisos del rol: borra e reinserta solo las filas con algun flag. Transaccional.</summary>
    Task<RolResult<bool>> SavePermisosAsync(
        Guid rolId, IReadOnlyList<ModulePermissionDto> permisos, Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Catalogo de modulos de la matriz, DERIVADO de los MenuNode Item con State=Ready de la vista
    /// IsDefault del tenant (Key=Route, Label=Name, Grupo=nombre de la Section padre). Cae a un
    /// catalogo minimo si el tenant aun no tiene menu configurado.
    /// </summary>
    Task<IReadOnlyList<ModuloInfo>> GetModuleCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Permisos efectivos del usuario (para B2 y la UI): Owner/Admin -> AllowAll; con RolId -> set
    /// del rol; sin rol -> vacio. platformUserId = PlatformUser.Id (resuelve el TenantUser del tenant).
    /// </summary>
    Task<EffectivePermissions> ResolveEffectivePermissionsAsync(
        Guid platformUserId, CancellationToken cancellationToken = default);

    /// <summary>Asigna (o desasigna con rolId null) el rol de permisos de un usuario del tenant. Auditado.</summary>
    Task<RolResult<bool>> AssignRoleToUserAsync(
        Guid tenantUserId, Guid? rolId, Guid actorUserId, CancellationToken cancellationToken = default);
}
