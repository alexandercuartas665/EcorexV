using ClosedXML.Excel;

namespace Ecorex.Application.Reporting;

/// <summary>Tipo de grafico nativo de Excel que acompana una seccion del reporte.</summary>
public enum ReportExcelChartKind
{
    /// <summary>Barras verticales (columnas).</summary>
    Column,
    /// <summary>Barras horizontales.</summary>
    Bar,
    /// <summary>Linea (serie temporal).</summary>
    Line,
    /// <summary>Dona (participacion).</summary>
    Doughnut,
    /// <summary>Pareto: barras (columna 2) + acumulado % en linea sobre eje secundario (columna 3).</summary>
    Pareto,
}

/// <summary>
/// Una seccion del Excel de un reporte: titulo + encabezados + filas (celdas ya resueltas). Si trae
/// <paramref name="Chart"/>, la primera columna son las categorias y las siguientes las series, y el
/// export dibuja un grafico NATIVO de Excel ligado a esas celdas.
/// </summary>
public sealed record ReportExcelSheet(
    string Name,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    ReportExcelChartKind? Chart = null);

/// <summary>
/// Serializa los datos de un reporte (indicadores KPI + tablas + series de graficos, ya calculados por el
/// renderer respetando los filtros) a un archivo .xlsx. Todo va en UNA hoja "Reporte": arriba los KPIs y
/// debajo cada widget como una seccion (titulo + tabla), con su grafico nativo de Excel a la derecha.
/// Caso especial: una sola tabla sin KPIs, sin titulo y sin grafico sale como hoja plana (encabezado en la
/// fila 1) para que se pueda filtrar directo (descarga de registros del analisis).
/// </summary>
public static class ReportExcelExport
{
    /// <summary>Nombre de la hoja unica del reporte.</summary>
    public const string ReportSheetName = "Reporte";

    private const int ChartWidthPx = 620;
    private const int ChartHeightPx = 300;
    private const int RowHeightPx = 20;      // alto por defecto de fila en Excel (15 pt)
    private const int MaxColWidth = 45;
    private const int WideChartPoints = 20;   // series largas (ej. por dia): grafico mas ancho
    private const int WideChartWidthPx = 900;
    private const int MinColWidth = 10;

    private static readonly XLColor TitleColor = XLColor.FromHtml("#1F1F2E");
    private static readonly XLColor SectionColor = XLColor.FromHtml("#4F46E5");
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#EEF0F7");
    private static readonly XLColor HeaderBorder = XLColor.FromHtml("#C9CCDA");

    public static byte[] Build(
        IReadOnlyList<(string Label, string Value)> kpis,
        IReadOnlyList<ReportExcelSheet> sheets,
        string? title = null)
    {
        using var wb = new XLWorkbook();

        var plain = kpis.Count == 0 && sheets.Count == 1 && string.IsNullOrWhiteSpace(title) && sheets[0].Chart is null;
        var charts = new List<ReportExcelChartPlacement>();

        if (plain)
        {
            WritePlain(wb, sheets[0]);
        }
        else if (kpis.Count > 0 || sheets.Count > 0 || !string.IsNullOrWhiteSpace(title))
        {
            WriteCombined(wb, kpis, sheets, title, charts);
        }

        if (!wb.Worksheets.Any()) { wb.Worksheets.Add(ReportSheetName); }   // un xlsx valido necesita >= 1 hoja

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        if (charts.Count == 0)
        {
            return ms.ToArray();
        }

        // ClosedXML no dibuja graficos: se agregan como partes OpenXML sobre el libro ya guardado.
        return ReportExcelCharts.AddCharts(ms.ToArray(), ReportSheetName, charts);
    }

