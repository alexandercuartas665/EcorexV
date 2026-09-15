using ClosedXML.Excel;
using Ecorex.Application.Tenancy;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Extraccion de un adjunto de hoja de calculo a TEXTO tabular (Parte B, ADR-0104): Gemini no acepta xlsx
/// nativo, asi que un Excel entrante se convierte a texto y se inyecta al turno del cliente. Se valida que
/// el texto lleve encabezados y valores de celda, y que IsSpreadsheet reconozca por mime/nombre.
/// </summary>
public class SpreadsheetTextTests
{
    private static string XlsxBase64()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Productos");
        ws.Cell(1, 1).Value = "Codigo";
        ws.Cell(1, 2).Value = "Descripcion";
        ws.Cell(1, 3).Value = "Precio";
        ws.Cell(2, 1).Value = "SKU-1";
        ws.Cell(2, 2).Value = "Tornillo 1/4";
        ws.Cell(2, 3).Value = 1500;
        ws.Cell(3, 1).Value = "SKU-2";
        ws.Cell(3, 2).Value = "Tuerca 1/4";
        ws.Cell(3, 3).Value = 900;
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return Convert.ToBase64String(ms.ToArray());
    }

    [Fact]
    public void FromBase64_extrae_encabezados_y_valores()
    {
        var text = SpreadsheetText.FromBase64(XlsxBase64(), "Productos.xlsx");

        Assert.NotNull(text);
        Assert.Contains("Productos", text);       // nombre de la hoja
        Assert.Contains("Codigo", text);          // encabezado
        Assert.Contains("SKU-1", text);           // valor de celda
        Assert.Contains("Tornillo 1/4", text);
        Assert.Contains("1500", text);
        Assert.Contains(" | ", text);             // separador de columnas
    }

    [Fact]
    public void FromBase64_csv_se_devuelve_como_texto()
    {
        var csv = "a,b,c\n1,2,3";
        var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(csv));
        var text = SpreadsheetText.FromBase64(b64, "datos.csv");
        Assert.Equal(csv, text);
    }

    [Fact]
    public void FromBase64_base64_invalido_devuelve_null()
        => Assert.Null(SpreadsheetText.FromBase64("no-es-base64-!!!", "x.xlsx"));

    [Theory]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "a.xlsx", true)]
    [InlineData("application/vnd.ms-excel", "a.xls", true)]
    [InlineData("text/csv", "a.csv", true)]
    [InlineData("application/pdf", "a.pdf", false)]
    [InlineData("image/jpeg", "foto.jpg", false)]
    public void IsSpreadsheet_reconoce_por_mime_o_nombre(string mime, string name, bool expected)
        => Assert.Equal(expected, SpreadsheetText.IsSpreadsheet(mime, name));
}
