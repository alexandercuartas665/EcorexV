using Ecorex.Application.Common;

namespace Ecorex.Api.Auth;

/// <summary>
/// Resuelve el tenant y usuario actuales desde los claims del JWT del request
/// (claims "tenant_id" y "sub"). En requests sin token quedan en null (fail-closed).
/// </summary>
public sealed class HttpContextTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextTenantContext(IHttpContextAccessor accessor) => _accessor = accessor;

    public Guid? TenantId => ReadGuidClaim("tenant_id");
    public Guid? UserId => ReadGuidClaim("sub");

    private Guid? ReadGuidClaim(string claimType)
    {
        var value = _accessor.HttpContext?.User.FindFirst(claimType)?.Value;
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }
}
