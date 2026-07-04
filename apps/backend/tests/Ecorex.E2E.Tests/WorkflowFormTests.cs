using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// Escenario (e): recorrido del flujo demo COT-COM. Crear una actividad del tipo
/// vinculado ("Direccion Comercial / Cotizacion") arranca la instancia (la tarea pasa a
/// Activa); el primer paso es "Requerimiento", que NO tiene formulario ni UI para
/// completarse todavia (bandeja de pasos = deuda ADR-0014), asi que el test lo completa
/// via el propio WorkflowEngine (backdoor documentado en E2eDbBackdoor). Con el paso
/// "Cotizacion" vigente, el detalle muestra "Formularios del paso" con FRM-001 Pendiente;
/// diligenciarlo y enviarlo completa el paso y el flujo avanza a la compuerta Aprobacion.
/// </summary>
public sealed class WorkflowFormTests : E2eTestBase
{
    public WorkflowFormTests(E2eAppFixture fx) : base(fx)
    {
    }

    [SkippableFact]
    public async Task FlujoCotizacion_FormularioDelPaso_EnviarAvanzaElFlujo()
    {
        RequireApp();
        var page = await LoginAsync();
        var title = $"E2E flujo {Sfx}";
        var backdoor = new E2eDbBackdoor(Fx.ConnectionString);

        // 1. Crear la actividad del tipo con flujo (creacion rapida en PRY-0042): arranca
        //    COT-COM y la tarea nace Activa; la tarjeta queda en la columna elegida.
        await OpenBoardAsync(page, "Comercial - Requerimiento Infraestructura");
        var number = await QuickCreateTaskAsync(page, title, column: "Por hacer",
            typeLabel: "Direccion Comercial / Cotizacion");
        var card = CardIn(BoardColumn(page, "Por hacer"), title);
        await Assertions.Expect(card).ToBeVisibleAsync();

        // 2. El paso current es Requerimiento (sin formulario): el detalle aun NO muestra
        //    la seccion "Formularios del paso".
        await OpenTaskDetailAsync(page, title);
        await Assertions.Expect(page.Locator(".tk-card h3", new PageLocatorOptions { HasText = "Formularios del paso" }))
            .ToHaveCountAsync(0);
        await CloseTaskDetailAsync(page);

        Assert.Equal(new[] { "Task_Requerimiento" }, await backdoor.GetCurrentStepElementIdsAsync(number));

        // 3. Backdoor: completar Requerimiento con el motor -> el paso Cotizacion queda vigente.
        var error = await backdoor.CompleteCurrentStepAsync(number, "Task_Requerimiento");
        Assert.True(error is null, $"No se pudo completar Requerimiento via motor: {error}");
        Assert.Equal(new[] { "Task_Cotizacion" }, await backdoor.GetCurrentStepElementIdsAsync(number));

        // 4. Reabrir el detalle: seccion "Formularios del paso" con FRM-001 Pendiente.
        await page.ReloadAsync();
        await OpenTaskDetailAsync(page, title);
        var formsCard = page.Locator(".tk-card").Filter(new LocatorFilterOptions
        {
            Has = page.Locator("h3", new PageLocatorOptions { HasText = "Formularios del paso" })
        });
        await Assertions.Expect(formsCard).ToBeVisibleAsync();
        await Assertions.Expect(formsCard).ToContainTextAsync("Solicitud de cotizacion");
        await Assertions.Expect(formsCard).ToContainTextAsync("Paso: Cotizacion");
        await Assertions.Expect(formsCard.Locator(".tk-status.st-pending")).ToHaveTextAsync("Pendiente");

        // 5. Diligenciar FRM-001 (campos requeridos) y enviar.
        await formsCard.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Diligenciar" }).ClickAsync();
        var form = page.Locator(".dfr-root");
        await form.WaitForAsync();
        await PublicFormFiller.FillRequiredAsync(page, Sfx);
        await page.Locator(".dfr-foot").GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Enviar" }).ClickAsync();

        // 6. El submit completa el paso: el modal del formulario se cierra y, como el nuevo
        //    paso current es la compuerta Aprobacion (sin formulario), la seccion desaparece.
        await form.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        await Assertions.Expect(page.Locator(".tk-card h3", new PageLocatorOptions { HasText = "Formularios del paso" }))
            .ToHaveCountAsync(0);

        // 7. Estado del motor: el flujo avanzo de Cotizacion a la compuerta de aprobacion.
        Assert.Equal(new[] { "Gateway_Aprobacion" }, await backdoor.GetCurrentStepElementIdsAsync(number));
    }
}
