using System.Text;
using ClosedXML.Excel;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Convierte un adjunto de hoja de calculo (xlsx/xls) o CSV a TEXTO tabular, para INYECTARLO como texto del
/// turno del cliente cuando el modelo no acepta el binario nativo (Gemini no procesa xlsx). Acota hojas,
/// filas, columnas y tamano total para no inflar el prompt ni gastar tokens de mas.
/// </summary>
public static class SpreadsheetText
{
    private const int MaxSheets = 10;
    private const int MaxRows = 500;
    private const int MaxCols = 50;
    private const int MaxChars = 20000;

    /// <summary>true si el mime/nombre corresponde a una hoja de calculo (xlsx/xls) o CSV.</summary>
    public static bool IsSpreadsheet(string? mime, string? fileName)
    {
        var m = (mime ?? "").ToLowerInvariant();
        var f = (fileName ?? "").ToLowerInvariant();
        return m.Contains("spreadsheet") || m.Contains("excel") || m.Contains("ms-excel") || m.Contains("csv")
            || f.EndsWith(".xlsx") || f.EndsWith(".xls") || f.EndsWith(".csv");
    }

    /// <summary>Extrae el contenido a texto tabular. Null si el base64 es invalido o no se pudo leer.</summary>
    public static string? FromBase64(string? base64, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(base64)) { return null; }
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64); }
        catch { return null; }

        var f = (fileName ?? "").ToLowerInvariant();
        if (f.EndsWith(".csv"))
        {
            try { return Cap(Encoding.UTF8.GetString(bytes)); }
            catch { return null; }
        }

        try
        {
            using var ms = new MemoryStream(bytes);
            using var wb = new XLWorkbook(ms);
            var sb = new StringBuilder();
            var sheetCount = 0;
            foreach (var ws in wb.Worksheets)
            {
                if (++sheetCount > MaxSheets) { sb.AppendLine("[... hojas adicionales omitidas ...]"); break; }
                sb.Append("## Hoja: ").AppendLine(ws.Name);
                var used = ws.RangeUsed();
                if (used is null) { sb.AppendLine("(vacia)").AppendLine(); continue; }

                var firstRow = used.FirstRow().RowNumber();
                var lastRow = used.LastRow().RowNumber();
                var firstCol = used.FirstColumn().ColumnNumber();
                var lastCol = Math.Min(used.LastColumn().ColumnNumber(), firstCol + MaxCols - 1);

                var rowN = 0;
                for (var r = firstRow; r <= lastRow; r++)
                {
                    if (++rowN > MaxRows) { sb.AppendLine("[... filas adicionales omitidas ...]"); break; }
                    var cells = new List<string>();
                    for (var c = firstCol; c <= lastCol; c++)
                    {
                        var v = ws.Cell(r, c).GetFormattedString();
                        cells.Add((v ?? "").Replace("\r", " ").Replace("\n", " ").Trim());
                    }
                    if (cells.All(string.IsNullOrEmpty)) { continue; }   // omite filas totalmente vacias
                    sb.AppendLine(string.Join(" | ", cells));
                }
                sb.AppendLine();
            }
            return Cap(sb.ToString());
        }
        catch { return null; }
    }

    private static string? Cap(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) { return null; }
        var t = s.Trim();
        return t.Length > MaxChars ? t[..MaxChars] + "\n[... contenido truncado ...]" : t;
    }
}
