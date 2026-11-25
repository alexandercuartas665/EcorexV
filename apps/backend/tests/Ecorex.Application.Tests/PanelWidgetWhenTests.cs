using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Panels;

namespace Ecorex.Application.Tests;

/// <summary>
/// When por WIDGET y fecha relativa lastDays del motor de paneles (extiende ADR-0066/0068/0089): un widget
/// puede mirar solo un subconjunto (p.ej. Etapa = Cierre) y "los ultimos N dias" se evalua en la zona del
/// tenant. Puro: sin BD ni Blazor.
/// </summary>
public class PanelWidgetWhenTests
{
    private static readonly TimeZoneInfo Bogota = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "SA Pacific Standard Time" : "America/Bogota");

    // "Ahora" fijo: 2026-11-24 10:00 en Bogota (15:00 UTC).
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 15, 0, 0, TimeSpan.Zero);

    private static PanelRow Row(params (string k, object? v)[] cells)
    {
        var r = new PanelRow();
        foreach (var (k, v) in cells) { r[k] = v; }
        return r;
    }

    [Theory]
    [InlineData("2026-11-24T14:00:00Z", true)]   // hoy
    [InlineData("2026-11-18T15:00:00Z", true)]   // hace 6 dias: el 7o dia contando hoy
    [InlineData("2026-11-17T15:00:00Z", false)]  // hace 7 dias: fuera
    [InlineData("2026-11-25T15:00:00Z", false)]  // futuro
    [InlineData("2026-11-18T03:00:00Z", false)]  // 17-nov 22:00 en Bogota: el dia LOCAL queda fuera
    [InlineData("2026-11-25T03:00:00Z", true)]   // 24-nov 22:00 en Bogota: hoy local aunque en UTC sea manana
    public void LastDays_UsaDiasCalendarioDelTenant(string fecha, bool esperado)
    {
        var row = Row(("ColumnEnteredAt", DateTimeOffset.Parse(fecha, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(esperado, PanelDataEngine.Matches(row, "ColumnEnteredAt", "lastDays", "7", Bogota, Now));
    }

    [Fact]
    public void LastDays_FechaSinHora_NoSeCorreAlDiaAnterior()
    {
        // Campo de solo fecha (EAV "2026-11-18"): es el dia calendario tal cual, no 17-nov por la zona.
        var row = Row(("Fecha", "2026-11-18"));
        Assert.True(PanelDataEngine.Matches(row, "Fecha", "lastDays", "7", Bogota, Now));
        Assert.False(PanelDataEngine.Matches(row, "Fecha", "lastDays", "6", Bogota, Now));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("siete")]
    [InlineData("")]
    public void LastDays_ValorInvalido_NoCoincide(string dias)
    {
        var row = Row(("Fecha", Now));
        Assert.False(PanelDataEngine.Matches(row, "Fecha", "lastDays", dias, Bogota, Now));
    }

    [Fact]
    public void LastDays_SinFecha_NoCoincide()
        => Assert.False(PanelDataEngine.Matches(Row(("Fecha", "no es fecha")), "Fecha", "lastDays", "7", Bogota, Now));

    [Fact]
    public void Subset_AplicaElWhenDelWidget_Y_VacioDevuelveTodo()
    {
        var rows = new List<PanelRow>
        {
            Row(("Etapa", "Cierre"), ("ColumnEnteredAt", Now.AddDays(-1)), ("Asignado", "Ana")),
            Row(("Etapa", "Cierre"), ("ColumnEnteredAt", Now.AddDays(-20)), ("Asignado", "Ana")),
            Row(("Etapa", "Cotizacion"), ("ColumnEnteredAt", Now.AddDays(-1)), ("Asignado", "Luis")),
        };

        Assert.Equal(3, PanelDataEngine.Subset(rows, new List<PanelWhere>(), Bogota, Now).Count);

        var cierres = PanelDataEngine.Subset(rows, new List<PanelWhere>
        {
            new() { Field = "Etapa", Op = "eq", Value = "Cierre" }
        }, Bogota, Now);
        Assert.Equal(2, cierres.Count);

        var cierres7d = PanelDataEngine.Subset(rows, new List<PanelWhere>
        {
            new() { Field = "Etapa", Op = "eq", Value = "Cierre" },
            new() { Field = "ColumnEnteredAt", Op = "lastDays", Value = "7" }
        }, Bogota, Now);
        Assert.Single(cierres7d);

        // El widget agrega sobre su subconjunto: cotizaciones cerradas por usuario.
        var porUsuario = PanelDataEngine.GroupAggregate(cierres, "Asignado", "count", null);
        Assert.Equal(new PanelSlice("Ana", 2), Assert.Single(porUsuario));
    }

    [Fact]
    public void PanelSpec_DeserializaElWhenDelWidget()
    {
        var spec = PanelSpec.FromJson("""
            { "title": "Pipeline", "sources": { "main": { "container": "x" } },
              "widgets": [ { "type": "bar", "dim": "Asignado", "agg": "count", "sortDim": "asc",
                             "when": [ { "op": "eq", "field": "Etapa", "value": "Cierre" },
                                       { "op": "lastDays", "field": "ColumnEnteredAt", "value": "7" } ] } ] }
            """)!;
        var w = Assert.Single(spec.Widgets);
        Assert.Equal(2, w.When.Count);
        Assert.Equal("lastDays", w.When[1].Op);
        Assert.Equal("asc", w.SortDim);
    }

    // ---- Validador ----

    private static IReadOnlyList<ReportSourceDescriptor> Catalog() => new[]
    {
        new ReportSourceDescriptor("native:taskitem", "Actividades", ReportSourceKind.Native, new[]
        {
            new ReportField("Etapa", "Etapa", ReportFieldType.Text, CanFilter: true, CanGroup: true, CanAggregate: false),
            new ReportField("Asignado", "Asignado", ReportFieldType.Text, CanFilter: true, CanGroup: true, CanAggregate: false),
            new ReportField("ColumnEnteredAt", "ColumnEnteredAt", ReportFieldType.Date, CanFilter: true, CanGroup: true, CanAggregate: false)
        })
    };

    private static PanelSpec SpecConWhen(string whenJson) => PanelSpec.FromJson($$"""
        { "title": "Pipeline", "sources": { "main": { "source": "native:taskitem" } },
          "widgets": [ { "type": "bar", "title": "Cierres", "dim": "Asignado", "agg": "count", "when": {{whenJson}} } ] }
        """)!;

    [Fact]
    public void Validador_AceptaWhenDeWidgetConLastDays()
    {
        var errors = PanelSpecValidator.Validate(SpecConWhen("""
            [ { "op": "eq", "field": "Etapa", "value": "Cierre" }, { "op": "lastDays", "field": "ColumnEnteredAt", "value": "7" } ]
            """), Catalog());
        Assert.Empty(errors);
    }

    [Fact]
    public void Validador_RechazaWhenDeWidgetConCampoUOperadorOValorInvalidos()
    {
        var errors = PanelSpecValidator.Validate(SpecConWhen("""
            [ { "op": "eq", "field": "Inventado", "value": "x" },
              { "op": "between", "field": "Etapa", "value": "x" },
              { "op": "lastDays", "field": "ColumnEnteredAt", "value": "cero" } ]
            """), Catalog());
        Assert.Contains(errors, e => e.Contains("Cierres") && e.Contains("Inventado"));
        Assert.Contains(errors, e => e.Contains("Cierres") && e.Contains("between"));
        Assert.Contains(errors, e => e.Contains("lastDays") && e.Contains("cero"));
    }
}
