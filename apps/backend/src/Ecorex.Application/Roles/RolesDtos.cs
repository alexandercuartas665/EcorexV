namespace Ecorex.Application.Roles;

/// <summary>Rol de permisos (fila del listado). UserCount = usuarios del tenant que lo tienen asignado.</summary>
public sealed record RolDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    bool IsSystem,
    int UserCount);

/// <summary>Permiso de un modulo dentro de un rol (fila de la matriz Modulo x Accion).</summary>
public sealed record ModulePermissionDto(
    string ModuleKey,
    bool CanView,
    bool CanCreate,
    bool CanEdit,
    bool CanDelete)
{
    /// <summary>true si la fila tiene al menos un flag (se persiste); false = fila vacia (no se guarda).</summary>
    public bool HasAny => CanView || CanCreate || CanEdit || CanDelete;
}

/// <summary>Detalle de un rol: sus datos + la lista de permisos por modulo (solo los persistidos).</summary>
public sealed record RolDetailDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    bool IsSystem,
    IReadOnlyList<ModulePermissionDto> Permisos);

/// <summary>Modulo del catalogo sobre el que se definen permisos. Derivado del menu (Route/Name/Section).</summary>
public sealed record ModuloInfo(string Key, string Label, string Grupo);

/// <summary>Accion de la matriz de permisos.</summary>
public enum PermissionAction
{
    View = 0,
    Create,
    Edit,
    Delete
}
