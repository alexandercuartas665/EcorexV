using ClosedXML.Excel;

namespace Ecorex.Application.Forms.Calc;

/// <summary>
/// Exportacion / plantilla / importacion en Excel (.xlsx) de una tabla (GridDetail) de un
/// formulario. La logica vive aqui (en Application, donde esta ClosedXML) y el renderer solo la
/// invoca. Regla clave: las columnas CALCULADAS (Calc) no van en la plantilla ni se importan; las
/// llena el motor de calculo al recomputar la tabla.
/// </summary>
public static class FormGridXlsx
{
    private const string SpreadsheetMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>MIME de un .xlsx, para la descarga en el navegador.</summary>
    public static string Mime => SpreadsheetMime;

    /// <summary>Columnas capturables (no calculadas): las unicas que se piden en plantilla / importacion.</summary>
    private static List<FormGridColumn> Capturable(IReadOnlyList<FormGridColumn> columns)
        => columns.Where(c => c.Calc is null).ToList();

    /// <summary>
    /// Exporta los datos actuales de la tabla: TODAS las columnas (incluye las calculadas con su
    /// resultado ya computado en la celda).
    /// </summary>
    public static byte[] Export(IReadOnlyList<FormGridColumn> columns, IEnumerable<IReadOnlyDictionary<string, string?>> rows, string sheetName)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SafeSheet(sheetName));
        for (var c = 0; c < columns.Count; c++)
        {
            ws.Cell(1, c + 1).Value = columns[c].Label ?? columns[c].Id;
        }
        ws.Row(1).Style.Font.Bold = true;

        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < columns.Count; c++)
            {
                ws.Cell(r, c + 1).Value = row.TryGetValue(columns[c].Id, out var v) ? (v ?? string.Empty) : string.Empty;
            }
            r++;
        }
        ws.Columns().AdjustToContents();
        return ToBytes(wb);
    }

    /// <summary>
    /// Plantilla de importacion: SOLO columnas capturables (sin calculadas) como encabezado, sin filas.
    /// Para columnas de lista, deja las opciones validas como comentario de la celda de encabezado.
    /// </summary>
    public static byte[] Template(IReadOnlyList<FormGridColumn> columns, string sheetName)
    {
        var cap = Capturable(columns);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SafeSheet(sheetName));
        for (var c = 0; c < cap.Count; c++)
        {
            var head = ws.Cell(1, c + 1);
            head.Value = cap[c].Label ?? cap[c].Id;
            if (cap[c].IsSelect && cap[c].Options is { Count: > 0 } opts)
            {
                head.GetComment().AddText("Valores validos: " + string.Join(", ", opts.Select(o => o.Label)));
            }
        }
        ws.Row(1).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();
        return ToBytes(wb);
    }

    /// <summary>
    /// Lee un .xlsx e importa filas mapeando los encabezados a columnas capturables por etiqueta (o id),
    /// sin distinguir mayusculas. Ignora columnas calculadas o encabezados no reconocidos. Devuelve una
    /// fila por cada renglon con al menos un valor.
    /// </summary>
    public static List<Dictionary<string, string?>> Import(byte[] bytes, IReadOnlyList<FormGridColumn> columns)
    {
        var result = new List<Dictionary<string, string?>>();
        var cap = Capturable(columns);
        if (cap.Count == 0) { return result; }

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheets.FirstOrDefault();
        var used = ws?.RangeUsed();
        if (ws is null || used is null) { return result; }

        // Coordenadas ABSOLUTAS de la hoja (no del rango): el rango usado puede no empezar en A1.
        var firstRow = used.RangeAddress.FirstAddress.RowNumber;
        var lastRow = used.RangeAddress.LastAddress.RowNumber;
        var firstCol = used.RangeAddress.FirstAddress.ColumnNumber;
        var lastCol = used.RangeAddress.LastAddress.ColumnNumber;
        if (lastRow <= firstRow) { return result; }  // solo encabezado o vacio

        // Mapa columna-de-hoja -> columna del formulario, segun el encabezado (primera fila usada).
        var byExcelCol = new Dictionary<int, FormGridColumn>();
        for (var c = firstCol; c <= lastCol; c++)
        {
            var name = ws.Cell(firstRow, c).GetString().Trim();
            if (string.IsNullOrEmpty(name)) { continue; }
            var col = cap.FirstOrDefault(x => string.Equals((x.Label ?? x.Id).Trim(), name, StringComparison.OrdinalIgnoreCase))
                      ?? cap.FirstOrDefault(x => string.Equals(x.Id, name, StringComparison.OrdinalIgnoreCase));
            if (col is not null) { byExcelCol[c] = col; }
        }
        if (byExcelCol.Count == 0) { return result; }

        for (var r = firstRow + 1; r <= lastRow; r++)
        {
            var dict = new Dictionary<string, string?>(StringComparer.Ordinal);
            var any = false;
            foreach (var (excelCol, col) in byExcelCol)
            {
                var v = ws.Cell(r, excelCol).GetString().Trim();
                dict[col.Id] = v;
                if (!string.IsNullOrWhiteSpace(v)) { any = true; }
            }
            if (any) { result.Add(dict); }
        }
        return result;
    }

    private static byte[] ToBytes(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>Nombre de hoja valido: sin caracteres prohibidos por Excel y como maximo 31 chars.</summary>
    private static string SafeSheet(string? name)
    {
        var clean = new string((name ?? "Datos").Select(ch => "[]*?/\\:".Contains(ch) ? ' ' : ch).ToArray()).Trim();
        if (string.IsNullOrEmpty(clean)) { clean = "Datos"; }
        return clean.Length > 31 ? clean[..31] : clean;
    }
}
