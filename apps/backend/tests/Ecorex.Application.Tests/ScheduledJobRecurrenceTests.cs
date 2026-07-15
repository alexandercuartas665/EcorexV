using Ecorex.Application.Scheduling;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests del motor de recurrencia del modulo 000889 (ola P2, doc D4). Es PURO (sin BD ni reloj propio),
/// asi que se prueba a fondo y rapido: las 4 frecuencias del prototipo, intervalos, dias marcados,
/// ordinal mensual, repeticion intradia, vigencia y la aritmetica EN LA ZONA DEL TENANT (regla 9).
/// </summary>
public class ScheduledJobRecurrenceTests
{
    // Bogota = UTC-5 todo el ano (sin DST): 08:00 local == 13:00 UTC.
    private static readonly TimeZoneInfo Bogota = ScheduledJobRecurrence.ResolveTimeZone("America/Bogota");

    private static DateTimeOffset Utc(int y, int m, int d, int h = 0, int min = 0)
        => new(new DateTime(y, m, d, h, min, 0, DateTimeKind.Utc));

    [Fact]
    public void Daily_EveryDay_FiresAtLocalTime_ConvertedToUtc()
    {
        var rule = new ScheduledJobRule
        {
            Frequency = ScheduledJobFrequency.Daily,
            IntervalNum = 1,
            AtTime = "08:00",
            ValidFrom = new DateOnly(2026, 7, 1),
        };

        // Antes de la hora del dia 1 -> dispara ese mismo dia a las 08:00 Bogota = 13:00 UTC.
        var next = ScheduledJobRecurrence.ComputeNextRun(rule, Utc(2026, 7, 1, 0, 0), Bogota);
        Assert.Equal(Utc(2026, 7, 1, 13, 0), next);

        // Justo despues del disparo -> la siguiente es el dia 2 a la misma hora local.
        var after = ScheduledJobRecurrence.ComputeNextRun(rule, Utc(2026, 7, 1, 13, 0), Bogota);
        Assert.Equal(Utc(2026, 7, 2, 13, 0), after);
    }

    [Fact]
    public void Daily_EveryThreeDays_RespectsInterval()
    {
        var rule = new ScheduledJobRule
        {
            Frequency = ScheduledJobFrequency.Daily,
            IntervalNum = 3,
            AtTime = "06:00",
            ValidFrom = new DateOnly(2026, 7, 1),
        };
        // Ancla = 1-jul. Dispara 1, 4, 7...
        Assert.Equal(Utc(2026, 7, 1, 11, 0), ScheduledJobRecurrence.ComputeNextRun(rule, Utc(2026, 7, 1), Bogota));
        Assert.Equal(Utc(2026, 7, 4, 11, 0), ScheduledJobRecurrence.ComputeNextRun(rule, Utc(2026, 7, 1, 11, 0), Bogota));
        Assert.Equal(Utc(2026, 7, 7, 11, 0), ScheduledJobRecurrence.ComputeNextRun(rule, Utc(2026, 7, 4, 11, 0), Bogota));
    }

    [Fact]
    public void Weekly_OnlyMarkedWeekdays()
    {
        // 2026-07-01 es MIERCOLES. Regla: lunes y miercoles.
        var rule = new ScheduledJobRule
        {
            Frequency = ScheduledJobFrequency.Weekly,
            IntervalNum = 1,
            Weekdays = "Lun,Mie",
            AtTime = "08:00",
            ValidFrom = new DateOnly(2026, 7, 1),
        };

        var first = ScheduledJobRecurrence.ComputeNextRun(rule, Utc(2026, 7, 1), Bogota);
        Assert.Equal(Utc(2026, 7, 1, 13, 0), first); // miercoles 1

        var second = ScheduledJobRecurrence.ComputeNextRun(rule, first!.Value, Bogota);
        Assert.Equal(Utc(2026, 7, 6, 13, 0), second); // lunes 6 (salta jue/vie/sab/dom)

        var third = ScheduledJobRecurrence.ComputeNextRun(rule, second!.Value, Bogota);
        Assert.Equal(Utc(2026, 7, 8, 13, 0), third); // miercoles 8
    }

