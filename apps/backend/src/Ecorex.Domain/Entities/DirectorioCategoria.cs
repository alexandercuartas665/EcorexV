using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Categoria del 2do motor de contactos ("Directorio Modular", Capa 8; en el prototipo:
/// "tipo de directorio"). Arma su ficha componiendo 1+ SECCIONES
/// (<see cref="TerceroFichaDefinition"/> via <see cref="DirectorioCategoriaSeccion"/>) y declara que
/// areas la tienen autorizada. Es la pestana sobre la que se para el usuario en el listado. Un tercero
/// puede pertenecer a varias categorias a la vez (<see cref="TerceroCategoria"/>). CONFIGURABLE por
/// tenant. TENANT-SCOPED (filtro global por reflexion). Solo la usa el motor Modular; el Clasico usa
/// perfiles [Flags] en <see cref="Tercero"/>.
/// </summary>
public class DirectorioCategoria : TenantEntity
{
    /// <summary>Clave estable (slug) de la categoria, NO cambia al renombrar: "publico", "comercial",
    /// "fiscal", o el slug derivado del titulo para las que crea el tenant.</summary>
    public string CategoriaKey { get; set; } = null!;

    /// <summary>Nombre visible de la pestana/categoria (editable).</summary>
    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    /// <summary>Icono (clase Font Awesome, p.ej. "fa-handshake").</summary>
    public string? Icono { get; set; }

    /// <summary>Color de acento (hex).</summary>
    public string? Color { get; set; }

    /// <summary>Areas/roles que tienen AUTORIZADA la categoria: CSV de ids de area. Quien no la tenga no
    /// ve su pestana, no crea terceros ahi, y no ve los registros que solo viven en ella. Admin ve todo.</summary>
    public string? Areas { get; set; }

    /// <summary>Categoria protegida: no se elimina (p.ej. "Publico", base del catalogo).</summary>
    public bool Protegido { get; set; }

    /// <summary>Regla de homologacion (prototipo: bandera "homologa"): FichaKey de la seccion que los
    /// datos de esta categoria SOBRESCRIBEN al guardar (Fiscal -> "publica": el RUT homologa el
    /// directorio publico, IDE pasa a NIT). Null = sin homologacion.</summary>
    public string? HomologaSeccion { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Oculta la categoria sin eliminarla.</summary>
    public bool IsHidden { get; set; }

    /// <summary>Categoria sembrada por defecto (del prototipo: publico/comercial/proveedores/laboral/
    /// fiscal). Las de sistema no se eliminan; permite re-sembrar sin duplicar.</summary>
    public bool IsSystem { get; set; }
}
