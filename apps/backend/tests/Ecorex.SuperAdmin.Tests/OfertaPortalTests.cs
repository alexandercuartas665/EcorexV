using System.Text.Json;
using Ecorex.SuperAdmin.Agents;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// Responder ofertas de portales desde la tarea (ADR-0181): prellenado de la grilla con la ultima cotizacion del
/// mismo codigo, vencimiento de la tarea desde el plazo de la oferta y variables que el boton EJECUTAR_DRON le pasa
/// al flujo. Datos con la forma real del contenedor "Drones Ingenios / Items" de SOLDARCO.
/// </summary>
public class OfertaPortalTests
{
    private static readonly ScrapeCrearActividad.PrellenadoConfig Pre = new()
    {
        ClaveGrilla = "codigo",
        ColumnaClave = "CODIGO",
        OrdenColumna = "PETOF",
        ExcluirColumna = "PETOF",
        RequiereColumna = "VLR_UNI",
        Columnas = new() { ["marca"] = "MARCA", ["vlr_uni"] = "VLR_UNI", ["dias_entrega"] = "DIAS_ENTREGA" },
    };

    private static IReadOnlyDictionary<string, string?> H(string petof, string codigo, string? vlr, string? marca = "ALLOYS", string? dias = "5")
        => new Dictionary<string, string?> { ["PETOF"] = petof, ["CODIGO"] = codigo, ["VLR_UNI"] = vlr, ["MARCA"] = marca, ["DIAS_ENTREGA"] = dias };

    [Fact]
    public void Prellenado_toma_la_cotizacion_mas_reciente_del_mismo_codigo()
    {
        var filas = new List<Dictionary<string, string?>>
        {
            new() { ["codigo"] = "1428020", ["cnt_sol"] = "27" },
            new() { ["codigo"] = "9999999", ["cnt_sol"] = "1" },
        };
        var historico = new[]
        {
            H("1501890000", "1428020", "150000", "VIEJA"),
            H("1501898504", "1428020", "185000", "ALLOYS", "8"),
            H("1501897000", "1428020", "170000"),
        };

        var n = ScrapeCrearActividad.ApplyPrefill(filas, historico, Pre, "1501899112");

        Assert.Equal(1, n);
        Assert.Equal("185000", filas[0]["vlr_uni"]);
        Assert.Equal("ALLOYS", filas[0]["marca"]);
        Assert.Equal("8", filas[0]["dias_entrega"]);
        Assert.False(filas[1].ContainsKey("vlr_uni"));
    }

    [Fact]
    public void Prellenado_no_se_sugiere_a_si_mismo_ni_usa_filas_sin_precio()
    {
        var filas = new List<Dictionary<string, string?>> { new() { ["codigo"] = "1428020" } };
        var historico = new[]
        {
            H("1501899112", "1428020", "999999"),   // el mismo pedido que se esta creando
            H("1501899500", "1428020", null),        // mas reciente pero no se cotizo
            H("1501898000", "1428020", "185000"),
        };

        ScrapeCrearActividad.ApplyPrefill(filas, historico, Pre, "1501899112");

        Assert.Equal("185000", filas[0]["vlr_uni"]);
    }

    [Fact]
    public void Prellenado_no_pisa_lo_que_ya_trae_la_fila()
    {
        var filas = new List<Dictionary<string, string?>> { new() { ["codigo"] = "1428020", ["marca"] = "LINCOLN" } };
        ScrapeCrearActividad.ApplyPrefill(filas, new[] { H("1501898000", "1428020", "185000", "ALLOYS") }, Pre, "X");
        Assert.Equal("LINCOLN", filas[0]["marca"]);
        Assert.Equal("185000", filas[0]["vlr_uni"]);
    }

    [Fact]
    public void Vencimiento_es_fin_del_dia_en_la_zona_del_tenant()
    {
        var bogota = Ecorex.Application.Scheduling.ScheduledJobRecurrence.ResolveTimeZone("America/Bogota");
        var d = ScrapeCrearActividad.EndOfDay("17/11/2026", bogota);
        Assert.NotNull(d);
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 23, 59, 0, TimeSpan.FromHours(-5)), d);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 4, 59, 0, TimeSpan.Zero), d!.Value.ToUniversalTime());
        Assert.Null(ScrapeCrearActividad.EndOfDay("", bogota));
        Assert.Null(ScrapeCrearActividad.EndOfDay("Seleccione...", bogota));
    }

    [Fact]
    public void Overrides_del_boton_llevan_la_tarea_y_el_formulario_con_grillas_anidadas()
    {
        var data = new Dictionary<string, string?>
        {
            ["petof"] = "1501899112",
            ["moneda"] = "PESO COLOMBIANO",
            ["items"] = """[{"item":"10","codigo":"1428020","cnt_cot":"27","vlr_uni":"185000"}]""",
            ["notas"] = null,
            ["texto_raro"] = "[no es json",
        };

        var o = EjecutarDronVerb.BuildOverrides("T00096", data);

        Assert.Equal("T00096", o[EjecutarDronVerb.VarTarea]);
        using var doc = JsonDocument.Parse(o[EjecutarDronVerb.VarForm]);
        var form = doc.RootElement;
        Assert.Equal("1501899112", form.GetProperty("petof").GetString());
        Assert.Equal(JsonValueKind.Array, form.GetProperty("items").ValueKind);
        Assert.Equal("185000", form.GetProperty("items")[0].GetProperty("vlr_uni").GetString());
        Assert.Equal("", form.GetProperty("notas").GetString());
        Assert.Equal("[no es json", form.GetProperty("texto_raro").GetString());

        // El flujo lo lee igual que cualquier variable: {{FORM.items.0.vlr_uni}}.
        var ctx = new ScrapeRunContext(o);
        Assert.Equal("185000", ScrapeCrearActividad.Resolve(ctx, "FORM.items.0.vlr_uni"));
    }

    [Fact]
    public void Resumen_del_comentario()
    {
        var ok = new EjecutarDronVerb.StepCollector();
        ok.Report(new FlowStepProgress(0, 1, "F", 0, 2, "Login", true, true, "AAA", "ok"));
        ok.Report(new FlowStepProgress(0, 1, "F", 1, 2, "Llenar", true, true, "BBB", "ok"));
        var txtOk = EjecutarDronVerb.Summarize("Responder oferta", new BrowserRunResult(true, Guid.NewGuid(), "c", false, null), ok);
        Assert.Contains("2 paso(s) ejecutados sin error", txtOk);
        Assert.Equal("BBB", ok.LastScreenshot);

        var mal = new EjecutarDronVerb.StepCollector();
        mal.Report(new FlowStepProgress(0, 1, "F", 0, 2, "Login", false, false, null, null)); // "en curso": no cuenta
        mal.Report(new FlowStepProgress(0, 1, "F", 0, 2, "Login", true, false, null, "Dominio no permitido"));
        var txtMal = EjecutarDronVerb.Summarize("Responder oferta", new BrowserRunResult(true, Guid.NewGuid(), "c", false, null), mal);
        Assert.Contains("1 de 1 paso(s) con error", txtMal);
        Assert.Contains("Dominio no permitido", txtMal);

        var off = EjecutarDronVerb.Summarize("R", new BrowserRunResult(false, null, null, true, null), new EjecutarDronVerb.StepCollector());
        Assert.Contains("no esta en linea", off);
    }
}
