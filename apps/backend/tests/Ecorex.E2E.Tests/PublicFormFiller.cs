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
/// 2. ORDEN de llenado por la SEMANTICA de las reglas demo (RUL-005), no por timing:
///    "nombre_solicitante" tiene la regla PASAR_CAMPOS que copia su valor al campo
///    "descripcion". Por eso:
///    - nombre_solicitante se llena PRIMERO y se espera su efecto OBSERVABLE (la copia
///      hacia descripcion), senal deterministica de que el roundtrip de reglas termino.
///    - descripcion se llena DE ULTIMO para pisar el valor que copio PASAR_CAMPOS.
///    - prioridad (con "Media" la regla BLOQUEAR_CAMPO_XCONDICION no produce cambio
///      visible; con "baja" ocultaria fecha_requerida) solo se marca, sin espera.
///    NOTA: antes habia una pausa fija tras marcar prioridad para tapar un bug de
///    concurrencia del renderer (dos @onchange rapidos interleaban consultas EF sobre el
///    DbContext del circuito y lo tumbaban con "A second operation was started on this
///    context"). Ese bug ya esta corregido en el producto (commit 7f312a9: un SemaphoreSlim
///    serializa despacho de reglas, autosave y submit), asi que la pausa se elimino.
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

        // 3. Campo con regla BLOQUEAR_CAMPO_XCONDICION: sin efecto visible con "Media".
        //    El renderer serializa el despacho de reglas (SemaphoreSlim _dbGate), asi que
        //    ya no hace falta la pausa fija que antes tapaba la carrera sobre el DbContext.
        await Group(page, "Prioridad de la solicitud").Locator(".dfr-check")
            .Filter(new LocatorFilterOptions { HasText = "Media" })
            .Locator("input").CheckAsync();

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
