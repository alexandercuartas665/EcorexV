using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Panels;

namespace Ecorex.Application.Tests;

/// <summary>
/// Validador del PanelSpec contra el catalogo tenant-safe (ADR-0066): limite de seguridad de la autoria.
/// Un spec valido no produce errores; referenciar fuentes/campos fuera del catalogo o tipos incorrectos
/// produce mensajes claros. Puro (sin BD).
/// </summary>
public class PanelSpecValidatorTests
{
    private static ReportField F(string name, ReportFieldType type) =>
        new(name, name, type, CanFilter: true, CanGroup: true, CanAggregate: type is ReportFieldType.Number or ReportFieldType.Decimal);

    private static IReadOnlyList<ReportSourceDescriptor> Catalog() => new[]
    {
        new ReportSourceDescriptor("container:fact", "facturas", ReportSourceKind.Container, new[]
        {
            F("Fecha", ReportFieldType.Date),
            F("Cliente NIT", ReportFieldType.Text),
            F("Vendedor", ReportFieldType.Text),
            F("Total", ReportFieldType.Decimal),
            F("Saldo", ReportFieldType.Decimal),
            F("Estado DIAN", ReportFieldType.Text)
        }),
        new ReportSourceDescriptor("container:cli", "clientes", ReportSourceKind.Container, new[]
        {
            F("Identificacion", ReportFieldType.Text),
            F("Nombre", ReportFieldType.Text)
        }),
        new ReportSourceDescriptor("container:ven", "vendedores", ReportSourceKind.Container, new[]
        {
            F("Siigo Id", ReportFieldType.Text),
            F("Nombre completo", ReportFieldType.Text)
        })
    };

    // Escenario de aceptacion ADR-0066 (multi-lookup): DOS lookups AUTOCONTENIDOS via MainKey (sin Join):
    // clientes por NIT y vendedores por codigo. Ambos alias se referencian en widgets/filtros.
    private static PanelSpec MultiLookupSpec() => PanelSpec.FromJson(@"{
      ""title"": ""Ventas"",
      ""sources"": { ""main"": { ""container"": ""facturas"" },
        ""lookups"": [
          { ""container"": ""clientes"", ""mainKey"": ""Cliente NIT"", ""key"": ""Identificacion"", ""bring"": { ""Nombre"": ""ClienteNombre"" } },
          { ""container"": ""vendedores"", ""mainKey"": ""Vendedor"", ""key"": ""Siigo Id"", ""bring"": { ""Nombre completo"": ""VendedorNombre"" } }
        ] },
      ""filters"": [ { ""field"": ""VendedorNombre"", ""control"": ""dropdown"" }, { ""field"": ""ClienteNombre"", ""control"": ""text"" } ],
      ""kpis"": [ { ""label"": ""Ventas"", ""agg"": ""sum"", ""field"": ""Total"", ""format"": ""moneyM"" } ],
      ""widgets"": [
        { ""type"": ""pareto"", ""dim"": ""ClienteNombre"", ""agg"": ""sum"", ""field"": ""Total"" },
        { ""type"": ""bar"", ""dim"": ""VendedorNombre"", ""agg"": ""sum"", ""field"": ""Total"" }
      ]
    }")!;

