using ClosedXML.Excel;

namespace Ecorex.Application.Inventory;

/// <summary>
/// Genera la PLANTILLA de importacion de Items en Excel (.xlsx): una hoja principal "Items" con
/// encabezados y filas de EJEMPLO, y HOJAS ALTERNAS de referencia que listan los valores validos de
/// cada campo relacional (Marcas, Grupos, Subgrupos, Tipos, Bodegas). Los catalogos de inventario se
/// identifican por su NOMBRE (no tienen codigo), asi que la plantilla usa el nombre como clave.
/// Vive en Application (donde esta ClosedXML), igual que <see cref="Forms.Calc.FormGridXlsx"/>.
/// </summary>
public static class ItemTemplateXlsx
{
    /// <summary>MIME de un .xlsx, para la descarga en el navegador.</summary>
    public static string Mime => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static byte[] Build(
        IReadOnlyList<CatalogEntryDto> brands,
        IReadOnlyList<CatalogEntryDto> groups,
        IReadOnlyList<CatalogEntryDto> subgroups,
        IReadOnlyList<CatalogEntryDto> types,
        IReadOnlyList<WarehouseDto> warehouses)
    {
        using var wb = new XLWorkbook();

        // ---- Hoja principal: Items ----
        var ws = wb.Worksheets.Add("Items");
        var headers = new List<string> { "Nombre*", "SKU", "Descripcion", "Especificaciones", "Precio", "Marca", "Grupo", "Subgrupo", "Tipo" };
        foreach (var w in warehouses) { headers.Add("Stock: " + w.Name); }
        for (var c = 0; c < headers.Count; c++) { ws.Cell(1, c + 1).Value = headers[c]; }
        ws.Row(1).Style.Font.Bold = true;

        // Ayuda en los encabezados relacionales: el valor debe existir en su hoja de referencia.
        ws.Cell(1, 6).GetComment().AddText("Nombre EXACTO de una marca (ver hoja 'Marcas').");
        ws.Cell(1, 7).GetComment().AddText("Nombre EXACTO de un grupo (ver hoja 'Grupos').");
        ws.Cell(1, 8).GetComment().AddText("Nombre EXACTO de un subgrupo (ver hoja 'Subgrupos'; debe pertenecer al Grupo indicado).");
        ws.Cell(1, 9).GetComment().AddText("Nombre EXACTO de un tipo (ver hoja 'Tipos').");

        // Valores de ejemplo tomados de los primeros catalogos existentes (si no hay, texto guia).
        var ejMarca = brands.Count > 0 ? brands[0].Name : "Marca ejemplo";
        var ejGrupo = groups.Count > 0 ? groups[0].Name : "Grupo ejemplo";
        var ejSub = subgroups.FirstOrDefault(s => s.GroupName == ejGrupo)?.Name
                    ?? (subgroups.Count > 0 ? subgroups[0].Name : "Subgrupo ejemplo");
        var ejTipo = types.Count > 0 ? types[0].Name : "Tipo ejemplo";

        void ExampleRow(int r, string nombre, string sku, string desc, int precio, int stock)
        {
            ws.Cell(r, 1).Value = nombre;
            ws.Cell(r, 2).Value = sku;
            ws.Cell(r, 3).Value = desc;
            ws.Cell(r, 4).Value = "";
            ws.Cell(r, 5).Value = precio;
            ws.Cell(r, 6).Value = ejMarca;
            ws.Cell(r, 7).Value = ejGrupo;
            ws.Cell(r, 8).Value = ejSub;
            ws.Cell(r, 9).Value = ejTipo;
            var col = 10;
            foreach (var _ in warehouses) { ws.Cell(r, col++).Value = stock; }
        }
        ExampleRow(2, "Producto de ejemplo 1", "SKU-0001", "Descripcion de ejemplo", 15000, 10);
        ExampleRow(3, "Producto de ejemplo 2", "SKU-0002", "Otro producto de ejemplo", 32000, 0);

        ws.Columns().AdjustToContents();

        // ---- Hojas de referencia (los "codigos equivalentes" son los NOMBRES) ----
        AddRefSheet(wb, "Marcas", new[] { "Marca" }, brands.Select(b => new[] { b.Name }));
        AddRefSheet(wb, "Grupos", new[] { "Grupo" }, groups.Select(g => new[] { g.Name }));
        AddRefSheet(wb, "Subgrupos", new[] { "Subgrupo", "Grupo padre" }, subgroups.Select(s => new[] { s.Name, s.GroupName ?? "" }));
        AddRefSheet(wb, "Tipos", new[] { "Tipo" }, types.Select(t => new[] { t.Name }));
        AddRefSheet(wb, "Bodegas", new[] { "Bodega", "Ciudad" }, warehouses.Select(w => new[] { w.Name, w.City }));

        return ToBytes(wb);
    }

    private static void AddRefSheet(XLWorkbook wb, string name, string[] headers, IEnumerable<string[]> rows)
    {
        var ws = wb.Worksheets.Add(name);
        for (var c = 0; c < headers.Length; c++) { ws.Cell(1, c + 1).Value = headers[c]; }
        ws.Row(1).Style.Font.Bold = true;
        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++) { ws.Cell(r, c + 1).Value = row[c]; }
            r++;
        }
        if (r == 2) { ws.Cell(2, 1).Value = "(aun no hay registros: crealos en Configurar campos)"; }
        ws.Columns().AdjustToContents();
    }

    private static byte[] ToBytes(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
