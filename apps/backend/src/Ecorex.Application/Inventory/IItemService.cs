namespace Ecorex.Application.Inventory;

/// <summary>
/// CRUD del Item de inventario (000066): datos + catalogos normalizados, imagenes por URL,
/// existencias por bodega (SaveItemRequest.StockByWarehouse, recreadas en transaccion),
/// listado con filtros/paginado y detalle con stock total y disponibilidad. Resultados
/// tipados. Tenant-scoped por el filtro global. SKU unico por tenant; consecutivo opcional
/// (ISequenceService, prefijo "ITM").
/// </summary>
public interface IItemService
{
    Task<ItemPageDto> ListAsync(ItemQuery query, CancellationToken cancellationToken = default);
    Task<ItemDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);

    Task<InventoryResult<ItemDetailDto>> CreateAsync(SaveItemRequest request, CancellationToken cancellationToken = default);
    Task<InventoryResult<ItemDetailDto>> UpdateAsync(Guid id, SaveItemRequest request, CancellationToken cancellationToken = default);

    /// <summary>Archiva/restaura el item (soft-delete; no hay DELETE fisico).</summary>
    Task<InventoryResult<bool>> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default);

    // ---- Imagenes por URL ----
    Task<InventoryResult<ItemImageDto>> AddImageAsync(Guid itemId, string url, string? fileName = null, string? texto = null, CancellationToken cancellationToken = default);
    Task<InventoryResult<bool>> RemoveImageAsync(Guid imageId, CancellationToken cancellationToken = default);

    /// <summary>Marca una imagen como principal (portada) y desmarca las demas del mismo item.</summary>
    Task<InventoryResult<bool>> SetImagePrincipalAsync(Guid imageId, CancellationToken cancellationToken = default);

    /// <summary>Actualiza el texto a superponer sobre una imagen (max 200; null/vacio lo borra).</summary>
    Task<InventoryResult<bool>> UpdateImageTextoAsync(Guid imageId, string? texto, CancellationToken cancellationToken = default);

    // ---- Importar / exportar por archivo (.xlsx) ----

    /// <summary>
    /// Da de alta en lote las filas VALIDAS parseadas de la plantilla (misma via que CreateAsync: SKU unico,
    /// consecutivo, stock por bodega, validacion de catalogos). Cada fila en su propio intento; una que falla
    /// no frena las demas. Devuelve cuantas se crearon y cuantas fallaron.
    /// </summary>
    Task<(int Done, int Failed)> ImportAsync(IReadOnlyList<ItemImportXlsx.ItemImportRow> rows, CancellationToken cancellationToken = default);

    /// <summary>Exporta los items activos del tenant a un .xlsx con la misma estructura de la plantilla (re-importable).</summary>
    Task<byte[]> ExportXlsxAsync(CancellationToken cancellationToken = default);

    /// <summary>SKUs existentes del tenant (normalizados a minusculas), para detectar duplicados en la vista previa.</summary>
    Task<IReadOnlyCollection<string>> GetExistingSkusAsync(CancellationToken cancellationToken = default);
}
