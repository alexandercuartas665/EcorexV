using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// Escenario del CARGADOR DE CONTACTOS (/cargador-contactos, modulo 000873, ADR-0024):
/// generar un CSV pequeno en el test, subirlo por el InputFile, verificar el mapeo
/// automatico y la previsualizacion validada (KPIs valida/duplicada/invalida), cargar
/// las filas validas, ver el resumen del resultado + historial, y confirmar que los
/// leads aparecen en el pipeline real. Selectores por clases estables del modulo (cl-*).
/// </summary>
public sealed class CargadorContactosTests : E2eTestBase
{
    public CargadorContactosTests(E2eAppFixture fx) : base(fx)
    {
    }

    [SkippableFact]
    public async Task SubirCsv_Mapear_Cargar_VerResumen_YLeadsEnPipeline()
    {
        RequireApp();
        var page = await LoginAsync();

        // Datos unicos por corrida: telefonos aleatorios para no chocar con cargas previas.
        var phone1 = UniquePhone();
        var phone2 = UniquePhone();
        var nameOk1 = $"Contacto E2E {Sfx} Uno";
        var nameOk2 = $"Contacto E2E {Sfx} Dos";

        var csvPath = Path.Combine(Path.GetTempPath(), $"contactos-e2e-{Sfx}.csv");
        await File.WriteAllTextAsync(csvPath,
            "nombre,telefono,email,empresa,destino,valor_estimado\r\n" +
            $"{nameOk1},{phone1},uno.{Sfx}@e2e.test,ACME E2E,Bogota,1500000\r\n" +
            $"{nameOk2},{phone2},dos.{Sfx}@e2e.test,Beta E2E,Medellin,2000000\r\n" +
            $"Duplicado E2E {Sfx},{phone1},dup.{Sfx}@e2e.test,,,\r\n" +
            $",{UniquePhone()},sin.nombre.{Sfx}@e2e.test,,,\r\n");

        try
        {
            // 1. La pagina carga con la estructura del proto: sidebar + 4 KPIs + tabs.
            await page.GotoAsync("cargador-contactos");
            await page.Locator(".cl-layout").WaitForAsync();
            await Assertions.Expect(page.Locator(".cl-kpi")).ToHaveCountAsync(4);
            await Assertions.Expect(page.Locator(".cl-mod-code")).ToContainTextAsync("MOD 000873");

            // 2. Subir el CSV: el parser + automapeo + validacion llenan la previsualizacion.
            // KPIs en el orden fijo del proto: Filas / Validas / Duplicadas / Invalidas.
            await page.SetInputFilesAsync(".cl-add-file input[type=file]", csvPath);
            await Assertions.Expect(KpiValue(page, 0)).ToHaveTextAsync("4");
            await Assertions.Expect(KpiValue(page, 1)).ToHaveTextAsync("2");
            await Assertions.Expect(KpiValue(page, 2)).ToHaveTextAsync("1");
            await Assertions.Expect(KpiValue(page, 3)).ToHaveTextAsync("1");

            // El mapeo automatico reconocio las 6 columnas de la plantilla.
            await Assertions.Expect(page.Locator(".cl-map-row")).ToHaveCountAsync(6);
            await Assertions.Expect(page.Locator(".cl-f-sec-title").Filter(
                new LocatorFilterOptions { HasText = "Mapeo de columnas" })).ToContainTextAsync("6 de 6");

            // La grilla marca la fila duplicada y la invalida con su motivo.
            await Assertions.Expect(page.Locator(".cl-contacts .cl-mini.dup")).ToHaveCountAsync(1);
            await Assertions.Expect(page.Locator(".cl-contacts .cl-mini.bad")).ToHaveCountAsync(1);
            await Assertions.Expect(page.Locator(".cl-contacts tr.bad .cl-msg"))
                .ToContainTextAsync("nombre del contacto esta vacio");

            // 3. Cargar las validas (boton primario del topbar) y ver el resumen.
            await page.Locator(".cl-topbar .cl-btn.primary").ClickAsync();
            await Assertions.Expect(page.Locator(".cl-result-title"))
                .ToContainTextAsync("2 contactos insertados");
            await Assertions.Expect(page.Locator(".cl-result-card.ok .v")).ToHaveTextAsync("2");
            await Assertions.Expect(page.Locator(".cl-result-card.dup .v")).ToHaveTextAsync("1");
            await Assertions.Expect(page.Locator(".cl-result-card.bad .v")).ToHaveTextAsync("1");

            // El historial del sidebar registro la carga con sus insertadas.
            await Assertions.Expect(page.Locator(".cl-batch").Filter(
                    new LocatorFilterOptions { HasText = $"contactos-e2e-{Sfx}.csv" }))
                .ToBeVisibleAsync();

            // 4. Los leads cargados existen en el pipeline real del CRM.
            await page.GotoAsync("pipeline");
            await Assertions.Expect(page.GetByText(nameOk1).First).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText(nameOk2).First).ToBeVisibleAsync();
        }
        finally
        {
            File.Delete(csvPath);
        }
    }

    private static ILocator KpiValue(IPage page, int index) =>
        page.Locator(".cl-kpi").Nth(index).Locator(".cl-kpi-v");

    private static string UniquePhone() =>
        "3" + Random.Shared.NextInt64(100_000_000, 999_999_999).ToString();
}
