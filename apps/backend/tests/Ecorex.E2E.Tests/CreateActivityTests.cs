using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// Escenario (b), ola 2: creacion de tareas en la experiencia de tableros.
/// (b1) Wizard "Actividad completa" de 3 pasos desde el indice -> toast T#####
///      (la tarea NO cuelga de un tablero: es el flujo completo con tipo/flujo BPMN).
/// (b2) Creacion rapida (boton "Tarea") dentro de PRY-0042 -> toast T##### y la
///      tarjeta aparece en la columna elegida del kanban del tablero.
/// Usa el tipo "Gestion Humana / Solicitud" (SIN flujo vinculado) a proposito.
/// </summary>
public sealed class CreateActivityTests : E2eTestBase
{
    public CreateActivityTests(E2eAppFixture fx) : base(fx)
    {
    }

    [SkippableFact]
    public async Task Wizard_TresPasos_CreaActividad_ConToast()
    {
        RequireApp();
        var page = await LoginAsync();
        var title = $"E2E {Sfx}";

        var number = await CreateActivityAsync(
            page, category: "Gestion Humana", type: "Solicitud", title: title, priority: "Alta");

        Assert.Matches(new Regex(@"^T\d{5,}$"), number);
    }

    [SkippableFact]
    public async Task CreacionRapida_EnTablero_ToastYTarjetaEnColumna()
    {
        RequireApp();
        var page = await LoginAsync();
        var title = $"E2E rapida {Sfx}";

        await OpenBoardAsync(page, "Comercial - Requerimiento Infraestructura");
        var number = await QuickCreateTaskAsync(
            page, title, column: "Por hacer", priority: "Alta",
            typeLabel: "Gestion Humana / Solicitud");

        Assert.Matches(new Regex(@"^T\d{5,}$"), number);

        // La tarjeta aparece en la columna "Por hacer" del tablero con su titulo.
        var card = CardIn(BoardColumn(page, "Por hacer"), title);
        await Assertions.Expect(card).ToBeVisibleAsync();
    }
}
