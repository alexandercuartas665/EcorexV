using System.Security.Claims;
using Ecorex.Application.Roles;
using Microsoft.AspNetCore.Components.Authorization;
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
    private readonly IServiceProvider _services;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private EffectivePermissions? _cached;

    public CurrentPermissions(IHttpContextAccessor accessor, IServiceScopeFactory scopeFactory, IServiceProvider services)
    {
        _accessor = accessor;
        _scopeFactory = scopeFactory;
        _services = services;
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
            var (tenantId, platformUserId) = await ResolveIdentityAsync();
            if (platformUserId is not Guid userId)
            {
                // Sin usuario resoluble (o no autenticado): no restringir (fail-open).
                return EffectivePermissions.UnrestrictedAccess();
            }

            // Scope propio: DbContext aislado del circuito Blazor (patron de NavMenu).
            await using var scope = _scopeFactory.CreateAsyncScope();

            // El TenantUser es tenant-scoped: sin tenant en el contexto, el filtro global NO lo
            // encuentra y la resolucion caeria en "sin TenantUser -> Unrestricted". En una peticion
            // HTTP el tenant sale del claim; en un circuito Blazor no hay HttpContext, asi que se fija
            // aqui de forma ambiental para que el filtro vea al usuario. Ver ResolveIdentityAsync.
            using var ambient = tenantId is Guid tid ? AmbientTenantContext.Begin(tid, userId) : null;

            var roles = scope.ServiceProvider.GetRequiredService<IRolService>();
            return await roles.ResolveEffectivePermissionsAsync(userId, cancellationToken);
        }
        catch
        {
            // Fail-OPEN documentado (ADR-0033): si la resolucion falla, no bloqueamos la consola.
            return EffectivePermissions.UnrestrictedAccess();
        }
    }

    /// <summary>
    /// Tenant y usuario actuales, mirando las DOS realidades donde vive este servicio.
    ///
    /// OJO (bug corregido 2026-07-16): antes solo se leia de IHttpContextAccessor. En una peticion
    /// HTTP eso funciona (es donde corre el handler de autorizacion), pero en un CIRCUITO Blazor
    /// interactivo (las paginas con prerender:false) NO hay HttpContext, y fallaban DOS cosas: el
    /// claim del usuario salia null, y el tenant tambien (con lo que el filtro global tampoco
    /// encontraba el TenantUser). Por cualquiera de las dos, GetAsync caia en fail-open y devolvia
    /// Unrestricted: el gateado en pagina (ocultar botones / negar acceso) NO restringia a nadie
    /// aunque su rol lo prohibiera. Solo se salvaban las paginas con [Authorize(Policy="Perm:...")],
    /// que se evalua en la peticion. En el circuito ambos claims viven en el AuthenticationState.
    ///
    /// El proveedor de autenticacion se pide por IServiceProvider (no por constructor) para no
    /// exigirlo en scopes que no lo tengan (p. ej. trabajo de fondo).
    /// </summary>
    private async Task<(Guid? TenantId, Guid? UserId)> ResolveIdentityAsync()
    {
        var http = _accessor.HttpContext?.User;
        if (http is not null)
        {
            var httpUser = http.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(httpUser, out var uid))
            {
                Guid.TryParse(http.FindFirst("tenant_id")?.Value, out var htid);
                return (htid == Guid.Empty ? null : htid, uid);
            }
        }

        var authProvider = _services.GetService<AuthenticationStateProvider>();
        if (authProvider is null) { return (null, null); }

        var state = await authProvider.GetAuthenticationStateAsync();
        var user = state.User;
        if (!Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var circuitUser))
        {
            return (null, null);
        }
        Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var ctid);
        return (ctid == Guid.Empty ? null : ctid, circuitUser);
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
