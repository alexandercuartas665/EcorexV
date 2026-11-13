using Ecorex.Application.Agendas;

namespace Ecorex.Application.Tests;

/// <summary>ADR-0165: calculo puro del modulo de Agendas (cupos, validacion, vigencia que aplica, disponibilidad).</summary>
public class AgendaCalculoTests
{
    private static AgendaFranjaDto F(DayOfWeek d, int hi, int hf, int iv = 30, int cupo = 1) =>
        new(d, new TimeOnly(hi, 0), new TimeOnly(hf, 0), iv, cupo);

    [Fact]
    public void Cupos_SonBloquesCompletosPorCupo()
    {
        Assert.Equal(8, AgendaCalculo.CuposFranja(F(DayOfWeek.Monday, 8, 12)));            // 240/30
        Assert.Equal(4 * 6, AgendaCalculo.CuposFranja(F(DayOfWeek.Monday, 9, 12, 45, 6)));  // 180/45 = 4 bloques * aforo 6
        // El sobrante que no completa un bloque no cuenta: 08:00-09:50 en bloques de 60 = 1.
        Assert.Equal(1, AgendaCalculo.CuposFranja(new AgendaFranjaDto(DayOfWeek.Monday, new(8, 0), new(9, 50), 60, 1)));
        Assert.Equal(16, AgendaCalculo.CuposSemana(new[] { F(DayOfWeek.Monday, 8, 12), F(DayOfWeek.Monday, 14, 18) }));
    }

    [Fact]
    public void Validar_RechazaCrucesFinAntesYBloquesInvalidos()
    {
        Assert.Null(AgendaCalculo.ValidarFranjas(new[] { F(DayOfWeek.Monday, 8, 12), F(DayOfWeek.Monday, 12, 14), F(DayOfWeek.Tuesday, 8, 12) }));
        Assert.Contains("se cruzan", AgendaCalculo.ValidarFranjas(new[] { F(DayOfWeek.Monday, 8, 12), F(DayOfWeek.Monday, 11, 14) }));
        Assert.Contains("posterior", AgendaCalculo.ValidarFranjas(new[] { F(DayOfWeek.Friday, 12, 8) }));
        Assert.Contains("bloque", AgendaCalculo.ValidarFranjas(new[] { F(DayOfWeek.Friday, 8, 12, iv: 2) }));
        Assert.Contains("cupo", AgendaCalculo.ValidarFranjas(new[] { F(DayOfWeek.Friday, 8, 12, cupo: 0) }));
        Assert.Contains("mas corto", AgendaCalculo.ValidarFranjas(new[] { new AgendaFranjaDto(DayOfWeek.Friday, new(8, 0), new(8, 20), 30, 1) }));
    }

    [Fact]
    public void Vigencia_GanaLaMasRecienteQueCubreLaFecha()
    {
        var normal = new AgendaVigenciaCalc("Normal", new DateOnly(2026, 11, 1), null, new[] { F(DayOfWeek.Monday, 8, 12) });
        var temporada = new AgendaVigenciaCalc("Temporada", new DateOnly(2026, 12, 15), new DateOnly(2027, 1, 10), new[] { F(DayOfWeek.Monday, 8, 10) });
        var lista = new[] { normal, temporada };
        Assert.Null(AgendaCalculo.VigenciaPara(lista, new DateOnly(2026, 10, 31)));
        Assert.Equal("Normal", AgendaCalculo.VigenciaPara(lista, new DateOnly(2026, 12, 14))!.Nombre);
        Assert.Equal("Temporada", AgendaCalculo.VigenciaPara(lista, new DateOnly(2026, 12, 20))!.Nombre);
        Assert.Equal("Normal", AgendaCalculo.VigenciaPara(lista, new DateOnly(2027, 1, 11))!.Nombre);
    }

    [Fact]
    public void Dia_DescuentaNovedadParcialYCierraPorCalendario()
    {
        var lunes = new DateOnly(2026, 11, 16); // lunes
        var vig = new[] { new AgendaVigenciaCalc("Oficina", new DateOnly(2026, 1, 1), null, new[] { F(DayOfWeek.Monday, 8, 12), F(DayOfWeek.Monday, 14, 18) }) };

        var libre = AgendaCalculo.Dia(lunes, vig, Array.Empty<AgendaBloqueo>(), null);
        Assert.Equal(16, libre.CuposTotales);
        Assert.Equal(16, libre.CuposDisponibles);

        // Capacitacion 14:00-18:00: bloquea los 8 bloques de la tarde.
        var parcial = AgendaCalculo.Dia(lunes, vig, new[] { new AgendaBloqueo(lunes, lunes, new TimeOnly(14, 0), new TimeOnly(18, 0), "Capacitacion") }, null);
        Assert.Equal(8, parcial.CuposBloqueados);
        Assert.Equal(8, parcial.CuposDisponibles);
        Assert.Equal(new[] { "Capacitacion" }, parcial.Novedades);

        // Tramo que solo roza un bloque (11:45-12:00) bloquea ese bloque (11:30-12:00).
        var roce = AgendaCalculo.Dia(lunes, vig, new[] { new AgendaBloqueo(lunes, lunes, new TimeOnly(11, 45), new TimeOnly(12, 0), "Permiso") }, null);
        Assert.Equal(1, roce.CuposBloqueados);

        // Vacaciones de dia completo y festivo cierran todo.
        Assert.Equal(0, AgendaCalculo.Dia(lunes, vig, new[] { new AgendaBloqueo(lunes.AddDays(-2), lunes.AddDays(3), null, null, "Vacaciones") }, null).CuposDisponibles);
        var festivo = AgendaCalculo.Dia(lunes, vig, Array.Empty<AgendaBloqueo>(), "Dia de la Independencia de Cartagena");
        Assert.Equal(0, festivo.CuposDisponibles);
        Assert.Equal("Dia de la Independencia de Cartagena", festivo.Cierre);

        // Sin vigencia: cerrado con motivo.
        Assert.Equal("Sin agenda vigente", AgendaCalculo.Dia(new DateOnly(2025, 1, 6), vig, Array.Empty<AgendaBloqueo>(), null).Cierre);
    }

    [Fact]
    public void LunesDe_YMismasFranjas()
    {
        Assert.Equal(new DateOnly(2026, 11, 16), AgendaCalculo.LunesDe(new DateOnly(2026, 11, 22))); // domingo
        Assert.Equal(new DateOnly(2026, 11, 16), AgendaCalculo.LunesDe(new DateOnly(2026, 11, 16)));
        Assert.True(AgendaCalculo.MismasFranjas(
            new[] { F(DayOfWeek.Monday, 8, 12), F(DayOfWeek.Tuesday, 8, 12) },
            new[] { F(DayOfWeek.Tuesday, 8, 12), F(DayOfWeek.Monday, 8, 12) }));
        Assert.False(AgendaCalculo.MismasFranjas(new[] { F(DayOfWeek.Monday, 8, 12) }, new[] { F(DayOfWeek.Monday, 8, 12, cupo: 2) }));
    }
}
