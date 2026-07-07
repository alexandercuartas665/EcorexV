using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// E2E del Administrador de Menu (Ola 2): el owner del tenant SKY SYSTEM entra a
/// /configuracion-menu, crea una vista nueva, abre el editor, agrega una seccion y un item,
/// guarda, y asigna la vista a un usuario. Selectores por texto/clase del editor
/// (.mc-card, .mc-editor, .mc-row, .mc-toolbar) porque el producto no expone data-testid.
/// No toca la suite MenuProfileTests (Ola 1), que sigue verde.
/// </summary>
public sealed class MenuEditorTests : E2eTestBase
{
    public MenuEditorTests(E2eAppFixture fx) : base(fx) { }

    [SkippableFact]
    public async Task Owner_CreatesView_AddsNodes_AndAssignsUser()
    {
        RequireApp();
        var page = await LoginAsync(); // owner@sky-system.local

        await page.GotoAsync("configuracion-menu");
        await Assertions.Expect(page.Locator("h1.page-title")).ToContainTextAsync("Administrador de Menu");

        var viewName = $"E2E {Sfx}";

        // 1) Nueva vista.
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "+ Nueva vista" }).ClickAsync();
        var viewModal = page.Locator(".modal-dialog").Filter(new LocatorFilterOptions
        {
            Has = page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Nueva vista" })
        });
        await viewModal.WaitForAsync();
        await viewModal.Locator("input.form-control").First.FillAsync(viewName);
        await viewModal.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Guardar" }).ClickAsync();

        // La tarjeta de la nueva vista aparece en el index.
        var card = page.Locator(".mc-card").Filter(new LocatorFilterOptions { HasText = viewName });
        await Assertions.Expect(card).ToBeVisibleAsync();

        // 2) Abre el editor de esa vista.
        await card.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Editar" }).ClickAsync();
        var editor = page.Locator(".mc-editor");
        await editor.WaitForAsync();

        // 3) Agrega una seccion raiz.
        await editor.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "+ Seccion" }).ClickAsync();
        var sectionRow = editor.Locator(".mc-row").Filter(new LocatorFilterOptions { HasText = "Nueva seccion" });
        await Assertions.Expect(sectionRow).ToBeVisibleAsync();

        // 4) Agrega un item hijo (boton "+" de la fila de la seccion; aparece en hover).
        await sectionRow.HoverAsync();
        await sectionRow.Locator("button[title='Agregar hijo']").ClickAsync();
        await Assertions.Expect(editor.Locator(".mc-row").Filter(new LocatorFilterOptions { HasText = "Nuevo elemento" }))
            .ToBeVisibleAsync();

        // 5) Guarda (confirma la persistencia; el editor aplica al vuelo).
        await editor.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Guardar" }).ClickAsync();

        // KPI de secciones refleja al menos 1.
        var sectionsKpi = editor.Locator(".mc-kpi").Filter(new LocatorFilterOptions { HasText = "Secciones" }).Locator("b");
        await Assertions.Expect(sectionsKpi).Not.ToHaveTextAsync("0");

        // Cierra el editor.
        await editor.Locator(".modal-close").ClickAsync();
        await Assertions.Expect(page.Locator(".mc-editor")).ToHaveCountAsync(0);

        // 6) Asigna la vista a un usuario.
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Asignar usuarios" }).ClickAsync();
        var assignModal = page.Locator(".modal-dialog").Filter(new LocatorFilterOptions
        {
            Has = page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Asignar usuarios a vistas" })
        });
        await assignModal.WaitForAsync();

        // Elige la vista recien creada en el primer selector de usuario.
        var firstSelect = assignModal.Locator("tbody tr select").First;
        await firstSelect.SelectOptionAsync(new SelectOptionValue { Label = viewName });

        // La asignacion muestra el flash de confirmacion.
        await Assertions.Expect(page.Locator(".mc-flash.ok")).ToBeVisibleAsync();
    }
}