    private static PanelSpec SiigoSpec() => PanelSpec.FromJson(@"{
      ""title"": ""Ventas"",
      ""sources"": { ""main"": { ""container"": ""facturas"" },
        ""lookups"": [ { ""container"": ""clientes"", ""key"": ""Identificacion"", ""bring"": { ""Nombre"": ""ClienteNombre"" } } ] },
      ""join"": { ""mainKey"": ""Cliente NIT"", ""lookup"": ""clientes"" },
      ""derived"": [ { ""name"": ""Mes"", ""from"": ""Fecha"", ""op"": ""yyyymm"" } ],
      ""filters"": [ { ""field"": ""Vendedor"", ""control"": ""dropdown"" }, { ""field"": ""ClienteNombre"", ""control"": ""text"" } ],
      ""kpis"": [ { ""label"": ""Ventas"", ""agg"": ""sum"", ""field"": ""Total"", ""format"": ""moneyM"" },
                  { ""label"": ""Clientes"", ""agg"": ""countDistinct"", ""field"": ""Cliente NIT"", ""format"": ""int"" } ],
      ""widgets"": [
        { ""type"": ""line"", ""dim"": ""Mes"", ""agg"": ""sum"", ""field"": ""Total"", ""scale"": 1000000 },
        { ""type"": ""pareto"", ""dim"": ""ClienteNombre"", ""agg"": ""sum"", ""field"": ""Total"" },
        { ""type"": ""donut"", ""dim"": ""Estado DIAN"", ""agg"": ""count"" },
        { ""type"": ""table"", ""groupBy"": ""ClienteNombre"",
          ""columns"": [ { ""label"": ""Cliente"", ""field"": ""ClienteNombre"" }, { ""label"": ""Ventas"", ""agg"": ""sum"", ""aggField"": ""Total"" } ] }
      ]
    }")!;

    [Fact]
    public void ValidSiigoSpec_ProducesNoErrors()
    {
        var errors = PanelSpecValidator.Validate(SiigoSpec(), Catalog());
        Assert.Empty(errors);
    }

    [Fact]
    public void MissingMainSource_IsReported()
    {
        var spec = SiigoSpec();
        spec.Sources.Main.Container = "no-existe";
        var errors = PanelSpecValidator.Validate(spec, Catalog());
        Assert.Contains(errors, e => e.Contains("no-existe"));
    }

    [Fact]
    public void UnknownField_InWidget_IsReported()
    {
        var spec = SiigoSpec();
        spec.Widgets[0].Dim = "CampoInventado";
        var errors = PanelSpecValidator.Validate(spec, Catalog());
        Assert.Contains(errors, e => e.Contains("CampoInventado"));
    }

    [Fact]
    public void SumOnNonExistingField_IsReported()
    {
        var spec = SiigoSpec();
        spec.Kpis[0].Field = "NoExiste";
        var errors = PanelSpecValidator.Validate(spec, Catalog());
        Assert.Contains(errors, e => e.Contains("NoExiste"));
    }

    [Fact]
    public void DerivedFromNonDateField_IsReported()
    {
        var spec = SiigoSpec();
        spec.Derived[0].From = "Vendedor"; // texto, no fecha
        var errors = PanelSpecValidator.Validate(spec, Catalog());
        Assert.Contains(errors, e => e.Contains("Mes") && e.Contains("fecha"));
    }

    [Fact]
    public void BroughtAlias_IsAvailableForReferences()
    {
        // ClienteNombre no es campo de 'facturas'; solo existe como alias del lookup. Debe validar.
        var errors = PanelSpecValidator.Validate(SiigoSpec(), Catalog());
        Assert.Empty(errors);
    }

    [Fact]
    public void UnknownWidgetType_IsReported()
    {
        var spec = SiigoSpec();
        spec.Widgets[0].Type = "radar";
        var errors = PanelSpecValidator.Validate(spec, Catalog());
        Assert.Contains(errors, e => e.Contains("radar"));
    }

    [Fact]
    public void MultiLookup_TwoSelfContainedLookups_ProducesNoErrors()
    {
        // clientes (por NIT) Y vendedores (por codigo), ambos via MainByKey, sin Join. Debe validar.
        var errors = PanelSpecValidator.Validate(MultiLookupSpec(), Catalog());
        Assert.Empty(errors);
    }

    [Fact]
    public void Lookup_WithBadMainKey_IsReported()
    {
        var spec = MultiLookupSpec();
        spec.Sources.Lookups[1].MainKey = "NoEsCampoDeFacturas";
        var errors = PanelSpecValidator.Validate(spec, Catalog());
        Assert.Contains(errors, e => e.Contains("mainKey") && e.Contains("NoEsCampoDeFacturas"));
    }

