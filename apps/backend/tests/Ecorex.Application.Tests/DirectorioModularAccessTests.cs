using Ecorex.Application.Directorio;
using Ecorex.Application.Roles;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests del enforcement por area del Directorio Modular (Capa 8, regla 5, O5-1). El "area del usuario"
/// se mapea desde su rol: Owner/Admin o sin rol ven todo; un rol que no marca ninguna area no restringe
/// (opt-in); con areas marcadas, solo esas. PuedeArea cruza contra las areas etiquetadas de la seccion/categoria.
/// </summary>
public class DirectorioModularAccessTests
{
    private static ModulePermissionDto View(string key) => new(key, true, false, false, false);

    [Fact]
    public void Owner_admin_ve_todo()
        => Assert.True(DirectorioModularAccessService.Resolve(EffectivePermissions.AllowAllPermissions()).VeTodo);

    [Fact]
    public void Usuario_sin_rol_ve_todo()
        => Assert.True(DirectorioModularAccessService.Resolve(EffectivePermissions.UnrestrictedAccess()).VeTodo);

    [Fact]
    public void Rol_sin_areas_configuradas_ve_todo_optin()
    {
        // Un rol con permisos de OTROS modulos pero ninguna fila de area -> no restringe (opt-in).
        var eff = EffectivePermissions.FromPermissions(System.Guid.NewGuid(), new[] { View("inventario-items") });
        Assert.True(DirectorioModularAccessService.Resolve(eff).VeTodo);
    }

    [Fact]
    public void Rol_con_areas_restringe_a_las_marcadas()
    {
        var eff = EffectivePermissions.FromPermissions(System.Guid.NewGuid(), new[]
        {
            View(DirectorioModularAreaPermisos.PermKey("comercial")),
            // contabilidad presente pero SIN ver (no debe conceder)
            new ModulePermissionDto(DirectorioModularAreaPermisos.PermKey("contabilidad"), false, false, false, false),
        });
        var acc = DirectorioModularAccessService.Resolve(eff);

        Assert.False(acc.VeTodo);
        Assert.Contains("comercial", acc.Areas);
        Assert.DoesNotContain("contabilidad", acc.Areas);
    }

    [Fact]
    public void PuedeArea_cruza_correctamente()
    {
        var eff = EffectivePermissions.FromPermissions(System.Guid.NewGuid(), new[]
        {
            View(DirectorioModularAreaPermisos.PermKey("comercial")),
        });
        var acc = DirectorioModularAccessService.Resolve(eff);

        Assert.True(acc.PuedeArea("comercial,contabilidad"));   // interseca comercial
        Assert.False(acc.PuedeArea("contabilidad"));            // no interseca
        Assert.True(acc.PuedeArea(null));                       // sin areas => visible a todos
        Assert.True(acc.PuedeArea(""));
    }

    [Fact]
    public void VeTodo_puede_cualquier_area()
    {
        var acc = DirectorioModularAccessService.Resolve(EffectivePermissions.UnrestrictedAccess());
        Assert.True(acc.PuedeArea("contabilidad"));
        Assert.True(acc.PuedeArea("admin,logistica"));
    }
}
