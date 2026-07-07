using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Perfil ("vista del sistema") del menu configurable del workspace del tenant (Ola 1 del menu
/// por perfil). Un tenant tiene una o varias vistas; cada TenantUser se asigna a una (o usa la
/// IsDefault si no tiene asignacion). El arbol de nodos cuelga de la vista (MenuNode.MenuViewId).
/// TENANT-SCOPED: recibe el filtro global de consulta por TenantId. Nombre unico por tenant.
/// </summary>
public class MenuView : TenantEntity
{
    /// <summary>Nombre de la vista (unico por tenant), ej. "Completo" o "Simple".</summary>
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    /// <summary>Vista por defecto del tenant: la usan los usuarios sin asignacion explicita.</summary>
    public bool IsDefault { get; set; }

    public int SortOrder { get; set; }
}
