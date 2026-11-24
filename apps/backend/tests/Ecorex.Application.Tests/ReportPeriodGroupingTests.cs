using Ecorex.Application.Reporting;

namespace Ecorex.Application.Tests;

/// <summary>Claves de periodo para agrupar fechas (ADR-0206).</summary>
public class ReportPeriodGroupingTests
{
    private static readonly TimeZoneInfo Bogota = TimeZoneInfo.CreateCustomTimeZone("bog", TimeSpan.FromHours(-5), "bog", "bog");

    [Theory]
    [InlineData(ReportDatePeriod.Day, "2026-03-01")]
    [InlineData(ReportDatePeriod.Week, "2026-W09")]
    [InlineData(ReportDatePeriod.Month, "2026-03")]
    [InlineData(ReportDatePeriod.Quarter, "2026-T1")]
    [InlineData(ReportDatePeriod.Year, "2026")]
    public void FechaSinHora_NoSeCorreDeZona(ReportDatePeriod period, string expected)
    {
        // Una columna fecha "2026-03-01" llega como medianoche UTC: con la zona de Bogota NO debe caer en febrero.
        var value = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, ReportPeriodGrouping.BucketKey(value, period, Bogota));
    }

    [Fact]
    public void InstanteConHora_UsaLaZonaDelTenant()
    {
        // 2026-04-01 03:00 UTC = 2026-03-31 22:00 en Bogota: es marzo para el usuario.
        var value = new DateTimeOffset(2026, 4, 1, 3, 0, 0, TimeSpan.Zero);
        Assert.Equal("2026-03", ReportPeriodGrouping.BucketKey(value, ReportDatePeriod.Month, Bogota));
        Assert.Equal("2026-04", ReportPeriodGrouping.BucketKey(value, ReportDatePeriod.Month, null));
    }

    [Fact]
    public void SemanaIso_YTextoYNoFecha()
    {
        // 2027-01-01 es viernes: pertenece a la semana 53 de 2026 (ISO 8601).
        Assert.Equal("2026-W53", ReportPeriodGrouping.BucketKey("2027-01-01", ReportDatePeriod.Week));
        Assert.Equal("2026-T4", ReportPeriodGrouping.BucketKey(new DateOnly(2026, 12, 31), ReportDatePeriod.Quarter));
        Assert.Null(ReportPeriodGrouping.BucketKey("no es fecha", ReportDatePeriod.Month));
        Assert.Null(ReportPeriodGrouping.BucketKey(null, ReportDatePeriod.Month));
    }
}
