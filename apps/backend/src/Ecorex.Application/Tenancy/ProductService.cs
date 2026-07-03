using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed record ProductImageDto(Guid Id, string Url, string? FileName, int SortOrder);
public sealed record ProductSedeStockDto(Guid SedeId, string SedeName, string City, int Stock);

public sealed record ProductDto(Guid Id, string Name, string? Sku, string? Description, string? Specifications, decimal? Price, string? Category,
    bool IsActive, IReadOnlyDictionary<string, string?>? FieldValues, IReadOnlyList<ProductImageDto> Images,
    IReadOnlyList<ProductSedeStockDto> Stocks)
{
    /// <summary>Stock total sumando todas las sedes.</summary>
    public int TotalStock => Stocks.Sum(s => s.Stock);
    /// <summary>Sedes donde hay existencias (stock &gt; 0).</summary>
    public IReadOnlyList<ProductSedeStockDto> AvailableAt => Stocks.Where(s => s.Stock > 0).ToList();
}

public sealed record SaveProductRequest(string Name, string? Sku, string? Description, string? Specifications, decimal? Price, string? Category,
    IReadOnlyDictionary<Guid, int>? SedeStocks, IReadOnlyDictionary<string, string?>? FieldValues);

/// <summary>
/// Articulos / productos del salon (capa 2). Tenant-scoped CRUD con campos configurables (scope Product),
/// imagenes y STOCK POR SEDE. Los archivos de imagen los sube la UI (wwwroot); aqui solo se guarda la URL.
/// </summary>
public interface IProductService
{
    /// <summary>Lista productos. Si sedeId no es null, solo los DISPONIBLES (stock &gt; 0) en esa sede.</summary>
    Task<IReadOnlyList<ProductDto>> ListAsync(Guid? sedeId = null, bool includeInactive = true, CancellationToken cancellationToken = default);
    Task<ProductDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProductDto?> CreateAsync(SaveProductRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<ProductDto?> UpdateAsync(Guid id, SaveProductRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> SetActiveAsync(Guid id, bool isActive, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<ProductImageDto?> AddImageAsync(Guid productId, string url, string? fileName, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> RemoveImageAsync(Guid imageId, Guid actorUserId, CancellationToken cancellationToken = default);
}

public sealed class ProductService : IProductService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public ProductService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db; _tenantContext = tenantContext; _audit = audit;
    }

    public async Task<IReadOnlyList<ProductDto>> ListAsync(Guid? sedeId = null, bool includeInactive = true, CancellationToken cancellationToken = default)
    {
        var query = _db.Products.AsNoTracking().Where(p => includeInactive || p.IsActive);
        if (sedeId is Guid sid)
        {
            // Solo productos con existencias (stock > 0) en la sede elegida.
            var availableIds = _db.ProductStocks.AsNoTracking().Where(st => st.SedeId == sid && st.Stock > 0).Select(st => st.ProductId);
            query = query.Where(p => availableIds.Contains(p.Id));
        }
        var products = await query.OrderBy(p => p.Name).ToListAsync(cancellationToken);
        if (products.Count == 0) { return Array.Empty<ProductDto>(); }

        var ids = products.Select(p => p.Id).ToList();
        var images = await _db.ProductImages.AsNoTracking().Where(i => ids.Contains(i.ProductId)).OrderBy(i => i.SortOrder).ToListAsync(cancellationToken);
        var stocks = await _db.ProductStocks.AsNoTracking().Where(s => ids.Contains(s.ProductId)).ToListAsync(cancellationToken);
        var sedes = await _db.Sedes.AsNoTracking().ToDictionaryAsync(s => s.Id, cancellationToken);
        return products.Select(p => Map(p, images.Where(i => i.ProductId == p.Id), stocks.Where(s => s.ProductId == p.Id), sedes)).ToList();
    }

    public async Task<ProductDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var p = await _db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (p is null) { return null; }
        var images = await _db.ProductImages.AsNoTracking().Where(i => i.ProductId == id).OrderBy(i => i.SortOrder).ToListAsync(cancellationToken);
        var stocks = await _db.ProductStocks.AsNoTracking().Where(s => s.ProductId == id).ToListAsync(cancellationToken);
        var sedes = await _db.Sedes.AsNoTracking().ToDictionaryAsync(s => s.Id, cancellationToken);
        return Map(p, images, stocks, sedes);
    }

    public async Task<ProductDto?> CreateAsync(SaveProductRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0) { return null; }

        var product = new Product
        {
            TenantId = tenantId,
            Name = name,
            Sku = Clean(request.Sku),
            Description = Clean(request.Description),
            Specifications = Clean(request.Specifications),
            Price = request.Price is decimal pr ? Math.Max(0m, pr) : null,
            Category = Clean(request.Category),
            IsActive = true,
            FieldValuesJson = SalonFieldJson.Serialize(request.FieldValues)
        };
        _db.Products.Add(product);
        ApplyStocks(product.Id, tenantId, request.SedeStocks);
        _audit.Write(actorUserId, "product.create", nameof(Product), product.Id, null, new { product.Name }, tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return await GetAsync(product.Id, cancellationToken);
    }

    public async Task<ProductDto?> UpdateAsync(Guid id, SaveProductRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null) { return null; }
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0) { return null; }