    [Fact]
    public void Weekly_EveryTwoWeeks_SkipsTheOddWeek()
    {
        var rule = new ScheduledJobRule
        {
            Frequency = ScheduledJobFrequency.Weekly,
            IntervalNum = 2,
            Weekdays = "Lun",
            AtTime = "08:00",
            ValidFrom = new DateOnly(2026, 7, 6), // lunes
        };
        var first = ScheduledJobRecurrence.ComputeNextRun(rule, Utc(2026, 7, 6), Bogota);
        Assert.Equal(Utc(2026, 7, 6, 13, 0), first);

        // La siguiente NO es el 13 (semana impar) sino el 20.
        var second = ScheduledJobRecurrence.ComputeNextRun(rule, first!.Value, Bogota);
        Assert.Equal(Utc(2026, 7, 20, 13, 0), second);
    }

    [Fact]
    public void Monthly_FirstMonday_AndLastFriday()
    {
        var primerLunes = new ScheduledJobRule
        {
            Frequency = ScheduledJobFrequency.Monthly,
            IntervalNum = 1,
            MonthOrdinal = "Primer",
            MonthWeekday = "Lunes",
            AtTime = "09:00",
            ValidFrom = new DateOnly(2026, 7, 1),
        };
        // Primer lunes de julio 2026 = 6.
        Assert.Equal(Utc(2026, 7, 6, 14, 0), ScheduledJobRecurrence.ComputeNextRun(primerLunes, Utc(2026, 7, 1), Bogota));
        // Primer lunes de agosto 2026 = 3.
        Assert.Equal(Utc(2026, 8, 3, 14, 0), ScheduledJobRecurrence.ComputeNextRun(primerLunes, Utc(2026, 7, 6, 14, 0), Bogota));

        var ultimoViernes = new ScheduledJobRule
        {
            Frequency = ScheduledJobFrequency.Monthly,
            IntervalNum = 1,
            MonthOrdinal = "Ultimo",
            MonthWeekday = "Viernes",
            AtTime = "09:00",
            ValidFrom = new DateOnly(2026, 7, 1),
        };
        // Ultimo viernes de julio 2026 = 31.
        Assert.Equal(Utc(2026, 7, 31, 14, 0), ScheduledJobRecurrence.ComputeNextRun(ultimoViernes, Utc(2026, 7, 1), Bogota));
    }

    [Fact]
    public void Monthly_LastDayOfMonth_UsesDia()
    {
        // "El ultimo dia" = dia natural, no dia de semana. Febrero 2027 tiene 28.
        var rule = new ScheduledJobRule
        {
            Frequency = ScheduledJobFrequency.Monthly,
            IntervalNum = 1,
            MonthOrdinal = "Ultimo",
            MonthWeekday = "dia",
            AtTime = "23:00",
            ValidFrom = new DateOnly(2027, 2, 1),
        };
        Assert.Equal(Utc(2027, 3, 1, 4, 0), ScheduledJobRecurrence.ComputeNextRun(rule, Utc(2027, 2, 1), Bogota));
    }

    [Fact]
    public void Once_FiresOnlyOnce_AndThenNever()
    {
        var rule = new ScheduledJobRule
        {
            Frequency = ScheduledJobFrequency.Once,
            AtTime = "10:00",
            ValidFrom = new DateOnly(2026, 7, 30), // "fecha especifica"
        };
        var first = ScheduledJobRecurrence.ComputeNextRun(rule, Utc(2026, 7, 1), Bogota);
        Assert.Equal(Utc(2026, 7, 30, 15, 0), first);

        // Ya disparada: no vuelve.
        Assert.Null(ScheduledJobRecurrence.ComputeNextRun(rule, first!.Value, Bogota));
    }

