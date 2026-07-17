using System.Security.Claims;
using Ecorex.Application.Roles;
using Ecorex.SuperAdmin.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// Tests de <see cref="CurrentPermissions"/> (Ola B2, ADR-0033): resuelve el set del usuario actual
/// UNA sola vez por scope (cachea), es fail-open (Unrestricted) si no hay usuario o si la resolucion
/// lanza, y respeta el set resuelto cuando el usuario tiene rol.
/// </summary>
public class CurrentPermissionsTests
{
    private static IHttpContextAccessor AccessorFor(Guid? platformUserId)
    {
        var ctx = new DefaultHttpContext();
        if (platformUserId is Guid id)
        {
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, id.ToString())
            }, "test"));
        }
        return new HttpContextAccessor { HttpContext = ctx };
    }

    /// <summary>
    /// Contenedor con el IRolService dado. Devuelve las dos piezas que pide el constructor: el
    /// factory de scopes (para resolver en scope propio) y el provider (de donde saca el
    /// AuthenticationStateProvider cuando no hay HttpContext; aqui no se registra, y no hace falta
    /// porque se pide con GetService).
    /// </summary>
    private static (IServiceScopeFactory Scopes, IServiceProvider Services) ProviderWith(IRolService rolService)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => rolService);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IServiceScopeFactory>(), provider);
    }

    [Fact]
    public async Task Resolve_CachesResult_ResolvesOnce()
    {
        var userId = Guid.NewGuid();
        var fake = new CountingRolService(EffectivePermissions.FromPermissions(
            Guid.NewGuid(),
            new[] { new ModulePermissionDto("inventario-items", true, false, false, false) }));

        var (scopes, services) = ProviderWith(fake);
        var sut = new CurrentPermissions(AccessorFor(userId), scopes, services);

        var a = await sut.GetAsync();
        var b = await sut.GetAsync();
        var canView = await sut.CanViewAsync("inventario-items");
        var canCreate = await sut.CanCreateAsync("inventario-items");

        Assert.Same(a, b);                 // memoizado
        Assert.Equal(1, fake.Calls);       // resuelto una sola vez
        Assert.True(canView);
        Assert.False(canCreate);
    }

    [Fact]
    public async Task Resolve_NoUser_IsUnrestricted_FailOpen()
    {
        var fake = new CountingRolService(EffectivePermissions.FromPermissions(Guid.NewGuid(), Array.Empty<ModulePermissionDto>()));
        var (scopes, services) = ProviderWith(fake);
        var sut = new CurrentPermissions(AccessorFor(null), scopes, services);

        var eff = await sut.GetAsync();

        Assert.True(eff.Unrestricted);
        Assert.Equal(0, fake.Calls);       // ni siquiera llama al servicio: no hay usuario.
    }

    [Fact]
    public async Task Resolve_WhenServiceThrows_IsUnrestricted_FailOpen()
    {
        var (scopes, services) = ProviderWith(new ThrowingRolService());
        var sut = new CurrentPermissions(AccessorFor(Guid.NewGuid()), scopes, services);

        var eff = await sut.GetAsync();

        // Fail-OPEN documentado: si la resolucion falla, no bloqueamos la consola.
        Assert.True(eff.Unrestricted);
    }

    [Fact]
    public async Task Resolve_SinHttpContext_UsaElAuthenticationState_DelCircuito()
    {
        // Regresion del bug de seguridad del 2026-07-16: en un circuito Blazor NO hay HttpContext,
        // se caia en fail-open y el gateado en pagina no restringia a NADIE. La identidad tiene que
        // salir del AuthenticationState.
        var userId = Guid.NewGuid();
        var fake = new CountingRolService(EffectivePermissions.FromPermissions(
            Guid.NewGuid(),
            new[] { new ModulePermissionDto("inventario-items", true, false, false, false) }));

        var services = new ServiceCollection();
        services.AddScoped<IRolService>(_ => fake);
        services.AddScoped<AuthenticationStateProvider>(_ => new FakeAuthStateProvider(userId, Guid.NewGuid()));
        var provider = services.BuildServiceProvider();

        var sinHttp = new HttpContextAccessor { HttpContext = null };
        var sut = new CurrentPermissions(sinHttp, provider.GetRequiredService<IServiceScopeFactory>(), provider);

        var eff = await sut.GetAsync();

        Assert.False(eff.Unrestricted);                      // lo que fallaba: devolvia Unrestricted
        Assert.Equal(1, fake.Calls);                         // resolvio de verdad contra el rol
        Assert.True(await sut.CanViewAsync("inventario-items"));
        Assert.False(await sut.CanCreateAsync("inventario-items"));
    }

    /// <summary>AuthenticationStateProvider con un usuario y tenant fijos, como el de un circuito.</summary>
    private sealed class FakeAuthStateProvider(Guid userId, Guid tenantId) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim("tenant_id", tenantId.ToString())
            }, "circuit");
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }

    // ---- Fakes de IRolService (solo se ejercita ResolveEffectivePermissionsAsync) ----

    private sealed class CountingRolService : StubRolService
    {
        private readonly EffectivePermissions _eff;
        public int Calls { get; private set; }
        public CountingRolService(EffectivePermissions eff) => _eff = eff;

        public override Task<EffectivePermissions> ResolveEffectivePermissionsAsync(
            Guid platformUserId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_eff);
        }
    }

    private sealed class ThrowingRolService : StubRolService
    {
        public override Task<EffectivePermissions> ResolveEffectivePermissionsAsync(
            Guid platformUserId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("boom");
    }

    /// <summary>Base con todos los miembros de IRolService lanzando NotSupported salvo el resolutor.</summary>
    private abstract class StubRolService : IRolService
    {
        public virtual Task<EffectivePermissions> ResolveEffectivePermissionsAsync(Guid platformUserId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<RolDto>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RolDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RolResult<RolDto>> SaveAsync(Guid? id, string name, string? description, bool isActive, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RolResult<bool>> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RolResult<bool>> SavePermisosAsync(Guid rolId, IReadOnlyList<ModulePermissionDto> permisos, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModuloInfo>> GetModuleCatalogAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RolResult<bool>> AssignRoleToUserAsync(Guid tenantUserId, Guid? rolId, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