    [Fact]
    public void Lookup_AliasCollisionBetweenLookups_IsReported()
    {
        // vendedores trae su nombre bajo el MISMO alias que clientes: colision -> error.
        var spec = MultiLookupSpec();
        spec.Sources.Lookups[1].Bring["Nombre completo"] = "ClienteNombre";
        var errors = PanelSpecValidator.Validate(spec, Catalog());
        Assert.Contains(errors, e => e.Contains("ClienteNombre") && e.Contains("choca"));
    }

    [Fact]
    public void Lookup_WithoutMainKeyNorJoin_IsReported()
    {
        // Un lookup sin MainKey y sin Join que lo cruce no se aplicaria: se avisa.
        var spec = MultiLookupSpec();
        spec.Sources.Lookups[1].MainKey = null;
        var errors = PanelSpecValidator.Validate(spec, Catalog());
        Assert.Contains(errors, e => e.Contains("vendedores") && e.Contains("no se aplicaria"));
    }

    [Fact]
    public void LegacyJoinLookup_WithoutMainKey_StillValid()
    {
        // Compatibilidad: el SiigoSpec usa Join (lookup sin MainKey). Debe seguir validando.
        var errors = PanelSpecValidator.Validate(SiigoSpec(), Catalog());
        Assert.Empty(errors);
    }

    // ---- Fuentes EXTERNAS como Main del panel (ADR-0064) ----
    // Los campos de una fuente externa se publican con CanFilter/CanGroup/CanAggregate=false (el filtrado y
    // la agregacion del panel ocurren EN MEMORIA sobre las filas en vivo del conector), y las externas se
    // agregan al FINAL del catalogo del tenant. FindSource ya no debe excluirlas.

    private static ReportField Ext(string name, ReportFieldType type) =>
        new(name, name, type, CanFilter: false, CanGroup: false, CanAggregate: false);

    private static ReportSourceDescriptor ItemsDemo(string key = "external:11111111-1111-1111-1111-111111111111") =>
        new(key, "items_demo", ReportSourceKind.External, new[]
        {
            Ext("Codigo", ReportFieldType.Text),
            Ext("Nombre", ReportFieldType.Text)
        });

    // Externa AL FINAL, como la arma ReportCatalog.GetSourcesAsync (nativas -> contenedores -> externas).
    private static IReadOnlyList<ReportSourceDescriptor> CatalogWithExternal()
    {
        var list = Catalog().ToList();
        list.Add(ItemsDemo());
        return list;
    }

    private static PanelSpec ItemsDemoSpec() => PanelSpec.FromJson(@"{
      ""title"": ""Items en vivo"",
      ""sources"": { ""main"": { ""container"": ""items_demo"" } },
      ""kpis"": [ { ""label"": ""Items"", ""agg"": ""count"", ""format"": ""int"" } ],
      ""widgets"": [
        { ""type"": ""bar"", ""dim"": ""Codigo"", ""agg"": ""count"" },
        { ""type"": ""table"", ""groupBy"": ""Nombre"",
          ""columns"": [ { ""label"": ""Nombre"", ""field"": ""Nombre"" }, { ""label"": ""Items"", ""agg"": ""count"" } ] }
      ]
    }")!;

    [Fact]
    public void FindSource_ResolvesExternalByDisplayName()
    {
        var found = PanelSpecValidator.FindSource(CatalogWithExternal(), "items_demo");
        Assert.NotNull(found);
        Assert.Equal(ReportSourceKind.External, found!.Kind);
    }

    [Fact]
    public void ExternalSourceAsMain_ProducesNoErrors()
    {
        // Panel cuya Main es una fuente externa, listando/agrupando por sus campos (Codigo, Nombre).
        var errors = PanelSpecValidator.Validate(ItemsDemoSpec(), CatalogWithExternal());
        Assert.Empty(errors);
    }

