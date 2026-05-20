using System.Security.Claims;
using CubotTravels.Application.Common;

namespace CubotTravels.SuperAdmin.Auth;

/// <summary>
/// ITenantContext para la consola Super Admin. El operador no pertenece a un tenant
/// (TenantId siempre null); UserId se toma del usuario autenticado por cookie.
/// </summary>
public sealed class SuperAdminContext(IHttpContextAccessor accessor) : ITenantContext
{
    public Guid? TenantId => null;

    public Guid? UserId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)
            ? id
            : null;
}