    private static void WritePlain(XLWorkbook wb, ReportExcelSheet sheet)
    {
        var ws = wb.Worksheets.Add(SheetName(string.IsNullOrWhiteSpace(sheet.Name) ? "Hoja" : sheet.Name));
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

    private static void WriteCombined(
        XLWorkbook wb,
        IReadOnlyList<(string Label, string Value)> kpis,
        IReadOnlyList<ReportExcelSheet> sheets,
        string? title,
        List<ReportExcelChartPlacement> charts)
    {
        var ws = wb.Worksheets.Add(ReportSheetName);
        var widths = new Dictionary<int, double>();
        var row = 1;

        if (!string.IsNullOrWhiteSpace(title))
        {
            var t = ws.Cell(row, 1);
            t.Value = title.Trim();
            t.Style.Font.Bold = true;
            t.Style.Font.FontSize = 16;
            t.Style.Font.FontColor = TitleColor;
            row += 2;
        }

        if (kpis.Count > 0)
        {
            SectionTitle(ws, row++, "Indicadores");
            HeaderRow(ws, row++, new[] { "Indicador", "Valor" }, widths);
            foreach (var (label, value) in kpis)
            {
                ws.Cell(row, 1).Value = label ?? "";
                ws.Cell(row, 2).Value = value ?? "";
                Track(widths, 1, label);
                Track(widths, 2, value);
                row++;
            }
            row++;
        }

        var n = 1;
        foreach (var sheet in sheets)
        {
            var sectionStart = row;
            SectionTitle(ws, row++, string.IsNullOrWhiteSpace(sheet.Name) ? $"Datos {n}" : sheet.Name.Trim());
            n++;

            var headerRow = row;
            HeaderRow(ws, row++, sheet.Headers, widths);
            var firstData = row;
            foreach (var r in sheet.Rows)
            {
                for (var c = 0; c < r.Count; c++)
                {
                    var cell = ws.Cell(row, c + 1);
                    SetCell(cell, r[c]);
                    if (r[c] is string text && text.Length > MaxColWidth) { cell.Style.Alignment.WrapText = true; }
                    Track(widths, c + 1, r[c]);
                }
                row++;
            }

            if (sheet.Rows.Count == 0)
            {
                var empty = ws.Cell(row++, 1);
                empty.Value = "Sin datos para el filtro.";
                empty.Style.Font.Italic = true;
                empty.Style.Font.FontColor = XLColor.Gray;
            }

            var lastData = firstData + sheet.Rows.Count - 1;
            var endRow = row;
            var seriesCols = ChartSeriesColumns(sheet);
            if (sheet.Chart is { } kind && sheet.Rows.Count > 0 && seriesCols > 0)
            {
                var dataCols = Math.Max(sheet.Headers.Count, sheet.Rows.Max(x => x.Count));
                charts.Add(new ReportExcelChartPlacement(
                    kind,
                    sheet.Name ?? "",
                    AnchorColumn: dataCols + 1,               // 0-based: deja una columna libre a la derecha
                    AnchorRow: sectionStart - 1,              // 0-based: alineado con el titulo de la seccion
                    WidthPx: sheet.Rows.Count > WideChartPoints ? WideChartWidthPx : ChartWidthPx,
                    HeightPx: ChartHeightPx,
                    HeaderRow: headerRow,
                    FirstRow: firstData,
                    LastRow: lastData,
                    SeriesColumns: seriesCols,
                    Categories: sheet.Rows.Select(x => x.Count > 0 ? x[0]?.ToString() ?? "" : "").ToList(),
                    SeriesNames: Enumerable.Range(1, seriesCols).Select(c => c < sheet.Headers.Count ? sheet.Headers[c] ?? "" : "").ToList(),
                    Values: Enumerable.Range(1, seriesCols)
                        .Select(c => (IReadOnlyList<double?>)sheet.Rows.Select(x => c < x.Count ? AsDouble(x[c]) : null).ToList())
                        .ToList()));

                // El grafico ocupa ~ChartHeightPx: la siguiente seccion arranca debajo de el.
                var chartRows = (int)Math.Ceiling(ChartHeightPx / (double)RowHeightPx) + 1;
                endRow = Math.Max(endRow, sectionStart + chartRows);
            }

            row = endRow + 1;
        }

        foreach (var (col, width) in widths)
        {
            ws.Column(col).Width = Math.Clamp(width + 2, MinColWidth, MaxColWidth);
        }
    }

    /// <summary>Cuantas columnas de series grafica la seccion (pareto = barras + acumulado).</summary>
    private static int ChartSeriesColumns(ReportExcelSheet sheet)
    {
        if (sheet.Chart is null) { return 0; }
        var cols = Math.Max(sheet.Headers.Count, sheet.Rows.Count == 0 ? 0 : sheet.Rows.Max(x => x.Count)) - 1;
        return sheet.Chart switch
        {
            ReportExcelChartKind.Pareto => cols >= 2 ? 2 : 0,
            ReportExcelChartKind.Doughnut => cols >= 1 ? 1 : 0,
            _ => Math.Max(0, cols),
        };
    }

    private static void SectionTitle(IXLWorksheet ws, int row, string text)
    {
        var c = ws.Cell(row, 1);
        c.Value = text;
        c.Style.Font.Bold = true;
        c.Style.Font.FontSize = 12;
        c.Style.Font.FontColor = SectionColor;
    }

    private static void HeaderRow(IXLWorksheet ws, int row, IReadOnlyList<string> headers, Dictionary<int, double> widths)
    {
        for (var c = 0; c < headers.Count; c++)
        {
            var cell = ws.Cell(row, c + 1);
            cell.Value = headers[c] ?? "";
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = HeaderFill;
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.BottomBorderColor = HeaderBorder;
            Track(widths, c + 1, headers[c]);
        }
    }

    /// <summary>Ancho de columna segun el contenido de encabezados y datos (no los titulos de seccion).</summary>
    private static void Track(Dictionary<int, double> widths, int col, object? v)
    {
        var len = v switch
        {
            null => 0,
            DateTime or DateTimeOffset => 18,
            double d => d.ToString("N2").Length,
            decimal m => m.ToString("N2").Length,
            _ => v.ToString()?.Length ?? 0,
        };
        widths[col] = Math.Max(widths.GetValueOrDefault(col), len);
    }

    private static double? AsDouble(object? v) => v switch
    {
        double d => d,
        decimal m => (double)m,
        float f => f,
        int i => i,
        long l => l,
        short s => s,
        _ => null,
    };

    private static void SetCell(IXLCell cell, object? v)
    {
        switch (v)
        {
            case null: break;
            case double d: cell.Value = d; NumberFormat(cell, d); break;
            case decimal m: cell.Value = m; NumberFormat(cell, (double)m); break;
            case float f: cell.Value = f; NumberFormat(cell, f); break;
            case int i: cell.Value = i; NumberFormat(cell, i); break;
            case long l: cell.Value = l; NumberFormat(cell, l); break;
            case short s: cell.Value = s; break;
            case DateTime dt: cell.Value = dt; break;                       // fecha REAL: Excel la ordena y filtra
            case DateTimeOffset dto: cell.Value = dto.DateTime; break;
            default: cell.Value = v.ToString(); break;
        }
    }

    /// <summary>Separador de miles (1.500.000) y 2 decimales solo si el valor los tiene; sigue siendo numero.</summary>
    private static void NumberFormat(IXLCell cell, double v)
    {
        if (Math.Abs(v) < 1000 && v == Math.Floor(v)) { return; }   // enteros chicos: tal cual
        cell.Style.NumberFormat.Format = v == Math.Floor(v) ? "#,##0" : "#,##0.00";
    }

    /// <summary>Nombre de hoja valido para Excel: <=31 chars, sin [ ] : * ? / \.</summary>
    private static string SheetName(string raw)
    {
        var clean = new string((raw ?? "").Where(ch => ch is not ('[' or ']' or ':' or '*' or '?' or '/' or '\\')).ToArray()).Trim();
        if (clean.Length == 0) { clean = "Hoja"; }
        return clean.Length > 31 ? clean[..31] : clean;
    }
}
