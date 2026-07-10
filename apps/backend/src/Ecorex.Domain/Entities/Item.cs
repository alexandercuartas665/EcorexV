using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Item de inventario (grupo Sistema - Inventarios, legacy 000066). Portado del Product del
/// backbone CUBOT.nails pero con catalogos NORMALIZADOS: marca, grupo, subgrupo y tipo son
/// FKs a catalogos del tenant (no texto libre). Soporta campos configurables (FieldValuesJson,
/// jsonb/nvarchar dual), imagenes por URL (ItemImage) y existencias por bodega (ItemStock).
/// TENANT-SCOPED. No se borra fisicamente: se archiva (IsActive=false).
/// </summary>
public class Item : TenantEntity
{
    /// <summary>Codigo/SKU del item (unico por tenant cuando no esta vacio).</summary>
    public string? Sku { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    /// <summary>Especificaciones de uso (modo de empleo, advertencias). Texto largo.</summary>
    public string? Specifications { get; set; }

    public decimal? Price { get; set; }

    // ---- Catalogos normalizados (FKs opcionales, NO ACTION) ----
    public Guid? BrandId { get; set; }
    public Brand? Brand { get; set; }
    public Guid? GroupId { get; set; }
    public ItemGroup? Group { get; set; }
    public Guid? SubgroupId { get; set; }
    public ItemSubgroup? Subgroup { get; set; }
    public Guid? ItemTypeId { get; set; }
    public ItemType? ItemType { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Valores de los campos configurables del item (jsonb), indexados por FieldKey.
    /// Los campos disponibles los define <see cref="ItemFieldDefinition"/> segun el tipo del item.</summary>
    public string? FieldValuesJson { get; set; }

    /// <summary>Campos ad-hoc creados dentro del propio item ("Datos tienda"): pares etiqueta/valor
    /// (jsonb). A diferencia de <see cref="FieldValuesJson"/>, no dependen de un catalogo de definiciones;
    /// son datos libres que el usuario agrega en la ficha del item.</summary>
    public string? DatosTiendaJson { get; set; }
}

/// <summary>
/// Imagen de un item por URL (grupo Sistema - Inventarios). Vive y muere con el item (FK
/// cascade). Guarda la URL (500) igual que otros modulos que almacenan URLs (LeadFile,
/// TaskCardAttachment). TENANT-SCOPED.
/// </summary>
public class ItemImage : TenantEntity
{
    public Guid ItemId { get; set; }
    public Item? Item { get; set; }
    public string Url { get; set; } = null!;
    public string? FileName { get; set; }
    public int SortOrder { get; set; }

    /// <summary>Imagen principal (portada) del item. Solo una por item deberia estar marcada;
    /// el servicio garantiza la exclusividad al marcarla.</summary>
    public bool EsPrincipal { get; set; }

    /// <summary>Texto opcional a superponer sobre la imagen (titulo/etiqueta), max 200.</summary>
    public string? Texto { get; set; }
}

/// <summary>
/// Existencias de un item por bodega (grupo Sistema - Inventarios). El stock es por
/// (item, bodega): un item puede estar disponible (stock &gt; 0) en unas bodegas y agotado o
/// sin fila en otras. FK cascade hacia el item, NO ACTION hacia la bodega (una bodega con
/// existencias no se borra por cascada; se archiva). Unico por (ItemId, WarehouseId).
/// TENANT-SCOPED.
/// </summary>
public class ItemStock : TenantEntity
{
    public Guid ItemId { get; set; }
    public Item? Item { get; set; }
    public Guid WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public int Stock { get; set; }
}
