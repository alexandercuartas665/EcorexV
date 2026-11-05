namespace Ecorex.Application.Tenancy;

/// <summary>
/// Calcula los festivos NACIONALES de Colombia de un anio dado (Ley 51 de 1983, "Ley Emiliani").
/// Puro y sin estado: se usa para precargar el calendario operativo del tenant (dias no operativos) sin que
/// el cliente tenga que escribirlos uno por uno. Reglas:
///  - Festivos FIJOS: se celebran en su fecha exacta.
///  - Festivos EMILIANI: se trasladan al LUNES siguiente (si ya caen en lunes, no se mueven).
///  - Festivos de PASCUA: se calculan a partir del Domingo de Resurreccion (algoritmo de computus gregoriano).
///    Jueves y Viernes Santo NO se trasladan; Ascension, Corpus Christi y Sagrado Corazon SI (ya con el
///    traslado incorporado en el desplazamiento desde Pascua: +43, +64, +71).
/// Nombres en ASCII por convencion del repo (sin tildes ni enie).
/// </summary>
public static class ColombianHolidays
{
    /// <summary>Un festivo resuelto a fecha concreta del anio.</summary>
    public readonly record struct Holiday(DateOnly Date, string Name);

    /// <summary>Festivos nacionales de Colombia del <paramref name="year"/>, ordenados por fecha.</summary>
    public static IReadOnlyList<Holiday> ForYear(int year)
    {
        var easter = EasterSunday(year);
        var list = new List<Holiday>
        {
            // Fijos
            new(new DateOnly(year, 1, 1), "Ano Nuevo"),
            new(new DateOnly(year, 5, 1), "Dia del Trabajo"),
            new(new DateOnly(year, 7, 20), "Independencia de Colombia"),
            new(new DateOnly(year, 8, 7), "Batalla de Boyaca"),
            new(new DateOnly(year, 12, 8), "Inmaculada Concepcion"),
            new(new DateOnly(year, 12, 25), "Navidad"),

            // Emiliani (al lunes siguiente)
            new(NextMonday(new DateOnly(year, 1, 6)), "Reyes Magos"),
            new(NextMonday(new DateOnly(year, 3, 19)), "San Jose"),
            new(NextMonday(new DateOnly(year, 6, 29)), "San Pedro y San Pablo"),
            new(NextMonday(new DateOnly(year, 8, 15)), "Asuncion de la Virgen"),
            new(NextMonday(new DateOnly(year, 10, 12)), "Dia de la Raza"),
            new(NextMonday(new DateOnly(year, 11, 1)), "Todos los Santos"),
            new(NextMonday(new DateOnly(year, 11, 11)), "Independencia de Cartagena"),

            // Basados en Pascua
            new(easter.AddDays(-3), "Jueves Santo"),
            new(easter.AddDays(-2), "Viernes Santo"),
            new(easter.AddDays(43), "Ascension del Senor"),
            new(easter.AddDays(64), "Corpus Christi"),
            new(easter.AddDays(71), "Sagrado Corazon de Jesus"),
        };

        list.Sort((a, b) => a.Date.CompareTo(b.Date));
        return list;
    }

    /// <summary>Traslada una fecha al lunes siguiente; si ya es lunes, la deja igual (regla Emiliani).</summary>
    private static DateOnly NextMonday(DateOnly date)
    {
        var delta = (((int)DayOfWeek.Monday - (int)date.DayOfWeek) + 7) % 7;
        return date.AddDays(delta);
    }

    /// <summary>Domingo de Resurreccion (computus gregoriano, algoritmo anonimo de Meeus/Jones/Butcher).</summary>
    private static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var month = (h + l - (7 * m) + 114) / 31;
        var day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }
}
