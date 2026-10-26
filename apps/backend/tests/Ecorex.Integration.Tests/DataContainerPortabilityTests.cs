using System.Text;
using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Exportar/importar la DEFINICION de un contenedor a JSON (ADR-0132), matriz dual PG / SQL Server. Verifica
/// el round-trip: la estructura (tablas, columnas, relacion, tabla derivada con su receta remapeada a los ids
/// nuevos) se recrea en un contenedor NUEVO, SIN traer datos (filas) ni secretos (credenciales), y con avisos
/// para las fuentes que usaban credenciales.
/// </summary>
public abstract class DataContainerPortabilityTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected DataContainerPortabilityTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Export_Then_Import_RecreatesStructure_NoData_NoSecrets_RemapsDerivation()
    {
        var tenantId = Guid.CreateVersion7();
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = "Portab" });
            await ctx.SaveChangesAsync();
        }

        var modelId = Guid.CreateVersion7();
        var ventasId = Guid.CreateVersion7();
        var clientesId = Guid.CreateVersion7();
        var ciudadCol = Guid.CreateVersion7();
        var montoCol = Guid.CreateVersion7();

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            ctx.DataModels.Add(new DataModel { Id = modelId, TenantId = tenantId, Name = "Origen" });
            ctx.DataContainers.Add(new DataContainer { Id = ventasId, TenantId = tenantId, ModelId = modelId, Name = "Ventas", CanvasX = 10, CanvasY = 20 });
            ctx.DataContainers.Add(new DataContainer { Id = clientesId, TenantId = tenantId, ModelId = modelId, Name = "Clientes", CanvasX = 300, CanvasY = 20 });
            ctx.DataContainerColumns.Add(new DataContainerColumn { Id = ciudadCol, TenantId = tenantId, ContainerId = ventasId, Name = "Ciudad", Type = DataContainerColumnType.Text, SortOrder = 0 });
            ctx.DataContainerColumns.Add(new DataContainerColumn { Id = montoCol, TenantId = tenantId, ContainerId = ventasId, Name = "Monto", Type = DataContainerColumnType.Decimal, SortOrder = 1 });
            ctx.DataContainerColumns.Add(new DataContainerColumn { TenantId = tenantId, ContainerId = clientesId, Name = "Nombre", Type = DataContainerColumnType.Text, SortOrder = 0 });
            // Relacion Ventas -> Clientes.
            ctx.DataModelRelations.Add(new DataModelRelation { TenantId = tenantId, ModelId = modelId, FromTableId = ventasId, ToTableId = clientesId, Kind = DataModelRelationKind.ManyToOne, Name = "cliente" });
            // Datos en el origen (NO deben exportarse).
            var rowId = Guid.CreateVersion7();
            ctx.DataContainerRows.Add(new DataContainerRow { Id = rowId, TenantId = tenantId, ContainerId = ventasId });
            ctx.DataContainerCells.Add(new DataContainerCell { TenantId = tenantId, RowId = rowId, ColumnId = ciudadCol, Value = "Bogota" });
            ctx.DataContainerCells.Add(new DataContainerCell { TenantId = tenantId, RowId = rowId, ColumnId = montoCol, Value = "10" });
            // Conector REST CON credenciales (el secreto no debe exportarse; debe venir aviso).
            var alegraId = Guid.CreateVersion7();
            ctx.DataConnectors.Add(new DataConnector
            {
                Id = alegraId,
                TenantId = tenantId,
                ModelId = modelId,
                ContainerId = ventasId,
                Name = "Alegra",
                Kind = ConnectorKind.RestApi,
                AuthKind = ConnectorAuthKind.ApiKey,
                EndpointUrl = "https://api.example.com/ventas",
                HttpMethod = "GET",
                CredentialsEncrypted = "SECRETO-CIFRADO",
                MappingJson = "{\"fields\":[{\"column\":\"Ciudad\",\"path\":\"city\"}]}",
                IsActive = true
            });
            // Proceso de refresco del conector REST (programacion). Al importar debe quedar INACTIVO porque
            // su fuente usaba credenciales que no viajan.
            ctx.ImportProcesses.Add(new ImportProcess
            {
                TenantId = tenantId,
                ModelId = modelId,
                ConnectorId = alegraId,
                Name = "Refrescar Ventas",
                ScheduleKind = ImportScheduleKind.Manual,
                Mode = ImportRunMode.Replace,
                IsActive = true
            });
            await ctx.SaveChangesAsync();
        }

        // Tabla derivada "Por ciudad" (crea su conector FromTable + proceso Upsert con la spec por GUIDs).
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var derived = new DerivedTableService(ctx, new TestTenant(tenantId), new RowIngestService(ctx));
            var spec = new DerivationSpec(ventasId, new[] { ciudadCol }, new[]
            {
                new DerivationAggregate(null, DerivationFn.Count, "Cantidad"),
                new DerivationAggregate(montoCol, DerivationFn.Sum, "Total")
            });
            await derived.CreateAsync(new CreateDerivedTableRequest(modelId, "Por ciudad", null, spec), Guid.Empty);
        }

        // ---- EXPORT ----
        string json;
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var port = new DataContainerPortabilityService(ctx, new TestTenant(tenantId));
            var res = await port.ExportAsync(modelId);
            Assert.NotNull(res);
            json = Encoding.UTF8.GetString(res!.Bytes);
            // El secreto NUNCA viaja en el JSON.
            Assert.DoesNotContain("SECRETO-CIFRADO", json);
            // Tampoco los datos.
            Assert.DoesNotContain("Bogota", json);
        }

        // ---- IMPORT ----
        ContainerImportResult result;
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var port = new DataContainerPortabilityService(ctx, new TestTenant(tenantId));
            result = await port.ImportAsync(json, Guid.Empty);
        }
        Assert.Equal("Origen (importado)", result.Model.Name); // "Origen" ya existe -> nombre unico
        Assert.Contains(result.Warnings, w => w.Contains("Alegra")); // aviso: re-capturar credenciales

        // ---- VERIFICAR el contenedor nuevo ----
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var newModelId = result.Model.Id;
            var tables = await ctx.DataContainers.Where(c => c.ModelId == newModelId).ToListAsync();
            Assert.Equal(3, tables.Count); // Ventas, Clientes, Por ciudad
            var newVentas = tables.Single(t => t.Name == "Ventas");
            var newDerived = tables.Single(t => t.Name == "Por ciudad");

            // Estructura: columnas de Ventas replicadas.
            var ventasCols = await ctx.DataContainerColumns.Where(c => c.ContainerId == newVentas.Id).Select(c => c.Name).ToListAsync();
            Assert.Contains("Ciudad", ventasCols);
            Assert.Contains("Monto", ventasCols);

            // Relacion recreada por ids nuevos.
            var rel = await ctx.DataModelRelations.SingleAsync(r => r.ModelId == newModelId);
            Assert.Equal(newVentas.Id, rel.FromTableId);
            Assert.Equal("cliente", rel.Name);

            // La derivada apunta al NUEVO Ventas y su receta esta remapeada a ids nuevos.
            Assert.Equal(newVentas.Id, newDerived.DerivedFromContainerId);
            var fromTable = await ctx.DataConnectors.SingleAsync(c => c.ModelId == newModelId && c.Kind == ConnectorKind.FromTable);
            var newSpec = DerivationSpec.FromJson(fromTable.MappingJson)!;
            Assert.Equal(newVentas.Id, newSpec.SourceTableId);
            var newCiudad = await ctx.DataContainerColumns.SingleAsync(c => c.ContainerId == newVentas.Id && c.Name == "Ciudad");
            Assert.Contains(newCiudad.Id, newSpec.GroupBy);

            // El conector REST llego SIN secreto.
            var rest = await ctx.DataConnectors.SingleAsync(c => c.ModelId == newModelId && c.Kind == ConnectorKind.RestApi);
            Assert.Null(rest.CredentialsEncrypted);
            Assert.Equal("https://api.example.com/ventas", rest.EndpointUrl);

            // Procesos: el REST (fuente con credenciales) quedo inactivo; el de la derivada activo.
            var restProc = await ctx.ImportProcesses.SingleAsync(p => p.ModelId == newModelId && p.ConnectorId == rest.Id);
            Assert.False(restProc.IsActive);
            var derProc = await ctx.ImportProcesses.SingleAsync(p => p.ModelId == newModelId && p.ConnectorId == fromTable.Id);
            Assert.True(derProc.IsActive);
            Assert.Equal(ImportRunMode.Upsert, derProc.Mode);

            // SIN datos: ninguna fila en las tablas nuevas.
            var newTableIds = tables.Select(t => t.Id).ToList();
            Assert.Equal(0, await ctx.DataContainerRows.CountAsync(r => newTableIds.Contains(r.ContainerId)));
        }
    }

    private sealed class TestTenant(Guid? tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; }
    }
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class DataContainerPortabilityTests_Postgres
    : DataContainerPortabilityTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public DataContainerPortabilityTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture) { }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class DataContainerPortabilityTests_SqlServer
    : DataContainerPortabilityTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public DataContainerPortabilityTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture) { }
}
