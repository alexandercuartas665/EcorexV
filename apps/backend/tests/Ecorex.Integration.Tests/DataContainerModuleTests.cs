using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Application.MenuConfig;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion de la PUBLICACION de una tabla del Contenedor de datos como modulo del menu
/// (<c>IDataContainerModuleService</c>), en matriz dual PostgreSQL / SQL Server.
///
/// Lo que de verdad blindan: que la RUTA sea inmutable y sobreviva al renombrado y a la
/// despublicacion. La ruta es la CLAVE del modulo en la matriz de roles
/// (RolService.GetModuleCatalogAsync la deriva del menu), asi que si cambiara dejaria huerfanos, en
/// silencio, los permisos que los roles ya asignaron. Es el bug que hoy tienen los formularios y que
/// este servicio corrige a proposito.
/// </summary>
public abstract class DataContainerModuleTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected DataContainerModuleTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Publish_CreatesMenuNode_AndFreezesRoute()
    {
        var seed = await SeedAsync("Pub Alta", "Ordenes de Compra");

        var res = await RunAsync(seed, s => s.PublishAsync(
            new PublishContainerRequest(seed.ContainerId, seed.MenuViewId, seed.SectionNodeId, Icon: "cube")));
        Assert.True(res.IsOk, res.Error);

        var dto = res.Value!;
        Assert.True(dto.IsPublished);
        Assert.Equal("dc/ordenes-de-compra", dto.ModuleRoute);   // slug ASCII, sin barra inicial
        Assert.NotNull(dto.MenuNodeId);

        // El nodo existe en el menu, es Item, cuelga del grupo elegido y su Route == la clave del modulo.
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var node = await ctx.MenuNodes.AsNoTracking().FirstAsync(n => n.Id == dto.MenuNodeId!.Value);
        Assert.Equal(MenuNodeKind.Item, node.Kind);
        Assert.Equal("Ordenes de Compra", node.Name);
        Assert.Equal("dc/ordenes-de-compra", node.Route);
        Assert.Equal(seed.SectionNodeId, node.ParentId);
        Assert.Equal("cube", node.IconKey);
        Assert.Equal(MenuNodeState.Ready, node.State);
    }

    [Fact]
    public async Task Rename_UpdatesNodeName_ButNeverTheRoute()
    {
        var seed = await SeedAsync("Pub Renombre", "Clientes");
        var pub = await RunAsync(seed, s => s.PublishAsync(
            new PublishContainerRequest(seed.ContainerId, seed.MenuViewId, seed.SectionNodeId)));
        Assert.Equal("dc/clientes", pub.Value!.ModuleRoute);

        // Se renombra la tabla y se reconcilia el menu.
        await using (var ctx = _fixture.CreateContext(seed.TenantId))
        {
            var c = await ctx.DataContainers.FirstAsync(x => x.Id == seed.ContainerId);
            c.Name = "Clientes VIP";
            await ctx.SaveChangesAsync();
        }
        Assert.True(await RunAsync(seed, s => s.SyncNodeNameAsync(seed.ContainerId)));

        var after = await RunAsync(seed, s => s.GetAsync(seed.ContainerId));
        // La etiqueta cambia; la ruta NO (si cambiara, los permisos del rol quedarian huerfanos).
        Assert.Equal("dc/clientes", after!.ModuleRoute);

        await using var ctx2 = _fixture.CreateContext(seed.TenantId);
        var node = await ctx2.MenuNodes.AsNoTracking().FirstAsync(n => n.Id == after.MenuNodeId!.Value);
        Assert.Equal("Clientes VIP", node.Name);
        Assert.Equal("dc/clientes", node.Route);
    }

    [Fact]
    public async Task Unpublish_RemovesNode_KeepsRoute_AndRepublishReusesIt()
    {
        var seed = await SeedAsync("Pub Ciclo", "Facturas");
        var pub = await RunAsync(seed, s => s.PublishAsync(
            new PublishContainerRequest(seed.ContainerId, seed.MenuViewId, seed.SectionNodeId)));
        var nodeId = pub.Value!.MenuNodeId!.Value;
        var route = pub.Value.ModuleRoute!;

        var un = await RunAsync(seed, s => s.UnpublishAsync(seed.ContainerId));
        Assert.True(un.IsOk);

        // El nodo se fue del menu...
        await using (var ctx = _fixture.CreateContext(seed.TenantId))
        {
            Assert.False(await ctx.MenuNodes.AnyAsync(n => n.Id == nodeId));
        }
        // ...pero la ruta se conserva, y la pagina deja de responder.
        var off = await RunAsync(seed, s => s.GetAsync(seed.ContainerId));
        Assert.False(off!.IsPublished);
        Assert.Equal(route, off.ModuleRoute);
        Assert.Null(await RunAsync(seed, s => s.ResolveByRouteAsync(route)));

        // Al republicar se REUSA la misma ruta: los permisos ya asignados siguen valiendo.
        var re = await RunAsync(seed, s => s.PublishAsync(
            new PublishContainerRequest(seed.ContainerId, seed.MenuViewId, seed.SectionNodeId)));
        Assert.True(re.IsOk, re.Error);
        Assert.Equal(route, re.Value!.ModuleRoute);
        Assert.NotEqual(nodeId, re.Value.MenuNodeId);
        Assert.True(re.Value.IsPublished);
    }

    [Fact]
    public async Task Routes_AreUniquePerTenant_EvenWithTheSameName()
    {
        var seed = await SeedAsync("Pub Choque", "Clientes");
        var first = await RunAsync(seed, s => s.PublishAsync(
            new PublishContainerRequest(seed.ContainerId, seed.MenuViewId, seed.SectionNodeId)));
        Assert.Equal("dc/clientes", first.Value!.ModuleRoute);

        // Otra tabla, en otro modelo, con el MISMO nombre.
        var otherId = await AddTableAsync(seed.TenantId, "Clientes", modelName: "Otro modelo");
        var second = await RunAsync(seed, s => s.PublishAsync(
            new PublishContainerRequest(otherId, seed.MenuViewId, seed.SectionNodeId)));
        Assert.True(second.IsOk, second.Error);
        Assert.Equal("dc/clientes-2", second.Value!.ModuleRoute);
    }

    [Fact]
    public async Task Submodel_CannotBePublished()
    {
        var seed = await SeedAsync("Pub Submodelo", "Pedidos");
        var childId = await AddTableAsync(seed.TenantId, "Lineas", modelName: null, parentContainerId: seed.ContainerId);

        var res = await RunAsync(seed, s => s.PublishAsync(
            new PublishContainerRequest(childId, seed.MenuViewId, seed.SectionNodeId)));
        Assert.Equal(ModulePublishStatus.Invalid, res.Status);
    }

    [Fact]
    public async Task ResolveByRoute_IsTenantIsolated()
    {
        var a = await SeedAsync("Pub Tenant A", "Secretos");
        var pub = await RunAsync(a, s => s.PublishAsync(
            new PublishContainerRequest(a.ContainerId, a.MenuViewId, a.SectionNodeId)));
        var route = pub.Value!.ModuleRoute!;

        // Otro tenant pide la misma ruta: el filtro global no le entrega la tabla de A.
        var b = await SeedAsync("Pub Tenant B", "Otra");
        Assert.Null(await RunAsync(b, s => s.ResolveByRouteAsync(route)));

        // Y el dueno si la resuelve.
        Assert.NotNull(await RunAsync(a, s => s.ResolveByRouteAsync(route)));
    }

    // ---- Helpers ----

    private async Task<T> RunAsync<T>(SeedData seed, Func<IDataContainerModuleService, Task<T>> action)
    {
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantContext = new TestTenantContext(seed.TenantId);
        var service = new DataContainerModuleService(ctx, new MenuConfigService(ctx, tenantContext));
        return await action(service);
    }

    /// <summary>Tenant con vista de menu + una seccion donde colgar, un modelo y una tabla raiz.</summary>
    private async Task<SeedData> SeedAsync(string tenantName, string tableName)
    {
        var tenantId = Guid.CreateVersion7();
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = tenantName });
            await ctx.SaveChangesAsync();
        }

        var viewId = Guid.CreateVersion7();
        var sectionId = Guid.CreateVersion7();
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            ctx.MenuViews.Add(new MenuView { Id = viewId, TenantId = tenantId, Name = "Completo", IsDefault = true });
            ctx.MenuNodes.Add(new MenuNode
            {
                Id = sectionId,
                TenantId = tenantId,
                MenuViewId = viewId,
                Kind = MenuNodeKind.Section,
                Name = "Sistema . Datos",
                State = MenuNodeState.Ready
            });
            await ctx.SaveChangesAsync();
        }

        var containerId = await AddTableAsync(tenantId, tableName, modelName: "Modelo " + tenantName);
        return new SeedData(tenantId, containerId, viewId, sectionId);
    }

    /// <summary>Agrega una tabla (raiz o submodelo) con una columna, opcionalmente en un modelo nuevo.</summary>
    private async Task<Guid> AddTableAsync(Guid tenantId, string name, string? modelName, Guid? parentContainerId = null)
    {
        var containerId = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId);
        Guid? modelId = null;
        if (modelName is not null)
        {
            modelId = Guid.CreateVersion7();
            ctx.DataModels.Add(new DataModel { Id = modelId.Value, TenantId = tenantId, Name = modelName });
        }
        ctx.DataContainers.Add(new DataContainer
        {
            Id = containerId,
            TenantId = tenantId,
            ModelId = modelId,
            ParentContainerId = parentContainerId,
            Name = name
        });
        ctx.DataContainerColumns.Add(new DataContainerColumn
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            ContainerId = containerId,
            Name = "Nombre",
            Type = DataContainerColumnType.Text
        });
        await ctx.SaveChangesAsync();
        return containerId;
    }

    private sealed record SeedData(Guid TenantId, Guid ContainerId, Guid MenuViewId, Guid SectionNodeId);

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class DataContainerModuleTests_Postgres
    : DataContainerModuleTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public DataContainerModuleTests_Postgres(PostgresTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class DataContainerModuleTests_SqlServer
    : DataContainerModuleTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public DataContainerModuleTests_SqlServer(SqlServerTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}
