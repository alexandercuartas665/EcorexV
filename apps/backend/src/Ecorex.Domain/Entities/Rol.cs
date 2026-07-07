using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Rol de permisos dentro del tenant (Ola B1). Define, via RolPermiso, que puede hacer un usuario
/// sobre cada modulo real del menu (matriz Modulo x Accion). TENANT-SCOPED (filtro global por
/// TenantId). Es distinto de <see cref="Enums.TenantRole"/>: TenantRole modela el PODER organico
/// (Owner/Admin/Supervisor/Advisor) y decide gobierno; Rol modela permisos finos configurables por
/// el tenant. La aplicacion (enforcement) del set efectivo es Ola B2.
/// </summary>
public class Rol : TenantEntity
{
    /// <summary>Nombre del rol (requerido, unico por tenant).</summary>
    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    /// <summary>Activo: los roles inactivos no se asignan a usuarios nuevos (metadata en B1).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Rol de sistema (ej. "Administrador"): protegido de borrado y de renombrado. Lo crea el
    /// seeder con todos los permisos; el tenant no lo puede eliminar ni cambiarle el nombre.
    /// </summary>
    public bool IsSystem { get; set; }
}

/// <summary>
/// Permiso de un rol sobre un modulo (fila de la matriz). ModuleKey = Route del MenuNode Item
/// (ej. "inventario-items", "admin-usuarios"). Unico por (RolId, ModuleKey). TENANT-SCOPED.
/// Solo se persisten las filas con al menos un flag en true (SavePermisos borra e reinserta).
/// </summary>
public class RolPermiso : TenantEntity
{
    public Guid RolId { get; set; }
    public Rol? Rol { get; set; }

    /// <summary>Clave del modulo = Route del MenuNode Item (ej. "inventario-items").</summary>
    public string ModuleKey { get; set; } = null!;

    public bool CanView { get; set; }
    public bool CanCreate { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
}
