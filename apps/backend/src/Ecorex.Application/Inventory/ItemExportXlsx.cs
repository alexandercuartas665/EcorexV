using ClosedXML.Excel;

namespace Ecorex.Application.Inventory;

/// <summary>
/// Exporta items a un .xlsx con la MISMA estructura (hoja "Items" + columnas fijas + una columna de stock
/// por bodega) que la plantilla de importacion (<see cref="ItemTemplateXlsx"/>), de modo que el archivo
/// exportado se puede volver a importar sin cambios (round-trip). Solo arma el libro; no toca la BD.
/// </summary>
public static class ItemExportXlsx
{
    public const string Mime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Fila lista para exportar (catalogos ya resueltos a nombre, stock por bodega).</summary>
    public sealed record Row(
        string Name,
        string? Sku,
        string? Description,
        string? Specifications,
        decimal? Price,
        string? Brand,
        string? Group,
        string? Subgroup,
        string? Type,
        IReadOnlyDictionary<Guid, int> Stock);

    public static byte[] Build(IEnumerable<Row> rows, IReadOnlyList<WarehouseDto> warehouses)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Items");

        var headers = new List<string> { "Nombre*", "SKU", "Descripcion", "Especificaciones", "Precio", "Marca", "Grupo", "Subgrupo", "Tipo" };
        foreach (var w in warehouses) { headers.Add("Stock: " + w.Name); }
        for (var c = 0; c < headers.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
        }

        var r = 2;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = row.Name;
            ws.Cell(r, 2).Value = row.Sku ?? string.Empty;
            ws.Cell(r, 3).Value = row.Description ?? string.Empty;
            ws.Cell(r, 4).Value = row.Specifications ?? string.Empty;
            if (row.Price is decimal p) { ws.Cell(r, 5).Value = p; } else { ws.Cell(r, 5).Value = string.Empty; }
            ws.Cell(r, 6).Value = row.Brand ?? string.Empty;
            ws.Cell(r, 7).Value = row.Group ?? string.Empty;
            ws.Cell(r, 8).Value = row.Subgroup ?? string.Empty;
            ws.Cell(r, 9).Value = row.Type ?? string.Empty;
            var col = 10;
            foreach (var w in warehouses) { ws.Cell(r, col++).Value = row.Stock.TryGetValue(w.Id, out var q) ? q : 0; }
            r++;
        }

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
