using Ecorex.Application.Roles;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.MenuConfig;

/// <summary>
/// Filtrado PURO (sin IO) del arbol resuelto del menu por el permiso de "Ver" del usuario (Ola B2,
/// ADR-0033). Poda los nodos Item cuyo Route sea un modulo con View=false en los permisos efectivos.
/// Una Section o Subgroup queda oculta si tras podar sus hijos ya no le queda ningun descendiente
/// visible. Los QuickLink y los Item sin Route (o cuyo Route no es un modulo del catalogo) NO se
/// tocan: no hay matriz que aplicarles. Si el usuario es Unrestricted (Owner/Admin o sin rol) el
/// arbol se devuelve intacto. Testeable sin base de datos.
/// </summary>
public static class MenuPermissionFilter
{
    /// <summary>
    /// Devuelve una copia del arbol con los modulos sin "Ver" removidos. <paramref name="permissions"/>
    /// null o Unrestricted -> el arbol se devuelve tal cual (no se filtra nada).
    /// </summary>
    public static IReadOnlyList<MenuNodeDto> Filter(
        IReadOnlyList<MenuNodeDto> roots,
        EffectivePermissions? permissions)
    {
        if (permissions is null || permissions.Unrestricted)
        {
            return roots;
        }

        var result = new List<MenuNodeDto>(roots.Count);
        foreach (var node in roots)
        {
            var kept = FilterNode(node, permissions);
            if (kept is not null)
            {
                result.Add(kept);
            }
        }
        return result;
    }

    /// <summary>Aplica el filtro sobre un ResolvedMenuDto completo (null-safe).</summary>
    public static ResolvedMenuDto? Filter(ResolvedMenuDto? menu, EffectivePermissions? permissions)
    {
        if (menu is null)
        {
            return null;
        }
        if (permissions is null || permissions.Unrestricted)
        {
            return menu;
        }
        return menu with { Roots = Filter(menu.Roots, permissions) };
    }

    /// <summary>
    /// Devuelve el nodo (con hijos ya filtrados) o null si debe ocultarse.
    /// - Item: se oculta si su Route es un modulo y no tiene View. Sin Route -> se conserva.
    /// - Section/Subgroup: se conserva solo si le queda al menos un hijo visible.
    /// - QuickLink: siempre se conserva (no es un modulo de la matriz).
    /// </summary>
    private static MenuNodeDto? FilterNode(MenuNodeDto node, EffectivePermissions permissions)
    {
        switch (node.Kind)
        {
            case MenuNodeKind.Item:
                // Un Item con Route = modulo del catalogo: sujeto al permiso View. Sin Route no es
                // un modulo enforceable (no aparece en la matriz), asi que se conserva.
                if (!string.IsNullOrWhiteSpace(node.Route)
                    && !permissions.Can(node.Route!, PermissionAction.View))
                {
                    return null;
                }
                return node;

            case MenuNodeKind.Section:
            case MenuNodeKind.Subgroup:
                var keptChildren = new List<MenuNodeDto>(node.Children.Count);
                foreach (var child in node.Children)
                {
                    var kept = FilterNode(child, permissions);
                    if (kept is not null)
                    {
                        keptChildren.Add(kept);
                    }
                }
                // Contenedor sin hijos visibles -> se oculta entero.
                if (keptChildren.Count == 0)
                {
                    return null;
                }
                return node with { Children = keptChildren };

            default:
                // QuickLink u otros: no son modulos de la matriz, se conservan tal cual.
                return node;
        }
    }
}
