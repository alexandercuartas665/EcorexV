using Ecorex.Application.DataContainers;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// Unit tests del programador de horario amigable (FriendlySchedule): que las elecciones humanas
/// (Manual / Diario / Semanal / Mensual / Cada N) generen el cron/interval correcto, que el parseo
/// inverso reconstruya la eleccion (ida y vuelta), que un cron no-amigable caiga a null (modo avanzado),
/// que los resumenes legibles sean correctos y que las validaciones fallen con datos invalidos.
/// </summary>
public class FriendlyScheduleTests
{
    // ---- ToStorage: eleccion humana -> almacenamiento ----

    [Fact]
    public void Manual_MapsToManual()
    {
        var (kind, interval, cron) = FriendlySchedule.ToStorage(new FriendlyScheduleSpec(ScheduleRecurrence.Manual));
        Assert.Equal(ImportScheduleKind.Manual, kind);
        Assert.Null(interval);
        Assert.Null(cron);
    }

    [Fact]
    public void Daily_GeneratesCron()
    {
        var (kind, interval, cron) = FriendlySchedule.ToStorage(
            new FriendlyScheduleSpec(ScheduleRecurrence.Daily, Time: new TimeOnly(8, 0)));
        Assert.Equal(ImportScheduleKind.Cron, kind);
        Assert.Null(interval);
        Assert.Equal("0 8 * * *", cron);
    }

    [Fact]
    public void Weekly_GeneratesCron_WithSortedUniqueDays()
    {
        // Lunes(1) y jueves(4), pasados desordenados y con duplicado -> ordenados y unicos.
        var (kind, _, cron) = FriendlySchedule.ToStorage(new FriendlyScheduleSpec(
            ScheduleRecurrence.Weekly, Time: new TimeOnly(7, 30), DaysOfWeek: new[] { 4, 1, 4 }));
        Assert.Equal(ImportScheduleKind.Cron, kind);
        Assert.Equal("30 7 * * 1,4", cron);
    }

    [Fact]
    public void Monthly_GeneratesCron()
    {
        var (kind, _, cron) = FriendlySchedule.ToStorage(new FriendlyScheduleSpec(
            ScheduleRecurrence.Monthly, Time: new TimeOnly(23, 5), DayOfMonth: 15));
        Assert.Equal(ImportScheduleKind.Cron, kind);
        Assert.Equal("5 23 15 * *", cron);
    }

    [Theory]
    [InlineData(30, ScheduleEveryUnit.Minutes, 30)]
    [InlineData(2, ScheduleEveryUnit.Hours, 120)]
    [InlineData(1, ScheduleEveryUnit.Hours, 60)]
    public void EveryN_MapsToInterval(int value, ScheduleEveryUnit unit, int expectedMinutes)
    {
        var (kind, interval, cron) = FriendlySchedule.ToStorage(
            new FriendlyScheduleSpec(ScheduleRecurrence.EveryN, EveryValue: value, EveryUnit: unit));
        Assert.Equal(ImportScheduleKind.Interval, kind);
        Assert.Equal(expectedMinutes, interval);
        Assert.Null(cron);
    }

    // ---- Validaciones ----

    [Fact]
    public void Daily_WithoutTime_Throws()
        => Assert.Throws<ArgumentException>(() => FriendlySchedule.ToStorage(new FriendlyScheduleSpec(ScheduleRecurrence.Daily)));

    [Fact]
    public void Weekly_WithoutDays_Throws()
        => Assert.Throws<ArgumentException>(() => FriendlySchedule.ToStorage(
            new FriendlyScheduleSpec(ScheduleRecurrence.Weekly, Time: new TimeOnly(8, 0), DaysOfWeek: Array.Empty<int>())));

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void Monthly_WithBadDay_Throws(int badDom)
        => Assert.Throws<ArgumentException>(() => FriendlySchedule.ToStorage(
            new FriendlyScheduleSpec(ScheduleRecurrence.Monthly, Time: new TimeOnly(8, 0), DayOfMonth: badDom)));

    [Fact]
    public void EveryN_Zero_Throws()
        => Assert.Throws<ArgumentException>(() => FriendlySchedule.ToStorage(
            new FriendlyScheduleSpec(ScheduleRecurrence.EveryN, EveryValue: 0)));

    // ---- FromStorage: almacenamiento -> eleccion humana (round-trip) ----

    [Fact]
    public void RoundTrip_Daily()
    {
        var original = new FriendlyScheduleSpec(ScheduleRecurrence.Daily, Time: new TimeOnly(8, 0));
        var (kind, interval, cron) = FriendlySchedule.ToStorage(original);
        var back = FriendlySchedule.FromStorage(kind, interval, cron);
        Assert.NotNull(back);
        Assert.Equal(ScheduleRecurrence.Daily, back!.Recurrence);
        Assert.Equal(new TimeOnly(8, 0), back.Time);
    }

