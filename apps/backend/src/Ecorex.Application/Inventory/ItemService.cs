using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Inventory;

/// <summary>
/// Implementacion de IItemService (000066). Aislamiento por tenant via filtro global. El SKU
/// es unico por tenant cuando no esta vacio (validado con mensaje claro + indice unico). El
/// stock por bodega se recrea en transaccion (SaveItemRequest.StockByWarehouse). El consecutivo
/// opcional usa ISequenceService (prefijo "ITM"); EnsureSequenceAsync se llama ANTES de abrir
/// la transaccion (en PostgreSQL una violacion de unicidad dentro de la transaccion la
/// envenenaria, ver SequenceService).
/// </summary>
public sealed class ItemService : IItemService
{
    private const string SequenceCode = "ITM";
    private const string SequencePrefix = "ITM";
    private const int SequencePadding = 6;

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ISequenceService _sequences;

    public ItemService(IApplicationDbContext db, ITenantContext tenantContext, ISequenceService sequences)
    {
        _db = db;
        _tenantContext = tenantContext;
        _sequences = sequences;
    }

    public async Task<ItemPageDto> ListAsync(ItemQuery query, CancellationToken cancellationToken = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 200 ? 20 : query.PageSize;

        var items = _db.Items.AsNoTracking();
        if (!query.IncludeInactive)
        {
            items = items.Where(i => i.IsActive);
        }
        if (query.BrandId is Guid brandId)
        {
            items = items.Where(i => i.BrandId == brandId);
        }
        if (query.GroupId is Guid groupId)
        {
            items = items.Where(i => i.GroupId == groupId);
        }
        if (query.ItemTypeId is Guid typeId)
        {
            items = items.Where(i => i.ItemTypeId == typeId);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            items = items.Where(i =>
                EF.Functions.Like(i.Name, $"%{term}%") ||
                (i.Sku != null && EF.Functions.Like(i.Sku, $"%{term}%")));
        }
        // Filtro de disponibles: solo items con stock > 0 en la bodega indicada.
        if (query.WarehouseId is Guid warehouseId)
        {
            items = items.Where(i =>
                _db.ItemStocks.Any(s => s.ItemId == i.Id && s.WarehouseId == warehouseId && s.Stock > 0));
        }

        // Filtros por campo configurable (ADR-0029). Se resuelven ANTES de paginar: hacerlo sobre la
        // pagina ya cortada solo filtraria lo visible y el total mentiria.
        //
        // No se filtra con LIKE sobre el JSON: JsonSerializer escapa lo no-ASCII ("Algodon" con
        // tilde queda "ón"), asi que el LIKE fallaria en silencio justo con los valores en
        // espanol. Se traen las claves candidatas (id + json) y se resuelve en memoria; el catalogo
        // de un tenant es de un tamano manejable y solo ocurre si hay filtros activos.
        if (query.FieldFilters is { Count: > 0 })
        {
            var candidatos = await items
                .Select(i => new { i.Id, i.FieldValuesJson })
                .ToListAsync(cancellationToken);

            var permitidos = candidatos
                .Where(c => MatchesFieldFilters(c.FieldValuesJson, query.FieldFilters))
                .Select(c => c.Id)
                .ToHashSet();

            items = items.Where(i => permitidos.Contains(i.Id));
        }

        var total = await items.CountAsync(cancellationToken);
        var pageItems = await items
            .OrderBy(i => i.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new
            {
                i.Id,
                i.Sku,
                i.Name,
                i.Price,
                i.BrandId,
                BrandName = _db.Brands.Where(b => b.Id == i.BrandId).Select(b => b.Name).FirstOrDefault(),
                i.GroupId,
                GroupName = _db.ItemGroups.Where(g => g.Id == i.GroupId).Select(g => g.Name).FirstOrDefault(),
                i.SubgroupId,
                SubgroupName = _db.ItemSubgroups.Where(s => s.Id == i.SubgroupId).Select(s => s.Name).FirstOrDefault(),
                i.ItemTypeId,
                ItemTypeName = _db.ItemTypes.Where(t => t.Id == i.ItemTypeId).Select(t => t.Name).FirstOrDefault(),
                i.IsActive,
                i.FieldValuesJson,
                // La miniatura prefiere la imagen principal; si ninguna esta marcada, la primera por orden.
                ThumbnailUrl = _db.ItemImages.Where(im => im.ItemId == i.Id)
                    .OrderByDescending(im => im.EsPrincipal).ThenBy(im => im.SortOrder)
                    .Select(im => im.Url).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var ids = pageItems.Select(i => i.Id).ToList();
        var stockRows = await _db.ItemStocks.AsNoTracking()
            .Where(s => ids.Contains(s.ItemId))
            .Select(s => new
            {
                s.ItemId,
                s.WarehouseId,
                WarehouseName = _db.Warehouses.Where(w => w.Id == s.WarehouseId).Select(w => w.Name).FirstOrDefault() ?? "",
                s.Stock
            })
            .ToListAsync(cancellationToken);
        var stockByItem = stockRows
            .GroupBy(s => s.ItemId)
            .ToDictionary(g => g.Key, g => g
                .OrderBy(s => s.WarehouseName)
                .Select(s => new ItemStockDto(s.WarehouseId, s.WarehouseName, s.Stock)).ToList());

        // Claves marcadas "ofrecer como filtro": se consultan una vez, no por fila.
        var filterKeys = await _db.ItemFieldDefinitions
            .AsNoTracking()
            .Where(f => f.ShowInFilter)
            .Select(f => f.FieldKey)
            .ToListAsync(cancellationToken);

        var rows = pageItems.Select(i =>
        {
            var byWarehouse = stockByItem.TryGetValue(i.Id, out var list) ? list : new List<ItemStockDto>();
            return new ItemListDto(
                i.Id, i.Sku, i.Name, i.Price, i.BrandId, i.BrandName, i.GroupId, i.GroupName,
                i.SubgroupId, i.SubgroupName, i.ItemTypeId, i.ItemTypeName, i.IsActive, i.ThumbnailUrl,
                byWarehouse.Sum(s => s.Stock), byWarehouse,
                ExtractFilterables(i.FieldValuesJson, filterKeys));
        }).ToList();

        return new ItemPageDto(rows, total, page, pageSize);
    }

    /// <summary>Deserializa los valores del item. Vacio si no hay nada o el JSON esta corrupto.</summary>
    private static Dictionary<string, string> ReadFieldValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return new(StringComparer.Ordinal); }
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            // Un item con el JSON corrupto no debe tumbar el listado entero.
            return new(StringComparer.Ordinal);
        }
    }

    /// <summary>True si el item cumple TODOS los filtros de campo pedidos.</summary>
    private static bool MatchesFieldFilters(string? json, IReadOnlyDictionary<string, string> filters)
    {
        var values = ReadFieldValues(json);
        foreach (var (key, expected) in filters)
        {
            if (!values.TryGetValue(key, out var actual)) { return false; }
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) { return false; }
        }
        return true;
    }

    /// <summary>Solo los valores de las claves filtrables. Null si el tenant no marco ninguna.</summary>
    private static IReadOnlyDictionary<string, string>? ExtractFilterables(
        string? json, IReadOnlyCollection<string> filterKeys)
    {
        if (filterKeys.Count == 0) { return null; }

        var values = ReadFieldValues(json);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in filterKeys)
        {
            if (values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)) { result[key] = v; }
        }
        return result.Count > 0 ? result : null;
    }

    public async Task<ItemDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return null;
        }

        var images = await _db.ItemImages.AsNoTracking()
            .Where(im => im.ItemId == id)
            .OrderByDescending(im => im.EsPrincipal).ThenBy(im => im.SortOrder)
            .Select(im => new ItemImageDto(im.Id, im.Url, im.FileName, im.SortOrder, im.EsPrincipal, im.Texto))
            .ToListAsync(cancellationToken);

        var stock = await _db.ItemStocks.AsNoTracking()
            .Where(s => s.ItemId == id)
            .Select(s => new ItemStockDto(
                s.WarehouseId,
                _db.Warehouses.Where(w => w.Id == s.WarehouseId).Select(w => w.Name).FirstOrDefault() ?? "",
                s.Stock))
            .ToListAsync(cancellationToken);
        stock = stock.OrderBy(s => s.WarehouseName).ToList();

        var availableAt = stock.Where(s => s.Stock > 0).Select(s => s.WarehouseName).ToList();
        return new ItemDetailDto(
            item.Id, item.Sku, item.Name, item.Description, item.Specifications, item.Price,
            item.BrandId, item.GroupId, item.SubgroupId, item.ItemTypeId, item.IsActive,
            item.FieldValuesJson, images, stock, stock.Sum(s => s.Stock), availableAt,
            DatosTiendaJson.Parse(item.DatosTiendaJson));
    }

    public async Task<InventoryResult<ItemDetailDto>> CreateAsync(
        SaveItemRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return InventoryResult<ItemDetailDto>.Invalid("No hay tenant activo.");
        }
        var validation = ValidateItem(request);
        if (validation is not null)
        {
            return InventoryResult<ItemDetailDto>.Invalid(validation);
        }

        var sku = Normalize(request.Sku);
        // SKU consecutivo opcional: se emite fuera de la transaccion de stock.
        if (sku is null && request.GenerateSku)
        {
            await _sequences.EnsureSequenceAsync(SequenceCode, cancellationToken);
            sku = await _sequences.NextAsync(SequenceCode, SequencePrefix, SequencePadding, cancellationToken);
        }
        if (sku is not null && await _db.Items.AnyAsync(i => i.Sku == sku, cancellationToken))
        {
            return InventoryResult<ItemDetailDto>.Conflict($"Ya existe un item con el SKU '{sku}'.");
        }
        var catalogError = await ValidateCatalogRefsAsync(request, cancellationToken);
        if (catalogError is not null)
        {
            return InventoryResult<ItemDetailDto>.Invalid(catalogError);
        }
        var stockError = await ValidateStockAsync(request, cancellationToken);
        if (stockError is not null)
        {
            return InventoryResult<ItemDetailDto>.Invalid(stockError);
        }

        var item = new Item { TenantId = tenantId };
        ApplyItem(item, request, sku);

        var joinTx = _db.HasActiveTransaction;
        var tx = joinTx ? null : await _db.BeginTransactionAsync(cancellationToken);
        try
        {
            _db.Items.Add(item);
            await _db.SaveChangesAsync(cancellationToken);
            await ReplaceStockAsync(item.Id, tenantId, request.StockByWarehouse, cancellationToken);
            if (tx is not null) { await tx.CommitAsync(cancellationToken); }
        }
        catch
        {
            if (tx is not null) { await tx.RollbackAsync(cancellationToken); }
            throw;
        }
        finally
        {
            if (tx is not null) { await tx.DisposeAsync(); }
        }

        return InventoryResult<ItemDetailDto>.Ok((await GetDetailAsync(item.Id, cancellationToken))!);
    }

    public async Task<InventoryResult<ItemDetailDto>> UpdateAsync(
        Guid id, SaveItemRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return InventoryResult<ItemDetailDto>.Invalid("No hay tenant activo.");
        }
        var item = await _db.Items.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return InventoryResult<ItemDetailDto>.NotFound("El item no existe.");
        }
        var validation = ValidateItem(request);
        if (validation is not null)
        {
            return InventoryResult<ItemDetailDto>.Invalid(validation);
        }

        var sku = Normalize(request.Sku);
        if (sku is null && request.GenerateSku && item.Sku is null)
        {
            await _sequences.EnsureSequenceAsync(SequenceCode, cancellationToken);
            sku = await _sequences.NextAsync(SequenceCode, SequencePrefix, SequencePadding, cancellationToken);
        }
        if (sku is not null && await _db.Items.AnyAsync(i => i.Sku == sku && i.Id != id, cancellationToken))
        {
            return InventoryResult<ItemDetailDto>.Conflict($"Ya existe un item con el SKU '{sku}'.");
        }
        var catalogError = await ValidateCatalogRefsAsync(request, cancellationToken);
        if (catalogError is not null)
        {
            return InventoryResult<ItemDetailDto>.Invalid(catalogError);
        }
        var stockError = await ValidateStockAsync(request, cancellationToken);
        if (stockError is not null)
        {
            return InventoryResult<ItemDetailDto>.Invalid(stockError);
        }

        ApplyItem(item, request, sku);

        var joinTx = _db.HasActiveTransaction;
        var tx = joinTx ? null : await _db.BeginTransactionAsync(cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await ReplaceStockAsync(item.Id, tenantId, request.StockByWarehouse, cancellationToken);
            if (tx is not null) { await tx.CommitAsync(cancellationToken); }
        }
        catch
        {
            if (tx is not null) { await tx.RollbackAsync(cancellationToken); }
            throw;
        }
        finally
        {
            if (tx is not null) { await tx.DisposeAsync(); }
        }

        return InventoryResult<ItemDetailDto>.Ok((await GetDetailAsync(item.Id, cancellationToken))!);
    }

    public async Task<InventoryResult<bool>> SetActiveAsync(
        Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var item = await _db.Items.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return InventoryResult<bool>.NotFound("El item no existe.");
        }
        item.IsActive = active;
        await _db.SaveChangesAsync(cancellationToken);
        return InventoryResult<bool>.Ok(true);
    }

    // ---- Imagenes por URL ----

    public async Task<InventoryResult<ItemImageDto>> AddImageAsync(
        Guid itemId, string url, string? fileName = null, string? texto = null, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return InventoryResult<ItemImageDto>.Invalid("No hay tenant activo.");
        }
        if (string.IsNullOrWhiteSpace(url))
        {
            return InventoryResult<ItemImageDto>.Invalid("La URL de la imagen es obligatoria.");
        }
        if (url.Trim().Length > 500)
        {
            return InventoryResult<ItemImageDto>.Invalid("La URL no puede superar 500 caracteres.");
        }
        if (!await _db.Items.AnyAsync(i => i.Id == itemId, cancellationToken))
        {
            return InventoryResult<ItemImageDto>.NotFound("El item no existe.");
        }

        var count = await _db.ItemImages.CountAsync(im => im.ItemId == itemId, cancellationToken);
        var nextOrder = (await _db.ItemImages
            .Where(im => im.ItemId == itemId)
            .Select(im => (int?)im.SortOrder)
            .MaxAsync(cancellationToken) ?? -1) + 1;

        var image = new ItemImage
        {
            TenantId = tenantId,
            ItemId = itemId,
            Url = url.Trim(),
            FileName = Normalize(fileName),
            SortOrder = nextOrder,
            // La primera imagen del item queda como principal automaticamente.
            EsPrincipal = count == 0,
            Texto = NormalizeTexto(texto)
        };
        _db.ItemImages.Add(image);
        await _db.SaveChangesAsync(cancellationToken);
        return InventoryResult<ItemImageDto>.Ok(new ItemImageDto(image.Id, image.Url, image.FileName, image.SortOrder, image.EsPrincipal, image.Texto));
    }

    public async Task<InventoryResult<bool>> RemoveImageAsync(
        Guid imageId, CancellationToken cancellationToken = default)
    {
        var image = await _db.ItemImages.FirstOrDefaultAsync(im => im.Id == imageId, cancellationToken);
        if (image is null)
        {
            return InventoryResult<bool>.NotFound("La imagen no existe.");
        }
        var wasPrincipal = image.EsPrincipal;
        var itemId = image.ItemId;
        _db.ItemImages.Remove(image);
        await _db.SaveChangesAsync(cancellationToken);

        // Si se quito la principal y quedan imagenes, promover la primera por orden a principal.
        if (wasPrincipal)
        {
            var next = await _db.ItemImages
                .Where(im => im.ItemId == itemId)
                .OrderBy(im => im.SortOrder)
                .FirstOrDefaultAsync(cancellationToken);
            if (next is not null)
            {
                next.EsPrincipal = true;
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
        return InventoryResult<bool>.Ok(true);
    }

    public async Task<InventoryResult<bool>> SetImagePrincipalAsync(
        Guid imageId, CancellationToken cancellationToken = default)
    {
        var image = await _db.ItemImages.FirstOrDefaultAsync(im => im.Id == imageId, cancellationToken);
        if (image is null)
        {
            return InventoryResult<bool>.NotFound("La imagen no existe.");
        }
        // Exclusividad: desmarca las demas imagenes del mismo item y marca esta.
        var siblings = await _db.ItemImages
            .Where(im => im.ItemId == image.ItemId && im.Id != image.Id && im.EsPrincipal)
            .ToListAsync(cancellationToken);
        foreach (var s in siblings) { s.EsPrincipal = false; }
        image.EsPrincipal = true;
        await _db.SaveChangesAsync(cancellationToken);
        return InventoryResult<bool>.Ok(true);
    }

    public async Task<InventoryResult<bool>> UpdateImageTextoAsync(
        Guid imageId, string? texto, CancellationToken cancellationToken = default)
    {
        var image = await _db.ItemImages.FirstOrDefaultAsync(im => im.Id == imageId, cancellationToken);
        if (image is null)
        {
            return InventoryResult<bool>.NotFound("La imagen no existe.");
        }
        image.Texto = NormalizeTexto(texto);
        await _db.SaveChangesAsync(cancellationToken);
        return InventoryResult<bool>.Ok(true);
    }

    private static string? NormalizeTexto(string? texto)
    {
        var t = Normalize(texto);
        return t is { Length: > 200 } ? t[..200] : t;
    }

    // ---- Internos ----

    private async Task ReplaceStockAsync(
        Guid itemId, Guid tenantId, IReadOnlyDictionary<Guid, int>? stockByWarehouse, CancellationToken cancellationToken)
    {
        // Recrea las filas de stock del item: elimina las actuales y agrega las nuevas con
        // cantidad > 0 (cantidad <= 0 = sin fila = no disponible). Corre dentro de la
        // transaccion ambiente del caso de uso.
        var existing = await _db.ItemStocks.Where(s => s.ItemId == itemId).ToListAsync(cancellationToken);
        if (existing.Count > 0)
        {
            _db.ItemStocks.RemoveRange(existing);
        }
        if (stockByWarehouse is not null)
        {
            foreach (var (warehouseId, qty) in stockByWarehouse)
            {
                if (qty <= 0) { continue; }
                _db.ItemStocks.Add(new ItemStock
                {
                    TenantId = tenantId,
                    ItemId = itemId,
                    WarehouseId = warehouseId,
                    Stock = qty
                });
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<string?> ValidateCatalogRefsAsync(SaveItemRequest request, CancellationToken cancellationToken)
    {
        if (request.BrandId is Guid brandId && !await _db.Brands.AnyAsync(b => b.Id == brandId, cancellationToken))
        {
            return "La marca indicada no existe.";
        }
        if (request.GroupId is Guid groupId && !await _db.ItemGroups.AnyAsync(g => g.Id == groupId, cancellationToken))
        {
            return "El grupo indicado no existe.";
        }
        if (request.ItemTypeId is Guid typeId && !await _db.ItemTypes.AnyAsync(t => t.Id == typeId, cancellationToken))
        {
            return "El tipo indicado no existe.";
        }
        if (request.SubgroupId is Guid subgroupId)
        {
            var subgroup = await _db.ItemSubgroups.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == subgroupId, cancellationToken);
            if (subgroup is null)
            {
                return "El subgrupo indicado no existe.";
            }
            // Coherencia: el subgrupo debe pertenecer al grupo elegido (si hay grupo).
            if (request.GroupId is Guid g && subgroup.GroupId != g)
            {
                return "El subgrupo no pertenece al grupo seleccionado.";
            }
        }
        return null;
    }

    private async Task<string?> ValidateStockAsync(SaveItemRequest request, CancellationToken cancellationToken)
    {
        if (request.StockByWarehouse is null || request.StockByWarehouse.Count == 0)
        {
            return null;
        }
        foreach (var (warehouseId, qty) in request.StockByWarehouse)
        {
            if (qty < 0)
            {
                return "Las cantidades de stock no pueden ser negativas.";
            }
            if (qty > 0 && !await _db.Warehouses.AnyAsync(w => w.Id == warehouseId && w.IsActive, cancellationToken))
            {
                return "Una de las bodegas de stock no existe o esta archivada.";
            }
        }
        return null;
    }

    private static void ApplyItem(Item item, SaveItemRequest request, string? sku)
    {
        item.Sku = sku;
        item.Name = request.Name.Trim();
        item.Description = Normalize(request.Description);
        item.Specifications = Normalize(request.Specifications);
        item.Price = request.Price;
        item.BrandId = request.BrandId;
        item.GroupId = request.GroupId;
        item.SubgroupId = request.SubgroupId;
        item.ItemTypeId = request.ItemTypeId;
        item.FieldValuesJson = Normalize(request.FieldValuesJson);
        item.DatosTiendaJson = DatosTiendaJson.Serialize(request.DatosTienda);
    }

    private static string? ValidateItem(SaveItemRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) { return "El nombre es obligatorio."; }
        if (request.Name.Trim().Length > 200) { return "El nombre no puede superar 200 caracteres."; }
        if (request.Sku is { } sku && sku.Trim().Length > 80) { return "El SKU no puede superar 80 caracteres."; }
        if (request.Description is { } d && d.Trim().Length > 2000) { return "La descripcion no puede superar 2000 caracteres."; }
        if (request.Price is < 0) { return "El precio no puede ser negativo."; }
        return null;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
