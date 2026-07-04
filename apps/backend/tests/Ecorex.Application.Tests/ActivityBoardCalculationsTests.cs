using Ecorex.Application.Tenancy;

namespace Ecorex.Application.Tests;

/// <summary>
/// Unit tests de la logica PURA de los tableros de actividades (ADR-0020):
/// progreso del indice (checklist primero, fallback a columna final), porcentaje
/// de checklist y mapeo de los filtros de fecha hoy/manana/con-fecha a rangos UTC.
/// </summary>
public class ActivityBoardCalculationsTests
{
    // ---- Progreso del indice ----

    [Theory]
    // Con checklist manda el checklist (aunque haya tareas en columna final).
    [InlineData(3, 4, 0, 10, 75)]
    [InlineData(0, 4, 10, 10, 0)]
    [InlineData(4, 4, 0, 10, 100)]
    // Sin checklist cae a tareas en columna IsDone / total.
    [InlineData(0, 0, 2, 10, 20)]
    [InlineData(0, 0, 10, 10, 100)]
    [InlineData(0, 0, 0, 10, 0)]
    // Sin checklist ni tareas: 0.
    [InlineData(0, 0, 0, 0, 0)]
    // Redondeo half-away-from-zero: 1/3 = 33, 2/3 = 67, 1/8 = 13 (12.5 -> 13).
    [InlineData(1, 3, 0, 0, 33)]
    [InlineData(2, 3, 0, 0, 67)]
    [InlineData(1, 8, 0, 0, 13)]
    public void BoardProgressPct_ChecklistFirst_ThenDoneColumnFallback(
        int checklistDone, int checklistTotal, int tasksInDone, int totalTasks, int expected)
    {
        Assert.Equal(expected,
            ActivityBoardCalculations.BoardProgressPct(checklistDone, checklistTotal, tasksInDone, totalTasks));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 4, 0)]
    [InlineData(3, 4, 75)]
    [InlineData(4, 4, 100)]
    [InlineData(5, 0, 0)] // total 0 nunca divide
    public void Pct_HandlesZeroTotals(int done, int total, int expected)
        => Assert.Equal(expected, ActivityBoardCalculations.Pct(done, total));

    // ---- Rango de fechas de los filtros del detalle ----

    private static readonly DateTimeOffset Now = new(2026, 7, 4, 15, 30, 0, TimeSpan.Zero);

    [Fact]
    public void DueRangeUtc_Today_IsUtcDayOfNow()
    {
        var range = ActivityBoardCalculations.DueRangeUtc(ActivityDueFilter.Today, null, Now);
        Assert.NotNull(range);
        Assert.Equal(new DateTimeOffset(2026, 7, 4, 0, 0, 0, TimeSpan.Zero), range.Value.From);
        Assert.Equal(new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero), range.Value.To);
    }

    [Fact]
    public void DueRangeUtc_Tomorrow_IsNextUtcDay()
    {
        var range = ActivityBoardCalculations.DueRangeUtc(ActivityDueFilter.Tomorrow, null, Now);
        Assert.NotNull(range);
        Assert.Equal(new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero), range.Value.From);
        Assert.Equal(new DateTimeOffset(2026, 7, 6, 0, 0, 0, TimeSpan.Zero), range.Value.To);
    }

    [Fact]
    public void DueRangeUtc_OnDate_UsesTheGivenUtcDay_IgnoringTimeOfDay()
    {
        var picked = new DateTimeOffset(2026, 7, 20, 18, 45, 0, TimeSpan.Zero);
        var range = ActivityBoardCalculations.DueRangeUtc(ActivityDueFilter.OnDate, picked, Now);
        Assert.NotNull(range);
        Assert.Equal(new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero), range.Value.From);
        Assert.Equal(new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero), range.Value.To);
    }

    [Fact]
    public void DueRangeUtc_Any_OrOnDateWithoutDate_MeansNoFilter()
    {
        Assert.Null(ActivityBoardCalculations.DueRangeUtc(ActivityDueFilter.Any, null, Now));
        Assert.Null(ActivityBoardCalculations.DueRangeUtc(ActivityDueFilter.OnDate, null, Now));
    }

    // ---- Iniciales de avatar ----

    [Theory]
    [InlineData("Owner SKY SYSTEM", "OS")]
    [InlineData("ana maria perez", "AM")]
    [InlineData("ana", "A")]
    [InlineData("owner@sky-system.local", "O")]
    [InlineData("", "")]
    public void MemberInitials_TakesFirstLetterOfFirstTwoWords(string name, string expected)
        => Assert.Equal(expected, MemberInitials.From(name));
}
