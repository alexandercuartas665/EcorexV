using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// E2E del modulo de inventarios (grupo Sistema - Inventarios, ADR-0027): crea una bodega
/// (000556), una marca (000502) y un item (000066) con stock en esa bodega, y verifica que el
/// item aparece en el grid al filtrar por la bodega. Selectores por las clases estables de las
/// paginas (.module-head, .field/label.form-label, .table, .inv-filters). Cada dato lleva el
/// sufijo unico del test (Sfx) para idempotencia entre corridas.
/// </summary>
public sealed class InventoryTests : E2eTestBase
{
    public InventoryTests(E2eAppFixture fx) : base(fx)
    {
    }

    [SkippableFact]
    public async Task CrearBodega_Marca_Item_ConStock_ApareceEnGridFiltradoPorBodega()
    {
        RequireApp();
        var page = await LoginAsync();

        var warehouse = $"Bodega E2E {Sfx}";
        var brand = $"Marca E2E {Sfx}";
        var item = $"Item E2E {Sfx}";

        // ---- 1. Crear bodega (000556) ----
        await page.GotoAsync("inventario-bodegas");
        await page.Locator(".module-head").GetByRole(AriaRole.Button,
            new LocatorGetByRoleOptions { Name = "Nueva bodega" }).ClickAsync();
        var whModal = page.Locator(".modal-dialog");
        await whModal.WaitForAsync();
        await FieldIn(whModal, "Nombre").Locator("input").FillAsync(warehouse);
        await FieldIn(whModal, "Ciudad").Locator("input").FillAsync("Bogota");
        await whModal.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Crear bodega" }).ClickAsync();
        await Assertions.Expect(page.Locator("table tbody tr").Filter(
            new LocatorFilterOptions { HasText = warehouse })).ToBeVisibleAsync();

        // ---- 2. Crear marca (000502) ----
        await page.GotoAsync("inventario-marcas");
        await page.Locator(".module-head").GetByRole(AriaRole.Button,
            new LocatorGetByRoleOptions { Name = "Nueva marca" }).ClickAsync();
        var brModal = page.Locator(".modal-dialog");
        await brModal.WaitForAsync();
        await FieldIn(brModal, "Nombre").Locator("input").FillAsync(brand);
        await brModal.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Crear" }).ClickAsync();
        await Assertions.Expect(page.Locator("table tbody tr").Filter(
            new LocatorFilterOptions { HasText = brand })).ToBeVisibleAsync();

        // ---- 3. Crear item (000066) con marca + stock en la bodega ----
        await page.GotoAsync("inventario-items");
        await page.Locator(".module-head").GetByRole(AriaRole.Button,
            new LocatorGetByRoleOptions { Name = "Nuevo item" }).ClickAsync();
        var itemModal = page.Locator(".modal-dialog");
        await itemModal.WaitForAsync();
        await FieldIn(itemModal, "Nombre").Locator("input").First.FillAsync(item);
        // SKU explicito y unico (evita depender del consecutivo ITM del seeder). El campo SKU
        // contiene tambien el checkbox "Generar consecutivo"; se ancla al input de texto.
        await FieldIn(itemModal, "SKU").Locator("input.form-control").FillAsync($"E2E-{Sfx}");
        await FieldIn(itemModal, "Marca").Locator("select")
            .SelectOptionAsync(new SelectOptionValue { Label = brand });
        // Stock en la bodega recien creada (fila del editor anclada por el nombre de la bodega).
        var stockRow = itemModal.Locator(".inv-stock-row").Filter(
            new LocatorFilterOptions { HasText = warehouse });
        await stockRow.Locator("input").FillAsync("15");
        await stockRow.Locator("input").BlurAsync();
        await itemModal.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Crear item" }).ClickAsync();
        // El modal se cierra al crear y el grid se recarga.
        await itemModal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

        // ---- 4. Filtrar el grid por la bodega y verificar el item ----
        await page.Locator(".inv-filters select").Nth(0)
            .SelectOptionAsync(new SelectOptionValue { Label = warehouse });

        var row = page.Locator("table.inv-items-table tbody tr").Filter(
            new LocatorFilterOptions { HasText = item });
        await Assertions.Expect(row).ToBeVisibleAsync();
        // El total de stock (15) y la marca aparecen en la fila.
        await Assertions.Expect(row).ToContainTextAsync("15");
        await Assertions.Expect(row).ToContainTextAsync(brand);
    }
}
