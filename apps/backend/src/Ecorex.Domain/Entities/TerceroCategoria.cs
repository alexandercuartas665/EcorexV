using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Pertenencia de un <see cref="Tercero"/> a una CATEGORIA del motor Modular (multi-membership; en el
/// prototipo: tabla puente "tercero_tipo"). Un tercero puede estar en varias categorias a la vez (una
/// organizacion que es cliente y proveedor); su ficha muestra la union de las secciones de todas.
/// TENANT-SCOPED. Solo la usa el motor Modular.
/// </summary>
public class TerceroCategoria : TenantEntity
{
    /// <summary>Tercero al que pertenece la categoria (FK).</summary>
    public Guid TerceroId { get; set; }
    public Tercero? Tercero { get; set; }

    /// <summary>Categoria (por su clave estable). Ver <see cref="DirectorioCategoria.CategoriaKey"/>.</summary>
    public string CategoriaKey { get; set; } = null!;
}
