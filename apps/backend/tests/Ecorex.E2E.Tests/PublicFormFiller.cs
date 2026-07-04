using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// Rellena los campos REQUERIDOS del formulario demo FRM-001 "Solicitud de cotizacion"
/// dentro de un DynamicFormRenderer (sirve igual en el modal del detalle de tarea y en el
/// visor publico /f/{token}). Los labels del renderer no estan asociados por for/id, asi
/// que cada control se ancla a su columna (div col-*) por el texto del label.
///
/// Dos particularidades NO opcionales de este relleno:
///
/// 1. BlurAsync tras cada FillAsync: los inputs del renderer usan @onchange (no oninput)
///    y Playwright fill solo dispara "input"; el "change" recien sale al perder el foco.
///    Sin blur explicito el servidor nunca ve el valor.
///
/// 2. ORDEN y ESPERAS por el bug conocido del producto (detectado por esta suite):
///    "nombre_solicitante" y "prioridad" tienen reglas vinculadas (RUL-005) y su @onchange
///    dispara IFormRuleDispatcher con consultas EF sobre el DbContext del circuito Blazor.
///    Si otro evento de campo llega mientras ese dispatch esta en vuelo (velocidad
///    Playwright, no humana), EF lanza "A second operation was started on this context
///    instance" y el circuito MUERE en silencio (Enviar deja de responder, sin feedback).
///    Mitigacion:
///    - nombre_solicitante se llena PRIMERO y se espera su efecto OBSERVABLE: la regla
///      demo PASAR_CAMPOS copia el nombre al campo descripcion (senal deterministica de
///      que el roundtrip de reglas termino).
///    - prioridad (con "Media" la regla BLOQUEAR_CAMPO_XCONDICION no produce cambio
///      visible; con "baja" ocultaria fecha_requerida) se marca al final con pausa fija.
///    - descripcion se llena DE ULTIMO para pisar el valor que copio PASAR_CAMPOS.
///    El fix real (scope EF propio por dispatch, como TaskKanban.ReloadAsync) es del
///    producto y esta fuera del alcance de esta suite.
/// </summary>
internal static class PublicFormFiller
{
    public static async Task FillRequiredAsync(IPage page, string sfx)
    {
        var nombre = $"Cliente E2E {sfx}";

        // 1. Campo con regla PASAR_CAMPOS: blur para disparar el @onchange y espera de la
        //    copia hacia descripcion (senal de que el dispatch de reglas termino).
        var nombreInput = Group(page, "Nombre del solicitante").Locator("input.form-control");
        await nombreInput.FillAsync(nombre);
        await nombreInput.BlurAsync();
        await Assertions.Expect(Group(page, "Descripcion de la necesidad").Locator("textarea.form-control"))
            .ToHaveValueAsync(nombre);

        // 2. Campos sin reglas vinculadas (su @onchange no toca la base por reglas).
        var email = Group(page, "Correo electronico").Locator("input.form-control");
        await email.FillAsync($"e2e.{sfx}@ejemplo.com");
        await email.BlurAsync();
        await Group(page, "Tipo de servicio").Locator("select.form-control")
            .SelectOptionAsync(new SelectOptionValue { Label = "Licencias de software" });
        var cantidad = Group(page, "Cantidad estimada").Locator("input.form-control");
        await cantidad.FillAsync("5");
        await cantidad.BlurAsync();

        // 3. Campo con regla BLOQUEAR_CAMPO_XCONDICION: sin efecto visible con "Media",
        //    asi que la unica espera posible es fija (las consultas ya estan tibias).
        await Group(page, "Prioridad de la solicitud").Locator(".dfr-check")
            .Filter(new LocatorFilterOptions { HasText = "Media" })
            .Locator("input").CheckAsync();
        await page.WaitForTimeoutAsync(1500);

        // 4. Descripcion al final: pisa el valor que copio PASAR_CAMPOS (campo sin regla).
        var descripcion = Group(page, "Descripcion de la necesidad").Locator("textarea.form-control");
        await descripcion.FillAsync($"Solicitud generada por la suite E2E (corrida {sfx}).");
        await descripcion.BlurAsync();
    }

    private static ILocator Group(IPage page, string label)
        => page.Locator(".dfr-root [class*='col-']").Filter(new LocatorFilterOptions
        {
            Has = page.Locator("label.dfr-label", new PageLocatorOptions { HasText = label })
        }).First;
}
