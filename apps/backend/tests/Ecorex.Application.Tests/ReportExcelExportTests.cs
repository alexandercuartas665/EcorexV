using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Ecorex.Application.Reporting;
using Xunit;
using C = DocumentFormat.OpenXml.Drawing.Charts;

namespace Ecorex.Application.Tests;

/// <summary>
/// Export a Excel de un reporte (galeria de reportes): UNA hoja con los KPIs y cada tabla/serie como
/// seccion, con graficos nativos de Excel. Se valida releyendo con ClosedXML y con el validador OpenXML.
/// </summary>
public class ReportExcelExportTests
{
    private static XLWorkbook Roundtrip(byte[] bytes)
    {
        var ms = new MemoryStream(bytes);
        return new XLWorkbook(ms);
    }

    private static IReadOnlyList<object?>[] Serie(params (string Cat, double Val)[] pts)
        => pts.Select(p => (IReadOnlyList<object?>)new object?[] { p.Cat, p.Val }).ToArray();

    /// <summary>Errores de esquema OpenXML (si hay, Excel ofrece "reparar" el archivo).</summary>
    private static List<string> SchemaErrors(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var doc = SpreadsheetDocument.Open(ms, false);
        return new OpenXmlValidator(FileFormatVersions.Office2016).Validate(doc)
            .Select(e => $"{e.Part?.Uri} {e.Path?.XPath}: {e.Description}").ToList();
    }

    private static List<ChartPart> Charts(byte[] bytes, out SpreadsheetDocument doc)
    {
        doc = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        return doc.WorkbookPart!.WorksheetParts.SelectMany(w => w.DrawingsPart?.ChartParts ?? Enumerable.Empty<ChartPart>()).ToList();
    }

    [Fact]
    public void Build_pone_indicadores_y_secciones_en_una_sola_hoja()
    {
        var kpis = new (string, string)[] { ("Total", "42"), ("Abiertas", "7") };
        var sheets = new[]
        {
            new ReportExcelSheet("Ventas por mes", new[] { "Mes", "Monto" }, Serie(("Enero", 1500d), ("Febrero", 2300d))),
            new ReportExcelSheet("Detalle", new[] { "Cliente", "Total" }, Serie(("ACME", 10d))),
        };

        var bytes = ReportExcelExport.Build(kpis, sheets, "Mi reporte");
        using var wb = Roundtrip(bytes);

        var ws = Assert.Single(wb.Worksheets);
        Assert.Equal(ReportExcelExport.ReportSheetName, ws.Name);
        Assert.Equal("Mi reporte", ws.Cell(1, 1).GetString());
        Assert.Equal("Indicadores", ws.Cell(3, 1).GetString());
        Assert.Equal("Indicador", ws.Cell(4, 1).GetString());
        Assert.Equal("Total", ws.Cell(5, 1).GetString());
        Assert.Equal("42", ws.Cell(5, 2).GetString());
        Assert.Equal("Abiertas", ws.Cell(6, 1).GetString());

        // Secciones apiladas debajo, cada una con su titulo y encabezados.
        Assert.Equal("Ventas por mes", ws.Cell(8, 1).GetString());
        Assert.Equal("Mes", ws.Cell(9, 1).GetString());
        Assert.Equal("Enero", ws.Cell(10, 1).GetString());
        Assert.Equal(1500d, ws.Cell(10, 2).GetDouble());
        Assert.Equal("Detalle", ws.Cell(13, 1).GetString());
        Assert.Equal("ACME", ws.Cell(15, 1).GetString());
        Assert.Empty(SchemaErrors(bytes));
    }

