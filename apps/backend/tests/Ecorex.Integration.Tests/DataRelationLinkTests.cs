using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion de los vinculos DATO-A-DATO de las relaciones del Contenedor de datos
/// (FASE 2), en matriz dual PostgreSQL / SQL Server.
///
/// Cubren lo que el esquema NO puede imponer por si solo: la cardinalidad (N:1 y N:N comparten
/// tabla), que los extremos pertenezcan a las tablas de la arista, y sobre todo que BORRAR una fila
/// vinculada no reviente: las FKs a filas son Restrict a proposito (una cascada por ambos extremos
/// son rutas multiples y SQL Server la rechaza, error 1785), asi que la limpieza la hace el servicio.
/// </summary>
public abstract class DataRelationLinkTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected DataRelationLinkTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task SetLinks_RoundTrips_AndReplacesTheSet()
    {
        var s = await SeedAsync("Links N:N", DataModelRelationKind.ManyToMany);

        // Vincula dos destinos.
        var set = await RunLinksAsync(s, x => x.SetLinksAsync(s.RelationId, s.PedidoRow, new[] { s.ProdA, s.ProdB }));
        Assert.True(set.IsOk, set.Error);

        var rels = await RunLinksAsync(s, x => x.ListForRowAsync(s.PedidosTableId, s.PedidoRow));
        var rel = Assert.Single(rels);
        Assert.Equal(2, rel.LinkedRowIds.Count);
        Assert.Contains(s.ProdA, rel.LinkedRowIds);
        Assert.Contains(s.ProdB, rel.LinkedRowIds);
        // El selector ofrece las filas de la tabla destino con etiqueta legible.
        Assert.Equal(2, rel.Options.Count);
        Assert.Contains(rel.Options, o => o.Label == "Teclado");

        // Reemplaza el set: deja solo uno.
        Assert.True((await RunLinksAsync(s, x => x.SetLinksAsync(s.RelationId, s.PedidoRow, new[] { s.ProdB }))).IsOk);
        var after = await RunLinksAsync(s, x => x.ListForRowAsync(s.PedidosTableId, s.PedidoRow));
        Assert.Equal(new[] { s.ProdB }, after.Single().LinkedRowIds);

        // Lista vacia = desvincula todo.
        Assert.True((await RunLinksAsync(s, x => x.SetLinksAsync(s.RelationId, s.PedidoRow, Array.Empty<Guid>()))).IsOk);
        Assert.Empty((await RunLinksAsync(s, x => x.ListForRowAsync(s.PedidosTableId, s.PedidoRow))).Single().LinkedRowIds);
    }

    [Fact]
    public async Task SetLinks_IsIdempotent()
    {
        var s = await SeedAsync("Links Idempotente", DataModelRelationKind.ManyToMany);
        await RunLinksAsync(s, x => x.SetLinksAsync(s.RelationId, s.PedidoRow, new[] { s.ProdA }));
        // Repetir el mismo set no duplica (indice unico por relacion+origen+destino).
        Assert.True((await RunLinksAsync(s, x => x.SetLinksAsync(s.RelationId, s.PedidoRow, new[] { s.ProdA }))).IsOk);

        await using var ctx = _fixture.CreateContext(s.TenantId);
        Assert.Equal(1, await ctx.DataModelRelationLinks.CountAsync(l => l.FromRowId == s.PedidoRow));
    }

    [Fact]
    public async Task ManyToOne_RejectsMoreThanOneTarget()
    {
        var s = await SeedAsync("Links N:1", DataModelRelationKind.ManyToOne);

        var many = await RunLinksAsync(s, x => x.SetLinksAsync(s.RelationId, s.PedidoRow, new[] { s.ProdA, s.ProdB }));
        Assert.Equal(RelationLinkStatus.Invalid, many.Status);

        // Uno solo si vale.
        Assert.True((await RunLinksAsync(s, x => x.SetLinksAsync(s.RelationId, s.PedidoRow, new[] { s.ProdA }))).IsOk);
    }

    [Fact]
    public async Task Endpoints_MustBelongToTheRelationTables()
    {
        var s = await SeedAsync("Links Extremos", DataModelRelationKind.ManyToMany);

        // Destino que NO es de la tabla destino (es una fila de Pedidos).
        var badTarget = await RunLinksAsync(s, x => x.SetLinksAsync(s.RelationId, s.PedidoRow, new[] { s.PedidoRow }));
        Assert.Equal(RelationLinkStatus.Invalid, badTarget.Status);

        // Origen que NO es de la tabla origen (es una fila de Productos).
        var badSource = await RunLinksAsync(s, x => x.SetLinksAsync(s.RelationId, s.ProdA, new[] { s.ProdB }));
        Assert.Equal(RelationLinkStatus.NotFound, badSource.Status);
    }

    [Fact]
    public async Task DeleteRow_CleansItsLinks_AsSourceAndAsTarget()
    {
        // Este es el caso que motivo el test: las FKs a filas son Restrict, asi que sin limpieza
        // explicita el borrado reventaria por violacion de FK.
        var s = await SeedAsync("Links Borrado", DataModelRelationKind.ManyToMany);
        Assert.True((await RunLinksAsync(s, x => x.SetLinksAsync(s.RelationId, s.PedidoRow, new[] { s.ProdA, s.ProdB }))).IsOk);

        // 1) Borrar la fila DESTINO (extremo Restrict) no debe fallar y se lleva su vinculo.
        Assert.True(await RunContainerAsync(s, c => c.DeleteRowAsync(s.ProdA, s.Actor)));
        await using (var ctx = _fixture.CreateContext(s.TenantId))
        {
            Assert.False(await ctx.DataModelRelationLinks.AnyAsync(l => l.ToRowId == s.ProdA));
            Assert.Equal(1, await ctx.DataModelRelationLinks.CountAsync(l => l.FromRowId == s.PedidoRow));
        }

        // 2) Borrar la fila ORIGEN se lleva los suyos.
        Assert.True(await RunContainerAsync(s, c => c.DeleteRowAsync(s.PedidoRow, s.Actor)));
        await using (var ctx = _fixture.CreateContext(s.TenantId))
        {
            Assert.False(await ctx.DataModelRelationLinks.AnyAsync(l => l.FromRowId == s.PedidoRow));
        }
    }

    [Fact]
    public async Task DeletingTheRelation_CascadesItsLinks()
    {
        var s = await SeedAsync("Links Cascada", DataModelRelationKind.ManyToMany);
        Assert.True((await RunLinksAsync(s, x => x.SetLinksAsync(s.RelationId, s.PedidoRow, new[] { s.ProdA }))).IsOk);

        // El esquema manda sobre el dato: al borrar la arista, sus vinculos se van en cascada.
        await using (var ctx = _fixture.CreateContext(s.TenantId))
        {
            var rel = await ctx.DataModelRelations.FirstAsync(r => r.Id == s.RelationId);
            ctx.DataModelRelations.Remove(rel);
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = _fixture.CreateContext(s.TenantId))
        {
            Assert.False(await ctx.DataModelRelationLinks.AnyAsync(l => l.RelationId == s.RelationId));
        }
    }

    // ---- Helpers ----

    private async Task<T> RunLinksAsync<T>(SeedData s, Func<IDataRelationLinkService, Task<T>> action)
    {
        await using var ctx = _fixture.CreateContext(s.TenantId);
        var tenantContext = new TestTenantContext(s.TenantId);
        var containers = new DataContainerService(ctx, tenantContext);
        return await action(new DataRelationLinkService(ctx, containers));
    }

    private async Task<T> RunContainerAsync<T>(SeedData s, Func<IDataContainerService, Task<T>> action)
    {
        await using var ctx = _fixture.CreateContext(s.TenantId);
        return await action(new DataContainerService(ctx, new TestTenantContext(s.TenantId)));
    }

    /// <summary>Tenant con modelo Pedidos -> Productos, una arista y filas a ambos lados.</summary>
    private async Task<SeedData> SeedAsync(string tenantName, DataModelRelationKind kind)
    {
        var tenantId = Guid.CreateVersion7();
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = tenantName });
            await ctx.SaveChangesAsync();
        }

        var modelId = Guid.CreateVersion7();
        var pedidos = Guid.CreateVersion7();
        var productos = Guid.CreateVersion7();
        var relationId = Guid.CreateVersion7();
        var pedidoRow = Guid.CreateVersion7();
        var prodA = Guid.CreateVersion7();
        var prodB = Guid.CreateVersion7();

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            ctx.DataModels.Add(new DataModel { Id = modelId, TenantId = tenantId, Name = "Ventas " + tenantName });
            ctx.DataContainers.Add(new DataContainer { Id = pedidos, TenantId = tenantId, ModelId = modelId, Name = "Pedidos" });
            ctx.DataContainers.Add(new DataContainer { Id = productos, TenantId = tenantId, ModelId = modelId, Name = "Productos" });

            var pedidoCol = Guid.CreateVersion7();
            var prodCol = Guid.CreateVersion7();
            ctx.DataContainerColumns.Add(new DataContainerColumn
            { Id = pedidoCol, TenantId = tenantId, ContainerId = pedidos, Name = "Numero", Type = DataContainerColumnType.Text });
            ctx.DataContainerColumns.Add(new DataContainerColumn
            { Id = prodCol, TenantId = tenantId, ContainerId = productos, Name = "Nombre", Type = DataContainerColumnType.Text });

            ctx.DataModelRelations.Add(new DataModelRelation
            {
                Id = relationId,
                TenantId = tenantId,
                ModelId = modelId,
                FromTableId = pedidos,
                ToTableId = productos,
                Kind = kind,
                Name = "Productos"
            });

            ctx.DataContainerRows.Add(new DataContainerRow { Id = pedidoRow, TenantId = tenantId, ContainerId = pedidos });
            ctx.DataContainerCells.Add(new DataContainerCell
            { Id = Guid.CreateVersion7(), TenantId = tenantId, RowId = pedidoRow, ColumnId = pedidoCol, Value = "PED-1" });

            ctx.DataContainerRows.Add(new DataContainerRow { Id = prodA, TenantId = tenantId, ContainerId = productos });
            ctx.DataContainerCells.Add(new DataContainerCell
            { Id = Guid.CreateVersion7(), TenantId = tenantId, RowId = prodA, ColumnId = prodCol, Value = "Teclado" });

            ctx.DataContainerRows.Add(new DataContainerRow { Id = prodB, TenantId = tenantId, ContainerId = productos });
            ctx.DataContainerCells.Add(new DataContainerCell
            { Id = Guid.CreateVersion7(), TenantId = tenantId, RowId = prodB, ColumnId = prodCol, Value = "Monitor" });

            await ctx.SaveChangesAsync();
        }

        return new SeedData(tenantId, pedidos, productos, relationId, pedidoRow, prodA, prodB, Guid.CreateVersion7());
    }

    private sealed record SeedData(
        Guid TenantId, Guid PedidosTableId, Guid ProductosTableId, Guid RelationId,
        Guid PedidoRow, Guid ProdA, Guid ProdB, Guid Actor);

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class DataRelationLinkTests_Postgres
    : DataRelationLinkTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public DataRelationLinkTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture) { }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class DataRelationLinkTests_SqlServer
    : DataRelationLinkTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public DataRelationLinkTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture) { }
}
