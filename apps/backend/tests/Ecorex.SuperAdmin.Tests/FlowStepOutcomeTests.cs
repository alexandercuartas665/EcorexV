using Ecorex.Domain.Enums;
using Ecorex.SuperAdmin.Agents;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// Corrida paso a paso que quedaba "Ok" aunque fallaran todos sus pasos (2026-11-16). Caso real: DRON PLATAFORMAS
/// INGENIOS de SOLDARCO con apps.pdpartner.com fuera de la allow-list del agente. Los textos de error son los que
/// devolvio la colmena en prod; los benignos son los que hoy anota el DRON DIAN como "sin filas".
/// </summary>
public class FlowStepOutcomeTests
{
    private static StepRunResult Fail(string error) => new(false, false, error, null, null, 0, 0, 0, error);
    private static StepRunResult Ok() => new(true, false, null, null, null, 0, 0, 0, "Paso ejecutado");

    [Theory]
    [InlineData(ScrapeStepKind.Navigate, "Dominio no permitido por la allow-list local: apps.pdpartner.com")]
    [InlineData(ScrapeStepKind.InjectScript, "Inyeccion de JS no permitida en este dominio: ")]
    [InlineData(ScrapeStepKind.Exploracion, "Inyeccion de JS no permitida en este dominio: ")]
    [InlineData(ScrapeStepKind.Extract, "Dominio no permitido por la allow-list local: apps.pdpartner.com")]
    // Navegacion fallida por cualquier causa: sin pagina lo demas no sirve.
    [InlineData(ScrapeStepKind.Navigate, "El Navegador no respondio la orden 34bf30d5 en 60 s.")]
    public void Fallos_duros(ScrapeStepKind kind, string error)
    {
        Assert.True(FlowStepOutcome.IsHardFailure(kind, Fail(error), threw: false));
    }

    [Theory]
    // "Esperar fin de descargas" del DRON DIAN: las descargas siguen en 2do plano, la espera agotada es normal.
    [InlineData(ScrapeStepKind.Wait, "Espera agotada (timeout).")]
    [InlineData(ScrapeStepKind.Wait, "La colmena que atendia el Navegador no respondio (se cerro o excedio el tiempo).")]
    // Guarda/driver sin filas.
    [InlineData(ScrapeStepKind.Exploracion, "La extraccion no devolvio datos: sin resultado.")]
    [InlineData(ScrapeStepKind.Exploracion, "El Navegador no respondio la orden ebf12c en 60 s.")]
    [InlineData(ScrapeStepKind.CerrarNavegador, "Navegador ya cerrado.")]
    public void Fallos_benignos_siguen_como_sin_filas(ScrapeStepKind kind, string error)
    {
        Assert.False(FlowStepOutcome.IsHardFailure(kind, Fail(error), threw: false));
    }

    [Fact]
    public void Excepcion_del_runtime_es_dura_y_ok_u_offline_nunca()
    {
        Assert.True(FlowStepOutcome.IsHardFailure(ScrapeStepKind.Wait, Fail("Object reference not set"), threw: true));
        Assert.False(FlowStepOutcome.IsHardFailure(ScrapeStepKind.Navigate, Ok(), threw: false));
        var offline = new StepRunResult(false, true, "El agente asignado no esta en linea.", null, null, 0, 0, 0, null);
        Assert.False(FlowStepOutcome.IsHardFailure(ScrapeStepKind.Navigate, offline, threw: false));
    }

    [Fact]
    public void Sin_errores_duros_el_detalle_es_el_de_siempre()
    {
        Assert.Equal("Flujo ejecutado paso a paso (13 pasos); sin filas en: Exploracion, Esperar fin de descargas.",
            FlowStepOutcome.Summarize(13, Array.Empty<string>(), new[] { "Exploracion", "Esperar fin de descargas" }));
        Assert.Equal("Flujo ejecutado paso a paso (5 pasos).",
            FlowStepOutcome.Summarize(5, Array.Empty<string>(), Array.Empty<string>()));
    }

    [Fact]
    public void Con_errores_duros_el_detalle_los_pone_primero()
    {
        var errores = new[]
        {
            FlowStepOutcome.HardNote("pdpartner", Fail("Dominio no permitido por la allow-list local: apps.pdpartner.com")),
            FlowStepOutcome.HardNote("pdpartnet.cotizaciones", Fail("Inyeccion de JS no permitida en este dominio: ")),
        };
        var det = FlowStepOutcome.Summarize(25, errores, new[] { "Variable" });
        Assert.StartsWith("2 paso(s) con error: pdpartner: Dominio no permitido por la allow-list local: apps.pdpartner.com; ", det);
        Assert.Contains("pdpartnet.cotizaciones: Inyeccion de JS no permitida en este dominio:", det);
        Assert.EndsWith("(flujo de 25 pasos; sin filas en: Variable).", det);
    }

    [Fact]
    public void La_nota_recorta_errores_largos()
    {
        var note = FlowStepOutcome.HardNote("paso", Fail(new string('x', 500)));
        Assert.True(note.Length <= "paso: ".Length + 160);
        Assert.EndsWith("...", note);
    }
}
