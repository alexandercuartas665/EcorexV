using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Composicion de una CATEGORIA con una SECCION (2do motor de contactos, Capa 8; en el prototipo:
/// tabla puente "tipo_seccion"). Dice que secciones arma cada categoria y en que orden. La ficha de
/// un tercero es la UNION de las secciones de todas sus categorias. TENANT-SCOPED.
/// </summary>
public class DirectorioCategoriaSeccion : TenantEntity
{
    /// <summary>Categoria (por su clave estable). Ver <see cref="DirectorioCategoria.CategoriaKey"/>.</summary>
    public string CategoriaKey { get; set; } = null!;

    /// <summary>Seccion incluida (por su clave estable). Ver <see cref="TerceroFichaDefinition.FichaKey"/>.</summary>
    public string FichaKey { get; set; } = null!;

    /// <summary>Orden de la seccion dentro de la categoria.</summary>
    public int Orden { get; set; }
}
