using Ecorex.Domain.Enums;

namespace Ecorex.Application.MenuConfig;

/// <summary>
/// Nodo resuelto del arbol del menu (solo nodos visibles, ordenados por SortOrder, anidados por
/// ParentId). Children es recursivo; VisibleChildCount es el numero de hijos visibles (para el
/// contador del acordeon del prototipo).
/// </summary>
public sealed record MenuNodeDto(
    Guid Id,
    MenuNodeKind Kind,
    string Name,
    string? IconKey,
    string? LegacyCode,
    string? Route,
    MenuNodeState State,
    int SortOrder,
    IReadOnlyList<MenuNodeDto> Children)
{
    /// <summary>Numero de hijos visibles directos (contador del acordeon).</summary>
    public int VisibleChildCount => Children.Count;
}

/// <summary>Arbol resuelto de una vista: lista de nodos raiz (con Children recursivos).</summary>
public sealed record ResolvedMenuDto(
    Guid MenuViewId,
    string MenuViewName,
    IReadOnlyList<MenuNodeDto> Roots);

/// <summary>Vista del menu (perfil) para listados y edicion (Ola 2).</summary>
public sealed record MenuViewDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsDefault,
    int SortOrder,
    int NodeCount);