    [Fact]
    public void ExternalMain_UnknownField_IsReported()
    {
        var spec = ItemsDemoSpec();
        spec.Widgets[0].Dim = "NoEsCampoExterno";
        var errors = PanelSpecValidator.Validate(spec, CatalogWithExternal());
        Assert.Contains(errors, e => e.Contains("NoEsCampoExterno"));
    }

    [Fact]
    public void FindSource_PrefersNonExternalOnNameCollision()
    {
        // Un contenedor y una externa comparten DisplayName. La externa va al final del catalogo, asi que
        // gana la no-externa previa (no se cambia el comportamiento de los paneles existentes).
        var catalog = new List<ReportSourceDescriptor>
        {
            new("container:items", "items_demo", ReportSourceKind.Container, new[] { F("Codigo", ReportFieldType.Text) }),
            ItemsDemo()
        };
        var found = PanelSpecValidator.FindSource(catalog, "items_demo");
        Assert.NotNull(found);
        Assert.Equal(ReportSourceKind.Container, found!.Kind);
    }

    // ---- Pipeline comercial: Main native + lookup de MODULO de formulario (ADR-0068) ----
    // Ejercita: resolucion por Source/clave (native:taskitem, form:COT), MainKey por Key ("Number" cuyo
    // DisplayName es "Numero"), Where fijo, lookup con KeyTransform + Reduce, y Sum sobre un alias traido.

    private static IReadOnlyList<ReportSourceDescriptor> PipelineCatalog() => new[]
    {
        // Actividades: Key != DisplayName en varios campos (Number->Numero, Board->Tablero, Stage->Etapa).
        new ReportSourceDescriptor("native:taskitem", "Actividades", ReportSourceKind.Native, new[]
        {
            new ReportField("Number", "Numero", ReportFieldType.Text),
            new ReportField("Board", "Tablero", ReportFieldType.Text),
            new ReportField("Stage", "Etapa", ReportFieldType.Text),
            new ReportField("CreatedAt", "Creada", ReportFieldType.Date)
        }),
        // Modulo COT: campo numerico (Decimal, agregable) + sinteticos Reference/TransactionDate.
        new ReportSourceDescriptor("form:COT", "SIMULADOR COTIZACIONES", ReportSourceKind.Native, new[]
        {
            new ReportField("tot_total", "Total", ReportFieldType.Decimal, CanFilter: true, CanGroup: true, CanAggregate: true),
            new ReportField("Reference", "Referencia", ReportFieldType.Text),
            new ReportField("TransactionDate", "Fecha de transaccion", ReportFieldType.Date)
        })
    };

    private static PanelSpec PipelineSpec() => PanelSpec.FromJson(@"{
      ""Title"": ""Pipeline comercial - monto por estado"",
      ""Sources"": { ""Main"": { ""Source"": ""native:taskitem"" },
        ""Lookups"": [
          { ""Source"": ""form:COT"", ""MainKey"": ""Number"", ""Key"": ""Reference"",
            ""KeyTransform"": ""beforeDash"", ""Reduce"": { ""By"": ""Reference"", ""Keep"": ""latest"" },
            ""Bring"": { ""tot_total"": ""MontoCotizacion"" } }
        ] },
      ""Where"": [ { ""Field"": ""Tablero"", ""Op"": ""eq"", ""Value"": ""GESTION COMERCIAL"" } ],
      ""Filters"": [ { ""Field"": ""Etapa"", ""Label"": ""Estado"", ""Control"": ""dropdown"" } ],
      ""Kpis"": [
        { ""Label"": ""Monto total"", ""Agg"": ""sum"", ""Field"": ""MontoCotizacion"", ""Format"": ""moneyM"" },
        { ""Label"": ""Cotizaciones"", ""Agg"": ""count"", ""Format"": ""int"" }
      ],
      ""Widgets"": [
        { ""Type"": ""bar"", ""Title"": ""Monto por estado"", ""Dim"": ""Etapa"", ""Agg"": ""sum"", ""Field"": ""MontoCotizacion"" },
        { ""Type"": ""table"", ""Title"": ""Detalle"", ""GroupBy"": ""Etapa"",
          ""Columns"": [ { ""Field"": ""Etapa"", ""Label"": ""Estado"" },
                       { ""Agg"": ""count"", ""Label"": ""Cotizaciones"" },
                       { ""Agg"": ""sum"", ""AggField"": ""MontoCotizacion"", ""Label"": ""Monto"", ""Format"": ""money"" } ] }
      ]
    }")!;

