namespace Ecorex.Application.Inventory;

/// <summary>
/// Calculos puros del modulo de inventario (sin BD): totales de stock, disponibilidad y
/// validaciones de nombre de catalogo. Se prueban de forma unitaria (mismo enfoque que
/// ActivityBoardCalculations / OrgUnitTree).
/// </summary>
public static class InventoryCalculations
{
    /// <summary>Suma total de existencias a partir de las filas de stock por bodega.</summary>
    public static int TotalStock(IEnumerable<ItemStockDto> stockByWarehouse)
        => stockByWarehouse.Sum(s => s.Stock);

    /// <summary>Bodegas donde el item esta disponible (stock &gt; 0), por nombre.</summary>
    public static IReadOnlyList<string> AvailableAt(IEnumerable<ItemStockDto> stockByWarehouse)
        => stockByWarehouse.Where(s => s.Stock > 0).Select(s => s.WarehouseName).ToList();

    /// <summary>True si el item esta disponible (stock &gt; 0) en la bodega indicada.</summary>
    public static bool IsAvailableAt(IEnumerable<ItemStockDto> stockByWarehouse, Guid warehouseId)
        => stockByWarehouse.Any(s => s.WarehouseId == warehouseId && s.Stock > 0);

    /// <summary>
    /// Valida el nombre de un catalogo/bodega. Devuelve el mensaje de error o null si es valido.
    /// </summary>
    public static string? ValidateName(string? name, int maxLength = 150)
    {
        if (string.IsNullOrWhiteSpace(name)) { return "El nombre es obligatorio."; }
        if (name.Trim().Length > maxLength) { return $"El nombre no puede superar {maxLength} caracteres."; }
        return null;
    }
}
