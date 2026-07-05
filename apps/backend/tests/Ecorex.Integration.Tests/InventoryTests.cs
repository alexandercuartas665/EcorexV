using Ecorex.Application.Common;
using Ecorex.Application.Inventory;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion del modulo de inventarios (grupo Sistema - Inventarios, ADR-0027) en
/// matriz dual PostgreSQL / SQL Server: round-trip de item con stock por bodega, SKU unico por
/// tenant, aislamiento cross-tenant de items y catalogos, y validacion de que un subgrupo
/// pertenece a su grupo. Reusa las fixtures de aislamiento dual (Testcontainers).
/// </summary>
public abstract class InventoryTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected InventoryTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Item_WithStockByWarehouse_RoundTrips_AndComputesTotalsAndAvailability()
    {
        var seed = await SeedCatalogsAsync("Inv Round-Trip");

        var created = await RunItemAsync(seed, s => s.CreateAsync(new SaveItemRequest(
            Name: "Laptop Pro",
            Sku: "SKU-RT-1",
            Price: 4200000m,
            BrandId: seed.BrandId,
            GroupId: seed.GroupId,
            SubgroupId: seed.SubgroupId,
            ItemTypeId: seed.TypeId,
            StockByWarehouse: new Dictionary<Guid, int>
            {
                [seed.WarehouseA] = 12,
                [seed.WarehouseB] = 0, // cantidad 0 = no crea fila
                [seed.WarehouseC] = 5
            })));
        Assert.True(created.IsOk, created.Error);

        var detail = created.Value!;
        Assert.Equal(17, detail.TotalStock);
        // Solo bodegas con cantidad > 0 crean fila y aparecen como disponibles.
        Assert.Equal(2, detail.StockByWarehouse.Count);
        Assert.Contains("Bodega A", detail.AvailableAt);
        Assert.Contains("Bodega C", detail.AvailableAt);
        Assert.DoesNotContain("Bodega B", detail.AvailableAt);

        // El detalle vuelve a leerse coherente desde la BD.
        var reloaded = await RunItemAsync(seed, s => s.GetDetailAsync(detail.Id));
        Assert.NotNull(reloaded);
        Assert.Equal(17, reloaded!.TotalStock);

        // Update recrea el stock (sube A, agrega B, quita C).
        var updated = await RunItemAsync(seed, s => s.UpdateAsync(detail.Id, new SaveItemRequest(
            Name: "Laptop Pro",
            Sku: "SKU-RT-1",
            BrandId: seed.BrandId,
            GroupId: seed.GroupId,
            SubgroupId: seed.SubgroupId,
            ItemTypeId: seed.TypeId,
            StockByWarehouse: new Dictionary<Guid, int>
            {
                [seed.WarehouseA] = 20,
                [seed.WarehouseB] = 3
            })));
        Assert.True(updated.IsOk, updated.Error);
        Assert.Equal(23, updated.Value!.TotalStock);
        Assert.Equal(2, updated.Value.StockByWarehouse.Count);
        Assert.DoesNotContain("Bodega C", updated.Value.AvailableAt);

        // El filtro por bodega en el grid solo trae items con stock > 0 en esa bodega.
        var inA = await RunItemAsync(seed, s => s.ListAsync(new ItemQuery(WarehouseId: seed.WarehouseA)));
        Assert.Contains(inA.Items, i => i.Id == detail.Id);
        var inC = await RunItemAsync(seed, s => s.ListAsync(new ItemQuery(WarehouseId: seed.WarehouseC)));
        Assert.DoesNotContain(inC.Items, i => i.Id == detail.Id);
    }

    [Fact]
    public async Task Sku_MustBeUniquePerTenant()
    {
        var seed = await SeedCatalogsAsync("Inv SKU Unico");

        var first = await RunItemAsync(seed, s => s.CreateAsync(new SaveItemRequest("Item 1", Sku: "DUP-1")));
        Assert.True(first.IsOk, first.Error);

        var dup = await RunItemAsync(seed, s => s.CreateAsync(new SaveItemRequest("Item 2", Sku: "DUP-1")));
        Assert.Equal(InventoryServiceStatus.Conflict, dup.Status);

        // Sin SKU es valido (varios items sin SKU coexisten).
        Assert.True((await RunItemAsync(seed, s => s.CreateAsync(new SaveItemRequest("Item 3")))).IsOk);
        Assert.True((await RunItemAsync(seed, s => s.CreateAsync(new SaveItemRequest("Item 4")))).IsOk);
    }

    [Fact]
    public async Task GenerateSku_EmitsItmConsecutive()
    {
        var seed = await SeedCatalogsAsync("Inv Consecutivo");
        var a = await RunItemAsync(seed, s => s.CreateAsync(new SaveItemRequest("Auto 1", GenerateSku: true)));
        var b = await RunItemAsync(seed, s => s.CreateAsync(new SaveItemRequest("Auto 2", GenerateSku: true)));
        Assert.True(a.IsOk && b.IsOk);
        Assert.StartsWith("ITM", a.Value!.Sku);
        Assert.StartsWith("ITM", b.Value!.Sku);
        Assert.NotEqual(a.Value.Sku, b.Value.Sku);
    }

    [Fact]
    public async Task Subgroup_MustBelongToItsGroup()
    {
        var seed = await SeedCatalogsAsync("Inv Subgrupo");

        // Crear un subgrupo con grupo inexistente -> NotFound tipado.
        var badParent = await RunCatalogAsync(seed, s => s.CreateAsync(
            CatalogKind.ItemSubgroup, new SaveCatalogRequest("Huerfano", GroupId: Guid.CreateVersion7())));
        Assert.Equal(InventoryServiceStatus.NotFound, badParent.Status);

        // Sin grupo -> Invalid tipado.
        var noParent = await RunCatalogAsync(seed, s => s.CreateAsync(
            CatalogKind.ItemSubgroup, new SaveCatalogRequest("Sin grupo")));
        Assert.Equal(InventoryServiceStatus.Invalid, noParent.Status);

        // Item cuyo subgrupo no pertenece al grupo elegido -> Invalid.
        var otherGroup = await RunCatalogAsync(seed, s => s.CreateAsync(
            CatalogKind.ItemGroup, new SaveCatalogRequest("Otro grupo")));
        Assert.True(otherGroup.IsOk, otherGroup.Error);
        var mismatch = await RunItemAsync(seed, s => s.CreateAsync(new SaveItemRequest(
            "Incoherente", GroupId: otherGroup.Value!.Id, SubgroupId: seed.SubgroupId)));
        Assert.Equal(InventoryServiceStatus.Invalid, mismatch.Status);
    }

    [Fact]
    public async Task ArchiveGroup_BlockedWhileHasActiveSubgroupsOrItems()
    {
        var seed = await SeedCatalogsAsync("Inv Archivar");

        // El grupo tiene un subgrupo activo -> no se puede archivar.
        var blocked = await RunCatalogAsync(seed, s => s.SetActiveAsync(CatalogKind.ItemGroup, seed.GroupId, active: false));
        Assert.Equal(InventoryServiceStatus.Invalid, blocked.Status);

        // Una bodega con existencias no se puede archivar.
        await RunItemAsync(seed, s => s.CreateAsync(new SaveItemRequest(
            "Con stock", Sku: "ARCH-1",
            StockByWarehouse: new Dictionary<Guid, int> { [seed.WarehouseA] = 3 })));
        var whBlocked = await RunCatalogAsync(seed, s => s.SetWarehouseActiveAsync(seed.WarehouseA, active: false));
        Assert.Equal(InventoryServiceStatus.Invalid, whBlocked.Status);
    }

    [Fact]
    public async Task CrossTenant_ItemsAndCatalogs_AreIsolated()
    {
        var a = await SeedCatalogsAsync("Inv Tenant A");
        var b = await SeedCatalogsAsync("Inv Tenant B");

        var itemA = await RunItemAsync(a, s => s.CreateAsync(new SaveItemRequest("Solo de A", Sku: "ISO-A")));
        Assert.True(itemA.IsOk, itemA.Error);

        // El tenant B no ve el item ni los catalogos de A (filtro global).
        var bItems = await RunItemAsync(b, s => s.ListAsync(new ItemQuery(IncludeInactive: true)));
        Assert.DoesNotContain(bItems.Items, i => i.Id == itemA.Value!.Id);
        Assert.Null(await RunItemAsync(b, s => s.GetDetailAsync(itemA.Value!.Id)));

        var bBrands = await RunCatalogAsync(b, s => s.ListAsync(CatalogKind.Brand, includeInactive: true));
        Assert.DoesNotContain(bBrands, c => c.Id == a.BrandId);

        // Mismo SKU en dos tenants distintos NO colisiona (unico por tenant).
        var itemB = await RunItemAsync(b, s => s.CreateAsync(new SaveItemRequest("Solo de B", Sku: "ISO-A")));
        Assert.True(itemB.IsOk, itemB.Error);
    }

    // ---- Helpers ----

    private async Task<T> RunItemAsync<T>(SeedData seed, Func<IItemService, Task<T>> action)
    {
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantContext = new TestTenantContext(seed.TenantId);
        var service = new ItemService(ctx, tenantContext, new SequenceService(ctx, tenantContext));
        return await action(service);
    }

    private async Task<T> RunCatalogAsync<T>(SeedData seed, Func<IInventoryCatalogService, Task<T>> action)
    {
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var service = new InventoryCatalogService(ctx, new TestTenantContext(seed.TenantId));
        return await action(service);
    }

    private async Task<SeedData> SeedCatalogsAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
            await ctx.SaveChangesAsync();
        }

        var warehouseA = Guid.CreateVersion7();
        var warehouseB = Guid.CreateVersion7();
        var warehouseC = Guid.CreateVersion7();
        var brandId = Guid.CreateVersion7();
        var groupId = Guid.CreateVersion7();
        var subgroupId = Guid.CreateVersion7();
        var typeId = Guid.CreateVersion7();

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            ctx.Warehouses.AddRange(
                new Warehouse { Id = warehouseA, TenantId = tenantId, Name = "Bodega A", City = "Bogota" },
                new Warehouse { Id = warehouseB, TenantId = tenantId, Name = "Bodega B", City = "Cali" },
                new Warehouse { Id = warehouseC, TenantId = tenantId, Name = "Bodega C", City = "Medellin" });
            ctx.Brands.Add(new Brand { Id = brandId, TenantId = tenantId, Name = "Marca X" });
            ctx.ItemGroups.Add(new ItemGroup { Id = groupId, TenantId = tenantId, Name = "Grupo X" });
            ctx.ItemSubgroups.Add(new ItemSubgroup { Id = subgroupId, TenantId = tenantId, Name = "Subgrupo X", GroupId = groupId });
            ctx.ItemTypes.Add(new ItemType { Id = typeId, TenantId = tenantId, Name = "Tipo X" });
            await ctx.SaveChangesAsync();
        }

        return new SeedData(tenantId, warehouseA, warehouseB, warehouseC, brandId, groupId, subgroupId, typeId);
    }

    private sealed record SeedData(
        Guid TenantId, Guid WarehouseA, Guid WarehouseB, Guid WarehouseC,
        Guid BrandId, Guid GroupId, Guid SubgroupId, Guid TypeId);

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class InventoryTests_Postgres
    : InventoryTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public InventoryTests_Postgres(PostgresTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class InventoryTests_SqlServer
    : InventoryTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public InventoryTests_SqlServer(SqlServerTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}
