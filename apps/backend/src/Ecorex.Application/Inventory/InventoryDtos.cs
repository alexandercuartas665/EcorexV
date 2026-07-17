namespace Ecorex.Application.Inventory;

/// <summary>Tipo de catalogo normalizado del grupo Sistema - Inventarios.</summary>
public enum CatalogKind
{
    Warehouse = 0,
    Brand,
    ItemGroup,
    ItemSubgroup,
    ItemType
}

// ---- Catalogos ----

/// <summary>Fila generica de catalogo (marca, grupo, subgrupo, tipo). Para bodegas, ver WarehouseDto.</summary>
public sealed record CatalogEntryDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    int SortOrder,
    // Solo para subgrupos: grupo padre.
    Guid? GroupId,
    string? GroupName,
    // Cuantos items referencian este catalogo (para el guard de archivado y la UI).
    int ItemCount);

/// <summary>Alta/edicion de un catalogo generico (marca, grupo, subgrupo, tipo).</summary>
public sealed record SaveCatalogRequest(
    string Name,
    string? Description = null,
    int SortOrder = 0,
    // Requerido solo para subgrupos (grupo padre).
    Guid? GroupId = null);

/// <summary>Bodega (000556): catalogo con ciudad, direccion y telefono.</summary>
public sealed record WarehouseDto(
    Guid Id,
    string Name,
    string? Description,
    string City,
    string? Address,
    string? Phone,
    bool IsActive,
    int SortOrder,
    // Cuantas filas de stock (con o sin existencias) apuntan a esta bodega.
    int StockRowCount);

/// <summary>Alta/edicion de una bodega.</summary>
public sealed record SaveWarehouseRequest(
    string Name,
    string City,
    string? Description = null,
    string? Address = null,
    string? Phone = null,
    int SortOrder = 0);

// ---- Items ----

/// <summary>Existencia de un item en una bodega (para el detalle y el grid).</summary>
public sealed record ItemStockDto(Guid WarehouseId, string WarehouseName, int Stock);

/// <summary>Imagen de un item por URL. EsPrincipal marca la portada; Texto se superpone sobre la imagen.</summary>
public sealed record ItemImageDto(Guid Id, string Url, string? FileName, int SortOrder, bool EsPrincipal = false, string? Texto = null);

/// <summary>Fila del grid de items (con marca/grupo/tipo resueltos, stock total y por bodega).</summary>
/// <param name="Filtrables">
/// Valores de los campos marcados "ofrecer como filtro" (ADR-0029), por FieldKey. Null si el tenant
/// no marco ninguno.
/// </param>
public sealed record ItemListDto(
    Guid Id,
    string? Sku,
    string Name,
    decimal? Price,
    Guid? BrandId,
    string? BrandName,
    Guid? GroupId,
    string? GroupName,
    Guid? SubgroupId,
    string? SubgroupName,
    Guid? ItemTypeId,
    string? ItemTypeName,
    bool IsActive,
    string? ThumbnailUrl,
    int TotalStock,
    IReadOnlyList<ItemStockDto> StockByWarehouse,
    IReadOnlyDictionary<string, string>? Filtrables = null);

/// <summary>Detalle completo de un item (edicion): datos, catalogos, imagenes y stock por bodega.</summary>
public sealed record ItemDetailDto(
    Guid Id,
    string? Sku,
    string Name,
    string? Description,
    string? Specifications,
    decimal? Price,
    Guid? BrandId,
    Guid? GroupId,
    Guid? SubgroupId,
    Guid? ItemTypeId,
    bool IsActive,
    string? FieldValuesJson,
    IReadOnlyList<ItemImageDto> Images,
    IReadOnlyList<ItemStockDto> StockByWarehouse,
    int TotalStock,
    IReadOnlyList<string> AvailableAt,
    // Datos tienda: pares etiqueta/valor ad-hoc del propio item (ya parseados del JSON).
    IReadOnlyList<DatoTiendaDto> DatosTienda);

/// <summary>
/// Alta/edicion de un item. StockByWarehouse es un mapa WarehouseId -> cantidad; el servicio
/// recrea las filas de ItemStock en transaccion (las cantidades &lt;= 0 no crean fila).
/// </summary>
public sealed record SaveItemRequest(
    string Name,
    string? Sku = null,
    string? Description = null,
    string? Specifications = null,
    decimal? Price = null,
    Guid? BrandId = null,
    Guid? GroupId = null,
    Guid? SubgroupId = null,
    Guid? ItemTypeId = null,
    string? FieldValuesJson = null,
    IReadOnlyDictionary<Guid, int>? StockByWarehouse = null,
    // Si es true, el servicio asigna un SKU consecutivo (prefijo "ITM") cuando el SKU viene vacio.
    bool GenerateSku = false,
    // Datos tienda: pares etiqueta/valor ad-hoc; el servicio serializa a DatosTiendaJson.
    IReadOnlyList<DatoTiendaDto>? DatosTienda = null);

/// <summary>Filtros y paginado del grid de items.</summary>
public sealed record ItemQuery(
    // Si se indica, solo items con stock &gt; 0 en esa bodega.
    Guid? WarehouseId = null,
    Guid? BrandId = null,
    Guid? GroupId = null,
    Guid? ItemTypeId = null,
    string? Search = null,
    bool IncludeInactive = false,
    int Page = 1,
    int PageSize = 20,
    // Filtros por campo configurable (FieldKey -> valor exigido). Ver ADR-0029.
    IReadOnlyDictionary<string, string>? FieldFilters = null);

/// <summary>Pagina del grid de items.</summary>
public sealed record ItemPageDto(IReadOnlyList<ItemListDto> Items, int Total, int Page, int PageSize);
