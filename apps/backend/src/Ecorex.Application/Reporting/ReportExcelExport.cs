using ClosedXML.Excel;

namespace Ecorex.Application.Reporting;

/// <summary>Una hoja del Excel de un reporte: nombre + encabezados + filas (celdas ya resueltas).</summary>
public sealed record ReportExcelSheet(string Name, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<object?>> Rows);

/// <summary>
/// Serializa los datos de un reporte (indicadores KPI + tablas + series de graficos, ya calculados por el
/// renderer respetando los filtros) a un archivo .xlsx. Una hoja "Indicadores" con los KPIs y una hoja por
/// cada tabla/serie. Sin dependencias del renderer: recibe datos planos.
/// </summary>
public static class ReportExcelExport
{
    public static byte[] Build(
        IReadOnlyList<(string Label, string Value)> kpis,
        IReadOnlyList<ReportExcelSheet> sheets)
    {
        using var wb = new XLWorkbook();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (kpis.Count > 0)
        {
            var ws = wb.Worksheets.Add(UniqueName("Indicadores", used));
            ws.Cell(1, 1).Value = "Indicador";
            ws.Cell(1, 2).Value = "Valor";
            ws.Row(1).Style.Font.Bold = true;
            var r = 2;
            foreach (var (label, value) in kpis)
            {
                ws.Cell(r, 1).Value = label ?? "";
                ws.Cell(r, 2).Value = value ?? "";
                r++;
            }
            ws.Columns().AdjustToContents();
        }

        foreach (var sheet in sheets)
        {
            var ws = wb.Worksheets.Add(UniqueName(string.IsNullOrWhiteSpace(sheet.Name) ? "Hoja" : sheet.Name, used));
            for (var c = 0; c < sheet.Headers.Count; c++)
            {
                ws.Cell(1, c + 1).Value = sheet.Headers[c] ?? "";
            }
            if (sheet.Headers.Count > 0) { ws.Row(1).Style.Font.Bold = true; }

            for (var ri = 0; ri < sheet.Rows.Count; ri++)
            {
                var row = sheet.Rows[ri];
                for (var c = 0; c < row.Count; c++)
                {
                    SetCell(ws.Cell(ri + 2, c + 1), row[c]);
                }
            }
            ws.Columns().AdjustToContents();
        }

        if (!wb.Worksheets.Any()) { wb.Worksheets.Add("Reporte"); }   // un xlsx valido necesita >= 1 hoja

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void SetCell(IXLCell cell, object? v)
    {
        switch (v)
        {
            case null: break;
            case double d: cell.Value = d; break;
            case decimal m: cell.Value = m; break;
            case int i: cell.Value = i; break;
            case long l: cell.Value = l; break;
            default: cell.Value = v.ToString(); break;
        }
    }

    /// <summary>Nombre de hoja valido para Excel: <=31 chars, sin [ ] : * ? / \ y unico en el libro.</summary>
    private static string UniqueName(string raw, HashSet<string> used)
    {
        var clean = new string((raw ?? "").Where(ch => ch is not ('[' or ']' or ':' or '*' or '?' or '/' or '\\')).ToArray()).Trim();
        if (clean.Length == 0) { clean = "Hoja"; }
        if (clean.Length > 31) { clean = clean[..31]; }

        var name = clean;
        var n = 2;
        while (!used.Add(name))
        {
            var suffix = $" ({n++})";
            name = clean.Length + suffix.Length > 31 ? clean[..(31 - suffix.Length)] + suffix : clean + suffix;
        }
        return name;
    }
}