    [Fact]
    public void PipelineSpec_ProducesNoErrors()
    {
        var errors = PanelSpecValidator.Validate(PipelineSpec(), PipelineCatalog());
        Assert.Empty(errors);
    }

    [Fact]
    public void FindByRef_ResolvesByKey()
    {
        var found = PanelSpecValidator.FindByRef(PipelineCatalog(), "form:COT", null);
        Assert.NotNull(found);
        Assert.Equal("SIMULADOR COTIZACIONES", found!.DisplayName);
    }

    [Fact]
    public void FindByRef_FallsBackToDisplayName()
    {
        var found = PanelSpecValidator.FindByRef(PipelineCatalog(), null, "Actividades");
        Assert.NotNull(found);
        Assert.Equal("native:taskitem", found!.Key);
    }

    [Fact]
    public void Where_UnknownField_IsReported()
    {
        var spec = PipelineSpec();
        spec.Where[0].Field = "NoExisteCampo";
        var errors = PanelSpecValidator.Validate(spec, PipelineCatalog());
        Assert.Contains(errors, e => e.Contains("NoExisteCampo"));
    }

    [Fact]
    public void Where_UnknownOp_IsReported()
    {
        var spec = PipelineSpec();
        spec.Where[0].Op = "between"; // no soportado en Where fijo
        var errors = PanelSpecValidator.Validate(spec, PipelineCatalog());
        Assert.Contains(errors, e => e.Contains("operador desconocido"));
    }

    [Fact]
    public void Lookup_UnknownKeyTransform_IsReported()
    {
        var spec = PipelineSpec();
        spec.Sources.Lookups[0].KeyTransform = "afterColon";
        var errors = PanelSpecValidator.Validate(spec, PipelineCatalog());
        Assert.Contains(errors, e => e.Contains("keyTransform"));
    }

    [Fact]
    public void Reduce_ByNotALookupField_IsReported()
    {
        var spec = PipelineSpec();
        spec.Sources.Lookups[0].Reduce!.By = "NoEsCampoDelLookup";
        var errors = PanelSpecValidator.Validate(spec, PipelineCatalog());
        Assert.Contains(errors, e => e.Contains("NoEsCampoDelLookup"));
    }

    [Fact]
    public void Kpi_When_UnknownField_IsReported()
    {
        var spec = PipelineSpec();
        spec.Kpis[0].When.Add(new PanelWhere { Field = "CampoQueNoEsta", Op = "eq", Value = "x" });
        var errors = PanelSpecValidator.Validate(spec, PipelineCatalog());
        Assert.Contains(errors, e => e.Contains("CampoQueNoEsta"));
    }

    [Fact]
    public void InvalidJson_ReturnsNullSpec()
    {
        Assert.Null(PanelSpec.FromJson("{ esto no es json "));
    }

    [Fact]
    public void RoundTrip_PreservesShape()
    {
        var spec = SiigoSpec();
        var json = spec.ToJson();
        var again = PanelSpec.FromJson(json)!;
        Assert.Equal(spec.Sources.Main.Container, again.Sources.Main.Container);
        Assert.Equal(spec.Widgets.Count, again.Widgets.Count);
        Assert.Equal("Cliente NIT", again.Join!.MainKey);
        Assert.Equal("ClienteNombre", again.Sources.Lookups[0].Bring["Nombre"]);
    }
}
