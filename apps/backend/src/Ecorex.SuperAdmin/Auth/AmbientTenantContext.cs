using System.Security.Claims;
using Ecorex.Application.Common;

namespace Ecorex.SuperAdmin.Auth;

/// <summary>
/// ITenantContext de la consola con soporte para trabajo EN BACKGROUND sin usuario autenticado.
/// Por defecto resuelve tenant/usuario de los claims de la cookie (igual que CookieUserContext). Pero un
/// proceso de fondo (despachador de respuestas del agente, disparado por webhook) puede fijar el tenant
/// con <see cref="Begin"/>: ese valor (guardado en un AsyncLocal que fluye con la cadena async) tiene
/// prioridad, de modo que el query filter de EF aisla al tenant correcto aunque no haya HttpContext.
/// </summary>
public sealed class AmbientTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    private sealed record Scope(Guid TenantId, Guid? UserId);
    private static readonly AsyncLocal<Scope?> _ambient = new();

    public Guid? TenantId =>
        _ambient.Value is { } s
            ? s.TenantId
            : (Guid.TryParse(accessor.HttpContext?.User.FindFirst("tenant_id")?.Value, out var id) ? id : null);

    public Guid? UserId =>
        _ambient.Value is { } s
            ? s.UserId
            : (Guid.TryParse(accessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null);

    /// <summary>Fija el tenant (y usuario opcional) para el resto de la cadena async. Restaura al disponer.</summary>
    public static IDisposable Begin(Guid tenantId, Guid? userId = null)
    {
        var previous = _ambient.Value;
        _ambient.Value = new Scope(tenantId, userId);
        return new Resetter(previous);
    }

    private sealed class Resetter(Scope? previous) : IDisposable
    {
        public void Dispose() => _ambient.Value = previous;
    }
}
