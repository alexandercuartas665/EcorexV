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
    bool IsProcessGroup,
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

/// <summary>
/// Nodo del arbol del EDITOR (Ola 2): incluye los invisibles y TODOS los campos editables
/// (a diferencia de MenuNodeDto, que es la proyeccion de solo-lectura para el sidebar). Children
/// recursivo, ordenado por SortOrder.
/// </summary>
public sealed record MenuEditorNodeDto(
    Guid Id,
    Guid? ParentId,
    MenuNodeKind Kind,
    string Name,
    string? IconKey,
    string? LegacyCode,
    string? Route,
    string? Description,
    string? HelpText,
    MenuNodeState State,
    bool IsVisible,
    int SortOrder,
    bool IsProcessGroup,
    IReadOnlyList<MenuEditorNodeDto> Children);

/// <summary>
/// Arbol COMPLETO de una vista para el editor (incluye invisibles). Roots con Children recursivos.
/// </summary>
public sealed record MenuViewTreeDto(
    Guid ViewId,
    string ViewName,
    string? Description,
    bool IsDefault,
    IReadOnlyList<MenuEditorNodeDto> Roots);

/// <summary>Campos editables de un nodo (UpdateNodeAsync). Null = no tocar ese campo.</summary>
public sealed record MenuNodeEditDto(
    string? Name = null,
    string? IconKey = null,
    string? LegacyCode = null,
    string? Route = null,
    string? Description = null,
    string? HelpText = null,
    MenuNodeState? State = null,
    bool? IsProcessGroup = null);

/// <summary>Usuario del tenant con su vista asignada (para la pantalla de asignacion, Ola 2).</summary>
public sealed record TenantUserViewDto(
    Guid TenantUserId,
    string Email,
    string? DisplayName,
    Guid? MenuViewId,
    string? MenuViewName);

/// <summary>
/// Nodo PORTABLE del export/import (System.Text.Json). SIN ids de BD: la jerarquia va por Children.
/// </summary>
public sealed record MenuExportNode(
    string Kind,
    string Name,
    string? IconKey,
    string? LegacyCode,
    string? Route,
    string? Description,
    string? HelpText,
    string State,
    bool IsVisible,
    int SortOrder,
    List<MenuExportNode> Children,
    bool IsProcessGroup = false);

/// <summary>Documento portable de una vista completa (export/import). No incluye IsDefault.</summary>
public sealed record MenuExportDocument(
    string Name,
    string? Description,
    List<MenuExportNode> Roots)
{
    /// <summary>Version del formato de export (para migraciones futuras del JSON).</summary>
    public int FormatVersion { get; init; } = 1;
}
