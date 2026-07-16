using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion de la consulta PAGINADA de registros de un contenedor de datos
/// (<c>IDataContainerService.ListRowsPagedAsync</c>), en matriz dual PostgreSQL / SQL Server.
///
/// Valor real de estas pruebas: la consulta se resuelve EN EL SERVIDOR sobre un modelo EAV
/// (busqueda por EXISTS sobre celdas, filtro por columna, orden por el valor de una celda via
/// subconsulta correlacionada y paginado). Eso COMPILA aunque el proveedor no sepa traducirlo:
/// solo ejecutandola contra cada motor se sabe que no revienta. Ademas fija el contrato de
/// paginado estable y el aislamiento cross-tenant.
/// </summary>
public abstract class DataContainerPagingTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected DataContainerPagingTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Paged_Search_Filter_Sort_AreResolvedInTheServer()
    {
        var seed = await SeedTableAsync("DC Paging", new[]
        {
            ("Ana Lopez", "Bogota"),
            ("Bruno Diaz", "Medellin"),
            ("Carla Ruiz", "Bogota"),
            ("Diego Mora", "Cali"),
            ("Elena Paz", "Bogota")
        });

        // Sin filtros: total = todas, y la pagina respeta el tamano pedido.
        var page1 = await RunAsync(seed, s => s.ListRowsPagedAsync(
            new DataRowQuery(seed.ContainerId, PageSize: 2, SortColumnId: seed.NombreColumnId, SortDescending: false)));
        Assert.Equal(5, page1.Total);
        Assert.Equal(2, page1.Rows.Count);
        Assert.Equal("Ana Lopez", page1.Rows[0].ValuesByColumnId[seed.NombreColumnId]);
        Assert.Equal("Bruno Diaz", page1.Rows[1].ValuesByColumnId[seed.NombreColumnId]);

        // Segunda pagina: sigue la secuencia, sin repetir ni perder filas.
        var page2 = await RunAsync(seed, s => s.ListRowsPagedAsync(
            new DataRowQuery(seed.ContainerId, PageSize: 2, Page: 2, SortColumnId: seed.NombreColumnId, SortDescending: false)));
        Assert.Equal(5, page2.Total);
        Assert.Equal("Carla Ruiz", page2.Rows[0].ValuesByColumnId[seed.NombreColumnId]);
        Assert.Equal("Diego Mora", page2.Rows[1].ValuesByColumnId[seed.NombreColumnId]);

        // Orden descendente por la misma columna.
        var desc = await RunAsync(seed, s => s.ListRowsPagedAsync(
            new DataRowQuery(seed.ContainerId, PageSize: 1, SortColumnId: seed.NombreColumnId, SortDescending: true)));
        Assert.Equal("Elena Paz", desc.Rows[0].ValuesByColumnId[seed.NombreColumnId]);

        // Busqueda libre: casa contra CUALQUIER celda de la fila (aqui, la ciudad).
        var search = await RunAsync(seed, s => s.ListRowsPagedAsync(
            new DataRowQuery(seed.ContainerId, Search: "medellin")));
        Assert.Equal(1, search.Total);
        Assert.Equal("Bruno Diaz", search.Rows[0].ValuesByColumnId[seed.NombreColumnId]);

        // Filtro por columna: solo mira esa columna (ciudad = Bogota -> 3).
        var filtered = await RunAsync(seed, s => s.ListRowsPagedAsync(
            new DataRowQuery(seed.ContainerId,
                Filters: new Dictionary<Guid, string> { [seed.CiudadColumnId] = "bogota" })));
        Assert.Equal(3, filtered.Total);

        // El filtro por columna NO se confunde con otra columna: "Bogota" en Nombre no existe.
        var noMatch = await RunAsync(seed, s => s.ListRowsPagedAsync(
            new DataRowQuery(seed.ContainerId,
                Filters: new Dictionary<Guid, string> { [seed.NombreColumnId] = "bogota" })));
        Assert.Equal(0, noMatch.Total);
        Assert.Empty(noMatch.Rows);
    }

    [Fact]
    public async Task Search_IsCaseInsensitive_AndTreatsWildcardsAsLiterals()
    {
        var seed = await SeedTableAsync("DC Comodines", new[]
        {
            ("Descuento 50%", "Bogota"),
            ("Plan 50 anios", "Cali")
        });

        // Case-insensitive en ambos motores (PG distingue mayusculas por defecto).
        var upper = await RunAsync(seed, s => s.ListRowsPagedAsync(
            new DataRowQuery(seed.ContainerId, Search: "DESCUENTO")));
        Assert.Equal(1, upper.Total);

        // El "%" que teclea el usuario es literal, no comodin: casa solo "Descuento 50%".
        var literal = await RunAsync(seed, s => s.ListRowsPagedAsync(
            new DataRowQuery(seed.ContainerId, Search: "50%")));
        Assert.Equal(1, literal.Total);
        Assert.Equal("Descuento 50%", literal.Rows[0].ValuesByColumnId[seed.NombreColumnId]);
    }

    [Fact]
    public async Task Paged_IsTenantIsolated()
    {
        var a = await SeedTableAsync("DC Tenant A", new[] { ("Solo de A", "Bogota") });
        var b = await SeedTableAsync("DC Tenant B", new[] { ("Solo de B", "Cali") });

        // El tenant B pide la tabla de A: el filtro global no le devuelve NADA.
        var cross = await RunAsync(b, s => s.ListRowsPagedAsync(new DataRowQuery(a.ContainerId)));
        Assert.Equal(0, cross.Total);
        Assert.Empty(cross.Rows);

        // Y en su propia tabla si ve lo suyo.
        var own = await RunAsync(b, s => s.ListRowsPagedAsync(new DataRowQuery(b.ContainerId)));
        Assert.Equal(1, own.Total);
    }

    // ---- Helpers ----

    private async Task<T> RunAsync<T>(SeedData seed, Func<IDataContainerService, Task<T>> action)
    {
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var service = new DataContainerService(ctx, new TestTenantContext(seed.TenantId));
        return await action(service);
    }

    /// <summary>Siembra un tenant con un modelo, una tabla (Nombre, Ciudad) y sus filas EAV.</summary>
    private async Task<SeedData> SeedTableAsync(string tenantName, (string Nombre, string Ciudad)[] rows)
    {
        var tenantId = Guid.CreateVersion7();
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = tenantName });
            await ctx.SaveChangesAsync();
        }

        var modelId = Guid.CreateVersion7();
        var containerId = Guid.CreateVersion7();
        var nombreCol = Guid.CreateVersion7();
        var ciudadCol = Guid.CreateVersion7();

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            ctx.DataModels.Add(new DataModel { Id = modelId, TenantId = tenantId, Name = "Modelo " + tenantName });
            ctx.DataContainers.Add(new DataContainer
            {
                Id = containerId,
                TenantId = tenantId,
                ModelId = modelId,
                Name = "Clientes"
            });
            ctx.DataContainerColumns.Add(new DataContainerColumn
            {
                Id = nombreCol,
                TenantId = tenantId,
                ContainerId = containerId,
                Name = "Nombre",
                Type = DataContainerColumnType.Text,
                SortOrder = 0
            });
            ctx.DataContainerColumns.Add(new DataContainerColumn
            {
                Id = ciudadCol,
                TenantId = tenantId,
                ContainerId = containerId,
                Name = "Ciudad",
                Type = DataContainerColumnType.Text,
                SortOrder = 1
            });

            foreach (var (nombre, ciudad) in rows)
            {
                var rowId = Guid.CreateVersion7();
                ctx.DataContainerRows.Add(new DataContainerRow
                {
                    Id = rowId,
                    TenantId = tenantId,
                    ContainerId = containerId
                });
                ctx.DataContainerCells.Add(new DataContainerCell
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantId,
                    RowId = rowId,
                    ColumnId = nombreCol,
                    Value = nombre
                });
                ctx.DataContainerCells.Add(new DataContainerCell
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantId,
                    RowId = rowId,
                    ColumnId = ciudadCol,
                    Value = ciudad
                });
            }

            await ctx.SaveChangesAsync();
        }

        return new SeedData(tenantId, containerId, nombreCol, ciudadCol);
    }

    private sealed record SeedData(Guid TenantId, Guid ContainerId, Guid NombreColumnId, Guid CiudadColumnId);

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class DataContainerPagingTests_Postgres
    : DataContainerPagingTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public DataContainerPagingTests_Postgres(PostgresTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class DataContainerPagingTests_SqlServer
    : DataContainerPagingTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public DataContainerPagingTests_SqlServer(SqlServerTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}