    [Fact]
    public void RepeatIntraday_FiresEveryNHours_WithinTheWindow()
    {
        var rule = new ScheduledJobRule
        {
            Frequency = ScheduledJobFrequency.Daily,
            IntervalNum = 1,
            RepeatIntraday = true,
            RepeatEveryHours = 4,
            RepeatFrom = "08:00",
            RepeatTo = "16:00",
            ValidFrom = new DateOnly(2026, 7, 1),
        };
        // Ventana local 08:00, 12:00, 16:00 -> UTC 13:00, 17:00, 21:00.
        var t1 = ScheduledJobRecurrence.ComputeNextRun(rule, Utc(2026, 7, 1), Bogota);
        Assert.Equal(Utc(2026, 7, 1, 13, 0), t1);
        var t2 = ScheduledJobRecurrence.ComputeNextRun(rule, t1!.Value, Bogota);
        Assert.Equal(Utc(2026, 7, 1, 17, 0), t2);
        var t3 = ScheduledJobRecurrence.ComputeNextRun(rule, t2!.Value, Bogota);
        Assert.Equal(Utc(2026, 7, 1, 21, 0), t3);
        // Pasado el fin de la ventana, salta al dia siguiente a las 08:00 locales.
        var t4 = ScheduledJobRecurrence.ComputeNextRun(rule, t3!.Value, Bogota);
        Assert.Equal(Utc(2026, 7, 2, 13, 0), t4);
    }

    [Fact]
    public void Vigencia_StopsAfterValidTo_AndDoesNotStartBeforeValidFrom()
    {
        var rule = new ScheduledJobRule
        {
            Frequency = ScheduledJobFrequency.Daily,
            IntervalNum = 1,
            AtTime = "08:00",
            ValidFrom = new DateOnly(2026, 7, 10),
            ValidTo = new DateOnly(2026, 7, 11),
        };
        // Antes del inicio: la primera ejecucion es el 10, no antes.
        Assert.Equal(Utc(2026, 7, 10, 13, 0), ScheduledJobRecurrence.ComputeNextRun(rule, Utc(2026, 7, 1), Bogota));
        // Ultimo dia valido.
        Assert.Equal(Utc(2026, 7, 11, 13, 0), ScheduledJobRecurrence.ComputeNextRun(rule, Utc(2026, 7, 10, 13, 0), Bogota));
        // Pasada la vigencia: no hay mas.
        Assert.Null(ScheduledJobRecurrence.ComputeNextRun(rule, Utc(2026, 7, 11, 13, 0), Bogota));
    }

    [Fact]
    public void TimeZone_IsTheTenants_NotTheServers()
    {
        // La MISMA regla ("08:00") da instantes UTC distintos segun la zona del tenant.
        var rule = new ScheduledJobRule
        {
            Frequency = ScheduledJobFrequency.Daily,
            IntervalNum = 1,
            AtTime = "08:00",
            ValidFrom = new DateOnly(2026, 7, 1),
        };
        var bogota = ScheduledJobRecurrence.ComputeNextRun(rule, Utc(2026, 7, 1), Bogota);
        var madrid = ScheduledJobRecurrence.ComputeNextRun(rule, Utc(2026, 7, 1), ScheduledJobRecurrence.ResolveTimeZone("Europe/Madrid"));

        Assert.Equal(Utc(2026, 7, 1, 13, 0), bogota);  // UTC-5
        Assert.Equal(Utc(2026, 7, 1, 6, 0), madrid);   // UTC+2 en verano
        Assert.NotEqual(bogota, madrid);
    }

    [Fact]
    public void Weekly_WithoutMarkedDays_NeverFires_InsteadOfLoopingForever()
    {
        var rule = new ScheduledJobRule
        {
            Frequency = ScheduledJobFrequency.Weekly,
            IntervalNum = 1,
            Weekdays = null, // el usuario no marco ningun dia
            AtTime = "08:00",
            ValidFrom = new DateOnly(2026, 7, 1),
        };
        Assert.Null(ScheduledJobRecurrence.ComputeNextRun(rule, Utc(2026, 7, 1), Bogota));
    }

    [Fact]
    public void ResolveTimeZone_FallsBackToDefault_WhenUnknown()
    {
        var tz = ScheduledJobRecurrence.ResolveTimeZone("Zona/Inexistente");
        var bogota = ScheduledJobRecurrence.ResolveTimeZone("America/Bogota");
        Assert.Equal(bogota.BaseUtcOffset, tz.BaseUtcOffset);
    }
}
