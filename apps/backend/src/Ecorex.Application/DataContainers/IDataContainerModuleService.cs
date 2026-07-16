namespace Ecorex.Application.DataContainers;

/// <summary>Estado de publicacion de una tabla como modulo del menu.</summary>
/// <param name="ModuleRoute">Ruta/clave del modulo; sobrevive a la despublicacion (ver el servicio).</param>
/// <param name="IsPublished">True solo si hoy tiene nodo de menu vivo.</param>
public sealed record DataContainerModuleDto(
    Guid ContainerId,
    string ContainerName,
    string? ModuleRoute,
    Guid? MenuNodeId,
    bool IsPublished,
    string? ModuleIcon,
    IReadOnlyList<Guid> ListColumnIds,
    IReadOnlyList<Guid> FilterColumnIds);

/// <summary>Datos para publicar (o re-publicar) una tabla como modulo.</summary>
public sealed record PublishContainerRequest(
    Guid ContainerId,
    Guid MenuViewId,
    Guid? ParentNodeId,
    string? Icon = null,
    IReadOnlyList<Guid>? ListColumnIds = null,
    IReadOnlyList<Guid>? FilterColumnIds = null);

public enum ModulePublishStatus { Ok = 0, NotFound, Invalid, Conflict }

public sealed record ModulePublishResult<T>(ModulePublishStatus Status, T? Value, string? Error)
{
    public bool IsOk => Status == ModulePublishStatus.Ok;
    public static ModulePublishResult<T> Ok(T value) => new(ModulePublishStatus.Ok, value, null);
    public static ModulePublishResult<T> NotFound(string error) => new(ModulePublishStatus.NotFound, default, error);
    public static ModulePublishResult<T> Invalid(string error) => new(ModulePublishStatus.Invalid, default, error);
    public static ModulePublishResult<T> Conflict(string error) => new(ModulePublishStatus.Conflict, default, error);
}

/// <summary>
/// Publicacion de una tabla del Contenedor de datos como MODULO del menu, para que el usuario final
/// gestione sus registros sin entrar al configurador. Espeja el patron ya probado de los formularios
/// (FormDefinition.IsModule + ModuleMenuNodeId -> MenuNode), y de ahi salen gratis dos cosas:
/// la matriz de "Roles y permisos" lista el modulo sola (RolService.GetModuleCatalogAsync deriva el
/// catalogo del MENU, con la clave = Route) y MenuPermissionFilter oculta el item a quien no tenga Ver.
///
/// Corrige, a proposito, tres defectos del patron original:
/// 1. La RUTA es INMUTABLE: se congela al publicar y NO cambia al renombrar la tabla. Es la clave del
///    modulo, asi que cambiarla dejaria huerfanos los permisos que los roles ya asignaron (los
///    formularios tienen justo ese bug al renombrar su Code).
/// 2. Despublicar CONSERVA la ruta: al re-publicar se reusa la misma y los permisos siguen valiendo.
/// 3. Renombrar la tabla RECONCILIA el nombre del nodo (el original nunca actualiza el nodo).
///
/// Solo se publican tablas RAIZ: los submodelos (matrices) se editan dentro de la fila de su padre.
/// TENANT-SCOPED via el filtro global; aqui NUNCA se filtra a mano por TenantId.
/// </summary>
public interface IDataContainerModuleService
{
    /// <summary>Prefijo de las rutas de los modulos publicados. Sin barra inicial, como el resto del
    /// menu del tenant ("contenedor-datos", "inventario-items"); NavMenu lo emite tal cual en el href
    /// y RolService lo usa como clave del modulo.</summary>
    const string RoutePrefix = "dc/";

    /// <summary>Estado de publicacion de una tabla (null si la tabla no existe en el tenant).</summary>
    Task<DataContainerModuleDto?> GetAsync(Guid containerId, CancellationToken cancellationToken = default);

    /// <summary>Tabla publicada que responde a una ruta (para resolver la pagina /dc/{slug}).
    /// Devuelve null si la ruta no existe, no esta publicada, o es de otro tenant.</summary>
    Task<DataContainerModuleDto?> ResolveByRouteAsync(string route, CancellationToken cancellationToken = default);

    /// <summary>Publica (o actualiza la publicacion de) una tabla: crea/reusa su nodo de menu y guarda
    /// icono y columnas de grilla/filtro. Idempotente.</summary>
    Task<ModulePublishResult<DataContainerModuleDto>> PublishAsync(
        PublishContainerRequest request, CancellationToken cancellationToken = default);

    /// <summary>Retira el modulo del menu (borra el nodo). NO borra la tabla, sus datos ni su ruta.</summary>
    Task<ModulePublishResult<bool>> UnpublishAsync(Guid containerId, CancellationToken cancellationToken = default);

    /// <summary>Alinea el nombre del nodo con el de la tabla (se llama al renombrarla). No toca la ruta.
    /// No-op si la tabla no esta publicada.</summary>
    Task<bool> SyncNodeNameAsync(Guid containerId, CancellationToken cancellationToken = default);
}
