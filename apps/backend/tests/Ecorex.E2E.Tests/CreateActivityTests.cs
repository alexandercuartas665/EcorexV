using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// Escenario (b): wizard de 3 pasos completo -> toast con numero T##### y tarjeta en la
/// columna Pendiente del kanban. Usa el tipo "Gestion Humana / Solicitud" (SIN flujo
/// vinculado) a proposito: los tipos con flujo pasan la tarea a Activa al arrancar la
/// instancia y no aterrizarian en Pendiente.
/// </summary>
public sealed class CreateActivityTests : E2eTestBase
{
    public CreateActivityTests(E2eAppFixture fx) : base(fx)
    {
    }

    [SkippableFact]
    public async Task Wizard_TresPasos_CreaActividad_ToastYTarjetaEnPendiente()
    {
        RequireApp();
        var page = await LoginAsync();
        var title = $"E2E {Sfx}";

        var number = await CreateActivityAsync(
            page, category: "Gestion Humana", type: "Solicitud", title: title, priority: "Alta");

        Assert.Matches(new Regex(@"^T\d{5,}$"), number);

        // La tarjeta aparece en la columna Pendiente con su numero y prioridad Alta.
        var card = CardIn(KanbanColumn(page, "Pendiente"), title);
        await Assertions.Expect(card).ToBeVisibleAsync();
        await Assertions.Expect(card.Locator(".tk-number")).ToHaveTextAsync(number);
        await Assertions.Expect(card.Locator(".tk-priority")).ToHaveTextAsync("Alta");
    }
}
