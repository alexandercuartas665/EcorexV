using ClosedXML.Excel;

namespace Ecorex.Application.Inventory;

/// <summary>
/// Lee la plantilla de importacion de Items (hoja "Items" que genera <see cref="ItemTemplateXlsx"/>) y la
/// convierte en filas tipadas listas para dar de alta con <c>IItemService.CreateAsync</c>. Valida POR FILA
/// (no aborta todo el archivo por un error): cada fila trae su <see cref="ItemImportRow.Error"/> si no se
/// puede importar, y el resto sigue.
///
/// Multi-tenant: NO toca la BD; solo parsea. Los catalogos (Marca/Grupo/Subgrupo/Tipo/Bodega) se resuelven
/// por NOMBRE con los diccionarios que pasa la UI (catalogo VIVO del tenant), asi el parser queda puro. Las
/// columnas de stock son DINAMICAS: se leen del encabezado ("Stock: {bodega}") y se mapean por nombre.
/// </summary>
public static class ItemImportXlsx
{
    /// <summary>Referencia de subgrupo: su Id y el nombre de su grupo padre (para validar coherencia).</summary>
    public sealed record SubgroupRef(Guid Id, string? GroupName);

    /// <summary>Fila parseada. <see cref="Error"/> != null => no se importa.</summary>
    public sealed record ItemImportRow(
        int RowNumber,
        string Name,
        string? Sku,
        string? Description,
        string? Specifications,
        decimal? Price,
        Guid? BrandId,
        Guid? GroupId,
        Guid? SubgroupId,
        Guid? ItemTypeId,
        IReadOnlyDictionary<Guid, int> StockByWarehouse,
        string? Error)
    {
        public bool IsValid => Error is null;

        /// <summary>Clave de SKU normalizada (trim+minusculas) o null si la fila no trae SKU.</summary>
        public string? SkuKey => string.IsNullOrWhiteSpace(Sku) ? null : Sku.Trim().ToLowerInvariant();

        /// <summary>Arma el request de alta (solo se llama si es valida). Sin SKU => se genera consecutivo.</summary>
        public SaveItemRequest ToRequest() => new(
            Name: Name,
            Sku: Sku,
            Description: Description,
            Specifications: Specifications,
            Price: Price,
            BrandId: BrandId,
            GroupId: GroupId,
            SubgroupId: SubgroupId,
            ItemTypeId: ItemTypeId,
            StockByWarehouse: StockByWarehouse.Count > 0 ? StockByWarehouse : null,
            GenerateSku: string.IsNullOrWhiteSpace(Sku));
    }

    /// <summary>Resultado global del parseo.</summary>
    public sealed record ItemImportParse(IReadOnlyList<ItemImportRow> Rows, string? FatalError)
    {
        public int Total => Rows.Count;
        public int Valid => Rows.Count(r => r.IsValid);
        public int Invalid => Rows.Count(r => !r.IsValid);
    }

