using Ecorex.Application.Scraping;

namespace Ecorex.Application.Tests;

/// <summary>
/// Secuencia de tramos de una programacion de flujo (ADR-0171): generacion de tramos, recorte del ultimo,
/// overrides por tramo, validacion y round-trip del JSON guardado en ImportProcess.SequenceJson.
/// </summary>
public class FlowSequenceSpecTests
{
    [Fact]
    public void Tramos_CubreElRangoDeAPasoYRecortaElUltimo()
    {
        var spec = new FlowSequenceSpec { Desde = 0, Hasta = 8, Paso = 3 };

        var t = spec.Tramos();

        Assert.Equal(3, t.Count);
        Assert.Equal(new FlowTramo(1, 0, 3), t[0]);
        Assert.Equal(new FlowTramo(2, 3, 3), t[1]);
        Assert.Equal(new FlowTramo(3, 6, 2), t[2]); // no pasa de Hasta (8 exclusivo)
    }

    [Fact]
    public void Tramos_ConDesdeDistintoDeCero_EmpiezaAhi()
    {
        var spec = new FlowSequenceSpec { Desde = 43, Hasta = 53, Paso = 3 };

        var t = spec.Tramos();

        Assert.Equal(new[] { 43, 46, 49, 52 }, t.Select(x => x.Desde));
        Assert.Equal(1, t[^1].Largo);
    }

    [Fact]
    public void OverridesFor_UsaLasVariablesConfiguradas()
    {
        var spec = new FlowSequenceSpec { VarDesde = " DIAS_DESDE ", VarLargo = "DIAS_ATRAS", Desde = 0, Hasta = 6, Paso = 3 };

        var ov = spec.OverridesFor(spec.Tramos()[1]);

        Assert.Equal("3", ov["DIAS_DESDE"]);
        Assert.Equal("3", ov["DIAS_ATRAS"]);
        Assert.Equal(2, ov.Count);
    }

    [Theory]
    [InlineData("", "DIAS_ATRAS", 0, 6, 3)]          // falta variable
    [InlineData("X", "X", 0, 6, 3)]                    // variables iguales
    [InlineData("A", "B", -1, 6, 3)]                   // desde negativo
    [InlineData("A", "B", 0, 6, 0)]                    // paso 0
    [InlineData("A", "B", 6, 6, 3)]                    // hasta <= desde
    [InlineData("A", "B", 0, 1000, 1)]                 // demasiados tramos
    public void Validate_RechazaConfiguracionesInvalidas(string vd, string vl, int desde, int hasta, int paso)
    {
        var spec = new FlowSequenceSpec { VarDesde = vd, VarLargo = vl, Desde = desde, Hasta = hasta, Paso = paso };

        Assert.NotNull(spec.Validate());
    }

    [Fact]
    public void Validate_AceptaElBarridoDiarioYElSemanal()
    {
        Assert.Null(new FlowSequenceSpec { Desde = 0, Hasta = 6, Paso = 3 }.Validate());
        Assert.Null(new FlowSequenceSpec { Desde = 0, Hasta = 60, Paso = 3, PausaMin = 5, Reintentos = 1, ReintentoMin = 20 }.Validate());
    }

    [Fact]
    public void Json_RoundTrip_ConservaTodo()
    {
        var spec = new FlowSequenceSpec
        {
            VarDesde = "D",
            VarLargo = "L",
            Desde = 2,
            Hasta = 30,
            Paso = 4,
            PausaMin = 7,
            ReintentoMin = 15,
            Reintentos = 2,
        };

        var back = FlowSequenceSpec.Parse(spec.ToJson());

        Assert.Equal(spec, back);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{no es json")]
    public void Parse_SinSecuenciaOJsonRoto_DevuelveNull(string? json)
    {
        Assert.Null(FlowSequenceSpec.Parse(json));
    }

    [Fact]
    public void Describe_ResumeLosTramos()
    {
        Assert.Equal("2 tramo(s) de 3: 0-2, 3-5", new FlowSequenceSpec { Desde = 0, Hasta = 6, Paso = 3 }.Describe());
        Assert.Equal("20 tramo(s) de 3: 0-2, 3-5, ... 57-59", new FlowSequenceSpec { Desde = 0, Hasta = 60, Paso = 3 }.Describe());
    }
}
