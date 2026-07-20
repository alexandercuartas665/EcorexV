using Ecorex.Application.DataContainers;
using Ecorex.Application.Scheduling;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests del motor de recurrencia de las importaciones (ADR-0041). Es PURO (sin BD ni reloj propio),
/// asi que se prueba a fondo y rapido: intervalo, cron, la zona del tenant (regla 9) y los dos casos
/// que mas duelen en produccion: la rafaga tras una caida y el horario invalido.
/// </summary>
public class ImportRecurrenceTests
{
    // Bogota = UTC-5 todo el ano (sin DST).
    private static readonly TimeZoneInfo Bogota = ScheduledJobRecurrence.ResolveTimeZone("America/Bogota");

    private static DateTimeOffset Utc(int y, int m, int d, int h = 0, int min = 0, int s = 0)
        => new(new DateTime(y, m, d, h, min, s, DateTimeKind.Utc));

    private static ImportProcess Proc(ImportScheduleKind kind, int? interval = null, string? cron = null,
        DateTimeOffset? lastRun = null, bool active = true) => new()
        {
            Name = "p",
            ScheduleKind = kind,
            IntervalMinutes = interval,
            CronExpression = cron,
            LastRunAt = lastRun,
            IsActive = active
        };

    [Fact]
    public void Manual_no_se_programa()
    {
        var r = ImportRecurrence.ComputeNextRun(Proc(ImportScheduleKind.Manual), Utc(2026, 7, 17, 10), Bogota);

        Assert.Null(r.NextRunAt);
        Assert.Equal(ImportScheduleProblem.NotScheduled, r.Problem);
    }

    [Fact]
    public void Inactiva_no_se_programa_aunque_tenga_horario()
    {
        var r = ImportRecurrence.ComputeNextRun(
            Proc(ImportScheduleKind.Interval, interval: 15, active: false), Utc(2026, 7, 17, 10), Bogota);

        Assert.Null(r.NextRunAt);
        Assert.Equal(ImportScheduleProblem.NotScheduled, r.Problem);
    }

    [Fact]
    public void Intervalo_se_ancla_a_la_ultima_corrida_y_no_a_ahora()
    {
        var last = Utc(2026, 7, 17, 10, 0);

        // Han pasado 3 minutos desde la corrida.
        var r = ImportRecurrence.ComputeNextRun(
            Proc(ImportScheduleKind.Interval, interval: 15, lastRun: last), last.AddMinutes(3), Bogota);

        // 10:15, no 10:18: si contara desde "ahora", "cada 15 minutos" se iria corriendo hacia
        // adelante segun lo que tarde cada refresco.
        Assert.Equal(Utc(2026, 7, 17, 10, 15), r.NextRunAt);
    }

    [Fact]
    public void Intervalo_sin_corridas_previas_arranca_desde_ahora()
    {
        var now = Utc(2026, 7, 17, 10, 0);

        var r = ImportRecurrence.ComputeNextRun(Proc(ImportScheduleKind.Interval, interval: 5), now, Bogota);

        Assert.Equal(Utc(2026, 7, 17, 10, 5), r.NextRunAt);
    }

    [Fact]
    public void Intervalo_tras_una_caida_larga_NO_dispara_la_rafaga_atrasada()
    {
        // Servidor caido 3 horas con refresco cada 15 minutos: 12 ventanas perdidas.
        var last = Utc(2026, 7, 17, 10, 0);
        var now = last.AddHours(3);

        var r = ImportRecurrence.ComputeNextRun(
            Proc(ImportScheduleKind.Interval, interval: 15, lastRun: last), now, Bogota);

        // Salta al FUTURO en vez de devolver 10:15 y provocar 12 corridas seguidas: a nadie le sirve
        // recibir los refrescos que no ocurrieron anoche.
        Assert.Equal(Utc(2026, 7, 17, 13, 15), r.NextRunAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-5)]
    public void Intervalo_invalido_se_reporta_como_Invalid(int? minutes)
    {
        var r = ImportRecurrence.ComputeNextRun(
            Proc(ImportScheduleKind.Interval, interval: minutes), Utc(2026, 7, 17, 10), Bogota);

        Assert.Null(r.NextRunAt);
        Assert.Equal(ImportScheduleProblem.Invalid, r.Problem);
        Assert.False(string.IsNullOrWhiteSpace(r.Reason));
    }

