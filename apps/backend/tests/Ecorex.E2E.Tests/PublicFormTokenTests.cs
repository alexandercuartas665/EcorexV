using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// Escenario (f): visor publico por token. Desde /formularios/{id}/disenar de FRM-001 se
/// emite una URL publica de UN SOLO USO; en un contexto de navegador ANONIMO (sin cookies
/// de sesion) se abre /f/{token}, se envia el formulario y aparece la pantalla de gracias.
/// Reusar el token quemado debe mostrar el mensaje NEUTRO (identico para invalido /
/// expirado / usado / revocado, ADR-0015).
/// </summary>
public sealed class PublicFormTokenTests : E2eTestBase
{
    public PublicFormTokenTests(E2eAppFixture fx) : base(fx)
    {
    }

    [SkippableFact]
    public async Task TokenSingleUse_EnviaYAgradece_ReusoMuestraMensajeNeutro()
    {
        RequireApp();
        var page = await LoginAsync();

        // 1. Ir al CONSTRUCTOR de FRM-001 desde el indice del prototipo (ADR-0021):
        //    la tarjeta completa (.fx-card) abre el constructor; el codigo va en .fx-code.
        await page.GotoAsync("formularios");
        var frmCard = page.Locator(".fx-card").Filter(new LocatorFilterOptions
        {
            Has = page.Locator(".fx-code", new PageLocatorOptions { HasText = "FRM-001" })
        }).First;
        // El click puede perderse si el circuito Blazor aun se esta conectando con la
        // suite completa en marcha (el server esta ocupado): reintentar hasta navegar.
        for (var attempt = 0; ; attempt++)
        {
            await frmCard.ClickAsync();
            try
            {
                await page.WaitForURLAsync(new Regex("/formularios/.+/disenar"), new PageWaitForURLOptions { Timeout = 5000 });
                break;
            }
            catch (TimeoutException) when (attempt < 3)
            {
            }
            catch (PlaywrightException) when (attempt < 3)
            {
            }
        }

        // 2. Emitir URL publica de un solo uso (referencia = sufijo de la corrida).
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Publicar por URL" }).ClickAsync();
        var modal = page.Locator(".modal-dialog").Filter(new LocatorFilterOptions
        {
            Has = page.Locator("h3", new PageLocatorOptions { HasText = "Publicar por URL" })
        });
        await FieldIn(modal, "Referencia").Locator("input").FillAsync($"E2E-{Sfx}");
        await modal.Locator("label.pl-toggle").Filter(new LocatorFilterOptions { HasText = "Un solo uso" })
            .Locator("input").CheckAsync();
        await modal.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Emitir URL" }).ClickAsync();

        var urlBox = modal.Locator(".fd-token-url");
        await urlBox.WaitForAsync();
        var publicUrl = (await urlBox.InnerTextAsync()).Trim();
        Assert.Matches(new Regex(@"/f/[A-Za-z0-9_\-]+"), publicUrl);

        // 3. Contexto ANONIMO nuevo: abrir /f/{token}, llenar y enviar.
        var anonPage = await NewPageAsync();
        await anonPage.GotoAsync(publicUrl);
        await anonPage.Locator(".dfr-root").WaitForAsync();
        await Assertions.Expect(anonPage.Locator(".dfr-title")).ToHaveTextAsync("Solicitud de cotizacion");
        await PublicFormFiller.FillRequiredAsync(anonPage, Sfx);
        await anonPage.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Enviar" }).ClickAsync();
        try
        {
            await Assertions.Expect(anonPage.Locator(".fp-thanks h2"))
                .ToHaveTextAsync("Gracias, tu respuesta fue enviada.");
        }
        catch (PlaywrightException)
        {
            var alerts = string.Join(" | ", await anonPage.Locator(".dfr-alert").AllInnerTextsAsync());
            var fieldErrors = string.Join(" | ", await anonPage.Locator(".tk-field-error").AllInnerTextsAsync());
            var body = (await anonPage.Locator("body").InnerTextAsync()).Replace('\n', ' ');
            Assert.Fail($"No aparecio la pantalla de gracias. Alertas: [{alerts}] Errores de campo: [{fieldErrors}] Body: {body[..Math.Min(body.Length, 1200)]}");
        }

        // 4. Reuso del token quemado (otro contexto anonimo): mensaje neutro, sin formulario.
        var reusePage = await NewPageAsync();
        await reusePage.GotoAsync(publicUrl);
        await Assertions.Expect(reusePage.Locator(".fp-thanks h2"))
            .ToHaveTextAsync("Este enlace no esta disponible.");
        await Assertions.Expect(reusePage.Locator(".dfr-root")).ToHaveCountAsync(0);
    }
}
