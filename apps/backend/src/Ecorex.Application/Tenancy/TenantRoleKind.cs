using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Vista simplificada del rol del tenant (2026-11-16): en pantalla solo existen "Administrador" y
/// "Usuario". El detalle de que ve y que puede hacer un usuario lo da el "Rol de permisos" (RolService).
///
/// El enum <see cref="TenantRole"/> NO cambia (sin migracion): Owner y Admin se muestran como
/// Administrador; Supervisor y Advisor como Usuario. Al guardar, si el usuario no cambio de grupo se
/// conserva su valor original (un Propietario sigue siendo Owner, un Supervisor sigue siendo Supervisor);
/// solo al cambiar de grupo se escribe Admin o Advisor.
/// </summary>
public static class TenantRoleKind
{
    public const string AdminLabel = "Administrador";
    public const string UserLabel = "Usuario";

    /// <summary>True si el rol tiene poder de administrador (Owner/Admin: acceso total por gobierno).</summary>
    public static bool IsAdmin(TenantRole role) => role is TenantRole.Owner or TenantRole.Admin;

    public static string Label(TenantRole role) => IsAdmin(role) ? AdminLabel : UserLabel;

    /// <summary>Rol a guardar para un usuario NUEVO segun la opcion elegida.</summary>
    public static TenantRole ForNew(bool admin) => admin ? TenantRole.Admin : TenantRole.Advisor;

    /// <summary>
    /// Rol a guardar al editar: si la opcion elegida coincide con el grupo actual se conserva el valor
    /// original; si cambia de grupo, Admin o Advisor.
    /// </summary>
    public static TenantRole Resolve(TenantRole current, bool admin) =>
        IsAdmin(current) == admin ? current : ForNew(admin);
}
