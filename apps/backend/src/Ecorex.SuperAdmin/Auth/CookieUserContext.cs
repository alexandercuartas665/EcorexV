using System.Security.Claims;
using Ecorex.Application.Common;

namespace Ecorex.SuperAdmin.Auth;

/// <summary>
/// ITenantContext para la consola unificada (cookie auth). Resuelve:
/// - UserId: del claim NameIdentifier del usuario autenticado.
/// - TenantId: del claim "tenant_id" si el usuario es miembro de un tenant; null para
///   operadores de plataforma (Super Admin), que no pertenecen a ningun tenant.
/// Asi las consultas tenant-scoped quedan aisladas automaticamente para usuarios de agencia.
/// </summary>
public sealed class CookieUserContext(IHttpContextAccessor accessor) : ITenantContext
{
    public Guid? TenantId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirst("tenant_id")?.Value, out var id)
            ? id
            : null;

    public Guid? UserId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)
            ? id
            : null;
}
