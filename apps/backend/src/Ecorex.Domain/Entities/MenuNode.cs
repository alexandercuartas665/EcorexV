using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Nodo del arbol del menu configurable (adjacency-list: ParentId self-ref). Cuelga de una
/// MenuView y modela quick-links, secciones acordeon, subgrupos anidados e items hoja del
/// sidebar del workspace. Guarda una CLAVE de icono (IconKey), no el SVG: la presentacion
/// mapea la clave al SVG. TENANT-SCOPED (filtro global por TenantId). FK a la vista en cascada;
/// self-ref NO ACTION (evita multiples rutas de cascada en SQL Server).
/// </summary>
public class MenuNode : TenantEntity
{
    /// <summary>Vista a la que pertenece el nodo (cascade: borrar la vista borra sus nodos).</summary>
    public Guid MenuViewId { get; set; }
    public MenuView? MenuView { get; set; }

    /// <summary>Padre en el arbol (null = nodo de primer nivel). Self-ref NO ACTION.</summary>
    public Guid? ParentId { get; set; }
    public MenuNode? Parent { get; set; }

    public MenuNodeKind Kind { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>Clave del icono (ej. "list", "cube", "gear"); la UI la mapea al SVG. No es el SVG.</summary>
    public string? IconKey { get; set; }

    /// <summary>Codigo de modulo legacy (ej. "000850"). Se muestra como badge del item.</summary>
    public string? LegacyCode { get; set; }

    /// <summary>Ruta o slug de navegacion (ej. "inventario-items", "modulo/estados"); slug de la seccion (data-acc).</summary>
    public string? Route { get; set; }

    public string? Description { get; set; }
    public string? HelpText { get; set; }

    public MenuNodeState State { get; set; } = MenuNodeState.Ready;

    public bool IsVisible { get; set; } = true;

    public int SortOrder { get; set; }

    /// <summary>
    /// Marca este grupo (Section/Subgroup) como "despliega los procesos": en vez de items fijos,
    /// se expandira dinamicamente con las actividades tipo proceso (categoria/subcategoria con flujo)
    /// del catalogo de Conceptos. HOY solo persiste la marca (fundamento, PRE-5); el render dinamico
    /// es la Ola 4 del Modulo de Tareas.
    /// </summary>
    public bool IsProcessGroup { get; set; }
}
