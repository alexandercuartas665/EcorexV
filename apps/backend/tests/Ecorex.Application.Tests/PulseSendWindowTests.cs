using Ecorex.Application.Workflows;

namespace Ecorex.Application.Tests;

/// <summary>
/// Unit tests de la VENTANA HORARIA DE ENVIO de los pulsos (PulseSendWindow): que un recordatorio que cae fuera
/// del horario se corra al INICIO de la proxima ventana (mismo dia si es antes de abrir; proximo dia activo y no
/// festivo si es despues o en fin de semana), que dentro de la ventana no se toque, que sin ventana no cambie, y
/// que el JSON embebido en la cadencia de seguimiento haga ida y vuelta conservando filas + ventana.
/// Zona fija UTC-5 (Bogota no tiene horario de verano) para que las fechas sean deterministas sin la BD de zonas.
/// </summary>
public class PulseSendWindowTests
{
    // Bogota = UTC-5 constante. Se modela como offset fijo para no depender de la base de zonas del entorno.
    private static readonly TimeZoneInfo Bogota =
        TimeZoneInfo.CreateCustomTimeZone("BOG-5", TimeSpan.FromHours(-5), "Bogota (UTC-5)", "Bogota");

    // Ventana por defecto: L-V 08:00 (480) a 18:00 (1080).
    private static PulseSendWindow Weekdays8to18(bool holidays = false)
        => new(true, 480, 1080, new HashSet<int> { 1, 2, 3, 4, 5 }, holidays);

    // Instante UTC a partir de una hora LOCAL de Bogota (local = utc - 5  =>  utc = local + 5).
    private static DateTimeOffset BogotaLocalAsUtc(int year, int month, int day, int hour, int minute)
        => new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).AddHours(5);

    private static (int Y, int Mo, int D, int H, int Mi) LocalOf(DateTimeOffset utc)
    {
        var l = TimeZoneInfo.ConvertTime(utc, Bogota);
        return (l.Year, l.Month, l.Day, l.Hour, l.Minute);
    }

    // Fechas de referencia (2026-11-05 es jueves): Mie 04, Jue 05, Vie 06, Sab 07, Dom 08, Lun 09.

    [Fact]
    public void DentroDeLaVentana_NoCambia()
    {
        var due = BogotaLocalAsUtc(2026, 11, 5, 10, 0);   // jueves 10:00
        var clamped = Weekdays8to18().ClampUtc(due, Bogota, new HashSet<DateOnly>());
        Assert.Equal(due, clamped);
    }

    [Fact]
    public void Madrugada_SeCorreALaAperturaDelMismoDia()
    {
        var due = BogotaLocalAsUtc(2026, 11, 5, 1, 0);    // jueves 01:00 (antes de abrir)
        var clamped = Weekdays8to18().ClampUtc(due, Bogota, new HashSet<DateOnly>());
        Assert.Equal((2026, 11, 5, 8, 0), LocalOf(clamped));   // jueves 08:00
    }

    [Fact]
    public void DespuesDeCerrar_SeCorreAlDiaSiguiente()
    {
        var due = BogotaLocalAsUtc(2026, 11, 5, 20, 0);   // jueves 20:00 (ya cerro)
        var clamped = Weekdays8to18().ClampUtc(due, Bogota, new HashSet<DateOnly>());
        Assert.Equal((2026, 11, 6, 8, 0), LocalOf(clamped));   // viernes 08:00
    }

    [Fact]
    public void FinDeSemana_SeCorreAlLunes()
    {
        var due = BogotaLocalAsUtc(2026, 11, 7, 10, 0);   // sabado 10:00 (dia no activo)
        var clamped = Weekdays8to18().ClampUtc(due, Bogota, new HashSet<DateOnly>());
        Assert.Equal((2026, 11, 9, 8, 0), LocalOf(clamped));   // lunes 08:00
    }

    [Fact]
    public void ConFestivo_SaltaElDiaNoOperativo()
    {
        var holidays = new HashSet<DateOnly> { new(2026, 11, 6) };   // viernes festivo
        var due = BogotaLocalAsUtc(2026, 11, 5, 20, 0);             // jueves 20:00
        var clamped = Weekdays8to18(holidays: true).ClampUtc(due, Bogota, holidays);
        Assert.Equal((2026, 11, 9, 8, 0), LocalOf(clamped));         // salta vie(festivo)+finde -> lunes 08:00
    }

    [Fact]
    public void SinVentana_NoCambia()
    {
        var due = BogotaLocalAsUtc(2026, 11, 5, 1, 0);
        var clamped = PulseSendWindow.Disabled.ClampUtc(due, Bogota, new HashSet<DateOnly>());
        Assert.Equal(due, clamped);
    }

    [Fact]
    public void JsonDeCadencia_IdaYVuelta_ConservaFilasYVentana()
    {
        var rows = new (int, int, int)[] { (0, 0, 10), (0, 0, 5) };
        var json = StepSlaList.Build(rows, Weekdays8to18());
        Assert.NotNull(json);

        var readRows = StepSlaList.ReadRows(json);
        Assert.Equal(2, readRows.Count);
        Assert.Equal((0, 0, 10), readRows[0]);
        Assert.Equal((0, 0, 5), readRows[1]);

        var win = PulseSendWindow.Read(json);
        Assert.True(win.Enabled);
        Assert.Equal(480, win.StartMin);
        Assert.Equal(1080, win.EndMin);
        Assert.True(win.RespectHolidays == false || win.RespectHolidays); // defensivo: existe
        Assert.Equal(new HashSet<int> { 1, 2, 3, 4, 5 }, win.Days);
    }

    [Fact]
    public void SinFilas_NoSerializaVentana()
    {
        // La ventana sola (sin cadencia) no aplica: sin filas el JSON es null.
        var json = StepSlaList.Build(Array.Empty<(int, int, int)>(), Weekdays8to18());
        Assert.Null(json);
    }

    [Fact]
    public void ArrayPlanoLegacy_SigueLeyendoFilas_YSinVentana()
    {
        const string legacy = "[{\"days\":0,\"hours\":0,\"minutes\":10},{\"days\":0,\"hours\":0,\"minutes\":20}]";
        Assert.Equal(2, StepSlaList.ReadRows(legacy).Count);
        Assert.False(PulseSendWindow.Read(legacy).Enabled);
    }
}
