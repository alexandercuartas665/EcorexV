using Ecorex.Application.Roles;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests PUROS (sin base de datos) de la resolucion de permisos efectivos (Ola B1/B2, ADR-0032/0033):
/// Owner/Admin -> AllowAll; con rol -> set del rol; SIN rol -> Unrestricted (regla opt-in de B2, no
/// bloquea a quien hoy no tiene rol); Can(modulo, accion); y el filtrado de filas persistibles
/// (SavePermisos guarda solo las que tienen algun flag). La logica con BD (catalogo derivado del
/// menu, round-trip) se cubre en Ecorex.Integration.Tests (dual).
/// </summary>
public class PermissionResolverTests
{
    private static ModulePermissionDto P(string key, bool v = false, bool c = false, bool e = false, bool d = false)
        => new(key, v, c, e, d);

    // ---- Resolucion del set efectivo ----

    [Fact]
    public void Resolve_OwnerOrAdmin_AllowsAll()
    {
        var eff = PermissionResolver.Resolve(isOwnerOrAdmin: true, rolId: null, permisos: null);

        Assert.True(eff.AllowAll);
        Assert.True(eff.Can("cualquier-modulo", PermissionAction.Delete));
        Assert.True(eff.For("otro").Create);
    }

    [Fact]
    public void Resolve_WithRole_ResolvesTheSet()
    {
        var rolId = Guid.NewGuid();
        var permisos = new[]
        {
            P("inventario-items", v: true, c: true),
            P("actividades", v: true)
        };

        var eff = PermissionResolver.Resolve(isOwnerOrAdmin: false, rolId, permisos);

        Assert.False(eff.AllowAll);
        Assert.Equal(rolId, eff.RolId);
        Assert.True(eff.Can("inventario-items", PermissionAction.View));
        Assert.True(eff.Can("inventario-items", PermissionAction.Create));
        Assert.False(eff.Can("inventario-items", PermissionAction.Edit));
        Assert.False(eff.Can("inventario-items", PermissionAction.Delete));
        Assert.True(eff.Can("actividades", PermissionAction.View));
        Assert.False(eff.Can("actividades", PermissionAction.Create));
        // Modulo ausente del rol -> sin acceso.
        Assert.False(eff.Can("modulo-desconocido", PermissionAction.View));
    }

    [Fact]
    public void Resolve_NoRole_IsUnrestricted()
    {
        // Regla opt-in de la Ola B2: un usuario sin rol de permisos NO se restringe (conserva el
        // acceso del paso 1). Unrestricted pero NO AllowAll (no ostenta poder organico Owner/Admin).
        var eff = PermissionResolver.Resolve(isOwnerOrAdmin: false, rolId: null, permisos: null);

        Assert.False(eff.AllowAll);
        Assert.True(eff.Unrestricted);
        Assert.Null(eff.RolId);
        Assert.True(eff.Can("actividades", PermissionAction.View));
        Assert.True(eff.Can("cualquier-modulo", PermissionAction.Delete));
        Assert.Equal(ModuleAccess.All, eff.For("actividades"));
    }

    [Fact]
    public void Resolve_OwnerOrAdmin_IsAlsoUnrestricted()
    {
        var eff = PermissionResolver.Resolve(isOwnerOrAdmin: true, rolId: null, permisos: null);

        Assert.True(eff.AllowAll);
        Assert.True(eff.Unrestricted);
    }

    [Fact]
    public void Resolve_WithRole_IsNotUnrestricted()
    {
        var eff = PermissionResolver.Resolve(
            isOwnerOrAdmin: false, rolId: Guid.NewGuid(),
            permisos: new[] { P("actividades", v: true) });

        Assert.False(eff.AllowAll);
        Assert.False(eff.Unrestricted);
    }

    [Fact]
    public void ModuleAccess_Can_MapsEachAction()
    {
        var access = new ModuleAccess(View: true, Create: false, Edit: true, Delete: false);
        Assert.True(access.Can(PermissionAction.View));
        Assert.False(access.Can(PermissionAction.Create));
        Assert.True(access.Can(PermissionAction.Edit));
        Assert.False(access.Can(PermissionAction.Delete));
    }

    // ---- Filtrado de filas persistibles ----

    [Fact]
    public void FilterPersistable_DropsRowsWithoutAnyFlag()
    {
        var input = new[]
        {
            P("con-flag", v: true),
            P("vacio"),                 // sin flags: no se persiste
            P("otro-flag", d: true)
        };

        var kept = PermissionResolver.FilterPersistable(input);

        Assert.Equal(2, kept.Count);
        Assert.Contains(kept, k => k.ModuleKey == "con-flag");
        Assert.Contains(kept, k => k.ModuleKey == "otro-flag");
        Assert.DoesNotContain(kept, k => k.ModuleKey == "vacio");
    }

    [Fact]
    public void FilterPersistable_DedupesByModuleKey_LastWins()
    {
        var input = new[]
        {
            P("mod", v: true),
            P("mod", e: true)   // misma clave: gana la ultima
        };

        var kept = PermissionResolver.FilterPersistable(input);

        var row = Assert.Single(kept);
        Assert.True(row.CanEdit);
        Assert.False(row.CanView);
    }

    [Fact]
    public void FilterPersistable_HonorsCatalogWhitelist()
    {
        var input = new[]
        {
            P("valido", v: true),
            P("fuera-de-catalogo", c: true)
        };
        var valid = new HashSet<string>(StringComparer.Ordinal) { "valido" };

        var kept = PermissionResolver.FilterPersistable(input, valid);

        var row = Assert.Single(kept);
        Assert.Equal("valido", row.ModuleKey);
    }

    [Fact]
    public void FilterPersistable_IgnoresBlankModuleKeys()
    {
        var input = new[] { P("", v: true), P("  ", c: true), P("ok", d: true) };
        var kept = PermissionResolver.FilterPersistable(input);
        Assert.Single(kept);
        Assert.Equal("ok", kept[0].ModuleKey);
    }
}