        product.Name = name;
        product.Sku = Clean(request.Sku);
        product.Description = Clean(request.Description);
        product.Specifications = Clean(request.Specifications);
        product.Price = request.Price is decimal pr ? Math.Max(0m, pr) : null;
        product.Category = Clean(request.Category);
        product.FieldValuesJson = SalonFieldJson.Serialize(request.FieldValues);

        var existing = await _db.ProductStocks.Where(s => s.ProductId == id).ToListAsync(cancellationToken);
        _db.ProductStocks.RemoveRange(existing);
        ApplyStocks(id, product.TenantId, request.SedeStocks);

        _audit.Write(actorUserId, "product.update", nameof(Product), product.Id, null, new { product.Name }, product.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<bool> SetActiveAsync(Guid id, bool isActive, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null) { return false; }
        product.IsActive = isActive;
        _audit.Write(actorUserId, "product.set-active", nameof(Product), product.Id, null, new { isActive }, product.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null) { return false; }
        _db.ProductImages.RemoveRange(await _db.ProductImages.Where(i => i.ProductId == id).ToListAsync(cancellationToken));
        _db.ProductStocks.RemoveRange(await _db.ProductStocks.Where(s => s.ProductId == id).ToListAsync(cancellationToken));
        _db.Products.Remove(product);
        _audit.Write(actorUserId, "product.delete", nameof(Product), product.Id, new { product.Name }, null, product.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ProductImageDto?> AddImageAsync(Guid productId, string url, string? fileName, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product is null || string.IsNullOrWhiteSpace(url)) { return null; }
        var next = (await _db.ProductImages.Where(i => i.ProductId == productId).Select(i => (int?)i.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var img = new ProductImage { TenantId = tenantId, ProductId = productId, Url = url.Trim(), FileName = fileName, SortOrder = next };
        _db.ProductImages.Add(img);
        await _db.SaveChangesAsync(cancellationToken);
        return new ProductImageDto(img.Id, img.Url, img.FileName, img.SortOrder);
    }

    public async Task<bool> RemoveImageAsync(Guid imageId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var img = await _db.ProductImages.FirstOrDefaultAsync(i => i.Id == imageId, cancellationToken);
        if (img is null) { return false; }
        _db.ProductImages.Remove(img);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private void ApplyStocks(Guid productId, Guid tenantId, IReadOnlyDictionary<Guid, int>? sedeStocks)
    {
        if (sedeStocks is null) { return; }
        foreach (var (sedeId, qty) in sedeStocks)
        {
            // Solo guardamos filas con un valor (0 incluido si el usuario lo dejo en esa sede).
            _db.ProductStocks.Add(new ProductStock { TenantId = tenantId, ProductId = productId, SedeId = sedeId, Stock = Math.Max(0, qty) });
        }
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static ProductDto Map(Product p, IEnumerable<ProductImage> images, IEnumerable<ProductStock> stocks, IReadOnlyDictionary<Guid, Sede> sedes)
        => new(p.Id, p.Name, p.Sku, p.Description, p.Specifications, p.Price, p.Category, p.IsActive,
            SalonFieldJson.Parse(p.FieldValuesJson),
            images.OrderBy(i => i.SortOrder).Select(i => new ProductImageDto(i.Id, i.Url, i.FileName, i.SortOrder)).ToList(),
            stocks
                .Where(s => sedes.ContainsKey(s.SedeId))
                .Select(s => new ProductSedeStockDto(s.SedeId, sedes[s.SedeId].Name, sedes[s.SedeId].City, s.Stock))
                .OrderBy(s => s.City).ThenBy(s => s.SedeName)
                .ToList());
}
