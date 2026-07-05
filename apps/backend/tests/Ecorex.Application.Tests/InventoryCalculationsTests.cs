using Ecorex.Application.Inventory;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Pruebas unitarias de los calculos puros del inventario (grupo Sistema - Inventarios,
/// ADR-0027): total de stock, disponibilidad por bodega y validacion de nombre de catalogo.
/// </summary>
public class InventoryCalculationsTests
{
    private static ItemStockDto Stock(string name, int qty, Guid? id = null)
        => new(id ?? Guid.NewGuid(), name, qty);

    [Fact]
    public void TotalStock_SumsAllWarehouses()
    {
        var rows = new[] { Stock("Central", 12), Stock("Norte", 5), Stock("Sur", 0) };
        Assert.Equal(17, InventoryCalculations.TotalStock(rows));
    }

    [Fact]
    public void TotalStock_Empty_IsZero()
    {
        Assert.Equal(0, InventoryCalculations.TotalStock([]));
    }

    [Fact]
    public void AvailableAt_ReturnsOnlyWarehousesWithPositiveStock()
    {
        var rows = new[] { Stock("Central", 3), Stock("Norte", 0), Stock("Sur", 8) };
        var available = InventoryCalculations.AvailableAt(rows);
        Assert.Equal(new[] { "Central", "Sur" }, available);
    }

    [Fact]
    public void IsAvailableAt_TrueOnlyWhenStockPositiveInThatWarehouse()
    {
        var central = Guid.NewGuid();
        var norte = Guid.NewGuid();
        var rows = new[] { Stock("Central", 4, central), Stock("Norte", 0, norte) };
        Assert.True(InventoryCalculations.IsAvailableAt(rows, central));
        Assert.False(InventoryCalculations.IsAvailableAt(rows, norte));
        Assert.False(InventoryCalculations.IsAvailableAt(rows, Guid.NewGuid()));
    }

    [Theory]
    [InlineData(null, "El nombre es obligatorio.")]
    [InlineData("", "El nombre es obligatorio.")]
    [InlineData("   ", "El nombre es obligatorio.")]
    [InlineData("Acme", null)]
    public void ValidateName_ChecksRequired(string? name, string? expected)
    {
        Assert.Equal(expected, InventoryCalculations.ValidateName(name));
    }

    [Fact]
    public void ValidateName_RejectsTooLong()
    {
        var longName = new string('x', 151);
        Assert.NotNull(InventoryCalculations.ValidateName(longName));
        Assert.Null(InventoryCalculations.ValidateName(new string('x', 150)));
    }
}
