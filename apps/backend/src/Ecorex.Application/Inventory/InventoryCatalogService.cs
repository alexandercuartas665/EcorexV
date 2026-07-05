using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Inventory;

/// <summary>
/// Implementacion de IInventoryCatalogService (grupo Sistema - Inventarios). El aislamiento
/// por tenant lo garantiza el filtro global; la unicidad del nombre por tenant se valida en el
/// servicio (mensaje claro) y la respalda un indice unico. Los catalogos se archivan, no se
/// borran; el guard de archivado impide dejar items o existencias apuntando a un catalogo
/// inactivo de forma incoherente.
/// </summary>
public sealed class InventoryCatalogService : IInventoryCatalogService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;

    public InventoryCatalogService(IApplicationDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    // ---- Bodegas (000556) ----

    public async Task<IReadOnlyList<WarehouseDto>> ListWarehousesAsync(
        bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var query = _db.Warehouses.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(w => w.IsActive);
        }
        return await query
            .OrderBy(w => w.SortOrder).ThenBy(w => w.Name)
            .Select(w => new WarehouseDto(
                w.Id, w.Name, w.Description, w.City, w.Address, w.Phone, w.IsActive, w.SortOrder,
                _db.ItemStocks.Count(s => s.WarehouseId == w.Id)))
            .ToListAsync(cancellationToken);
    }

    public async Task<WarehouseDto?> GetWarehouseAsync(Guid id, CancellationToken cancellationToken = default)
        => await _db.Warehouses.AsNoTracking()
            .Where(w => w.Id == id)
            .Select(w => new WarehouseDto(
                w.Id, w.Name, w.Description, w.City, w.Address, w.Phone, w.IsActive, w.SortOrder,
                _db.ItemStocks.Count(s => s.WarehouseId == w.Id)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<InventoryResult<WarehouseDto>> CreateWarehouseAsync(
        SaveWarehouseRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return InventoryResult<WarehouseDto>.Invalid("No hay tenant activo.");
        }
        var error = ValidateWarehouse(request);
        if (error is not null)
        {
            return InventoryResult<WarehouseDto>.Invalid(error);
        }
        var name = request.Name.Trim();
        if (await _db.Warehouses.AnyAsync(w => w.Name == name, cancellationToken))
        {
            return InventoryResult<WarehouseDto>.Conflict("Ya existe una bodega con ese nombre.");
        }

        var warehouse = new Warehouse
        {
            TenantId = tenantId,
            Name = name,
            City = request.City.Trim(),
            Description = Normalize(request.Description),
            Address = Normalize(request.Address),
            Phone = Normalize(request.Phone),
            SortOrder = request.SortOrder
        };
        _db.Warehouses.Add(warehouse);
        await _db.SaveChangesAsync(cancellationToken);
        return InventoryResult<WarehouseDto>.Ok(ToWarehouseDto(warehouse, 0));
    }

    public async Task<InventoryResult<WarehouseDto>> UpdateWarehouseAsync(
        Guid id, SaveWarehouseRequest request, CancellationToken cancellationToken = default)
    {
        var warehouse = await _db.Warehouses.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (warehouse is null)
        {
            return InventoryResult<WarehouseDto>.NotFound("La bodega no existe.");
        }
        var error = ValidateWarehouse(request);
        if (error is not null)
        {
            return InventoryResult<WarehouseDto>.Invalid(error);
        }
        var name = request.Name.Trim();
        if (await _db.Warehouses.AnyAsync(w => w.Name == name && w.Id != id, cancellationToken))
        {
            return InventoryResult<WarehouseDto>.Conflict("Ya existe una bodega con ese nombre.");
        }

        warehouse.Name = name;
        warehouse.City = request.City.Trim();
        warehouse.Description = Normalize(request.Description);
        warehouse.Address = Normalize(request.Address);
        warehouse.Phone = Normalize(request.Phone);
        warehouse.SortOrder = request.SortOrder;
        await _db.SaveChangesAsync(cancellationToken);
        var stockRows = await _db.ItemStocks.CountAsync(s => s.WarehouseId == id, cancellationToken);
        return InventoryResult<WarehouseDto>.Ok(ToWarehouseDto(warehouse, stockRows));
    }

    public async Task<InventoryResult<bool>> SetWarehouseActiveAsync(
        Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var warehouse = await _db.Warehouses.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (warehouse is null)
        {
            return InventoryResult<bool>.NotFound("La bodega no existe.");
        }
        if (!active && await _db.ItemStocks.AnyAsync(s => s.WarehouseId == id, cancellationToken))
        {
            return InventoryResult<bool>.Invalid(
                "La bodega tiene existencias registradas; retira el stock antes de archivarla.");
        }
        warehouse.IsActive = active;
        await _db.SaveChangesAsync(cancellationToken);
        return InventoryResult<bool>.Ok(true);
    }

    // ---- Catalogos genericos: marcas, grupos, subgrupos, tipos ----

    public async Task<IReadOnlyList<CatalogEntryDto>> ListAsync(
        CatalogKind kind, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        switch (kind)
        {
            case CatalogKind.Brand:
                var brands = _db.Brands.AsNoTracking();
                if (!includeInactive) { brands = brands.Where(b => b.IsActive); }
                return await brands.OrderBy(b => b.SortOrder).ThenBy(b => b.Name)
                    .Select(b => new CatalogEntryDto(
                        b.Id, b.Name, b.Description, b.IsActive, b.SortOrder, null, null,
                        _db.Items.Count(i => i.BrandId == b.Id)))
                    .ToListAsync(cancellationToken);
            case CatalogKind.ItemGroup:
                var groups = _db.ItemGroups.AsNoTracking();
                if (!includeInactive) { groups = groups.Where(g => g.IsActive); }
                return await groups.OrderBy(g => g.SortOrder).ThenBy(g => g.Name)
                    .Select(g => new CatalogEntryDto(
                        g.Id, g.Name, g.Description, g.IsActive, g.SortOrder, null, null,
                        _db.Items.Count(i => i.GroupId == g.Id)))
                    .ToListAsync(cancellationToken);
            case CatalogKind.ItemType:
                var types = _db.ItemTypes.AsNoTracking();
                if (!includeInactive) { types = types.Where(t => t.IsActive); }
                return await types.OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
                    .Select(t => new CatalogEntryDto(
                        t.Id, t.Name, t.Description, t.IsActive, t.SortOrder, null, null,
                        _db.Items.Count(i => i.ItemTypeId == t.Id)))
                    .ToListAsync(cancellationToken);
            case CatalogKind.ItemSubgroup:
                return await ListSubgroupsAsync(includeInactive, cancellationToken);
            case CatalogKind.Warehouse:
            default:
                throw new InvalidOperationException("Usa ListWarehousesAsync para las bodegas.");
        }
    }

    public async Task<CatalogEntryDto?> GetAsync(
        CatalogKind kind, Guid id, CancellationToken cancellationToken = default)
        => (await ListAsync(kind, includeInactive: true, cancellationToken))
            .FirstOrDefault(c => c.Id == id);

    public async Task<InventoryResult<CatalogEntryDto>> CreateAsync(
        CatalogKind kind, SaveCatalogRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return InventoryResult<CatalogEntryDto>.Invalid("No hay tenant activo.");
        }
        var error = ValidateCatalog(request);
        if (error is not null)
        {
            return InventoryResult<CatalogEntryDto>.Invalid(error);
        }
        var name = request.Name.Trim();
        if (await NameExistsAsync(kind, name, null, cancellationToken))
        {
            return InventoryResult<CatalogEntryDto>.Conflict("Ya existe un registro con ese nombre.");
        }

        switch (kind)
        {
            case CatalogKind.Brand:
                var brand = new Brand { TenantId = tenantId };
                Apply(brand, request);
                _db.Brands.Add(brand);
                await _db.SaveChangesAsync(cancellationToken);
                return InventoryResult<CatalogEntryDto>.Ok(ToDto(brand, 0));
            case CatalogKind.ItemGroup:
                var group = new ItemGroup { TenantId = tenantId };
                Apply(group, request);
                _db.ItemGroups.Add(group);
                await _db.SaveChangesAsync(cancellationToken);
                return InventoryResult<CatalogEntryDto>.Ok(ToDto(group, 0));
            case CatalogKind.ItemType:
                var type = new ItemType { TenantId = tenantId };
                Apply(type, request);
                _db.ItemTypes.Add(type);
                await _db.SaveChangesAsync(cancellationToken);
                return InventoryResult<CatalogEntryDto>.Ok(ToDto(type, 0));
            case CatalogKind.ItemSubgroup:
                if (request.GroupId is not Guid groupId)
                {
                    return InventoryResult<CatalogEntryDto>.Invalid("El subgrupo requiere un grupo.");
                }
                var parent = await _db.ItemGroups.AsNoTracking()
                    .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
                if (parent is null)
                {
                    return InventoryResult<CatalogEntryDto>.NotFound("El grupo indicado no existe.");
                }
                var subgroup = new ItemSubgroup { TenantId = tenantId, GroupId = groupId };
                Apply(subgroup, request);
                _db.ItemSubgroups.Add(subgroup);
                await _db.SaveChangesAsync(cancellationToken);
                return InventoryResult<CatalogEntryDto>.Ok(ToDto(subgroup, 0, parent.Name));
            case CatalogKind.Warehouse:
            default:
                return InventoryResult<CatalogEntryDto>.Invalid("Tipo de catalogo no soportado.");
        }
    }

    public async Task<InventoryResult<CatalogEntryDto>> UpdateAsync(
        CatalogKind kind, Guid id, SaveCatalogRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            return InventoryResult<CatalogEntryDto>.Invalid("No hay tenant activo.");
        }
        var error = ValidateCatalog(request);
        if (error is not null)
        {
            return InventoryResult<CatalogEntryDto>.Invalid(error);
        }
        var name = request.Name.Trim();
        if (await NameExistsAsync(kind, name, id, cancellationToken))
        {
            return InventoryResult<CatalogEntryDto>.Conflict("Ya existe un registro con ese nombre.");
        }

        switch (kind)
        {
            case CatalogKind.Brand:
                var brand = await _db.Brands.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
                if (brand is null) { return InventoryResult<CatalogEntryDto>.NotFound("La marca no existe."); }
                Apply(brand, request);
                await _db.SaveChangesAsync(cancellationToken);
                return InventoryResult<CatalogEntryDto>.Ok(ToDto(brand, await _db.Items.CountAsync(i => i.BrandId == id, cancellationToken)));
            case CatalogKind.ItemGroup:
                var group = await _db.ItemGroups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
                if (group is null) { return InventoryResult<CatalogEntryDto>.NotFound("El grupo no existe."); }
                Apply(group, request);
                await _db.SaveChangesAsync(cancellationToken);
                return InventoryResult<CatalogEntryDto>.Ok(ToDto(group, await _db.Items.CountAsync(i => i.GroupId == id, cancellationToken)));
            case CatalogKind.ItemType:
                var type = await _db.ItemTypes.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
                if (type is null) { return InventoryResult<CatalogEntryDto>.NotFound("El tipo no existe."); }
                Apply(type, request);
                await _db.SaveChangesAsync(cancellationToken);
                return InventoryResult<CatalogEntryDto>.Ok(ToDto(type, await _db.Items.CountAsync(i => i.ItemTypeId == id, cancellationToken)));
            case CatalogKind.ItemSubgroup:
                var subgroup = await _db.ItemSubgroups.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
                if (subgroup is null) { return InventoryResult<CatalogEntryDto>.NotFound("El subgrupo no existe."); }
                if (request.GroupId is not Guid groupId)
                {
                    return InventoryResult<CatalogEntryDto>.Invalid("El subgrupo requiere un grupo.");
                }
                var parent = await _db.ItemGroups.AsNoTracking()
                    .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
                if (parent is null)
                {
                    return InventoryResult<CatalogEntryDto>.NotFound("El grupo indicado no existe.");
                }
                subgroup.GroupId = groupId;
                Apply(subgroup, request);
                await _db.SaveChangesAsync(cancellationToken);
                return InventoryResult<CatalogEntryDto>.Ok(ToDto(subgroup,
                    await _db.Items.CountAsync(i => i.SubgroupId == id, cancellationToken), parent.Name));
            case CatalogKind.Warehouse:
            default:
                return InventoryResult<CatalogEntryDto>.Invalid("Tipo de catalogo no soportado.");
        }
    }

    public async Task<InventoryResult<bool>> SetActiveAsync(
        CatalogKind kind, Guid id, bool active, CancellationToken cancellationToken = default)
    {
        switch (kind)
        {
            case CatalogKind.Brand:
                var brand = await _db.Brands.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
                if (brand is null) { return InventoryResult<bool>.NotFound("La marca no existe."); }
                if (!active && await _db.Items.AnyAsync(i => i.BrandId == id && i.IsActive, cancellationToken))
                {
                    return InventoryResult<bool>.Invalid("La marca tiene items activos; archivalos primero.");
                }
                brand.IsActive = active;
                break;
            case CatalogKind.ItemGroup:
                var group = await _db.ItemGroups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
                if (group is null) { return InventoryResult<bool>.NotFound("El grupo no existe."); }
                if (!active && await _db.ItemSubgroups.AnyAsync(s => s.GroupId == id && s.IsActive, cancellationToken))
                {
                    return InventoryResult<bool>.Invalid("El grupo tiene subgrupos activos; archivalos primero.");
                }
                if (!active && await _db.Items.AnyAsync(i => i.GroupId == id && i.IsActive, cancellationToken))
                {
                    return InventoryResult<bool>.Invalid("El grupo tiene items activos; archivalos primero.");
                }
                group.IsActive = active;
                break;
            case CatalogKind.ItemType:
                var type = await _db.ItemTypes.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
                if (type is null) { return InventoryResult<bool>.NotFound("El tipo no existe."); }
                if (!active && await _db.Items.AnyAsync(i => i.ItemTypeId == id && i.IsActive, cancellationToken))
                {
                    return InventoryResult<bool>.Invalid("El tipo tiene items activos; archivalos primero.");
                }
                type.IsActive = active;
                break;
            case CatalogKind.ItemSubgroup:
                var subgroup = await _db.ItemSubgroups.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
                if (subgroup is null) { return InventoryResult<bool>.NotFound("El subgrupo no existe."); }
                if (!active && await _db.Items.AnyAsync(i => i.SubgroupId == id && i.IsActive, cancellationToken))
                {
                    return InventoryResult<bool>.Invalid("El subgrupo tiene items activos; archivalos primero.");
                }
                subgroup.IsActive = active;
                break;
            case CatalogKind.Warehouse:
            default:
                return InventoryResult<bool>.Invalid("Tipo de catalogo no soportado.");
        }
        await _db.SaveChangesAsync(cancellationToken);
        return InventoryResult<bool>.Ok(true);
    }

    // ---- Internos ----

    private async Task<IReadOnlyList<CatalogEntryDto>> ListSubgroupsAsync(
        bool includeInactive, CancellationToken cancellationToken)
    {
        var query = _db.ItemSubgroups.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(s => s.IsActive);
        }
        return await query.OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
            .Select(s => new CatalogEntryDto(
                s.Id, s.Name, s.Description, s.IsActive, s.SortOrder,
                s.GroupId,
                _db.ItemGroups.Where(g => g.Id == s.GroupId).Select(g => g.Name).FirstOrDefault(),
                _db.Items.Count(i => i.SubgroupId == s.Id)))
            .ToListAsync(cancellationToken);
    }

    private async Task<bool> NameExistsAsync(CatalogKind kind, string name, Guid? excludeId, CancellationToken cancellationToken)
        => kind switch
        {
            CatalogKind.Brand => await _db.Brands.AnyAsync(b => b.Name == name && b.Id != excludeId, cancellationToken),
            CatalogKind.ItemGroup => await _db.ItemGroups.AnyAsync(g => g.Name == name && g.Id != excludeId, cancellationToken),
            CatalogKind.ItemType => await _db.ItemTypes.AnyAsync(t => t.Name == name && t.Id != excludeId, cancellationToken),
            CatalogKind.ItemSubgroup => await _db.ItemSubgroups.AnyAsync(s => s.Name == name && s.Id != excludeId, cancellationToken),
            _ => false
        };

    private static void Apply(ICatalogEntity entity, SaveCatalogRequest request)
    {
        entity.Name = request.Name.Trim();
        entity.Description = Normalize(request.Description);
        entity.SortOrder = request.SortOrder;
    }

    private static CatalogEntryDto ToDto(ICatalogEntity e, int itemCount, string? groupName = null)
    {
        var groupId = e is ItemSubgroup s ? s.GroupId : (Guid?)null;
        return new CatalogEntryDto(e.Id, e.Name, e.Description, e.IsActive, e.SortOrder, groupId, groupName, itemCount);
    }

    private static WarehouseDto ToWarehouseDto(Warehouse w, int stockRows)
        => new(w.Id, w.Name, w.Description, w.City, w.Address, w.Phone, w.IsActive, w.SortOrder, stockRows);

    private static string? ValidateCatalog(SaveCatalogRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) { return "El nombre es obligatorio."; }
        if (request.Name.Trim().Length > 150) { return "El nombre no puede superar 150 caracteres."; }
        if (request.Description is { } d && d.Trim().Length > 600) { return "La descripcion no puede superar 600 caracteres."; }
        return null;
    }

    private static string? ValidateWarehouse(SaveWarehouseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) { return "El nombre es obligatorio."; }
        if (request.Name.Trim().Length > 150) { return "El nombre no puede superar 150 caracteres."; }
        if (string.IsNullOrWhiteSpace(request.City)) { return "La ciudad es obligatoria."; }
        if (request.City.Trim().Length > 120) { return "La ciudad no puede superar 120 caracteres."; }
        if (request.Address is { } a && a.Trim().Length > 300) { return "La direccion no puede superar 300 caracteres."; }
        if (request.Phone is { } p && p.Trim().Length > 80) { return "El telefono no puede superar 80 caracteres."; }
        if (request.Description is { } d && d.Trim().Length > 600) { return "La descripcion no puede superar 600 caracteres."; }
        return null;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
