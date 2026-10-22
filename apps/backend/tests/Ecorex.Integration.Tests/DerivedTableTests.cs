using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tablas DERIVADAS (ADR-0127) en matriz dual PostgreSQL / SQL Server: alta transaccional (tabla hija +
/// columnas + conector FromTable + proceso Upsert), refresco (motor + ingesta), PODA de los grupos que
/// desaparecen del origen, supervivencia de una columna PROPIA de la hija, regla "un solo nivel" y el
/// guard que impide borrar el origen mientras exista la derivada.
/// </summary>
public abstract class DerivedTableTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected DerivedTableTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Create_Refresh_Prune_AndOwnColumnSurvives()
    {
        var seed = await SeedSourceAsync("Derivada OK", new[]
        {
            ("Bogota", "10"),
            ("Bogota", "10.0"),
            ("Cali", "2,5"),
            ("Cali", "7.5"),
            ("Medellin", "1")
        });

        // ---- Alta: tabla hija, columnas, conector FromTable y proceso Upsert por "Clave" ----
        var spec = new DerivationSpec(seed.SourceId, new[] { seed.CiudadCol }, new[]
        {
            new DerivationAggregate(null, DerivationFn.Count, "Cantidad"),
            new DerivationAggregate(seed.MontoCol, DerivationFn.Sum, "Total")
        });
        var created = await RunAsync(seed, s => s.CreateAsync(new CreateDerivedTableRequest(seed.ModelId, "Por ciudad", null, spec), Guid.Empty));

        await using (var ctx = _fixture.CreateContext(seed.TenantId))
        {
            var child = await ctx.DataContainers.SingleAsync(c => c.Id == created.TableId);
            Assert.Equal(seed.SourceId, child.DerivedFromContainerId);
            Assert.Equal(seed.ModelId, child.ModelId);

            var cols = await ctx.DataContainerColumns.Where(c => c.ContainerId == created.TableId).OrderBy(c => c.SortOrder).ToListAsync();
            Assert.Equal(new[] { "Ciudad", "Clave", "Cantidad", "Total" }, cols.Select(c => c.Name));
            Assert.Equal(DataContainerColumnType.Number, cols[2].Type);
            Assert.Equal(DataContainerColumnType.Decimal, cols[3].Type);

            var connector = await ctx.DataConnectors.SingleAsync(c => c.Id == created.ConnectorId);
            Assert.Equal(ConnectorKind.FromTable, connector.Kind);
            Assert.Equal(created.TableId, connector.ContainerId);
            Assert.NotNull(DerivationSpec.FromJson(connector.MappingJson));

            var process = await ctx.ImportProcesses.SingleAsync(p => p.Id == created.ProcessId);
            Assert.Equal(ImportRunMode.Upsert, process.Mode);
            Assert.Equal("Clave", process.KeyColumn);
            Assert.Equal(created.ConnectorId, process.ConnectorId);
        }

        // ---- Primer refresco: 3 grupos; "10" y "10.0" caen juntos; "2,5" suma como 2.5 ----
        var first = await RunAsync(seed, s => s.RefreshAsync(created.ConnectorId, ApiImportMode.Upsert, "Clave"));
        Assert.True(first.Success, first.Error);
        Assert.Equal(3, first.Inserted);
        Assert.Equal(0, first.Updated);
        Assert.Equal(0, first.Deleted);

        var rows = await ReadChildAsync(seed, created.TableId);
        Assert.Equal(3, rows.Count);
        Assert.Equal("2", rows["Bogota"]["Cantidad"]);
        Assert.Equal("20", rows["Bogota"]["Total"]);
        Assert.Equal("10", rows["Cali"]["Total"]);
        Assert.Equal("1", rows["Medellin"]["Cantidad"]);

        // ---- Columna PROPIA en la hija (la agrega el usuario) con un valor en la fila de Cali ----
        Guid notaCol;
        await using (var ctx = _fixture.CreateContext(seed.TenantId))
        {
            notaCol = Guid.CreateVersion7();
            ctx.DataContainerColumns.Add(new DataContainerColumn
            {
                Id = notaCol, TenantId = seed.TenantId, ContainerId = created.TableId, Name = "Nota", Type = DataContainerColumnType.Text, SortOrder = 99
            });
            var keyCol = await ctx.DataContainerColumns.SingleAsync(c => c.ContainerId == created.TableId && c.Name == "Clave");
            var caliRowId = await ctx.DataContainerCells.Where(c => c.ColumnId == keyCol.Id && c.Value == "Cali").Select(c => c.RowId).SingleAsync();
            ctx.DataContainerCells.Add(new DataContainerCell { TenantId = seed.TenantId, RowId = caliRowId, ColumnId = notaCol, Value = "revisar" });
            await ctx.SaveChangesAsync();
        }

        // ---- Cambia el origen: Medellin desaparece, Cali crece, nace Pasto ----
        await using (var ctx = _fixture.CreateContext(seed.TenantId))
        {
            var medellinRows = await ctx.DataContainerCells
                .Where(c => c.ColumnId == seed.CiudadCol && c.Value == "Medellin").Select(c => c.RowId).ToListAsync();
            var cells = await ctx.DataContainerCells.Where(c => medellinRows.Contains(c.RowId)).ToListAsync();
            ctx.DataContainerCells.RemoveRange(cells);
            ctx.DataContainerRows.RemoveRange(await ctx.DataContainerRows.Where(r => medellinRows.Contains(r.Id)).ToListAsync());
            AddSourceRow(ctx, seed, "Cali", "100");
            AddSourceRow(ctx, seed, "Pasto", "3");
            await ctx.SaveChangesAsync();
        }

        var second = await RunAsync(seed, s => s.RefreshAsync(created.ConnectorId, ApiImportMode.Upsert, "Clave"));
        Assert.True(second.Success, second.Error);
        Assert.Equal(1, second.Inserted);   // Pasto
        Assert.Equal(2, second.Updated);    // Bogota, Cali
        Assert.Equal(1, second.Deleted);    // PODA: Medellin

        rows = await ReadChildAsync(seed, created.TableId);
        Assert.Equal(3, rows.Count);
        Assert.False(rows.ContainsKey("Medellin"));
        Assert.Equal("3", rows["Cali"]["Cantidad"]);
        Assert.Equal("110", rows["Cali"]["Total"]);
        Assert.Equal("3", rows["Pasto"]["Total"]);
        // La columna propia SOBREVIVIO al refresco (Upsert conserva las celdas no mapeadas).
        Assert.Equal("revisar", rows["Cali"]["Nota"]);
    }

    [Fact]
    public async Task Create_FromDerived_IsRejected_OneLevelOnly()
    {
        var seed = await SeedSourceAsync("Derivada nivel", new[] { ("Bogota", "1") });
        var spec = new DerivationSpec(seed.SourceId, new[] { seed.CiudadCol }, Array.Empty<DerivationAggregate>());
        var created = await RunAsync(seed, s => s.CreateAsync(new CreateDerivedTableRequest(seed.ModelId, "Nivel 1", null, spec), Guid.Empty));

        Guid childCiudadCol;
        await using (var ctx = _fixture.CreateContext(seed.TenantId))
        {
            childCiudadCol = await ctx.DataContainerColumns.Where(c => c.ContainerId == created.TableId && c.Name == "Ciudad").Select(c => c.Id).SingleAsync();
        }

        var nested = new DerivationSpec(created.TableId, new[] { childCiudadCol }, Array.Empty<DerivationAggregate>());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunAsync(seed, s => s.CreateAsync(new CreateDerivedTableRequest(seed.ModelId, "Nivel 2", null, nested), Guid.Empty)));
        Assert.Contains("un solo nivel", ex.Message);

        // Tampoco en la vista previa.
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(seed, s => s.PreviewAsync(nested)));
    }

    [Fact]
    public async Task DeleteSource_IsBlockedWhileDerivedExists_AndDeletingDerivedCleansUp()
    {
        var seed = await SeedSourceAsync("Derivada borrar", new[] { ("Bogota", "1") });
        var spec = new DerivationSpec(seed.SourceId, new[] { seed.CiudadCol }, new[] { new DerivationAggregate(null, DerivationFn.Count, "N") });
        var created = await RunAsync(seed, s => s.CreateAsync(new CreateDerivedTableRequest(seed.ModelId, "Hija", null, spec), Guid.Empty));
        var refresh = await RunAsync(seed, s => s.RefreshAsync(created.ConnectorId, ApiImportMode.Upsert, "Clave"));
        Assert.True(refresh.Success, refresh.Error);

        // El origen no se puede borrar mientras exista la derivada.
        await using (var ctx = _fixture.CreateContext(seed.TenantId))
        {
            var containers = new DataContainerService(ctx, new TestTenantContext(seed.TenantId));
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => containers.DeleteAsync(seed.SourceId, Guid.Empty));
            Assert.Contains("Hija", ex.Message);
        }

        // Borrar la derivada se lleva su conector y su proceso; el origen sigue intacto.
        await using (var ctx = _fixture.CreateContext(seed.TenantId))
        {
            var containers = new DataContainerService(ctx, new TestTenantContext(seed.TenantId));
            Assert.True(await containers.DeleteAsync(created.TableId, Guid.Empty));
        }
        await using (var ctx = _fixture.CreateContext(seed.TenantId))
        {
            Assert.False(await ctx.DataContainers.AnyAsync(c => c.Id == created.TableId));
            Assert.False(await ctx.DataConnectors.AnyAsync(c => c.Id == created.ConnectorId));
            Assert.False(await ctx.ImportProcesses.AnyAsync(p => p.Id == created.ProcessId));
            Assert.True(await ctx.DataContainers.AnyAsync(c => c.Id == seed.SourceId));
            Assert.Equal(1, await ctx.DataContainerRows.CountAsync(r => r.ContainerId == seed.SourceId));
        }
    }

    [Fact]
    public async Task Refresh_IsTenantIsolated()
    {
        var a = await SeedSourceAsync("Derivada tenant A", new[] { ("Bogota", "1") });
        var b = await SeedSourceAsync("Derivada tenant B", new[] { ("Cali", "1") });
        var spec = new DerivationSpec(a.SourceId, new[] { a.CiudadCol }, Array.Empty<DerivationAggregate>());
        var created = await RunAsync(a, s => s.CreateAsync(new CreateDerivedTableRequest(a.ModelId, "Hija A", null, spec), Guid.Empty));

        // El tenant B no ve el conector de A: el refresco falla sin tocar nada.
        var cross = await RunAsync(b, s => s.RefreshAsync(created.ConnectorId, ApiImportMode.Upsert, "Clave"));
        Assert.False(cross.Success);

        var own = await RunAsync(a, s => s.RefreshAsync(created.ConnectorId, ApiImportMode.Upsert, "Clave"));
        Assert.True(own.Success, own.Error);
        Assert.Equal(1, own.Inserted);
    }

    // ---- Helpers ----

    private async Task<T> RunAsync<T>(SeedData seed, Func<IDerivedTableService, Task<T>> action)
    {
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenant = new TestTenantContext(seed.TenantId);
        var service = new DerivedTableService(ctx, tenant, new RowIngestService(ctx));
        return await action(service);
    }

    /// <summary>Filas de la hija indexadas por "Clave", cada una como nombre de columna -> valor.</summary>
    private async Task<Dictionary<string, Dictionary<string, string?>>> ReadChildAsync(SeedData seed, Guid childId)
    {
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var cols = await ctx.DataContainerColumns.Where(c => c.ContainerId == childId).ToDictionaryAsync(c => c.Id, c => c.Name);
        var rowIds = await ctx.DataContainerRows.Where(r => r.ContainerId == childId).Select(r => r.Id).ToListAsync();
        var cells = await ctx.DataContainerCells.Where(c => rowIds.Contains(c.RowId)).ToListAsync();
        var result = new Dictionary<string, Dictionary<string, string?>>(StringComparer.Ordinal);
        foreach (var g in cells.GroupBy(c => c.RowId))
        {
            var row = g.ToDictionary(c => cols[c.ColumnId], c => c.Value);
            result[row["Clave"] ?? ""] = row;
        }
        return result;
    }

    private static void AddSourceRow(Infrastructure.Persistence.EcorexDbContext ctx, SeedData seed, string ciudad, string monto)
    {
        var rowId = Guid.CreateVersion7();
        ctx.DataContainerRows.Add(new DataContainerRow { Id = rowId, TenantId = seed.TenantId, ContainerId = seed.SourceId });
        ctx.DataContainerCells.Add(new DataContainerCell { TenantId = seed.TenantId, RowId = rowId, ColumnId = seed.CiudadCol, Value = ciudad });
        ctx.DataContainerCells.Add(new DataContainerCell { TenantId = seed.TenantId, RowId = rowId, ColumnId = seed.MontoCol, Value = monto });
    }

    /// <summary>Siembra un tenant con un modelo y una tabla origen (Ciudad Text, Monto Decimal) con filas.</summary>
    private async Task<SeedData> SeedSourceAsync(string tenantName, (string Ciudad, string Monto)[] rows)
    {
        var tenantId = Guid.CreateVersion7();
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = tenantName });
            await ctx.SaveChangesAsync();
        }

        var seed = new SeedData(tenantId, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            ctx.DataModels.Add(new DataModel { Id = seed.ModelId, TenantId = tenantId, Name = "Modelo " + tenantName });
            ctx.DataContainers.Add(new DataContainer { Id = seed.SourceId, TenantId = tenantId, ModelId = seed.ModelId, Name = "Ventas" });
            ctx.DataContainerColumns.Add(new DataContainerColumn
            {
                Id = seed.CiudadCol, TenantId = tenantId, ContainerId = seed.SourceId, Name = "Ciudad", Type = DataContainerColumnType.Text, SortOrder = 0
            });
            ctx.DataContainerColumns.Add(new DataContainerColumn
            {
                Id = seed.MontoCol, TenantId = tenantId, ContainerId = seed.SourceId, Name = "Monto", Type = DataContainerColumnType.Decimal, SortOrder = 1
            });
            foreach (var (ciudad, monto) in rows) { AddSourceRow(ctx, seed, ciudad, monto); }
            await ctx.SaveChangesAsync();
        }
        return seed;
    }

    private sealed record SeedData(Guid TenantId, Guid ModelId, Guid SourceId, Guid CiudadCol, Guid MontoCol);

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class DerivedTableTests_Postgres
    : DerivedTableTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public DerivedTableTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class DerivedTableTests_SqlServer
    : DerivedTableTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public DerivedTableTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture)
    {
    }
}
