using ClosedXML.Excel;

namespace Ecorex.Application.Directorio;

/// <summary>
/// Exporta terceros a un .xlsx con la MISMA estructura (hoja "Terceros" + columnas) que la plantilla de
/// importacion (<see cref="TerceroTemplateXlsx"/>), de modo que el archivo exportado se puede volver a
/// importar sin cambios (round-trip). Solo arma el libro; no toca la BD.
/// </summary>
public static class TerceroExportXlsx
{
    public const string Mime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Fila lista para exportar (valores ya en texto, como los espera el parser de importacion).</summary>
    public sealed record Row(
        string Nombre,
        string Tipo,
        string Perfiles,
        string Estado,
        string TipoId,
        string? NumeroId,
        string? Ciudad,
        string? Sector,
        string? Cargo,
        string? Email,
        string? Telefono,
        string? Vendedor);

    private static readonly string[] Headers =
    {
        "Nombre", "Tipo", "Perfiles", "Estado", "TipoId", "NumeroId",
        "Ciudad", "Sector", "Cargo", "Email", "Telefono", "Vendedor"
    };

    public static byte[] Build(IEnumerable<Row> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Terceros");

        for (var c = 0; c < Headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = Headers[c];
            cell.Style.Font.Bold = true;
        }

        var r = 2;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = row.Nombre;
            ws.Cell(r, 2).Value = row.Tipo;
            ws.Cell(r, 3).Value = row.Perfiles;
            ws.Cell(r, 4).Value = row.Estado;
            ws.Cell(r, 5).Value = row.TipoId;
            ws.Cell(r, 6).Value = row.NumeroId ?? string.Empty;
            ws.Cell(r, 7).Value = row.Ciudad ?? string.Empty;
            ws.Cell(r, 8).Value = row.Sector ?? string.Empty;
            ws.Cell(r, 9).Value = row.Cargo ?? string.Empty;
            ws.Cell(r, 10).Value = row.Email ?? string.Empty;
            ws.Cell(r, 11).Value = row.Telefono ?? string.Empty;
            ws.Cell(r, 12).Value = row.Vendedor ?? string.Empty;
            r++;
        }

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
