using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// E2E de la bandeja operativa de flujos "Mis pasos" (runtime, ola F2, ADR-0036). El seed demo
/// crea una tarea del ActivityType COT-COM que arranca el flujo con el paso "Requerimiento"
/// Pending, cuyo candidato es el cargo "Asesor Comercial" (ocupado por operator@sky-system.local).
/// El test entra como operator@, abre /mis-pasos, ve el paso, lo Toma, lo Atiende (Completar,
/// porque Requerimiento NO tiene formulario) y verifica que el paso desaparece de su bandeja
/// (el flujo avanzo a Cotizacion, cuyo candidato es OTRO cargo).
/// </summary>
public sealed class WorkflowInboxTests : E2eTestBase
{
    public WorkflowInboxTests(E2eAppFixture fx) : base(fx)
    {
    }

    [SkippableFact]
    public async Task MisPasos_OperatorVeElPaso_Toma_Completa_YDesaparece()
    {
        RequireApp();
        // Login como el candidato del primer paso (Asesor Comercial).
        var page = await LoginAsync("operator@sky-system.local", "Demo123*");

        await page.GotoAsync("mis-pasos");
        await page.Locator(".module-head").WaitForAsync();

        // El paso de la tarea demo (proceso Cotizacion Comercial, nodo Requerimiento) aparece.
        // Se ancla por el titulo estable de la tarea demo para no cruzarse con otras corridas.
        const string demoTaskTitle = "Cotizacion de infraestructura para cliente demo";
        var demoCard = page.Locator(".mp-card").Filter(new LocatorFilterOptions { HasText = demoTaskTitle }).First;
        await Assertions.Expect(demoCard).ToBeVisibleAsync();
        await Assertions.Expect(demoCard).ToContainTextAsync("Requerimiento");
        await Assertions.Expect(demoCard).ToContainTextAsync("Cotizacion Comercial");

        // Tomar el paso (sin reclamar -> Tuyo).
        var tomar = demoCard.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Tomar" });
        if (await tomar.CountAsync() > 0)
        {
            await tomar.ClickAsync();
            demoCard = page.Locator(".mp-card").Filter(new LocatorFilterOptions { HasText = demoTaskTitle }).First;
            await Assertions.Expect(demoCard.Locator(".tk-status", new LocatorLocatorOptions { HasText = "Tuyo" }))
                .ToBeVisibleAsync();
        }
        var card = demoCard;

        // Atender: abre el panel y completa el paso (Requerimiento no tiene formulario ni gateway).
        await card.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Atender" }).ClickAsync();
        var complete = card.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Completar" });
        await Assertions.Expect(complete).ToBeVisibleAsync();
        await complete.ClickAsync();

        // El paso de la tarea demo desaparece de la bandeja de operator@ (avanzo a Cotizacion,
        // cuyo candidato es el cargo Aprobador, no operator@).
        await Assertions.Expect(page.Locator(".mp-card").Filter(new LocatorFilterOptions { HasText = demoTaskTitle }))
            .ToHaveCountAsync(0);
    }
}
