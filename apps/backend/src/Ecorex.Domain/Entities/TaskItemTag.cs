using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Etiqueta del catalogo POR TENANT para clasificar TaskItems (a diferencia de TaskCardTag,
/// que es por tablero). TENANT-SCOPED. Nombre unico por tenant.
/// </summary>
public class TaskItemTag : TenantEntity
{
    public string Name { get; set; } = null!;

    /// <summary>Color del badge en la UI (hex o token). Default a gris si nulo.</summary>
    public string? Color { get; set; }
}
