using Ecorex.Application.Tenancy;

namespace Ecorex.Application.Tests;

/// <summary>
/// Verifica el calculo de festivos nacionales de Colombia (Ley Emiliani + Pascua): que haya 18, que los fijos
/// caigan en su fecha, que los Emiliani queden SIEMPRE en lunes y que los de Pascua se desplacen bien respecto
/// al Domingo de Resurreccion (2026 = 5 de abril).
/// </summary>
public class ColombianHolidaysTests
{
    [Fact]
    public void ForYear_Devuelve18FestivosOrdenados()
    {
        var days = ColombianHolidays.ForYear(2026);
        Assert.Equal(18, days.Count);
        for (var i = 1; i < days.Count; i++)
        {
            Assert.True(days[i].Date >= days[i - 1].Date, "deben venir ordenados por fecha");
        }
    }

    [Fact]
    public void Fijos_CaenEnSuFecha()
    {
        var days = ColombianHolidays.ForYear(2026);
        Assert.Contains(days, d => d.Date == new DateOnly(2026, 1, 1) && d.Name == "Ano Nuevo");
        Assert.Contains(days, d => d.Date == new DateOnly(2026, 5, 1));
        Assert.Contains(days, d => d.Date == new DateOnly(2026, 7, 20));
        Assert.Contains(days, d => d.Date == new DateOnly(2026, 8, 7));
        Assert.Contains(days, d => d.Date == new DateOnly(2026, 12, 8));
        Assert.Contains(days, d => d.Date == new DateOnly(2026, 12, 25));
    }

    [Fact]
    public void Emiliani_SiempreCaenEnLunes()
    {
        string[] emiliani =
        {
            "Reyes Magos", "San Jose", "San Pedro y San Pablo", "Asuncion de la Virgen",
            "Dia de la Raza", "Todos los Santos", "Independencia de Cartagena",
            "Ascension del Senor", "Corpus Christi", "Sagrado Corazon de Jesus"
        };
        var days = ColombianHolidays.ForYear(2026);
        foreach (var name in emiliani)
        {
            var h = Assert.Single(days, d => d.Name == name);
            Assert.Equal(DayOfWeek.Monday, h.Date.DayOfWeek);
        }
    }

    [Fact]
    public void Pascua2026_JuevesYViernesSanto_Y_Movibles()
    {
        // Domingo de Resurreccion 2026 = 5 de abril.
        var days = ColombianHolidays.ForYear(2026);
        Assert.Contains(days, d => d.Date == new DateOnly(2026, 4, 2) && d.Name == "Jueves Santo");
        Assert.Contains(days, d => d.Date == new DateOnly(2026, 4, 3) && d.Name == "Viernes Santo");
        Assert.Contains(days, d => d.Date == new DateOnly(2026, 5, 18) && d.Name == "Ascension del Senor");
        Assert.Contains(days, d => d.Date == new DateOnly(2026, 6, 8) && d.Name == "Corpus Christi");
        Assert.Contains(days, d => d.Date == new DateOnly(2026, 6, 15) && d.Name == "Sagrado Corazon de Jesus");
    }

    [Fact]
    public void Reyes2026_SeTrasladaAlLunes12()
    {
        // 6 de enero de 2026 es martes -> lunes siguiente = 12 de enero.
        var days = ColombianHolidays.ForYear(2026);
        Assert.Contains(days, d => d.Date == new DateOnly(2026, 1, 12) && d.Name == "Reyes Magos");
    }
}
