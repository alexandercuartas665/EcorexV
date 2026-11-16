using Ecorex.Application.DataContainers;

namespace Ecorex.Application.Tests;

/// <summary>
/// Desencadenador estilo Programador de tareas de Windows (ADR-0171): una vez, cada N dias, cada N semanas con
/// dias, mensual por dias/ultimo/ordinal, repeticion en el dia, caducidad y zona del tenant.
/// </summary>
public class ScheduleTriggerTests
{
    // Bogota: UTC-5 sin horario de verano (calculos deterministas).
    private static readonly TimeZoneInfo Bog = TimeZoneInfo.CreateCustomTimeZone("bog-test", TimeSpan.FromHours(-5), "bog", "bog");

    private static DateTimeOffset Utc(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0, TimeSpan.Zero);
    private static DateTimeOffset Local(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0, TimeSpan.FromHours(-5));

    [Fact]
    public void Diaria_CadaDia_DevuelveLaHoraLocalEnUtc()
    {
        var t = new ScheduleTrigger { Tipo = TriggerTipo.Diaria, Inicio = new DateTime(2026, 11, 1, 6, 0, 0) };

        var n = t.NextAfter(Local(2026, 11, 16, 7, 0), Bog);

        Assert.Equal(Local(2026, 11, 17, 6, 0), n);
        Assert.Equal(TimeSpan.Zero, n!.Value.Offset); // Npgsql exige offset 0
    }

    [Fact]
    public void Diaria_CadaTresDias_RespetaElAncla()
    {
        var t = new ScheduleTrigger { Tipo = TriggerTipo.Diaria, Cada = 3, Inicio = new DateTime(2026, 11, 1, 6, 0, 0) };

        // 1, 4, 7, 10, 13, 16, 19 ...
        Assert.Equal(Local(2026, 11, 19, 6, 0), t.NextAfter(Local(2026, 11, 16, 7, 0), Bog));
        Assert.Equal(Local(2026, 11, 16, 6, 0), t.NextAfter(Local(2026, 11, 15, 0, 0), Bog));
    }

    [Fact]
    public void NoCorreAntesDelInicio()
    {
        var t = new ScheduleTrigger { Tipo = TriggerTipo.Diaria, Inicio = new DateTime(2026, 12, 1, 6, 0, 0) };

        Assert.Equal(Local(2026, 12, 1, 6, 0), t.NextAfter(Local(2026, 11, 16, 7, 0), Bog));
    }

    [Fact]
    public void Semanal_CadaDosSemanas_LunesYJueves()
    {
        // Inicio lunes 2026-11-02. Semanas validas: la del 2, la del 16, la del 30...
        var t = new ScheduleTrigger
        {
            Tipo = TriggerTipo.Semanal, Cada = 2, Dias = new[] { 1, 4 }, Inicio = new DateTime(2026, 11, 2, 1, 0, 0),
        };

        var proximas = t.NextLocal(Local(2026, 11, 2, 2, 0), Bog, 4);

        Assert.Equal(new[]
        {
            new DateTime(2026, 11, 5, 1, 0, 0),  // jueves misma semana
            new DateTime(2026, 11, 16, 1, 0, 0), // lunes 2 semanas despues
            new DateTime(2026, 11, 19, 1, 0, 0),
            new DateTime(2026, 11, 30, 1, 0, 0),
        }, proximas);
    }

    [Fact]
    public void Mensual_UltimoDiaDelMes_SoloMesesElegidos()
    {
        var t = new ScheduleTrigger
        {
            Tipo = TriggerTipo.Mensual, Meses = new[] { 2, 11 }, DiasMes = new[] { ScheduleTrigger.UltimoDia },
            Inicio = new DateTime(2026, 1, 1, 23, 0, 0),
        };

        var p = t.NextLocal(Local(2026, 11, 16, 0, 0), Bog, 2);

        Assert.Equal(new[] { new DateTime(2026, 11, 30, 23, 0, 0), new DateTime(2027, 2, 28, 23, 0, 0) }, p);
    }

    [Fact]
    public void Mensual_PrimerLunes_YUltimoViernes()
    {
        var primerLunes = new ScheduleTrigger
        {
            Tipo = TriggerTipo.Mensual, PorDiaDelMes = false, Ordinal = 1, DiaSemana = 1, Inicio = new DateTime(2026, 1, 1, 8, 0, 0),
        };
        var ultimoViernes = primerLunes with { Ordinal = 5, DiaSemana = 5 };

        Assert.Equal(Local(2026, 12, 7, 8, 0), primerLunes.NextAfter(Local(2026, 11, 16, 0, 0), Bog));
        Assert.Equal(Local(2026, 11, 27, 8, 0), ultimoViernes.NextAfter(Local(2026, 11, 16, 0, 0), Bog));
    }

