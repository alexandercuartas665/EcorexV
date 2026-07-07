using System.Security.Claims;
using Ecorex.Application.Roles;
using Microsoft.Extensions.DependencyInjection;

namespace Ecorex.SuperAdmin.Auth;

/// <summary>
/// Permisos efectivos del usuario ACTUAL, resueltos una sola vez por scope/circuito (Ola B2,
/// ADR-0033). Envuelve <see cref="IRolService.ResolveEffectivePermissionsAsync"/> tomando el
/// PlatformUserId del claim NameIdentifier, y cachea el resultado. Lo consumen las paginas (para
/// ocultar botones) y el filtrado del menu.
///
/// Regla opt-in (back-compat): Owner/Admin y usuario SIN rol -> <see cref="Unrestricted"/> (acceso
/// como en el paso 1). Solo un usuario CON rol queda sujeto a su matriz. Fail-OPEN: si la
/// resolucion falla o no hay usuario, Unrestricted=true (no bloquea la consola).
/// </summary>
public interface ICurrentPermissions
{
    /// <summary>Permisos efectivos del usuario actual (resueltos y cacheados en el scope).</summary>
    Task<EffectivePermissions> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>true si el usuario no tiene matriz que aplicar (Owner/Admin o sin rol). Fail-open.</summary>
    Task<bool> IsUnrestrictedAsync(CancellationToken cancellationToken = default);

    Task<bool> CanViewAsync(string moduleKey, CancellationToken cancellationToken = default);
    Task<bool> CanCreateAsync(string moduleKey, CancellationToken cancellationToken = default);
    Task<bool> CanEditAsync(string moduleKey, CancellationToken cancellationToken = default);
    Task<bool> CanDeleteAsync(string moduleKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementacion scoped de <see cref="ICurrentPermissions"/>. Resuelve en un scope PROPIO (como
/// NavMenu con la marca y el menu) para no compartir el DbContext del circuito, y memoiza el
/// resultado para todas las consultas del mismo scope.
/// </summary>
public sealed class CurrentPermissions : ICurrentPermissions
{
    private readonly IHttpContextAccessor _accessor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private EffectivePermissions? _cached;

    public CurrentPermissions(IHttpContextAccessor accessor, IServiceScopeFactory scopeFactory)
    {
        _accessor = accessor;
        _scopeFactory = scopeFactory;
    }

    public async Task<EffectivePermissions> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cached is not null)
            {
                return _cached;
            }
            _cached = await ResolveAsync(cancellationToken);
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<EffectivePermissions> ResolveAsync(CancellationToken cancellationToken)
    {
        try
        {
            var idClaim = _accessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(idClaim, out var platformUserId))
            {
                // Sin usuario resoluble (o no autenticado): no restringir (fail-open).
                return EffectivePermissions.UnrestrictedAccess();
            }

            // Scope propio: DbContext aislado del circuito Blazor (patron de NavMenu).
            await using var scope = _scopeFactory.CreateAsyncScope();
            var roles = scope.ServiceProvider.GetRequiredService<IRolService>();
            return await roles.ResolveEffectivePermissionsAsync(platformUserId, cancellationToken);
        }
        catch
        {
            // Fail-OPEN documentado (ADR-0033): si la resolucion falla, no bloqueamos la consola.
            return EffectivePermissions.UnrestrictedAccess();
        }
    }

    public async Task<bool> IsUnrestrictedAsync(CancellationToken cancellationToken = default)
        => (await GetAsync(cancellationToken)).Unrestricted;

    public async Task<bool> CanViewAsync(string moduleKey, CancellationToken cancellationToken = default)
        => (await GetAsync(cancellationToken)).Can(moduleKey, PermissionAction.View);

    public async Task<bool> CanCreateAsync(string moduleKey, CancellationToken cancellationToken = default)
        => (await GetAsync(cancellationToken)).Can(moduleKey, PermissionAction.Create);

    public async Task<bool> CanEditAsync(string moduleKey, CancellationToken cancellationToken = default)
        => (await GetAsync(cancellationToken)).Can(moduleKey, PermissionAction.Edit);

    public async Task<bool> CanDeleteAsync(string moduleKey, CancellationToken cancellationToken = default)
        => (await GetAsync(cancellationToken)).Can(moduleKey, PermissionAction.Delete);
}
