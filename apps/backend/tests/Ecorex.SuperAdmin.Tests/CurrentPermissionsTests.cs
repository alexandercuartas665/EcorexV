using System.Security.Claims;
using Ecorex.Application.Roles;
using Ecorex.SuperAdmin.Auth;
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

    private static IServiceScopeFactory ScopeFactoryWith(IRolService rolService)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => rolService);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public async Task Resolve_CachesResult_ResolvesOnce()
    {
        var userId = Guid.NewGuid();
        var fake = new CountingRolService(EffectivePermissions.FromPermissions(
            Guid.NewGuid(),
            new[] { new ModulePermissionDto("inventario-items", true, false, false, false) }));

        var sut = new CurrentPermissions(AccessorFor(userId), ScopeFactoryWith(fake));

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
        var sut = new CurrentPermissions(AccessorFor(null), ScopeFactoryWith(fake));

        var eff = await sut.GetAsync();

        Assert.True(eff.Unrestricted);
        Assert.Equal(0, fake.Calls);       // ni siquiera llama al servicio: no hay usuario.
    }

    [Fact]
    public async Task Resolve_WhenServiceThrows_IsUnrestricted_FailOpen()
    {
        var sut = new CurrentPermissions(AccessorFor(Guid.NewGuid()), ScopeFactoryWith(new ThrowingRolService()));

        var eff = await sut.GetAsync();

        // Fail-OPEN documentado: si la resolucion falla, no bloqueamos la consola.
        Assert.True(eff.Unrestricted);
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
