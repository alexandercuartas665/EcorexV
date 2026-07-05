using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Rasgo comun de los catalogos simples de inventario (marca, grupo, subgrupo, tipo): nombre,
/// descripcion, activo y orden. Permite tratar los cuatro catalogos de forma generica en el
/// servicio sin duplicar CRUD. La bodega NO lo implementa porque tiene campos propios (ciudad).
/// </summary>
public interface ICatalogEntity
{
    Guid Id { get; }
    string Name { get; set; }
    string? Description { get; set; }
    bool IsActive { get; set; }
    int SortOrder { get; set; }
}

/// <summary>
/// Bodega / almacen del tenant (grupo Sistema - Inventarios, legacy 000556). Catalogo
/// normalizado: el stock de cada Item se reparte por bodega (ItemStock). TENANT-SCOPED.
/// Nunca se borra fisicamente: se archiva (IsActive=false) y solo si no tiene existencias.
/// </summary>
public class Warehouse : TenantEntity
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    /// <summary>Ciudad de la bodega (obligatoria, replica de Sede.City del backbone).</summary>
    public string City { get; set; } = null!;
    public string? Address { get; set; }
    public string? Phone { get; set; }

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>
/// Marca comercial de los items (grupo Sistema - Inventarios, legacy 000502). Catalogo
/// normalizado con nombre unico por tenant. TENANT-SCOPED.
/// </summary>
public class Brand : TenantEntity, ICatalogEntity
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>
/// Grupo de inventarios (grupo Sistema - Inventarios, legacy 000506). Primer nivel de la
/// clasificacion; cada grupo agrupa varios subgrupos (ItemSubgroup). TENANT-SCOPED.
/// </summary>
public class ItemGroup : TenantEntity, ICatalogEntity
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>
/// Subgrupo de inventarios (grupo Sistema - Inventarios, legacy 000606). Segundo nivel:
/// pertenece a un ItemGroup (FK NO ACTION: un grupo con subgrupos no se borra por cascada).
/// TENANT-SCOPED.
/// </summary>
public class ItemSubgroup : TenantEntity, ICatalogEntity
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>Grupo al que pertenece el subgrupo (obligatorio).</summary>
    public Guid GroupId { get; set; }
    public ItemGroup? Group { get; set; }
}

/// <summary>
/// Tipo de inventario (grupo Sistema - Inventarios, legacy 000498): clasificacion
/// transversal del item (ej. producto, servicio, insumo). Catalogo normalizado. TENANT-SCOPED.
/// </summary>
public class ItemType : TenantEntity, ICatalogEntity
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
