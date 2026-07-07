using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// E2E del modulo Administracion de usuarios del tenant (000073, ADR-0031): el owner del tenant
/// SKY SYSTEM entra a /admin-usuarios, crea un usuario nuevo (email unico, rol Asesor, con clave)
/// y lo ve aparecer en la tabla; luego cambia su rol a Supervisor y verifica que se refleja.
/// Selectores por texto/clase estables (.table, .modal-dialog, .badge); el producto no tiene
/// data-testid. Reusa el helper de login (owner@sky-system.local).
/// </summary>
public sealed class AdmUsuariosTests : E2eTestBase
{
    public AdmUsuariosTests(E2eAppFixture fx) : base(fx) { }

    [SkippableFact]
    public async Task Owner_CreatesUser_AndChangesRole()
    {
        RequireApp();
        var page = await LoginAsync("owner@sky-system.local", "Demo123*");

        await page.GotoAsync("admin-usuarios");
        await page.Locator(".module-head").WaitForAsync();

        var email = $"e2e-{Sfx}@sky-system.local";

        // ---- Crear ----
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Nuevo usuario" }).ClickAsync();
        var modal = page.Locator(".modal-dialog");
        await modal.WaitForAsync();

        await FieldIn(modal, "Email").Locator("input").FillAsync(email);
        await FieldIn(modal, "Nombre para mostrar").Locator("input").FillAsync("Usuario E2E");
        await FieldIn(modal, "Rol").Locator("select")
            .SelectOptionAsync(new SelectOptionValue { Label = "Asesor" });
        await FieldIn(modal, "Contrasena").Locator("input[type='password'], input[type='text']").First
            .FillAsync("ClaveE2E123");
        await modal.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Crear usuario" }).ClickAsync();

        // Aparece en la tabla.
        var row = page.Locator("table.table tbody tr").Filter(new LocatorFilterOptions { HasText = email });
        await Assertions.Expect(row).ToBeVisibleAsync();
        await Assertions.Expect(row).ToContainTextAsync("Asesor");

        // ---- Cambiar rol a Supervisor ----
        await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Editar" }).ClickAsync();
        var editModal = page.Locator(".modal-dialog");
        await editModal.WaitForAsync();
        await FieldIn(editModal, "Rol").Locator("select")
            .SelectOptionAsync(new SelectOptionValue { Label = "Supervisor" });
        await editModal.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Guardar" }).ClickAsync();

        // Se refleja en la tabla.
        var updatedRow = page.Locator("table.table tbody tr").Filter(new LocatorFilterOptions { HasText = email });
        await Assertions.Expect(updatedRow).ToContainTextAsync("Supervisor");
    }
}