    [Fact]
    public void RoundTrip_Weekly()
    {
        var original = new FriendlyScheduleSpec(ScheduleRecurrence.Weekly, Time: new TimeOnly(7, 30), DaysOfWeek: new[] { 1, 4 });
        var (kind, interval, cron) = FriendlySchedule.ToStorage(original);
        var back = FriendlySchedule.FromStorage(kind, interval, cron);
        Assert.NotNull(back);
        Assert.Equal(ScheduleRecurrence.Weekly, back!.Recurrence);
        Assert.Equal(new TimeOnly(7, 30), back.Time);
        Assert.Equal(new[] { 1, 4 }, back.DaysOfWeek);
    }

    [Fact]
    public void RoundTrip_Monthly()
    {
        var original = new FriendlyScheduleSpec(ScheduleRecurrence.Monthly, Time: new TimeOnly(23, 5), DayOfMonth: 15);
        var (kind, interval, cron) = FriendlySchedule.ToStorage(original);
        var back = FriendlySchedule.FromStorage(kind, interval, cron);
        Assert.NotNull(back);
        Assert.Equal(ScheduleRecurrence.Monthly, back!.Recurrence);
        Assert.Equal(15, back.DayOfMonth);
        Assert.Equal(new TimeOnly(23, 5), back.Time);
    }

    [Fact]
    public void FromStorage_IntervalHours_ReturnsHours()
    {
        var back = FriendlySchedule.FromStorage(ImportScheduleKind.Interval, 120, null);
        Assert.NotNull(back);
        Assert.Equal(ScheduleRecurrence.EveryN, back!.Recurrence);
        Assert.Equal(2, back.EveryValue);
        Assert.Equal(ScheduleEveryUnit.Hours, back.EveryUnit);
    }

    [Fact]
    public void FromStorage_IntervalOddMinutes_ReturnsMinutes()
    {
        var back = FriendlySchedule.FromStorage(ImportScheduleKind.Interval, 45, null);
        Assert.NotNull(back);
        Assert.Equal(45, back!.EveryValue);
        Assert.Equal(ScheduleEveryUnit.Minutes, back.EveryUnit);
    }

    [Fact]
    public void FromStorage_Cron7AsSunday_NormalizesToZero()
    {
        var back = FriendlySchedule.FromStorage(ImportScheduleKind.Cron, null, "0 9 * * 7");
        Assert.NotNull(back);
        Assert.Equal(ScheduleRecurrence.Weekly, back!.Recurrence);
        Assert.Equal(new[] { 0 }, back.DaysOfWeek);
    }

    [Theory]
    [InlineData("0 * * * *")]        // cada hora (hora comodin) -> no amigable
    [InlineData("*/15 * * * *")]     // paso en minuto -> no amigable
    [InlineData("0 8 1 6 *")]        // mes fijo -> no amigable
    [InlineData("*/5 8-17 * * *")]   // paso + rango de HORAS -> no amigable
    [InlineData("0 8 * * 6-1")]      // rango de dias INVERTIDO -> no representable
    [InlineData("0 8 * * mon")]      // nombre de dia -> no amigable
    [InlineData("no es cron")]
    [InlineData("")]
    public void FromStorage_NonFriendlyCron_ReturnsNull(string cron)
        => Assert.Null(FriendlySchedule.FromStorage(ImportScheduleKind.Cron, null, cron));

    // ---- BUG 1: rangos de dias de semana + equivalencia lista/rango ----