    [Fact]
    public void Build_dibuja_un_grafico_nativo_por_serie_ligado_a_sus_celdas()
    {
        var sheets = new[]
        {
            new ReportExcelSheet("Por mes", new[] { "Mes", "Monto" }, Serie(("Ene", 1d), ("Feb", 2d), ("Mar", 3d)), ReportExcelChartKind.Line),
            new ReportExcelSheet("Por asesor", new[] { "Asesor", "Monto" }, Serie(("Ana", 5d), ("Luis", 4d)), ReportExcelChartKind.Bar),
            new ReportExcelSheet("Por estado", new[] { "Estado", "Cantidad" }, Serie(("Abierta", 5d), ("Cerrada", 9d)), ReportExcelChartKind.Column),
            new ReportExcelSheet("Participacion", new[] { "Linea", "Monto" }, Serie(("A", 60d), ("B", 40d)), ReportExcelChartKind.Doughnut),
            new ReportExcelSheet("Pareto", new[] { "Causa", "Casos", "Acumulado %" },
                new IReadOnlyList<object?>[] { new object?[] { "X", 8d, 80d }, new object?[] { "Y", 2d, 100d } },
                ReportExcelChartKind.Pareto),
            new ReportExcelSheet("Tabla", new[] { "Cliente", "Total" }, Serie(("ACME", 10d))),   // sin grafico
            new ReportExcelSheet("Vacio", new[] { "Mes", "Monto" }, System.Array.Empty<IReadOnlyList<object?>>(), ReportExcelChartKind.Line),
        };

        var bytes = ReportExcelExport.Build(new (string, string)[] { ("Total", "1") }, sheets, "Reporte ventas");

        Assert.Empty(SchemaErrors(bytes));
        var charts = Charts(bytes, out var doc);
        using (doc)
        {
            Assert.Equal(5, charts.Count);   // la tabla y la serie vacia no llevan grafico
            var plots = charts.Select(c => c.ChartSpace!.Descendants<C.PlotArea>().Single()).ToList();
            Assert.Single(plots[0].Elements<C.LineChart>());
            Assert.Equal(C.BarDirectionValues.Bar, plots[1].Descendants<C.BarDirection>().Single().Val!.Value);
            Assert.Equal(C.BarDirectionValues.Column, plots[2].Descendants<C.BarDirection>().Single().Val!.Value);
            Assert.Single(plots[3].Elements<C.DoughnutChart>());
            Assert.Single(plots[4].Elements<C.BarChart>());
            Assert.Single(plots[4].Elements<C.LineChart>());
            Assert.Equal(2, plots[4].Elements<C.ValueAxis>().Count());

            // La serie apunta al rango de la hoja (categorias en A, valores en B).
            using var wb = Roundtrip(bytes);
            var ws = wb.Worksheet(ReportExcelExport.ReportSheetName);
            var formulas = plots[0].Descendants<C.Formula>().Select(f => f.Text).ToList();
            var row = ws.Column(1).CellsUsed().First(c => c.GetString() == "Ene").Address.RowNumber;
            Assert.Contains($"'Reporte'!$A${row}:$A${row + 2}", formulas);
            Assert.Contains($"'Reporte'!$B${row}:$B${row + 2}", formulas);
        }
    }

    [Fact]
    public void Build_una_sola_tabla_sin_indicadores_sale_como_hoja_plana()
    {
        var sheets = new[] { new ReportExcelSheet("Registros", new[] { "Mes", "Monto" }, Serie(("Enero", 1500d))) };

        var bytes = ReportExcelExport.Build(System.Array.Empty<(string, string)>(), sheets);
        using var wb = Roundtrip(bytes);

        var s = Assert.Single(wb.Worksheets);
        Assert.Equal("Registros", s.Name);
        Assert.Equal("Mes", s.Cell(1, 1).GetString());
        Assert.Equal("Enero", s.Cell(2, 1).GetString());
        Assert.Equal(1500d, s.Cell(2, 2).GetDouble());
    }

    [Fact]
    public void Build_sin_datos_devuelve_un_xlsx_con_una_hoja()
    {
        var bytes = ReportExcelExport.Build(System.Array.Empty<(string, string)>(), System.Array.Empty<ReportExcelSheet>());
        using var wb = Roundtrip(bytes);
        Assert.True(wb.Worksheets.Count >= 1);
    }

    [Fact]
    public void Build_escribe_fechas_y_numeros_como_valores_reales()
    {
        var fecha = new System.DateTime(2026, 9, 18, 19, 26, 54);
        var sheets = new[]
        {
            new ReportExcelSheet("Cotizaciones",
                new[] { "Fecha", "Total", "Cliente" },
                new IReadOnlyList<object?>[]
                {
                    new object?[] { fecha, 1_500_000m, "ACME" },
                }),
        };

        var bytes = ReportExcelExport.Build(System.Array.Empty<(string, string)>(), sheets);
        using var wb = Roundtrip(bytes);
        var s = wb.Worksheet("Cotizaciones");

        // La fecha entra como fecha REAL (Excel puede ordenar/filtrar), no como texto.
        Assert.Equal(XLDataType.DateTime, s.Cell(2, 1).DataType);
        Assert.Equal(fecha, s.Cell(2, 1).GetDateTime());
        // El monto entra como numero.
        Assert.Equal(XLDataType.Number, s.Cell(2, 2).DataType);
        Assert.Equal(1_500_000d, s.Cell(2, 2).GetDouble());
        Assert.Equal("ACME", s.Cell(2, 3).GetString());
    }
}