    [Fact]
    public void Cron_se_interpreta_en_la_hora_del_TENANT_no_en_UTC()
    {
        // "0 3 * * *" = 3 de la madrugada. Bogota es UTC-5 -> 08:00 UTC.
        // Ahora: 17 jul 00:00 UTC = 16 jul 19:00 en Bogota.
        var r = ImportRecurrence.ComputeNextRun(
            Proc(ImportScheduleKind.Cron, cron: "0 3 * * *"), Utc(2026, 7, 17, 0, 0), Bogota);

        Assert.Equal(ImportScheduleProblem.None, r.Problem);
        Assert.Equal(Utc(2026, 7, 17, 8, 0), r.NextRunAt);
    }

    /// <summary>
    /// Regresion de un bug REAL que las demas pruebas no podian ver: Cronos devuelve el instante con
    /// el desfase de la zona pedida (-05:00 en Bogota), y Npgsql RECHAZA guardar un `timestamptz` que
    /// no venga con desfase 0 -> guardar una programacion cron reventaba al escribir en la BD.
    ///
    /// Los Assert.Equal de arriba pasaban igual, porque DateTimeOffset compara INSTANTES y
    /// 03:00-05:00 == 08:00Z. Por eso esta prueba afirma sobre el .Offset, que es lo unico que
    /// distingue el caso.
    /// </summary>
    [Fact]
    public void Cron_devuelve_la_hora_en_UTC_puro_porque_la_BD_no_acepta_otro_desfase()
    {
        var r = ImportRecurrence.ComputeNextRun(
            Proc(ImportScheduleKind.Cron, cron: "0 3 * * *"), Utc(2026, 7, 17, 0, 0), Bogota);

        Assert.NotNull(r.NextRunAt);
        Assert.Equal(TimeSpan.Zero, r.NextRunAt!.Value.Offset);
    }

    [Fact]
    public void Intervalo_tambien_devuelve_UTC_puro()
    {
        // El ancla puede venir con cualquier desfase (aqui -05:00); la salida debe salir normalizada.
        var last = new DateTimeOffset(2026, 7, 17, 5, 0, 0, TimeSpan.FromHours(-5));

        var r = ImportRecurrence.ComputeNextRun(
            Proc(ImportScheduleKind.Interval, interval: 15, lastRun: last), last.AddMinutes(1), Bogota);

        Assert.NotNull(r.NextRunAt);
        Assert.Equal(TimeSpan.Zero, r.NextRunAt!.Value.Offset);
    }

    [Fact]
    public void Cron_cada_cinco_minutos_da_el_siguiente_multiplo()
    {
        var r = ImportRecurrence.ComputeNextRun(
            Proc(ImportScheduleKind.Cron, cron: "*/5 * * * *"), Utc(2026, 7, 17, 10, 2, 30), Bogota);

        Assert.Equal(Utc(2026, 7, 17, 10, 5), r.NextRunAt);
    }

    [Fact]
    public void Cron_invalido_se_reporta_con_motivo_en_vez_de_reventar()
    {
        var r = ImportRecurrence.ComputeNextRun(
            Proc(ImportScheduleKind.Cron, cron: "esto no es un cron"), Utc(2026, 7, 17, 10), Bogota);

        Assert.Null(r.NextRunAt);
        Assert.Equal(ImportScheduleProblem.Invalid, r.Problem);
        // El motivo es lo que acaba viendo el operador en "Desactivada: ...".
        Assert.Contains("Cron invalido", r.Reason);
    }

    [Fact]
    public void Cron_vacio_es_Invalid()
    {
        var r = ImportRecurrence.ComputeNextRun(
            Proc(ImportScheduleKind.Cron, cron: "   "), Utc(2026, 7, 17, 10), Bogota);

        Assert.Equal(ImportScheduleProblem.Invalid, r.Problem);
    }
}