    [Fact]
    public void FromStorage_WeeklyRange_1to6_ParsesToMonToSat()
    {
        var back = FriendlySchedule.FromStorage(ImportScheduleKind.Cron, null, "0 5 * * 1-6");
        Assert.NotNull(back);
        Assert.Equal(ScheduleRecurrence.Weekly, back!.Recurrence);
        Assert.Equal(new TimeOnly(5, 0), back.Time);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, back.DaysOfWeek);
    }

    [Fact]
    public void FromStorage_WeeklyList_And_Range_AreEquivalent()
    {
        var fromList = FriendlySchedule.FromStorage(ImportScheduleKind.Cron, null, "0 5 * * 1,2,3,4,5,6");
        var fromRange = FriendlySchedule.FromStorage(ImportScheduleKind.Cron, null, "0 5 * * 1-6");
        Assert.NotNull(fromList);
        Assert.NotNull(fromRange);
        Assert.Equal(fromList!.Recurrence, fromRange!.Recurrence);
        Assert.Equal(fromList.Time, fromRange.Time);
        Assert.Equal(fromList.DaysOfWeek, fromRange.DaysOfWeek);
        // Y sus resumenes legibles coinciden.
        Assert.Equal(
            FriendlySchedule.Describe(ImportScheduleKind.Cron, null, "0 5 * * 1,2,3,4,5,6"),
            FriendlySchedule.Describe(ImportScheduleKind.Cron, null, "0 5 * * 1-6"));
    }

    [Fact]
    public void FromStorage_WeeklyRange_1to5_ParsesToWorkweek()
    {
        var back = FriendlySchedule.FromStorage(ImportScheduleKind.Cron, null, "0 8 * * 1-5");
        Assert.NotNull(back);
        Assert.Equal(ScheduleRecurrence.Weekly, back!.Recurrence);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, back.DaysOfWeek);
    }

    [Fact]
    public void FromStorage_WeeklyRange_1to7_NormalizesSundayToZero_AllDays()
    {
        var back = FriendlySchedule.FromStorage(ImportScheduleKind.Cron, null, "0 6 * * 1-7");
        Assert.NotNull(back);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6 }, back!.DaysOfWeek);
    }

    [Fact]
    public void FromStorage_MixedRangeAndList()
    {
        // 1-5 (lun-vie) + 0 (domingo)
        var back = FriendlySchedule.FromStorage(ImportScheduleKind.Cron, null, "0 7 * * 1-5,0");
        Assert.NotNull(back);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, back!.DaysOfWeek);
    }

    [Fact]
    public void RoundTrip_WeeklyRange_PreservesSchedule()
    {
        // Abrir "0 5 * * 1-6" y guardar sin tocar: el string se normaliza a lista, pero el HORARIO es el mismo.
        var parsed = FriendlySchedule.FromStorage(ImportScheduleKind.Cron, null, "0 5 * * 1-6");
        Assert.NotNull(parsed);
        var (kind, interval, cron) = FriendlySchedule.ToStorage(parsed!);
        Assert.Equal(ImportScheduleKind.Cron, kind);
        Assert.Null(interval);
        var reparsed = FriendlySchedule.FromStorage(kind, interval, cron);
        Assert.NotNull(reparsed);
        Assert.Equal(parsed!.DaysOfWeek, reparsed!.DaysOfWeek);
        Assert.Equal(parsed.Time, reparsed.Time);
        Assert.Equal(parsed.Recurrence, reparsed.Recurrence);
    }

    // ---- Describe: resumen legible ----

    [Fact]
    public void Describe_Daily()
        => Assert.Equal("Todos los dias a las 08:00", FriendlySchedule.Describe(ImportScheduleKind.Cron, null, "0 8 * * *"));

    [Fact]
    public void Describe_Weekly_TwoDays()
        => Assert.Equal("Cada lunes y jueves a las 07:30", FriendlySchedule.Describe(ImportScheduleKind.Cron, null, "30 7 * * 1,4"));

    [Fact]
    public void Describe_Weekly_AllSevenDays_SaysEveryDay()
        => Assert.Equal("Todos los dias a las 06:00", FriendlySchedule.Describe(ImportScheduleKind.Cron, null, "0 6 * * 0,1,2,3,4,5,6"));

    [Fact]
    public void Describe_Monthly()
        => Assert.Equal("El dia 15 de cada mes a las 23:05", FriendlySchedule.Describe(ImportScheduleKind.Cron, null, "5 23 15 * *"));

    [Fact]
    public void Describe_IntervalMinutes()
        => Assert.Equal("Cada 30 minutos", FriendlySchedule.Describe(ImportScheduleKind.Interval, 30, null));

    [Fact]
    public void Describe_IntervalHours()
        => Assert.Equal("Cada 2 horas", FriendlySchedule.Describe(ImportScheduleKind.Interval, 120, null));

    [Fact]
    public void Describe_Manual()
        => Assert.Equal("Manual (solo cuando le des a 'Actualizar datos')", FriendlySchedule.Describe(ImportScheduleKind.Manual, null, null));

    [Fact]
    public void Describe_NonFriendlyCron_ShowsRaw()
        => Assert.Equal("Segun expresion cron: 0 * * * *", FriendlySchedule.Describe(ImportScheduleKind.Cron, null, "0 * * * *"));

    [Fact]
    public void DescribeSpec_WeeklyThreeDays_JoinsWithCommasAndY()
    {
        var spec = new FriendlyScheduleSpec(ScheduleRecurrence.Weekly, Time: new TimeOnly(9, 0), DaysOfWeek: new[] { 1, 3, 5 });
        Assert.Equal("Cada lunes, miercoles y viernes a las 09:00", FriendlySchedule.DescribeSpec(spec));
    }
}
