using System.Security.Claims;
using Ecorex.Application.Roles;
using Ecorex.SuperAdmin.Auth;
using Microsoft.AspNetCore.Authorization;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// Tests unitarios del enforcement dinamico de permisos (Ola B2, ADR-0033): parseo de nombres de
/// policy "Perm:{module}:{action}", y decision del PermissionAuthorizationHandler (Owner/Admin y
/// sin-rol = Unrestricted -> permite; con rol -> respeta la matriz; sin permiso -> no concede).
/// </summary>
public class PermissionAuthorizationTests
{
    // ---- PermissionPolicy.TryParse ----

    [Theory]
    [InlineData("Perm:inventario-items:View", "inventario-items", PermissionAction.View)]
    [InlineData("Perm:admin-usuarios:Create", "admin-usuarios", PermissionAction.Create)]
    [InlineData("Perm:roles-permisos:Edit", "roles-permisos", PermissionAction.Edit)]
    [InlineData("Perm:modulo/estados:Delete", "modulo/estados", PermissionAction.Delete)]
    public void TryParse_ValidNames_Parses(string name, string expectedModule, PermissionAction expectedAction)
    {
        Assert.True(PermissionPolicy.TryParse(name, out var module, out var action));
        Assert.Equal(expectedModule, module);
        Assert.Equal(expectedAction, action);
    }

    [Theory]
    [InlineData("Inventario.Ver")]          // policy clasica, sin prefijo Perm:
    [InlineData("TenantMember")]
    [InlineData("Perm:solo-modulo")]        // falta la accion
    [InlineData("Perm:mod:Desconocida")]    // accion no valida
    [InlineData("Perm::View")]              // modulo vacio
    [InlineData("")]
    public void TryParse_InvalidNames_ReturnsFalse(string name)
    {
        Assert.False(PermissionPolicy.TryParse(name, out _, out _));
    }

    [Fact]
    public void For_BuildsRoundTrippableName()
    {
        var name = PermissionPolicy.For("inventario-items", PermissionAction.Create);
        Assert.Equal("Perm:inventario-items:Create", name);
        Assert.True(PermissionPolicy.TryParse(name, out var module, out var action));
        Assert.Equal("inventario-items", module);
        Assert.Equal(PermissionAction.Create, action);
    }

    // ---- PermissionAuthorizationHandler ----

    private static AuthorizationHandlerContext ContextFor(PermissionRequirement requirement)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tenant_id", Guid.NewGuid().ToString())
        }, "test"));
        return new AuthorizationHandlerContext(new[] { requirement }, user, resource: null);
    }

    [Fact]
    public async Task Handler_Unrestricted_Succeeds()
    {
        // Sin rol -> Unrestricted (regla opt-in). El handler debe conceder.
        var handler = new PermissionAuthorizationHandler(
            new FakeCurrentPermissions(EffectivePermissions.UnrestrictedAccess()));
        var requirement = new PermissionRequirement("inventario-items", PermissionAction.Delete);
        var ctx = ContextFor(requirement);

        await handler.HandleAsync(ctx);

        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task Handler_OwnerAdmin_Succeeds()
    {
        var handler = new PermissionAuthorizationHandler(
            new FakeCurrentPermissions(EffectivePermissions.AllowAllPermissions()));
        var requirement = new PermissionRequirement("cualquier-modulo", PermissionAction.Edit);
        var ctx = ContextFor(requirement);

        await handler.HandleAsync(ctx);

        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task Handler_WithRole_Grants_WhenMatrixAllows()
    {
        var eff = EffectivePermissions.FromPermissions(Guid.NewGuid(), new[]
        {
            new ModulePermissionDto("inventario-items", CanView: true, CanCreate: false, CanEdit: false, CanDelete: false)
        });
        var handler = new PermissionAuthorizationHandler(new FakeCurrentPermissions(eff));
        var requirement = new PermissionRequirement("inventario-items", PermissionAction.View);
        var ctx = ContextFor(requirement);

        await handler.HandleAsync(ctx);

        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task Handler_WithRole_Denies_WhenMatrixLacksPermission()
    {
        var eff = EffectivePermissions.FromPermissions(Guid.NewGuid(), new[]
        {
            new ModulePermissionDto("inventario-items", CanView: true, CanCreate: false, CanEdit: false, CanDelete: false)
        });
        var handler = new PermissionAuthorizationHandler(new FakeCurrentPermissions(eff));

        // Ver SI (concede); pero Crear en el mismo modulo NO, y Ver en un modulo ausente NO.
        var denyCreate = ContextFor(new PermissionRequirement("inventario-items", PermissionAction.Create));
        var denyOther = ContextFor(new PermissionRequirement("modulo-desconocido", PermissionAction.View));

        await handler.HandleAsync(denyCreate);
        await handler.HandleAsync(denyOther);

        Assert.False(denyCreate.HasSucceeded);
        Assert.False(denyOther.HasSucceeded);
    }

    private sealed class FakeCurrentPermissions : ICurrentPermissions
    {
        private readonly EffectivePermissions _eff;
        public FakeCurrentPermissions(EffectivePermissions eff) => _eff = eff;

        public Task<EffectivePermissions> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(_eff);
        public Task<bool> IsUnrestrictedAsync(CancellationToken cancellationToken = default) => Task.FromResult(_eff.Unrestricted);
        public Task<bool> CanViewAsync(string moduleKey, CancellationToken cancellationToken = default) => Task.FromResult(_eff.Can(moduleKey, PermissionAction.View));
        public Task<bool> CanCreateAsync(string moduleKey, CancellationToken cancellationToken = default) => Task.FromResult(_eff.Can(moduleKey, PermissionAction.Create));
        public Task<bool> CanEditAsync(string moduleKey, CancellationToken cancellationToken = default) => Task.FromResult(_eff.Can(moduleKey, PermissionAction.Edit));
        public Task<bool> CanDeleteAsync(string moduleKey, CancellationToken cancellationToken = default) => Task.FromResult(_eff.Can(moduleKey, PermissionAction.Delete));
    }
}
