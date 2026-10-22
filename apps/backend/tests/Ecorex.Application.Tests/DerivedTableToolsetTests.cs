using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Toolset del asistente de tablas derivadas (ADR-0130): argumentos invalidos se rechazan con mensaje claro,
/// un origen DERIVADO se rechaza (un solo nivel) ANTES de tocar el servicio, describir_tabla devuelve columnas
/// tipadas + muestra acotada, proponer_derivacion arma la spec por NOMBRES (Count siempre) y devuelve la vista
/// previa del servicio, y crear_tabla_derivada delega el alta al mismo servicio del wizard. EF InMemory + fake
/// del servicio: sin BD real ni IA.
/// </summary>
public class DerivedTableToolsetTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid ModelId = Guid.NewGuid();

    private sealed class FakeTenant : ITenantContext { public Guid? TenantId => Tenant; public Guid? UserId => null; }

    // Fake del servicio de derivadas: registra lo que le piden y devuelve una vista previa canonica.
    private sealed class FakeDerived : IDerivedTableService
    {
        public DerivationSpec? LastPreviewSpec { get; private set; }
        public int LastPreviewTake { get; private set; }
        public CreateDerivedTableRequest? LastCreate { get; private set; }
        public Guid LastActor { get; private set; }

        public Task<DerivationPreviewDto> PreviewAsync(DerivationSpec spec, int take = 20, CancellationToken ct = default)
        {
            LastPreviewSpec = spec;
            LastPreviewTake = take;
            var cols = new[]
            {
                new DerivedColumnPlan("Categoria", DataContainerColumnType.Text, DerivedColumnRole.GroupBy),
                new DerivedColumnPlan("Clave", DataContainerColumnType.Text, DerivedColumnRole.Key),
                new DerivedColumnPlan("Cantidad", DataContainerColumnType.Number, DerivedColumnRole.Aggregate),
                new DerivedColumnPlan("Total precio", DataContainerColumnType.Decimal, DerivedColumnRole.Aggregate),
            };
            var rows = new List<IReadOnlyDictionary<string, string?>>
            {
                new Dictionary<string, string?> { ["Categoria"] = "Tornillos", ["Clave"] = "tornillos", ["Cantidad"] = "2", ["Total precio"] = "30" },
                new Dictionary<string, string?> { ["Categoria"] = "Tuercas", ["Clave"] = "tuercas", ["Cantidad"] = "1", ["Total precio"] = "5" },
            };
            return Task.FromResult(new DerivationPreviewDto(cols, rows, 2, 3, 3));
        }

        public Task<DerivedTableCreatedDto> CreateAsync(CreateDerivedTableRequest req, Guid actorUserId, CancellationToken ct = default)
        {
            LastCreate = req;
            LastActor = actorUserId;
            return Task.FromResult(new DerivedTableCreatedDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
        }

        public Task<DerivedRefreshOutcome> RefreshAsync(Guid connectorId, ApiImportMode mode, string? keyColumnName, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed record Fixture(DerivedTableToolset Toolset, FakeDerived Derived, DerivedChatTestDb.InnerDb Inner,
        Guid ItemsId, Guid DerivedId, Guid CategoriaCol, Guid MarcaCol, Guid PrecioCol);

    // Contenedor con una tabla "Items" (Categoria texto, Marca texto, Precio dec, Fecha) con 3 filas, y una
    // tabla "Items por categoria" que YA es derivada de Items.
    private static Fixture NewFixture()
    {
        var inner = DerivedChatTestDb.NewInner();
        inner.DataModels.Add(new DataModel { Id = ModelId, TenantId = Tenant, Name = "ALEGRA" });

        var items = new DataContainer { TenantId = Tenant, ModelId = ModelId, Name = "Items" };
        inner.DataContainers.Add(items);
        var cat = new DataContainerColumn { TenantId = Tenant, ContainerId = items.Id, Name = "Categoria", Type = DataContainerColumnType.Text, SortOrder = 0 };
        var marca = new DataContainerColumn { TenantId = Tenant, ContainerId = items.Id, Name = "Marca", Type = DataContainerColumnType.Text, SortOrder = 1 };
        var precio = new DataContainerColumn { TenantId = Tenant, ContainerId = items.Id, Name = "Precio", Type = DataContainerColumnType.Decimal, SortOrder = 2 };
        var fecha = new DataContainerColumn { TenantId = Tenant, ContainerId = items.Id, Name = "Fecha", Type = DataContainerColumnType.Date, SortOrder = 3 };
        inner.DataContainerColumns.AddRange(cat, marca, precio, fecha);
        // CreatedAt explicito y creciente: la muestra se ordena por CreatedAt y el test fija la primera fila.
        var t0 = new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
        void Row(string c, string m, string p)
        {
            var r = new DataContainerRow { TenantId = Tenant, ContainerId = items.Id, CreatedAt = t0 = t0.AddSeconds(1) };
            inner.DataContainerRows.Add(r);
            inner.DataContainerCells.Add(new DataContainerCell { TenantId = Tenant, RowId = r.Id, ColumnId = cat.Id, Value = c });
            inner.DataContainerCells.Add(new DataContainerCell { TenantId = Tenant, RowId = r.Id, ColumnId = marca.Id, Value = m });
            inner.DataContainerCells.Add(new DataContainerCell { TenantId = Tenant, RowId = r.Id, ColumnId = precio.Id, Value = p });
        }
        Row("Tornillos", "ACME", "10");
        Row("Tornillos", "ACME", "20");
        Row("Tuercas", "Bosch", "5");

        var derived = new DataContainer { TenantId = Tenant, ModelId = ModelId, Name = "Items por categoria", DerivedFromContainerId = items.Id };
        inner.DataContainers.Add(derived);
        inner.DataContainerColumns.Add(new DataContainerColumn { TenantId = Tenant, ContainerId = derived.Id, Name = "Categoria", Type = DataContainerColumnType.Text });
        inner.SaveChanges();

        var fake = new FakeDerived();
        var ts = new DerivedTableToolset(new DerivedChatTestDb.FakeAppDb(inner), new FakeTenant(), fake);
        return new Fixture(ts, fake, inner, items.Id, derived.Id, cat.Id, marca.Id, precio.Id);
    }

    private static async Task<JsonElement> RunAsync(DerivedTableToolset ts, string tool, object args)
    {
        var json = JsonSerializer.Serialize(args);
        var r = await ts.ExecuteAsync(tool, json, Guid.NewGuid(), autonomous: true);
        return JsonDocument.Parse(r.Json).RootElement;
    }

    private static bool Ok(JsonElement root) => root.GetProperty("ok").GetBoolean();
    private static string Error(JsonElement root) => root.GetProperty("error").GetString() ?? "";

    [Fact]
    public void Las_tres_herramientas_estan_declaradas_y_solo_crear_es_mutante()
    {
        var f = NewFixture();
        var names = f.Toolset.GetSpecs().Select(s => s.Name).ToList();
        Assert.Equal(new[] { "describir_tabla", "proponer_derivacion", "crear_tabla_derivada" }, names);
        Assert.Contains("describir_tabla", f.Toolset.ReadOnlyTools);
        Assert.Contains("proponer_derivacion", f.Toolset.ReadOnlyTools);
        Assert.DoesNotContain("crear_tabla_derivada", f.Toolset.ReadOnlyTools);
    }

    // ---------- Argumentos invalidos ----------

    [Fact]
    public async Task Argumentos_que_no_son_json_devuelven_error()
    {
        var f = NewFixture();
        var r = await f.Toolset.ExecuteAsync("describir_tabla", "{esto no es json", Guid.NewGuid(), true);
        var root = JsonDocument.Parse(r.Json).RootElement;
        Assert.False(Ok(root));
        Assert.Contains("JSON", Error(root));
    }

    [Fact]
    public async Task Sin_contenedor_id_devuelve_error()
    {
        var f = NewFixture();
        var root = await RunAsync(f.Toolset, "describir_tabla", new { tabla = "Items" });
        Assert.False(Ok(root));
        Assert.Contains("contenedor_id", Error(root));
    }

    [Fact]
    public async Task Tabla_inexistente_devuelve_error_con_las_tablas_disponibles()
    {
        var f = NewFixture();
        var root = await RunAsync(f.Toolset, "describir_tabla", new { contenedor_id = ModelId, tabla = "NoExiste" });
        Assert.False(Ok(root));
        Assert.Contains("NoExiste", Error(root));
        Assert.Contains("Items", Error(root));
    }

    [Fact]
    public async Task Proponer_sin_agrupar_por_devuelve_error_y_no_llama_al_servicio()
    {
        var f = NewFixture();
        var root = await RunAsync(f.Toolset, "proponer_derivacion", new { contenedor_id = ModelId, tabla_origen = "Items", agrupar_por = Array.Empty<string>() });
        Assert.False(Ok(root));
        Assert.Contains("agrupar_por", Error(root));
        Assert.Null(f.Derived.LastPreviewSpec);
    }

    [Fact]
    public async Task Proponer_con_columna_inexistente_devuelve_error_con_las_columnas()
    {
        var f = NewFixture();
        var root = await RunAsync(f.Toolset, "proponer_derivacion", new { contenedor_id = ModelId, tabla_origen = "Items", agrupar_por = new[] { "Color" } });
        Assert.False(Ok(root));
        Assert.Contains("Color", Error(root));
        Assert.Contains("Categoria", Error(root));
        Assert.Null(f.Derived.LastPreviewSpec);
    }

    [Fact]
    public async Task Proponer_suma_sobre_texto_devuelve_error()
    {
        var f = NewFixture();
        var root = await RunAsync(f.Toolset, "proponer_derivacion", new
        {
            contenedor_id = ModelId,
            tabla_origen = "Items",
            agrupar_por = new[] { "Categoria" },
            agregados = new[] { new { funcion = "sum", columna = "Marca" } }
        });
        Assert.False(Ok(root));
        Assert.Contains("Suma", Error(root));
        Assert.Contains("Marca", Error(root));
    }

    [Fact]
    public async Task Proponer_con_funcion_desconocida_devuelve_error()
    {
        var f = NewFixture();
        var root = await RunAsync(f.Toolset, "proponer_derivacion", new
        {
            contenedor_id = ModelId,
            tabla_origen = "Items",
            agrupar_por = new[] { "Categoria" },
            agregados = new[] { new { funcion = "mediana", columna = "Precio" } }
        });
        Assert.False(Ok(root));
        Assert.Contains("mediana", Error(root));
    }

    [Fact]
    public async Task Crear_sin_nombre_devuelve_error_y_no_crea()
    {
        var f = NewFixture();
        var root = await RunAsync(f.Toolset, "crear_tabla_derivada", new { contenedor_id = ModelId, tabla_origen = "Items", agrupar_por = new[] { "Categoria" } });
        Assert.False(Ok(root));
        Assert.Contains("nombre", Error(root));
        Assert.Null(f.Derived.LastCreate);
    }

    // ---------- Un solo nivel ----------

    [Fact]
    public async Task Origen_derivado_se_rechaza_antes_de_llamar_al_servicio()
    {
        var f = NewFixture();
        var root = await RunAsync(f.Toolset, "proponer_derivacion", new { contenedor_id = ModelId, tabla_origen = "Items por categoria", agrupar_por = new[] { "Categoria" } });
        Assert.False(Ok(root));
        Assert.Contains("un solo nivel", Error(root));
        Assert.Null(f.Derived.LastPreviewSpec);

        var crear = await RunAsync(f.Toolset, "crear_tabla_derivada", new { contenedor_id = ModelId, nombre = "X", tabla_origen = "Items por categoria", agrupar_por = new[] { "Categoria" } });
        Assert.False(Ok(crear));
        Assert.Contains("un solo nivel", Error(crear));
        Assert.Null(f.Derived.LastCreate);
    }

    // Hallado en ALEGRA: 'Items' e 'ITEMS' conviven. El nombre EXACTO gana; si solo coincide sin mayusculas y hay
    // varias, se pide precisar (nunca se elige una al azar); por id siempre resuelve.
    [Fact]
    public async Task Tablas_que_solo_difieren_en_mayusculas_resuelven_por_nombre_exacto_o_id()
    {
        var f = NewFixture();
        var upper = new DataContainer { TenantId = Tenant, ModelId = ModelId, Name = "ITEMS" };
        f.Inner.DataContainers.Add(upper);
        f.Inner.DataContainerColumns.Add(new DataContainerColumn { TenantId = Tenant, ContainerId = upper.Id, Name = "codigo", Type = DataContainerColumnType.Text });
        f.Inner.SaveChanges();

        var exact = await RunAsync(f.Toolset, "describir_tabla", new { contenedor_id = ModelId, tabla = "Items" });
        Assert.True(Ok(exact));
        Assert.Equal("Items", exact.GetProperty("tabla").GetProperty("nombre").GetString());
        Assert.Equal(3, exact.GetProperty("total_filas").GetInt32());

        var byId = await RunAsync(f.Toolset, "describir_tabla", new { contenedor_id = ModelId, tabla = upper.Id });
        Assert.True(Ok(byId));
        Assert.Equal("ITEMS", byId.GetProperty("tabla").GetProperty("nombre").GetString());

        var ambiguous = await RunAsync(f.Toolset, "describir_tabla", new { contenedor_id = ModelId, tabla = "items" });
        Assert.False(Ok(ambiguous));
        Assert.Contains("mayusculas", Error(ambiguous));
        Assert.Contains(upper.Id.ToString("D"), Error(ambiguous));
    }

    [Fact]
    public async Task Describir_una_tabla_derivada_la_marca_y_advierte()
    {
        var f = NewFixture();
        var root = await RunAsync(f.Toolset, "describir_tabla", new { contenedor_id = ModelId, tabla = "Items por categoria" });
        Assert.True(Ok(root));
        Assert.True(root.GetProperty("tabla").GetProperty("derivada").GetBoolean());
        Assert.Contains("un solo nivel", root.GetProperty("tabla").GetProperty("advertencia").GetString());
    }

    // ---------- describir_tabla ----------

    [Fact]
    public async Task Describir_devuelve_columnas_tipadas_total_y_muestra()
    {
        var f = NewFixture();
        var root = await RunAsync(f.Toolset, "describir_tabla", new { contenedor_id = ModelId, tabla = "items" }); // case-insensitive
        Assert.True(Ok(root));
        Assert.Equal("Items", root.GetProperty("tabla").GetProperty("nombre").GetString());
        Assert.False(root.GetProperty("tabla").GetProperty("derivada").GetBoolean());
        Assert.Equal(3, root.GetProperty("total_filas").GetInt32());

        var cols = root.GetProperty("columnas").EnumerateArray().ToList();
        Assert.Equal(4, cols.Count);
        var precio = cols.Single(c => c.GetProperty("nombre").GetString() == "Precio");
        Assert.Equal("dec", precio.GetProperty("tipo").GetString());
        Assert.Contains("sum", precio.GetProperty("agregados").EnumerateArray().Select(x => x.GetString()));
        var fecha = cols.Single(c => c.GetProperty("nombre").GetString() == "Fecha");
        Assert.Equal(new[] { "min", "max" }, fecha.GetProperty("agregados").EnumerateArray().Select(x => x.GetString()).ToArray());

        var muestra = root.GetProperty("muestra").EnumerateArray().ToList();
        Assert.Equal(3, muestra.Count);
        Assert.Equal("Tornillos", muestra[0].GetProperty("Categoria").GetString());
        Assert.Equal("10", muestra[0].GetProperty("Precio").GetString());
    }

    [Fact]
    public async Task Describir_acota_la_muestra_a_diez_filas()
    {
        var f = NewFixture();
        for (var i = 0; i < 20; i++)
        {
            var r = new DataContainerRow { TenantId = Tenant, ContainerId = f.ItemsId };
            f.Inner.DataContainerRows.Add(r);
            f.Inner.DataContainerCells.Add(new DataContainerCell { TenantId = Tenant, RowId = r.Id, ColumnId = f.CategoriaCol, Value = "Extra" + i });
        }
        f.Inner.SaveChanges();

        var root = await RunAsync(f.Toolset, "describir_tabla", new { contenedor_id = ModelId, tabla = "Items" });
        Assert.True(Ok(root));
        Assert.Equal(23, root.GetProperty("total_filas").GetInt32());
        Assert.Equal(DerivedTableToolset.SampleRows, root.GetProperty("muestra").GetArrayLength());
    }

    // ---------- proponer_derivacion ----------

    [Fact]
    public async Task Proponer_arma_la_spec_por_nombres_con_count_siempre_y_devuelve_la_vista_previa()
    {
        var f = NewFixture();
        var root = await RunAsync(f.Toolset, "proponer_derivacion", new
        {
            contenedor_id = ModelId,
            tabla_origen = "Items",
            agrupar_por = new[] { "categoria", "Marca" },
            agregados = new[] { new { funcion = "sum", columna = "precio", nombre = "Total precio" } },
            filtro = new[] { new { columna = "Precio", operador = "mayor", valor = "1" } }
        });
        Assert.True(Ok(root));

        // La spec que llego al servicio: ids reales, Count insertado primero, filtro traducido.
        var spec = f.Derived.LastPreviewSpec!;
        Assert.Equal(f.ItemsId, spec.SourceTableId);
        Assert.Equal(new[] { f.CategoriaCol, f.MarcaCol }, spec.GroupBy);
        Assert.Equal(DerivationFn.Count, spec.Aggregates[0].Fn);
        Assert.Equal("Cantidad", spec.Aggregates[0].TargetColumnName);
        Assert.Equal(DerivationFn.Sum, spec.Aggregates[1].Fn);
        Assert.Equal(f.PrecioCol, spec.Aggregates[1].SourceColumnId);
        Assert.Equal("Total precio", spec.Aggregates[1].TargetColumnName);
        Assert.Single(spec.Filter!);
        Assert.Equal(DerivationFilterOp.GreaterThan, spec.Filter![0].Op);
        Assert.Equal(DerivedTableToolset.PreviewGroups, f.Derived.LastPreviewTake);

        // El resultado trae la vista previa y la UI la puede leer tipada.
        Assert.Equal(2, root.GetProperty("total_grupos").GetInt32());
        Assert.Equal(2, root.GetProperty("vista_previa").GetArrayLength());
        var preview = DerivedTableToolset.TryParsePreview(root.GetRawText());
        Assert.NotNull(preview);
        Assert.Equal(2, preview!.TotalGroups);
        Assert.Equal(4, preview.Columns.Count);
        Assert.Equal(DerivedColumnRole.Key, preview.Columns[1].Role);
        Assert.Equal(DataContainerColumnType.Decimal, preview.Columns[3].Type);
        Assert.Equal("30", preview.Rows[0]["Total precio"]);
        Assert.Contains("Total precio = Suma(Precio)", root.GetProperty("resumen").GetString());
    }

    [Fact]
    public async Task Proponer_sin_agregados_deja_solo_count_y_respeta_su_nombre()
    {
        var f = NewFixture();
        var root = await RunAsync(f.Toolset, "proponer_derivacion", new
        {
            contenedor_id = ModelId,
            tabla_origen = "Items",
            agrupar_por = new[] { "Categoria" },
            agregados = new[] { new { funcion = "count", nombre = "Items" } }
        });
        Assert.True(Ok(root));
        var spec = f.Derived.LastPreviewSpec!;
        Assert.Single(spec.Aggregates);
        Assert.Equal(DerivationFn.Count, spec.Aggregates[0].Fn);
        Assert.Equal("Items", spec.Aggregates[0].TargetColumnName);
    }

    // ---------- crear_tabla_derivada ----------

    [Fact]
    public async Task Crear_delega_el_alta_al_servicio_con_la_misma_spec_y_devuelve_los_ids()
    {
        var f = NewFixture();
        var actor = Guid.NewGuid();
        var json = JsonSerializer.Serialize(new
        {
            contenedor_id = ModelId,
            nombre = "Items por categoria y marca",
            descripcion = "Total de precio por categoria y marca",
            tabla_origen = "Items",
            agrupar_por = new[] { "Categoria", "Marca" },
            agregados = new[] { new { funcion = "sum", columna = "Precio", nombre = "Total precio" } },
            refresco = new { tipo = "intervalo", cada_minutos = 30 }
        });
        var r = await f.Toolset.ExecuteAsync("crear_tabla_derivada", json, actor, true);
        var root = JsonDocument.Parse(r.Json).RootElement;
        Assert.True(Ok(root));

        var req = f.Derived.LastCreate!;
        Assert.Equal(ModelId, req.ModelId);
        Assert.Equal("Items por categoria y marca", req.Name);
        Assert.Equal("Total de precio por categoria y marca", req.Description);
        Assert.Equal(ImportScheduleKind.Interval, req.ScheduleKind);
        Assert.Equal(30, req.IntervalMinutes);
        Assert.Equal(new[] { f.CategoriaCol, f.MarcaCol }, req.Spec.GroupBy);
        Assert.Equal(2, req.Spec.Aggregates.Count);
        Assert.Equal(actor, f.Derived.LastActor);

        var created = DerivedTableToolset.TryParseCreated(r.Json);
        Assert.NotNull(created);
        Assert.Equal("Items por categoria y marca", created!.Value.Name);
        Assert.NotEqual(Guid.Empty, created.Value.Created.TableId);
        Assert.NotEqual(Guid.Empty, created.Value.Created.ProcessId);
    }

    [Fact]
    public async Task Crear_con_refresco_intervalo_sin_minutos_devuelve_error()
    {
        var f = NewFixture();
        var root = await RunAsync(f.Toolset, "crear_tabla_derivada", new
        {
            contenedor_id = ModelId,
            nombre = "X",
            tabla_origen = "Items",
            agrupar_por = new[] { "Categoria" },
            refresco = new { tipo = "intervalo" }
        });
        Assert.False(Ok(root));
        Assert.Contains("cada_minutos", Error(root));
        Assert.Null(f.Derived.LastCreate);
    }
}
