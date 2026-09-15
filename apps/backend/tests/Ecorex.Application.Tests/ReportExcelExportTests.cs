using ClosedXML.Excel;
using Ecorex.Application.Reporting;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Export a Excel de un reporte (galeria de reportes): una hoja "Indicadores" con los KPIs y una hoja por
/// cada tabla/serie. Se valida armando el xlsx y volviendolo a leer con ClosedXML.
/// </summary>
public class ReportExcelExportTests
{
    private static XLWorkbook Roundtrip(byte[] bytes)
    {
        var ms = new MemoryStream(bytes);
        return new XLWorkbook(ms);
    }

    [Fact]
    public void Build_arma_hoja_de_indicadores_y_una_por_serie()
    {
        var kpis = new (string, string)[] { ("Total", "42"), ("Abiertas", "7") };
        var sheets = new[]
        {
            new ReportExcelSheet("Ventas por mes",
                new[] { "Mes", "Monto" },
                new IReadOnlyList<object?>[]
                {
                    new object?[] { "Enero", 1500d },
                    new object?[] { "Febrero", 2300d },
                }),
        };

        var bytes = ReportExcelExport.Build(kpis, sheets);
        using var wb = Roundtrip(bytes);

        var ind = wb.Worksheet("Indicadores");
        Assert.Equal("Indicador", ind.Cell(1, 1).GetString());
        Assert.Equal("Total", ind.Cell(2, 1).GetString());
        Assert.Equal("42", ind.Cell(2, 2).GetString());
        Assert.Equal("Abiertas", ind.Cell(3, 1).GetString());

        var s = wb.Worksheet("Ventas por mes");
        Assert.Equal("Mes", s.Cell(1, 1).GetString());
        Assert.Equal("Monto", s.Cell(1, 2).GetString());
        Assert.Equal("Enero", s.Cell(2, 1).GetString());
        Assert.Equal(1500d, s.Cell(2, 2).GetDouble());
        Assert.Equal("Febrero", s.Cell(3, 1).GetString());
    }

    [Fact]
    public void Build_nombres_de_hoja_duplicados_o_largos_se_hacen_unicos_y_validos()
    {
        var largo = new string('X', 50);   // > 31 chars
        var sheets = new[]
        {
            new ReportExcelSheet(largo, new[] { "A" }, System.Array.Empty<IReadOnlyList<object?>>()),
            new ReportExcelSheet(largo, new[] { "A" }, System.Array.Empty<IReadOnlyList<object?>>()),
        };

        var bytes = ReportExcelExport.Build(System.Array.Empty<(string, string)>(), sheets);
        using var wb = Roundtrip(bytes);

        Assert.Equal(2, wb.Worksheets.Count);
        Assert.All(wb.Worksheets, ws => Assert.True(ws.Name.Length <= 31));
        Assert.Equal(wb.Worksheets.Count, wb.Worksheets.Select(w => w.Name).Distinct().Count());
    }

    [Fact]
    public void Build_sin_datos_devuelve_un_xlsx_con_una_hoja()
    {
        var bytes = ReportExcelExport.Build(System.Array.Empty<(string, string)>(), System.Array.Empty<ReportExcelSheet>());
        using var wb = Roundtrip(bytes);
        Assert.True(wb.Worksheets.Count >= 1);
    }
}
