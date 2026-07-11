using Ecorex.Domain.Enums;

namespace Ecorex.Application.MenuConfig;

/// <summary>
/// Construccion PURA (sin IO) del arbol resuelto del menu a partir de una lista plana de nodos:
/// filtra los no visibles, ordena por SortOrder (y Name como desempate estable) y anida por
/// ParentId. Un nodo cuyo padre no es visible (o no existe) NO aparece (la rama se poda entera).
/// Testeable sin base de datos.
/// </summary>
public static class MenuTreeBuilder
{
    /// <summary>Nodo plano de entrada (proyeccion minima desde la entidad MenuNode).</summary>
    public readonly record struct FlatNode(
        Guid Id,
        Guid? ParentId,
        MenuNodeKind Kind,
        string Name,
        string? IconKey,
        string? LegacyCode,
        string? Route,
        MenuNodeState State,
        bool IsVisible,
        int SortOrder,
        bool IsProcessGroup = false);

    /// <summary>
    /// Devuelve los nodos raiz (ParentId null) visibles con sus Children recursivos. Los nodos
    /// invisibles se descartan junto con toda su descendencia (no se re-cuelgan).
    /// </summary>
    public static IReadOnlyList<MenuNodeDto> Build(IEnumerable<FlatNode> nodes)
    {
        // Solo nodos visibles: un padre invisible poda a sus hijos porque estos ya no lo
        // encuentran como padre visible (childrenByParent solo indexa visibles).
        var visible = nodes.Where(n => n.IsVisible).ToList();

        var childrenByParent = visible
            .Where(n => n.ParentId is not null)
            .GroupBy(n => n.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var roots = visible.Where(n => n.ParentId is null);
        return roots
            .OrderBy(n => n.SortOrder)
            .ThenBy(n => n.Name, StringComparer.Ordinal)
            .Select(n => ToDto(n, childrenByParent))
            .ToList();
    }

    private static MenuNodeDto ToDto(FlatNode node, Dictionary<Guid, List<FlatNode>> childrenByParent)
    {
        List<MenuNodeDto> children;
        if (childrenByParent.TryGetValue(node.Id, out var kids))
        {
            children = kids
                .OrderBy(k => k.SortOrder)
                .ThenBy(k => k.Name, StringComparer.Ordinal)
                .Select(k => ToDto(k, childrenByParent))
                .ToList();
        }
        else
        {
            children = new List<MenuNodeDto>();
        }

        return new MenuNodeDto(
            node.Id, node.Kind, node.Name, node.IconKey, node.LegacyCode,
            node.Route, node.State, node.SortOrder, node.IsProcessGroup, children);
    }
}
