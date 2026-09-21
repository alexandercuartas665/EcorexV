using Ecorex.Application.Workflows;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Calculo de vencimientos por plazo de paso (Fase 1). Dias calendario vs habiles (saltando fines de semana +
/// dias no operativos del tenant), horas/minutos como tiempo real.
/// </summary>
public class StepDeadlineCalculatorTests
{
    private static DateTime D(int y, int mo, int d, int h = 0, int mi = 0) => new(y, mo, d, h, mi, 0, DateTimeKind.Unspecified);

    [Fact]
    public void Read_build_roundtrip()
    {
        var json = StepSla.Build(2, 4, 30, StepSlaDayMode.Business);
        Assert.NotNull(json);
        var sla = StepSla.Read(json);
        Assert.Equal(2, sla.Days);
        Assert.Equal(4, sla.Hours);
        Assert.Equal(30, sla.Minutes);
        Assert.Equal(StepSlaDayMode.Business, sla.DayMode);
        Assert.False(sla.IsEmpty);
    }

    [Fact]
    public void Build_null_si_vacio_y_Read_none()
    {
        Assert.Null(StepSla.Build(0, 0, 0, StepSlaDayMode.Business));
        Assert.True(StepSla.Read(null).IsEmpty);
        Assert.True(StepSla.Read("no es json").IsEmpty);
    }

    [Fact]
    public void Calendario_suma_dias_corridos()
    {
        // Viernes + 2 dias calendario + 4h = domingo, sin saltar nada.
        var start = D(2026, 9, 18, 16, 0); // viernes 4pm
        var due = StepDeadlineCalculator.AddPlazo(start, StepSla.Read(StepSla.Build(2, 4, 0, StepSlaDayMode.Calendar)));
        Assert.Equal(D(2026, 9, 20, 20, 0), due); // domingo 8pm
    }

    [Fact]
    public void Habil_salta_fin_de_semana_y_festivo_ejemplo_del_usuario()
    {
        // Viernes 4pm + 2 dias HABILES con el LUNES festivo -> salta sab, dom, lunes -> miercoles; +4h = 8pm.
        var start = D(2026, 9, 18, 16, 0);      // viernes 2026-09-18
        var festivos = new HashSet<DateOnly> { new(2026, 9, 21) }; // lunes festivo
        var sla = StepSla.Read(StepSla.Build(2, 4, 0, StepSlaDayMode.Business));
        var due = StepDeadlineCalculator.AddPlazo(start, sla, festivos);
        Assert.Equal(D(2026, 9, 23, 20, 0), due); // miercoles 8pm
    }

    [Fact]
    public void Habil_sin_festivos_solo_salta_fin_de_semana()
    {
        // Viernes + 1 dia habil = lunes (salta sab/dom).
        var due = StepDeadlineCalculator.AddPlazo(D(2026, 9, 18, 9, 0),
            StepSla.Read(StepSla.Build(1, 0, 0, StepSlaDayMode.Business)));
        Assert.Equal(D(2026, 9, 21, 9, 0), due); // lunes 9am
    }

    [Fact]
    public void Horas_minutos_son_tiempo_real_sin_dias()
    {
        var due = StepDeadlineCalculator.AddPlazo(D(2026, 9, 18, 22, 30),
            StepSla.Read(StepSla.Build(0, 3, 45, StepSlaDayMode.Business)));
        Assert.Equal(D(2026, 9, 19, 2, 15), due); // cruza medianoche, es tiempo real
    }

    [Fact]
    public void AddPlazos_encadena_para_el_estimado_total()
    {
        // 3 pasos: 1 dia habil + (0) + 2 horas, desde jueves 9am (sin festivos).
        var start = D(2026, 9, 17, 9, 0); // jueves
        var slas = new[]
        {
            StepSla.Read(StepSla.Build(1, 0, 0, StepSlaDayMode.Business)), // -> viernes 9am
            StepSla.None,                                                  // sin plazo, se ignora
            StepSla.Read(StepSla.Build(0, 2, 0, StepSlaDayMode.Calendar)), // -> viernes 11am
        };
        var end = StepDeadlineCalculator.AddPlazos(start, slas);
        Assert.Equal(D(2026, 9, 18, 11, 0), end);
    }

    [Fact]
    public void IsWorkingDay_respeta_fin_de_semana_y_no_operativos()
    {
        var nonWorking = new HashSet<DateOnly> { new(2026, 9, 21) };
        var wk = StepDeadlineCalculator.DefaultWeekend;
        Assert.True(StepDeadlineCalculator.IsWorkingDay(D(2026, 9, 18), nonWorking, wk));  // viernes
        Assert.False(StepDeadlineCalculator.IsWorkingDay(D(2026, 9, 19), nonWorking, wk)); // sabado
        Assert.False(StepDeadlineCalculator.IsWorkingDay(D(2026, 9, 21), nonWorking, wk)); // festivo
    }
}
