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
