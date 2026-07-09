using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Categoria del catalogo de Conceptos de actividades (modulo 000270), nivel 1 de la
/// jerarquia Categoria -> Subcategoria (calca del legacy TIPO_TAR agrupador). Ej.:
/// "CAT-01 Comercial". TENANT-SCOPED (filtro global por reflexion). Nunca se borra
/// fisicamente: se archiva (IsArchived). Unica por (TenantId, Codigo).
/// </summary>
public class ActividadCategoria : TenantEntity
{
    /// <summary>Codigo legible unico por tenant (ej. "CAT-01").</summary>
    public string Codigo { get; set; } = null!;

    public string Nombre { get; set; } = null!;

    public string? Descripcion { get; set; }

    /// <summary>Orden de visualizacion entre categorias del tenant.</summary>
    public int SortOrder { get; set; }

    /// <summary>Archivada: fuera de las listas por defecto pero conserva historia.</summary>
    public bool IsArchived { get; set; }

    /// <summary>Subcategorias (conceptos) que cuelgan de esta categoria.</summary>
    public ICollection<ActividadSubcategoria> Subcategorias { get; set; } = new List<ActividadSubcategoria>();
}
