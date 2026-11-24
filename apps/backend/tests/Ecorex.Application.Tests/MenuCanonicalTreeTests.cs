using Ecorex.Application.MenuConfig;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>El menu estandar (fuente unica del alta, "Cargar menu estandar" y el catalogo de permisos).</summary>
public class MenuCanonicalTreeTests
{
    [Fact]
    public void Items_TienenRutaUnica()
    {
        var routes = MenuCanonicalTree.AllItems().Select(i => i.Route).ToList();
        Assert.NotEmpty(routes);
        Assert.Equal(routes.Count, routes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void BuildNodes_ConservaJerarquiaOrdenYEstado()
    {
        var tenantId = Guid.NewGuid();
        var viewId = Guid.NewGuid();
        var nodes = MenuCanonicalTree.BuildNodes(tenantId, viewId);

        var expected = MenuCanonicalTree.Roots.Count + MenuCanonicalTree.Roots.Sum(r => r.Items.Count);
        Assert.Equal(expected, nodes.Count);
        Assert.All(nodes, n => { Assert.Equal(tenantId, n.TenantId); Assert.Equal(viewId, n.MenuViewId); Assert.True(n.IsVisible); });
        Assert.Equal(nodes.Count, nodes.Select(n => n.SortOrder).Distinct().Count());

        var byId = nodes.ToDictionary(n => n.Id);
        Assert.All(nodes.Where(n => n.ParentId is not null), n => Assert.Equal(MenuNodeKind.Section, byId[n.ParentId!.Value].Kind));
        Assert.Equal(MenuNodeState.InDevelopment, nodes.First(n => n.Route == "modulo/vendedores").State);
        Assert.Equal(MenuNodeState.Ready, nodes.First(n => n.Route == "cerebros").State);
    }
}
