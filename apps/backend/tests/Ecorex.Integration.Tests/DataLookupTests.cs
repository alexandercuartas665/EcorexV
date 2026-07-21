using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Application.DataLookups;
using Ecorex.Infrastructure.Persistence;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests del motor COMPARTIDO de campos tipo lista alimentados por el Contenedor de datos
/// (<c>IDataLookupService</c>), en matriz dual PostgreSQL / SQL Server.
///
/// Valor real: este motor es la base de los campos de lista del tercero, del item y (mas
/// adelante) de los formularios, asi que un fallo aqui se propaga a tres modulos. Se prueba
/// contra ambos motores porque las consultas se resuelven EN EL SERVIDOR sobre un modelo EAV
/// (celdas), donde lo que compila no necesariamente traduce igual en los dos proveedores.
/// Fija ademas dos contratos que sostienen la "referencia viva": resolver por Id de fila y
/// no filtrarse entre tenants.
/// </summary>
public abstract class DataLookupTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected DataLookupTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Catalogo_Expone_Modelo_Tabla_Y_Columnas()
    {
        var seed = await SeedAsync("DL Catalogo");

        var modelos = await RunAsync(seed, s => s.ListModelsAsync());
        var modelo = Assert.Single(modelos);
        Assert.Equal(1, modelo.TableCount);

        // Las tablas se pueden pedir por modelo (es el flujo del configurador: modelo -> tabla).
        var tablas = await RunAsync(seed, s => s.ListTablesAsync(modelo.Id));
        var tabla = Assert.Single(tablas);
        Assert.Equal("Items", tabla.Name);
        Assert.Equal(modelo.Id, tabla.ModelId);
        Assert.Equal(modelo.Name, tabla.ModelName);

        // Y sin modelo, salen todas las raiz.
        Assert.Single(await RunAsync(seed, s => s.ListTablesAsync()));

        var columnas = await RunAsync(seed, s => s.ListColumnsAsync(tabla.Id));
        Assert.Equal(3, columnas.Count);
        Assert.Equal(new[] { "Nombre", "Grupo", "Marca" }, columnas.Select(c => c.Name).ToArray());
    }

    [Fact]
    public async Task Busqueda_Filtra_Y_Trae_Solo_Las_Columnas_Pedidas()
    {
        var seed = await SeedAsync("DL Busqueda");

        // Busqueda libre + recorte de columnas: solo vuelven display y las extra pedidas,
        // que es lo que necesita el autollenado (no la fila entera).
        var page = await RunAsync(seed, s => s.SearchAsync(new DataLookupQuery(
            seed.TablaId,
            Search: "martillo",
            DisplayColumnId: seed.NombreCol,
            ExtraColumnIds: new[] { seed.MarcaCol })));

        var fila = Assert.Single(page.Rows);
        Assert.Equal("Martillo", fila.Label);
        Assert.Equal("Stanley", fila.Values[seed.MarcaCol]);
        Assert.False(fila.Values.ContainsKey(seed.GrupoCol));

        // Filtro por columna: es el mecanismo sobre el que se apoyara la cascada.
        var porGrupo = await RunAsync(seed, s => s.SearchAsync(new DataLookupQuery(
            seed.TablaId,
            DisplayColumnId: seed.NombreCol,
            Filters: new Dictionary<Guid, string> { [seed.GrupoCol] = "Herramienta" })));
        Assert.Equal(2, porGrupo.Total);
        Assert.Equal(new[] { "Destornillador", "Martillo" }, porGrupo.Rows.Select(r => r.Label).ToArray());
    }

    [Fact]
    public async Task Etiqueta_Cae_A_La_Primera_Columna_De_Texto_Si_No_Se_Configura()
    {
        var seed = await SeedAsync("DL Etiqueta");

        // Sin DisplayColumnId se usa la misma heuristica del Contenedor (primera columna de
        // texto por orden), para que la fila no se llame distinto segun por donde se mire.
        var page = await RunAsync(seed, s => s.SearchAsync(new DataLookupQuery(seed.TablaId, Search: "martillo")));
        Assert.Equal("Martillo", Assert.Single(page.Rows).Label);

        // Una columna que ya no existe no debe romper el selector: se cae a la heuristica.
        var fantasma = await RunAsync(seed, s => s.SearchAsync(new DataLookupQuery(
            seed.TablaId, Search: "martillo", DisplayColumnId: Guid.CreateVersion7())));
        Assert.Equal("Martillo", Assert.Single(fantasma.Rows).Label);
    }

    [Fact]
    public async Task Resolve_Devuelve_El_Valor_Vigente_Y_Omite_Las_Filas_Borradas()
    {
        var seed = await SeedAsync("DL Resolve");

        var martillo = await RunAsync(seed, s => s.SearchAsync(new DataLookupQuery(
            seed.TablaId, Search: "martillo", DisplayColumnId: seed.NombreCol)));
        var rowId = Assert.Single(martillo.Rows).RowId;

        // Referencia VIVA: se corrige el dato en el Contenedor y lo guardado refleja el cambio,
        // porque el registro solo guarda el Id de la fila.
        await using (var ctx = _fixture.CreateContext(seed.TenantId))
        {
            var celda = ctx.DataContainerCells.Single(c => c.RowId == rowId && c.ColumnId == seed.NombreCol);
            celda.Value = "Martillo de bola";
            await ctx.SaveChangesAsync();
        }

        var resuelto = await RunAsync(seed, s => s.ResolveAsync(
            seed.TablaId, new[] { rowId }, seed.NombreCol, new[] { seed.MarcaCol }));
        var fila = Assert.Single(resuelto);
        Assert.Equal("Martillo de bola", fila.Label);
        Assert.Equal("Stanley", fila.Values[seed.MarcaCol]);

        // Una fila borrada no vuelve (en vez de reventar): el consumidor decide como mostrarlo.
        var inexistente = await RunAsync(seed, s => s.ResolveAsync(
            seed.TablaId, new[] { Guid.CreateVersion7() }, seed.NombreCol));
        Assert.Empty(inexistente);

        // Pedir ninguna fila no consulta nada.
        Assert.Empty(await RunAsync(seed, s => s.ResolveAsync(seed.TablaId, Array.Empty<Guid>())));
    }

    [Fact]
    public async Task Cascada_Filtra_La_Lista_Segun_Otro_Campo_Del_Formulario()
    {
        var seed = await SeedAsync("DL Cascada");

        // Campo "item" que se filtra por el campo "grupo" del formulario y autollena "marca".
        var cfg = new DataLookupConfig(
            seed.TablaId,
            DisplayColumnId: seed.NombreCol,
            Filters: new[] { new DataLookupFilterConfig(seed.GrupoCol, FromFieldKey: "grupo") },
            Autofill: new[] { new DataLookupAutofillConfig(seed.MarcaCol, "marca") });

        // Sin elegir grupo: se ve todo el catalogo.
        var todo = await RunAsync(seed, s => s.SearchForFieldAsync(cfg, null));
        Assert.Equal(3, todo.Total);

        // Con grupo = Material, la lista se reduce a lo de ese grupo.
        var material = await RunAsync(seed, s => s.SearchForFieldAsync(
            cfg, new Dictionary<string, string?> { ["grupo"] = "Material" }));
        var fila = Assert.Single(material.Rows);
        Assert.Equal("Cemento gris", fila.Label);
        // Y trae la columna del autollenado, sin pedir la fila entera.
        Assert.Equal("Argos", DataLookupBinder.BuildAutofill(cfg, fila)["marca"]);

        // La cascada se combina con la busqueda por texto dentro del grupo ya filtrado.
        var conTexto = await RunAsync(seed, s => s.SearchForFieldAsync(
            cfg, new Dictionary<string, string?> { ["grupo"] = "Herramienta" }, search: "destor"));
        Assert.Equal("Destornillador", Assert.Single(conTexto.Rows).Label);

        // Un valor de grupo que no existe deja la lista vacia (no cae al catalogo completo).
        var ninguno = await RunAsync(seed, s => s.SearchForFieldAsync(
            cfg, new Dictionary<string, string?> { ["grupo"] = "Inexistente" }));
        Assert.Equal(0, ninguno.Total);
    }

    [Fact]
    public async Task Con_Filtro_Obligatorio_No_Se_Consulta_Hasta_Elegir_El_Padre()
    {
        var seed = await SeedAsync("DL Obligatorio");

        var cfg = new DataLookupConfig(
            seed.TablaId,
            DisplayColumnId: seed.NombreCol,
            Filters: new[] { new DataLookupFilterConfig(seed.GrupoCol, FromFieldKey: "grupo", RequireSource: true) });

        // Sin el padre: pagina vacia SIN ir a la base (es "elige primero grupo", no "no hay nada").
        var bloqueado = await RunAsync(seed, s => s.SearchForFieldAsync(cfg, null));
        Assert.Equal(0, bloqueado.Total);
        Assert.Empty(bloqueado.Rows);

        // Con el padre elegido, ya devuelve lo suyo.
        var abierto = await RunAsync(seed, s => s.SearchForFieldAsync(
            cfg, new Dictionary<string, string?> { ["grupo"] = "Herramienta" }));
        Assert.Equal(2, abierto.Total);
    }

    [Fact]
    public async Task Es_Aislado_Entre_Tenants()
    {
        var a = await SeedAsync("DL Tenant A");
        var b = await SeedAsync("DL Tenant B");

        // El tenant B apunta a la tabla de A: ni catalogo, ni busqueda, ni resolucion.
        Assert.Empty(await RunAsync(b, s => s.ListColumnsAsync(a.TablaId)));

        var cross = await RunAsync(b, s => s.SearchAsync(new DataLookupQuery(a.TablaId)));
        Assert.Equal(0, cross.Total);
        Assert.Empty(cross.Rows);

        var propias = await RunAsync(a, s => s.SearchAsync(new DataLookupQuery(a.TablaId, DisplayColumnId: a.NombreCol)));
        var idDeA = propias.Rows[0].RowId;
        Assert.Empty(await RunAsync(b, s => s.ResolveAsync(a.TablaId, new[] { idDeA }, a.NombreCol)));

        // Y el catalogo de B solo muestra lo de B.
        var tablasDeB = await RunAsync(b, s => s.ListTablesAsync());
        Assert.DoesNotContain(tablasDeB, t => t.Id == a.TablaId);
    }

    [Fact]
    public void Config_Se_Serializa_Y_Distingue_De_Las_Opciones_De_Texto()
    {
        var tablaId = Guid.CreateVersion7();
        var colId = Guid.CreateVersion7();
        var cfg = new DataLookupConfig(
            tablaId,
            DisplayColumnId: colId,
            Filters: new[] { new DataLookupFilterConfig(colId, FromFieldKey: "grupo") },
            Autofill: new[] { new DataLookupAutofillConfig(colId, "marca") });

        var vuelta = DataLookupConfig.TryParse(cfg.ToJson());
        Assert.NotNull(vuelta);
        Assert.Equal(tablaId, vuelta!.TableId);
        Assert.Equal(colId, vuelta.DisplayColumnId);
        Assert.Equal("grupo", vuelta.Filters![0].FromFieldKey);
        Assert.Equal("marca", vuelta.Autofill![0].TargetFieldKey);

        // Un campo Select de toda la vida (opciones una por linea) NO se confunde con lookup:
        // de eso depende que los campos existentes sigan funcionando sin migrar nada.
        Assert.Null(DataLookupConfig.TryParse("Opcion A\nOpcion B"));
        Assert.Null(DataLookupConfig.TryParse(null));
        Assert.Null(DataLookupConfig.TryParse("   "));
        Assert.Null(DataLookupConfig.TryParse("{ esto no es json valido"));
        // Un JSON sin tabla tampoco es una configuracion utilizable.
        Assert.Null(DataLookupConfig.TryParse("{\"displayColumnId\":\"" + colId + "\"}"));
    }

    // ---- Helpers ----

    private async Task<T> RunAsync<T>(SeedData seed, Func<IDataLookupService, Task<T>> action)
    {
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenant = new TestTenantContext(seed.TenantId);
        var service = new DataLookupService(ctx, new DataContainerService(ctx, tenant));
        return await action(service);
    }

    /// <summary>Tenant con un modelo y una tabla Items (Nombre, Grupo, Marca) con 3 filas.</summary>
    private async Task<SeedData> SeedAsync(string tenantName)
    {
        var tenantId = Guid.CreateVersion7();
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = tenantName });
            await ctx.SaveChangesAsync();
        }

        var modelId = Guid.CreateVersion7();
        var tablaId = Guid.CreateVersion7();
        var nombreCol = Guid.CreateVersion7();
        var grupoCol = Guid.CreateVersion7();
        var marcaCol = Guid.CreateVersion7();

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            ctx.DataModels.Add(new DataModel { Id = modelId, TenantId = tenantId, Name = "Catalogo " + tenantName });
            ctx.DataContainers.Add(new DataContainer
            {
                Id = tablaId,
                TenantId = tenantId,
                ModelId = modelId,
                Name = "Items"
            });
            AddColumn(ctx, tenantId, tablaId, nombreCol, "Nombre", 0);
            AddColumn(ctx, tenantId, tablaId, grupoCol, "Grupo", 1);
            AddColumn(ctx, tenantId, tablaId, marcaCol, "Marca", 2);

            foreach (var (nombre, grupo, marca) in new[]
            {
                ("Martillo", "Herramienta", "Stanley"),
                ("Destornillador", "Herramienta", "Bahco"),
                ("Cemento gris", "Material", "Argos")
            })
            {
                var rowId = Guid.CreateVersion7();
                ctx.DataContainerRows.Add(new DataContainerRow { Id = rowId, TenantId = tenantId, ContainerId = tablaId });
                AddCell(ctx, tenantId, rowId, nombreCol, nombre);
                AddCell(ctx, tenantId, rowId, grupoCol, grupo);
                AddCell(ctx, tenantId, rowId, marcaCol, marca);
            }

            await ctx.SaveChangesAsync();
        }

        return new SeedData(tenantId, tablaId, nombreCol, grupoCol, marcaCol);
    }

    private static void AddColumn(
        EcorexDbContext ctx, Guid tenantId, Guid tablaId, Guid id, string name, int sortOrder)
        => ctx.DataContainerColumns.Add(new DataContainerColumn
        {
            Id = id,
            TenantId = tenantId,
            ContainerId = tablaId,
            Name = name,
            Type = DataContainerColumnType.Text,
            SortOrder = sortOrder
        });

    private static void AddCell(
        EcorexDbContext ctx, Guid tenantId, Guid rowId, Guid columnId, string value)
        => ctx.DataContainerCells.Add(new DataContainerCell
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            RowId = rowId,
            ColumnId = columnId,
            Value = value
        });

    private sealed record SeedData(Guid TenantId, Guid TablaId, Guid NombreCol, Guid GrupoCol, Guid MarcaCol);

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class DataLookupTests_Postgres
    : DataLookupTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public DataLookupTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class DataLookupTests_SqlServer
    : DataLookupTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public DataLookupTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture)
    {
    }
}
