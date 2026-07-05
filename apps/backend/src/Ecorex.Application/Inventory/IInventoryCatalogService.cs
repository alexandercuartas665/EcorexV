namespace Ecorex.Application.Inventory;

/// <summary>
/// CRUD de los catalogos normalizados del grupo Sistema - Inventarios: bodegas (000556),
/// marcas (000502), grupos (000506), subgrupos (000606) y tipos de inventario (000498).
/// Resultados tipados (mismo patron que OrgUnitService). Todo tenant-scoped por el filtro
/// global. Los catalogos no se borran fisicamente: se archivan (IsActive=false), y solo si
/// no estan referenciados por items o existencias.
/// </summary>
public interface IInventoryCatalogService
{
    // ---- Bodegas (000556) ----
    Task<IReadOnlyList<WarehouseDto>> ListWarehousesAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<WarehouseDto?> GetWarehouseAsync(Guid id, CancellationToken cancellationToken = default);
    Task<InventoryResult<WarehouseDto>> CreateWarehouseAsync(SaveWarehouseRequest request, CancellationToken cancellationToken = default);
    Task<InventoryResult<WarehouseDto>> UpdateWarehouseAsync(Guid id, SaveWarehouseRequest request, CancellationToken cancellationToken = default);
    /// <summary>Archiva/restaura la bodega (no se puede archivar si tiene existencias).</summary>
    Task<InventoryResult<bool>> SetWarehouseActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default);

    // ---- Catalogos genericos: marcas, grupos, subgrupos, tipos ----
    Task<IReadOnlyList<CatalogEntryDto>> ListAsync(CatalogKind kind, bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<CatalogEntryDto?> GetAsync(CatalogKind kind, Guid id, CancellationToken cancellationToken = default);
    Task<InventoryResult<CatalogEntryDto>> CreateAsync(CatalogKind kind, SaveCatalogRequest request, CancellationToken cancellationToken = default);
    Task<InventoryResult<CatalogEntryDto>> UpdateAsync(CatalogKind kind, Guid id, SaveCatalogRequest request, CancellationToken cancellationToken = default);
    /// <summary>Archiva/restaura el catalogo (no se puede archivar si esta referenciado por items o, para grupos, por subgrupos).</summary>
    Task<InventoryResult<bool>> SetActiveAsync(CatalogKind kind, Guid id, bool active, CancellationToken cancellationToken = default);
}