    public static ItemImportParse Parse(
        Stream xlsx,
        IReadOnlyDictionary<string, Guid> brandsByName,
        IReadOnlyDictionary<string, Guid> groupsByName,
        IReadOnlyDictionary<string, SubgroupRef> subgroupsByName,
        IReadOnlyDictionary<string, Guid> typesByName,
        IReadOnlyDictionary<string, Guid> warehousesByName)
    {
        var brands = NormIndex(brandsByName);
        var groups = NormIndex(groupsByName);
        var types = NormIndex(typesByName);
        var warehouses = NormIndex(warehousesByName);
        var subgroups = new Dictionary<string, SubgroupRef>(StringComparer.Ordinal);
        foreach (var kv in subgroupsByName)
        {
            var k = Norm(kv.Key);
            if (!string.IsNullOrEmpty(k)) { subgroups[k] = kv.Value; }
        }

        XLWorkbook wb;
        try { wb = new XLWorkbook(xlsx); }
        catch (Exception ex) { return new ItemImportParse(Array.Empty<ItemImportRow>(), "No se pudo leer el archivo Excel: " + ex.Message); }

        using (wb)
        {
            var ws = wb.Worksheets.FirstOrDefault(s => string.Equals(s.Name, "Items", StringComparison.OrdinalIgnoreCase))
                     ?? wb.Worksheets.FirstOrDefault();
            if (ws is null) { return new ItemImportParse(Array.Empty<ItemImportRow>(), "El archivo no tiene hojas."); }

            // Columnas de stock (dinamicas): desde la 10, encabezado "Stock: {bodega}"; se mapea la bodega por
            // nombre. Una columna "Stock: X" cuya bodega no exista se ignora (no rompe la importacion).
            var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 9;
            var stockCols = new List<(int Col, Guid WarehouseId)>();
            for (var c = 10; c <= lastCol; c++)
            {
                var h = ws.Cell(1, c).GetString().Trim();
                if (h.StartsWith("Stock:", StringComparison.OrdinalIgnoreCase))
                {
                    var wname = h.Substring(6).Trim();
                    if (warehouses.TryGetValue(Norm(wname), out var wid)) { stockCols.Add((c, wid)); }
                }
            }

            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            var rows = new List<ItemImportRow>();
            for (var r = 2; r <= lastRow; r++)
            {
                string Cell(int c) => ws.Cell(r, c).GetString().Trim();

                var name = Cell(1);
                var sku = Cell(2);
                var desc = Cell(3);
                var espec = Cell(4);
                var precioRaw = Cell(5);
                var marca = Cell(6);
                var grupo = Cell(7);
                var sub = Cell(8);
                var tipo = Cell(9);

                // Fila totalmente vacia: se ignora en silencio.
                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(sku) &&
                    string.IsNullOrWhiteSpace(desc) && string.IsNullOrWhiteSpace(precioRaw) &&
                    string.IsNullOrWhiteSpace(marca) && string.IsNullOrWhiteSpace(grupo) &&
                    string.IsNullOrWhiteSpace(sub) && string.IsNullOrWhiteSpace(tipo) &&
                    !stockCols.Any(sc => !string.IsNullOrWhiteSpace(Cell(sc.Col))))
                {
                    continue;
                }

                string? error = null;
                if (string.IsNullOrWhiteSpace(name)) { error = "Falta el nombre (obligatorio)."; }

                var price = ParsePrice(precioRaw, ref error);
                var brandId = Resolve(marca, brands, ref error, "Marca");
                var groupId = Resolve(grupo, groups, ref error, "Grupo");
                var typeId = Resolve(tipo, types, ref error, "Tipo");

                Guid? subId = null;
                if (!string.IsNullOrWhiteSpace(sub))
                {
                    if (subgroups.TryGetValue(Norm(sub), out var sref))
                    {
                        subId = sref.Id;
                        if (!string.IsNullOrWhiteSpace(grupo) && !string.IsNullOrWhiteSpace(sref.GroupName)
                            && !string.Equals(sref.GroupName, grupo, StringComparison.OrdinalIgnoreCase))
                        {
                            error ??= $"El subgrupo '{sub}' no pertenece al grupo '{grupo}'.";
                        }
                    }
                    else { error ??= $"Subgrupo no existe: '{sub}'."; }
                }

                var stock = new Dictionary<Guid, int>();
                foreach (var (col, wid) in stockCols)
                {
                    var sraw = Cell(col);
                    if (string.IsNullOrWhiteSpace(sraw)) { continue; }
                    if (int.TryParse(new string(sraw.Where(char.IsDigit).ToArray()), out var q) && q > 0) { stock[wid] = q; }
                    else if (!int.TryParse(sraw, out _)) { error ??= $"Stock no valido: '{sraw}'."; }
                }

                rows.Add(new ItemImportRow(
                    r, name, Nz(sku), Nz(desc), Nz(espec), price,
                    brandId, groupId, subId, typeId, stock, error));
            }

            return new ItemImportParse(rows, null);
        }
    }

    private static Guid? Resolve(string raw, IReadOnlyDictionary<string, Guid> byName, ref string? error, string field)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return null; }
        if (byName.TryGetValue(Norm(raw), out var id)) { return id; }
        error ??= $"{field} no existe: '{raw}'.";
        return null;
    }

    // Precio: los catalogos son enteros en COP. Se toman solo los digitos (asi '15.000' o '$15,000' -> 15000).
    private static decimal? ParsePrice(string raw, ref string? error)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return null; }
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) { error ??= $"Precio no valido: '{raw}'."; return null; }
        return decimal.TryParse(digits, out var d) ? d : null;
    }

    private static Dictionary<string, Guid> NormIndex(IReadOnlyDictionary<string, Guid> src)
    {
        var d = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var kv in src)
        {
            var k = Norm(kv.Key);
            if (!string.IsNullOrEmpty(k)) { d[k] = kv.Value; }
        }
        return d;
    }

    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string Norm(string s) => s.Trim().ToLowerInvariant();
}
