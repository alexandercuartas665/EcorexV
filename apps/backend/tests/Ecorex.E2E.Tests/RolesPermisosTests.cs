using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// E2E de Roles y permisos (Ola B1, ADR-0032): login owner -> /roles-permisos -> crear un rol
/// "QA Rol {sfx}" -> marcar algunos permisos (toggle de una fila) -> guardar -> abrir "Asignar
/// usuarios" y asignarle el rol a un usuario. Selectores por las clases CSS estables de la pagina.
/// </summary>
public sealed class RolesPermisosTests : E2eTestBase
{
    public RolesPermisosTests(E2eAppFixture fx) : base(fx)
    {
    }

    [SkippableFact]
    public async Task Owner_CreaRol_MarcaPermisos_Guarda_YAsigna()
    {
        RequireApp();
        var page = await LoginAsync();

        await page.GotoAsync("roles-permisos");
        await page.Locator(".rp-layout").WaitForAsync();

        var rolName = $"QA Rol {Sfx}";

        // ---- Crear rol ----
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "+ Nuevo rol" }).ClickAsync();
        var modal = page.Locator(".modal-dialog");
        await modal.WaitForAsync();
        await FieldIn(modal, "Nombre").Locator("input").FillAsync(rolName);
        await FieldIn(modal, "Descripcion").Locator("input").FillAsync("Rol creado por E2E");
        await modal.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Guardar" }).ClickAsync();

        // El rol nuevo aparece seleccionado en el editor.
        await Assertions.Expect(page.Locator(".rp-editor-title h2")).ToContainTextAsync(rolName);

        // ---- Marcar algunos permisos: toggle de la primera fila de modulo ----
        var firstRowToggle = page.Locator(".rp-row-toggle").First;
        await firstRowToggle.ClickAsync();
        // Al menos un checkbox marcado en la matriz.
        await Assertions.Expect(page.Locator(".rp-matrix input[type=checkbox]:checked").First).ToBeVisibleAsync();

        // ---- Guardar permisos ----
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Guardar permisos" }).ClickAsync();
        await Assertions.Expect(page.Locator(".tk-toast.ok")).ToContainTextAsync("Permisos guardados");

        // ---- Asignar el rol a un usuario ----
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Asignar usuarios" }).ClickAsync();
        var assignModal = page.Locator(".modal-dialog");
        await assignModal.WaitForAsync();

        // Selecciona el rol nuevo para el primer usuario de la lista.
        var firstSelect = assignModal.Locator("tbody tr select").First;
        await firstSelect.SelectOptionAsync(new SelectOptionValue { Label = rolName });
        await Assertions.Expect(page.Locator(".tk-toast.ok")).ToContainTextAsync("Rol asignado");

        await assignModal.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Listo" }).ClickAsync();
    }
}