    [Fact]
    public void UnaVez_DespuesDeCorrer_NoTieneMas()
    {
        var t = new ScheduleTrigger { Tipo = TriggerTipo.UnaVez, Inicio = new DateTime(2026, 11, 20, 1, 0, 0) };

        Assert.Equal(Local(2026, 11, 20, 1, 0), t.NextAfter(Local(2026, 11, 16, 0, 0), Bog));
        Assert.Null(t.NextAfter(Local(2026, 11, 20, 1, 0), Bog));
    }

    [Fact]
    public void Repeticion_CadaDosHorasDuranteSeis()
    {
        var t = new ScheduleTrigger
        {
            Tipo = TriggerTipo.Diaria, Inicio = new DateTime(2026, 11, 1, 8, 0, 0), RepetirCadaMin = 120, RepetirDuranteMin = 360,
        };

        var p = t.NextLocal(Local(2026, 11, 16, 7, 0), Bog, 4);

        // 8, 10, 12 (14 queda fuera: durante 6 h es exclusivo) y al dia siguiente 8.
        Assert.Equal(new[]
        {
            new DateTime(2026, 11, 16, 8, 0, 0), new DateTime(2026, 11, 16, 10, 0, 0),
            new DateTime(2026, 11, 16, 12, 0, 0), new DateTime(2026, 11, 17, 8, 0, 0),
        }, p);
    }

    [Fact]
    public void Caducidad_CortaLasEjecuciones()
    {
        var t = new ScheduleTrigger
        {
            Tipo = TriggerTipo.Diaria, Inicio = new DateTime(2026, 11, 1, 6, 0, 0), Caduca = new DateTime(2026, 11, 17, 12, 0, 0),
        };

        Assert.Equal(Local(2026, 11, 17, 6, 0), t.NextAfter(Local(2026, 11, 16, 7, 0), Bog));
        Assert.Null(t.NextAfter(Local(2026, 11, 17, 6, 0), Bog));
    }

    [Theory]
    [InlineData(TriggerTipo.Semanal, 1, false)]   // semanal sin dias
    [InlineData(TriggerTipo.Diaria, 0, true)]     // cada 0 dias
    [InlineData(TriggerTipo.Semanal, 60, true)]   // cada 60 semanas
    public void Validate_RechazaConfiguracionesInvalidas(TriggerTipo tipo, int cada, bool conDias)
    {
        var t = new ScheduleTrigger
        {
            Tipo = tipo, Cada = cada, Dias = conDias ? new[] { 1 } : Array.Empty<int>(), Inicio = new DateTime(2026, 11, 1, 6, 0, 0),
        };

        Assert.NotNull(t.Validate());
    }

    [Fact]
    public void Validate_CaducaAntesDelInicio_EsInvalido()
    {
        var t = new ScheduleTrigger { Inicio = new DateTime(2026, 11, 10), Caduca = new DateTime(2026, 11, 1) };

        Assert.NotNull(t.Validate());
    }

    [Fact]
    public void Json_RoundTrip_YDescripcion()
    {
        var t = new ScheduleTrigger
        {
            Tipo = TriggerTipo.Semanal, Cada = 1, Dias = new[] { 0 }, Inicio = new DateTime(2026, 11, 16, 1, 0, 0),
        };

        var back = ScheduleTrigger.Parse(t.ToJson());

        Assert.NotNull(back);
        Assert.Equal(TriggerTipo.Semanal, back!.Tipo);
        Assert.Equal(new[] { 0 }, back.Dias);
        Assert.Equal(t.Inicio, back.Inicio);
        Assert.Equal("Cada semana el domingo a las 01:00 (desde el 16/11/2026)", back.Describe());
        Assert.Equal("0 1 * * 0", back.ToCronApprox());
    }

    [Fact]
    public void ImportRecurrence_UsaElDesencadenadorYMarcaTerminada()
    {
        var p = new Ecorex.Domain.Entities.ImportProcess
        {
            Name = "x", IsActive = true, ScheduleKind = Ecorex.Domain.Enums.ImportScheduleKind.Cron, CronExpression = "0 0 * * *",
            TriggerJson = new ScheduleTrigger { Tipo = TriggerTipo.UnaVez, Inicio = new DateTime(2026, 11, 20, 1, 0, 0) }.ToJson(),
        };

        var r1 = ImportRecurrence.ComputeNextRun(p, Local(2026, 11, 16, 0, 0), Bog);
        var r2 = ImportRecurrence.ComputeNextRun(p, Local(2026, 11, 21, 0, 0), Bog);

        Assert.Equal(Local(2026, 11, 20, 1, 0), r1.NextRunAt);
        Assert.Equal(ImportScheduleProblem.Finished, r2.Problem);
        Assert.Null(r2.NextRunAt);
    }
}
