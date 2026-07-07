using Ecorex.Application.MenuConfig;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// Validacion PURA del constructor del arbol del menu configurable (Ola 1): anidado por ParentId,
/// filtrado de invisibles (con poda de descendencia), conteo de hijos visibles y orden por
/// SortOrder. Sin base de datos.
/// </summary>
public class MenuTreeBuilderTests
{
    private static MenuTreeBuilder.FlatNode Node(
        Guid id, Guid? parent, MenuNodeKind kind, string name, int sort, bool visible = true, string? route = null)
        => new(id, parent, kind, name, null, null, route, MenuNodeState.Ready, visible, sort);

    [Fact]
    public void BuildsNestedTree_FromFlatList()
    {
        var section = Guid.NewGuid();
        var sub = Guid.NewGuid();
        var leaf1 = Guid.NewGuid();
        var leaf2 = Guid.NewGuid();

        var roots = MenuTreeBuilder.Build(new[]
        {
            Node(section, null, MenuNodeKind.Section, "Mis Procesos", 0),
            Node(leaf1, section, MenuNodeKind.Item, "Crear", 0),
            Node(sub, section, MenuNodeKind.Subgroup, "Comercial", 1),
            Node(leaf2, sub, MenuNodeKind.Item, "Requerimientos", 0),
        });

        Assert.Single(roots);
        var s = roots[0];
        Assert.Equal("Mis Procesos", s.Name);
        Assert.Equal(2, s.VisibleChildCount); // leaf1 + subgroup
        var subgroup = s.Children.Single(c => c.Kind == MenuNodeKind.Subgroup);
        Assert.Single(subgroup.Children);
        Assert.Equal("Requerimientos", subgroup.Children[0].Name);
    }

    [Fact]
    public void OrdersBySortOrder()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var roots = MenuTreeBuilder.Build(new[]
        {
            Node(a, null, MenuNodeKind.Section, "Tercero", 2),
            Node(b, null, MenuNodeKind.Section, "Primero", 0),
            Node(c, null, MenuNodeKind.Section, "Segundo", 1),
        });

        Assert.Equal(new[] { "Primero", "Segundo", "Tercero" }, roots.Select(r => r.Name).ToArray());
    }

    [Fact]
    public void FiltersInvisibleNodes_AndPrunesTheirChildren()
    {
        var visibleSection = Guid.NewGuid();
        var hiddenSection = Guid.NewGuid();
        var childOfHidden = Guid.NewGuid();
        var hiddenLeaf = Guid.NewGuid();

        var roots = MenuTreeBuilder.Build(new[]
        {
            Node(visibleSection, null, MenuNodeKind.Section, "Visible", 0),
            Node(hiddenSection, null, MenuNodeKind.Section, "Oculta", 1, visible: false),
            // Hijo de una seccion oculta: NO debe reaparecer como raiz ni en ningun lado.
            Node(childOfHidden, hiddenSection, MenuNodeKind.Item, "Huerfano", 0),
            // Hoja invisible dentro de una seccion visible: no cuenta.
            Node(hiddenLeaf, visibleSection, MenuNodeKind.Item, "Item oculto", 0, visible: false),
        });

        Assert.Single(roots);
        Assert.Equal("Visible", roots[0].Name);
        Assert.Equal(0, roots[0].VisibleChildCount);
    }

    [Fact]
    public void CountsOnlyVisibleChildren()
    {
        var section = Guid.NewGuid();
        var v1 = Guid.NewGuid();
        var v2 = Guid.NewGuid();
        var hidden = Guid.NewGuid();

        var roots = MenuTreeBuilder.Build(new[]
        {
            Node(section, null, MenuNodeKind.Section, "Seccion", 0),
            Node(v1, section, MenuNodeKind.Item, "A", 0),
            Node(v2, section, MenuNodeKind.Item, "B", 1),
            Node(hidden, section, MenuNodeKind.Item, "C", 2, visible: false),
        });

        Assert.Equal(2, roots[0].VisibleChildCount);
    }

    [Fact]
    public void SimpleView_ResolvesFewerNodes_ThanCompletoView()
    {
        // "Completo": 2 secciones con 3 + 2 items.
        var completo = MenuTreeBuilder.Build(BuildView(sectionCount: 2, itemsPerSection: 3));
        // "Simple": 1 seccion con 1 item.
        var simple = MenuTreeBuilder.Build(BuildView(sectionCount: 1, itemsPerSection: 1));

        var completoTotal = completo.Sum(CountNodes);
        var simpleTotal = simple.Sum(CountNodes);
        Assert.True(simpleTotal < completoTotal, $"Simple ({simpleTotal}) debe resolver menos nodos que Completo ({completoTotal}).");
    }

    private static IEnumerable<MenuTreeBuilder.FlatNode> BuildView(int sectionCount, int itemsPerSection)
    {
        var list = new List<MenuTreeBuilder.FlatNode>();
        for (var s = 0; s < sectionCount; s++)
        {
            var sectionId = Guid.NewGuid();
            list.Add(Node(sectionId, null, MenuNodeKind.Section, $"Sec {s}", s));
            for (var i = 0; i < itemsPerSection; i++)
            {
                list.Add(Node(Guid.NewGuid(), sectionId, MenuNodeKind.Item, $"Item {s}-{i}", i));
            }
        }
        return list;
    }

    private static int CountNodes(MenuNodeDto node) => 1 + node.Children.Sum(CountNodes);
}
