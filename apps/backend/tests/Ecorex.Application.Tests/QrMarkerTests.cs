using Ecorex.Application.Forms;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests del generador de codigo QR (Barcode.QrSvg) y del marcador {{qr:...}} en el merge de plantillas
/// (FormTemplateMerge.Render). El QR reemplaza al Code39 en impresoras de baja calidad: correccion H.
/// </summary>
public class QrMarkerTests
{
    private static readonly IReadOnlyDictionary<string, string?> NoFmt =
        new Dictionary<string, string?>();

    [Fact]
    public void QrSvg_Vacio_DevuelveCadenaVacia()
    {
        Assert.Equal(string.Empty, Barcode.QrSvg(null));
        Assert.Equal(string.Empty, Barcode.QrSvg(""));
    }

    [Fact]
    public void QrSvg_EmiteSvgCuadradoConCrispEdges()
    {
        var svg = Barcode.QrSvg("T00041", 110);
        Assert.StartsWith("<svg", svg);
        Assert.Contains("shape-rendering=\"crispEdges\"", svg);
        Assert.Contains("width:110px;height:110px", svg);
        // viewBox cuadrado (mismo lado en ambos ejes).
        var m = System.Text.RegularExpressions.Regex.Match(svg, @"viewBox=""0 0 (\d+) (\d+)""");
        Assert.True(m.Success);
        Assert.Equal(m.Groups[1].Value, m.Groups[2].Value);
        // Tiene modulos negros (al menos un rect de datos ademas del fondo blanco).
        Assert.Contains("fill=\"#000\"", svg);
    }

    [Fact]
    public void QrSvg_QuietZone_LadoIncluyeMargen8Modulos()
    {
        // El lado del viewBox = modulos + 2*quiet(4). Para "T00041" (QR version chica), el lado debe
        // ser >= 21 (version 1) + 8 = 29. Confirma que el margen (quiet zone) esta incluido.
        var svg = Barcode.QrSvg("T00041");
        var m = System.Text.RegularExpressions.Regex.Match(svg, @"viewBox=""0 0 (\d+) ");
        var side = int.Parse(m.Groups[1].Value);
        Assert.True(side >= 29, $"lado {side} deberia incluir quiet zone de 8 modulos");
    }

    [Fact]
    public void Render_MarcadorQrTarea_ProduceSvgQr()
    {
        const string tpl = "<div>OT {{tarea}}</div><div class=\"q\">{{qr:tarea:110}}</div>";
        var html = FormTemplateMerge.Render(
            tpl, responseDataJson: "{}", fieldFormat: NoFmt, gridOptions: NoFmt, canvasOptions: NoFmt,
            empresa: "ACME", fecha: DateTimeOffset.UtcNow, numero: "T00041-1", tarea: "T00041");

        Assert.Contains("<svg", html);
        Assert.Contains("shape-rendering=\"crispEdges\"", html);
        Assert.Contains("width:110px", html);
        // El texto legible {{tarea}} sigue funcionando aparte del QR.
        Assert.Contains("OT T00041", html);
        // No quedan marcadores sin resolver.
        Assert.DoesNotContain("{{qr:", html);
    }

    [Fact]
    public void Render_MarcadorQrCampo_CodificaValorDelCampo()
    {
        const string tpl = "{{qr:campo.folio}}";
        var html = FormTemplateMerge.Render(
            tpl, responseDataJson: "{\"folio\":\"ABC-999\"}", fieldFormat: NoFmt, gridOptions: NoFmt,
            canvasOptions: NoFmt, empresa: "ACME", fecha: DateTimeOffset.UtcNow, numero: "R-1", tarea: "T1");
        Assert.Contains("<svg", html);
        Assert.DoesNotContain("{{qr:", html);
    }

    [Fact]
    public void Barcode_SigueDisponible_NoSeRemueve()
    {
        // El {{barcode:...}} (Code39) sigue existiendo en paralelo al QR.
        const string tpl = "{{barcode:tarea}}";
        var html = FormTemplateMerge.Render(
            tpl, responseDataJson: "{}", fieldFormat: NoFmt, gridOptions: NoFmt, canvasOptions: NoFmt,
            empresa: "ACME", fecha: DateTimeOffset.UtcNow, numero: "T00041-1", tarea: "T00041");
        Assert.Contains("<svg", html);
        Assert.Contains("Codigo de barras", html);
    }

    [Fact]
    public void Emit_HtmlParaEscanear_SiSePideConEnvVar()
    {
        // Verificacion visual opcional: si QR_EMIT_PATH esta seteada, escribe un HTML con el QR para
        // escanearlo con celular. No afecta la corrida normal (sin env var no hace nada).
        var path = Environment.GetEnvironmentVariable("QR_EMIT_PATH");
        if (string.IsNullOrWhiteSpace(path)) { return; }

        var tpl = "<div style=\"font:16px sans-serif;text-align:center;padding:24px\">"
                + "<div style=\"font-weight:700;margin-bottom:8px\">Orden de Trabajo {{tarea}}</div>"
                + "{{qr:tarea:180}}"
                + "<div style=\"margin-top:6px;font-family:monospace\">{{tarea}}</div></div>";
        var html = FormTemplateMerge.Render(
            tpl, responseDataJson: "{}", fieldFormat: NoFmt, gridOptions: NoFmt, canvasOptions: NoFmt,
            empresa: "ACME", fecha: DateTimeOffset.UtcNow, numero: "T00041-1", tarea: "T00041");
        File.WriteAllText(path, "<!doctype html><meta charset=\"utf-8\"><title>QR OT</title>" + html);
    }
}
