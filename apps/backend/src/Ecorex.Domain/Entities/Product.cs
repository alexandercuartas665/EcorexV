using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Articulo / producto del salon (capa 2). Entidad TENANT-SCOPED. Soporta campos configurables
/// (FieldValuesJson, scope Product) e imagenes (ProductImage).
/// </summary>
public class Product : TenantEntity
{
    public string Name { get; set; } = null!;
    public string? Sku { get; set; }
    public string? Description { get; set; }
    /// <summary>Especificaciones de uso del producto (como se usa, modo de empleo, advertencias).</summary>
    public string? Specifications { get; set; }
    public decimal? Price { get; set; }
    public string? Category { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Valores de los campos configurables del producto (jsonb), indexados por FieldKey.</summary>
    public string? FieldValuesJson { get; set; }
}

/// <summary>Imagen de un producto (archivo subido a wwwroot/uploads/products). TENANT-SCOPED.</summary>
public class ProductImage : TenantEntity
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public string Url { get; set; } = null!;
    public string? FileName { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>
/// Existencias de un producto por sede (capa 2). TENANT-SCOPED. El stock es por (producto, sede):
/// un producto puede estar disponible (stock &gt; 0) en unas sedes y agotado o sin stock en otras.
/// </summary>
public class ProductStock : TenantEntity
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public Guid SedeId { get; set; }
    public Sede? Sede { get; set; }
    public int Stock { get; set; }
}
