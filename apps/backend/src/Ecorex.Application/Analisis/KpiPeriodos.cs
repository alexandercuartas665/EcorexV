using System.Globalization;
using Ecorex.Application.Reporting;

namespace Ecorex.Application.Analisis;

// ---- Ola 7b (ADR-0158): KPI con periodo (variacion + tendencia) y calendario de actividad ----
// Todo aqui es PURO (sin BD ni reloj): el runner trae "hoy" en la zona del tenant y las filas.

/// <summary>Ventana de dias [Desde, HastaExclusivo) en la zona del tenant, con su etiqueta corta.</summary>
public sealed record VentanaFechas(DateOnly Desde, DateOnly HastaExclusivo, string Etiqueta)
{
    public bool Contiene(DateOnly dia) => dia >= Desde && dia < HastaExclusivo;
}

/// <summary>
/// Plan de un KPI con periodo: la serie de ventanas consecutivas (la ULTIMA es la actual, alimenta la
/// tendencia) y la ventana contra la que se compara (en periodos de calendario, "al mismo corte").
/// </summary>
public sealed record PlanPeriodos(IReadOnlyList<VentanaFechas> Serie, VentanaFechas Anterior, string Comparacion)
{
    public VentanaFechas Actual => Serie[^1];

    /// <summary>Primer dia que hay que traer (cubre la serie y la comparacion).</summary>
    public DateOnly Desde => Serie[0].Desde < Anterior.Desde ? Serie[0].Desde : Anterior.Desde;

    public DateOnly HastaExclusivo => Actual.HastaExclusivo;
}

/// <summary>Periodos de un KPI: mes/trimestre/ano en curso o los ultimos 30/7 dias.</summary>
public static class KpiPeriodos
{
    public const string Mes = "mes";
    public const string Trimestre = "trimestre";
    public const string Anio = "anio";
    public const string Ultimos30 = "30d";
    public const string Ultimos7 = "7d";

    /// <summary>Opciones del editor (clave guardada en ConfigJson + nombre visible).</summary>
    public static readonly IReadOnlyList<(string Clave, string Nombre)> Opciones = new[]
    {
        (Mes, "Mes en curso"),
        (Trimestre, "Trimestre en curso"),
        (Anio, "A\u00f1o en curso"),
        (Ultimos30, "Ultimos 30 dias"),
        (Ultimos7, "Ultimos 7 dias")
    };

    private static readonly string[] Meses = { "ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic" };

    public static bool EsValido(string? clave) => clave is not null && Opciones.Any(o => o.Clave == clave);

    /// <summary>
    /// Plan del KPI. Sin periodo valido = null (KPI clasico, sin comparar). Un rango COMPLETO de la hoja
    /// (desde y hasta) manda sobre el periodo: se compara con el rango anterior de igual largo, igual que
    /// el resto de widgets de la hoja respetan ese rango.
    /// </summary>
    public static PlanPeriodos? Planear(string? periodo, HojaFiltros? hoja, DateOnly hoy, int puntos = 6)
    {
        if (!EsValido(periodo))
        {
            return null;
        }

        puntos = Math.Clamp(puntos, 2, 12);
        if (hoja is { Desde: DateOnly desde, Hasta: DateOnly hasta } && hasta >= desde)
        {
            return PorRango(desde, hasta, puntos);
        }

        return periodo switch
        {
            Ultimos30 => Rodante(hoy, 30, puntos, "vs 30 dias anteriores"),
            Ultimos7 => Rodante(hoy, 7, Math.Max(puntos, 8), "vs 7 dias anteriores"),
            Mes => Calendario(hoy, 1, puntos, "vs mes anterior"),
            Trimestre => Calendario(hoy, 3, Math.Min(puntos, 5), "vs trimestre anterior"),
            _ => Calendario(hoy, 12, Math.Min(puntos, 5), "vs a\u00f1o anterior")
        };
    }

    /// <summary>Variacion relativa (0.12 = +12%); null sin base de comparacion (anterior nulo o cero).</summary>
    public static decimal? Variacion(decimal? actual, decimal? anterior) =>
        actual is decimal a && anterior is decimal p && p != 0 ? Math.Round((a - p) / Math.Abs(p), 4) : null;

    /// <summary>Rango de la hoja y los N rangos anteriores de igual largo.</summary>
    private static PlanPeriodos PorRango(DateOnly desde, DateOnly hasta, int n)
    {
        var largo = hasta.DayNumber - desde.DayNumber + 1;
        var serie = new List<VentanaFechas>(n);
        for (var k = n - 1; k >= 0; k--)
        {
            var d = desde.AddDays(-k * largo);
            serie.Add(new VentanaFechas(d, d.AddDays(largo), EtiquetaRango(d, d.AddDays(largo))));
        }

        return new PlanPeriodos(serie, serie[^2], "vs periodo anterior");
    }

    /// <summary>Ventanas moviles de N dias que terminan hoy (incluido).</summary>
    private static PlanPeriodos Rodante(DateOnly hoy, int largo, int n, string comparacion)
    {
        var fin = hoy.AddDays(1);
        var serie = new List<VentanaFechas>(n);
        for (var k = n - 1; k >= 0; k--)
        {
            var hastaEx = fin.AddDays(-k * largo);
            var d = hastaEx.AddDays(-largo);
            serie.Add(new VentanaFechas(d, hastaEx, EtiquetaRango(d, hastaEx)));
        }

        return new PlanPeriodos(serie, serie[^2], comparacion);
    }

    /// <summary>
    /// Periodos de calendario (mes, trimestre, ano). El actual va hasta hoy (incompleto); la tendencia
    /// muestra los anteriores completos, pero la COMPARACION es contra el anterior al mismo corte (mismo
    /// numero de dias), para no comparar medio mes contra un mes entero.
    /// </summary>
    private static PlanPeriodos Calendario(DateOnly hoy, int meses, int n, string comparacion)
    {
        var inicio = meses switch
        {
            1 => new DateOnly(hoy.Year, hoy.Month, 1),
            3 => new DateOnly(hoy.Year, ((hoy.Month - 1) / 3 * 3) + 1, 1),
            _ => new DateOnly(hoy.Year, 1, 1)
        };

        var serie = new List<VentanaFechas>(n);
        for (var k = n - 1; k >= 0; k--)
        {
            var d = inicio.AddMonths(-k * meses);
            var hastaEx = k == 0 ? hoy.AddDays(1) : d.AddMonths(meses);
            serie.Add(new VentanaFechas(d, hastaEx, EtiquetaPeriodo(d, meses)));
        }

        var previoInicio = inicio.AddMonths(-meses);
        var transcurridos = hoy.DayNumber - inicio.DayNumber + 1;
        var previoFin = previoInicio.AddDays(transcurridos);
        if (previoFin > inicio)
        {
            previoFin = inicio;
        }

        var anterior = new VentanaFechas(previoInicio, previoFin,
            $"{EtiquetaPeriodo(previoInicio, meses)} (al mismo corte)");
        return new PlanPeriodos(serie, anterior, comparacion);
    }

    private static string EtiquetaPeriodo(DateOnly d, int meses) => meses switch
    {
        1 => $"{Meses[d.Month - 1]} {d:yy}",
        3 => $"T{((d.Month - 1) / 3) + 1} {d:yy}",
        _ => d.Year.ToString(CultureInfo.InvariantCulture)
    };

    private static string EtiquetaRango(DateOnly desde, DateOnly hastaExclusivo)
    {
        var fin = hastaExclusivo.AddDays(-1);
        return fin == desde
            ? desde.ToString("dd/MM", CultureInfo.InvariantCulture)
            : $"{desde.ToString("dd/MM", CultureInfo.InvariantCulture)}-{fin.ToString("dd/MM", CultureInfo.InvariantCulture)}";
    }
}

/// <summary>Fechas del dataset -> dias en la zona del tenant, y agregacion por ventana o por dia. Puro.</summary>
public static class AnalisisFechas
{
    /// <summary>Tope de dias del calendario de actividad (un ano).</summary>
    public const int MaxDiasCalendario = 366;

    /// <summary>Hoy en la zona del tenant (null = UTC).</summary>
    public static DateOnly Hoy(DateTimeOffset ahora, TimeZoneInfo? zona) =>
        DateOnly.FromDateTime(zona is null ? ahora.UtcDateTime : TimeZoneInfo.ConvertTime(ahora, zona).DateTime);

    /// <summary>Dia (zona del tenant) de un valor de fecha del dataset; null si no es una fecha.</summary>
    public static DateOnly? Dia(object? valor, TimeZoneInfo? zona) => valor switch
    {
        null => null,
        DateTimeOffset d => DateOnly.FromDateTime(zona is null ? d.UtcDateTime : TimeZoneInfo.ConvertTime(d, zona).DateTime),
        DateTime dt => DateOnly.FromDateTime(dt.Kind == DateTimeKind.Utc && zona is not null ? TimeZoneInfo.ConvertTimeFromUtc(dt, zona) : dt),
        DateOnly x => x,
        string s => DeTexto(s, zona),
        _ => null
    };

    /// <summary>
    /// Rango del calendario: el de la hoja si esta completo (acotado al ultimo ano), si no el ultimo ano
    /// hasta hoy (o hasta el "hasta" de la hoja).
    /// </summary>
    public static (DateOnly Desde, DateOnly Hasta) RangoCalendario(HojaFiltros? hoja, DateOnly hoy)
    {
        var hasta = hoja?.Hasta ?? hoy;
        var desde = hoja?.Desde ?? hasta.AddDays(-(MaxDiasCalendario - 2));
        if (desde > hasta)
        {
            desde = hasta;
        }

        if (hasta.DayNumber - desde.DayNumber >= MaxDiasCalendario)
        {
            desde = hasta.AddDays(-(MaxDiasCalendario - 1));
        }

        return (desde, hasta);
    }

    /// <summary>Valor agregado de las filas que caen en la ventana.</summary>
    public static decimal? AgregarEn(VentanaFechas ventana, IReadOnlyList<(DateOnly Dia, object? Valor)> filas, ReportAggregateFunction fn) =>
        AnalisisWidgetRunner.Agregar(fn, filas.Where(f => ventana.Contiene(f.Dia)).Select(f => f.Valor));

    /// <summary>Calendario: un valor por dia con datos dentro del rango.</summary>
    public static CalendarioShape PorDia(DateOnly desde, DateOnly hasta, IReadOnlyList<(DateOnly Dia, object? Valor)> filas, ReportAggregateFunction fn) =>
        new(desde, hasta, filas
            .Where(f => f.Dia >= desde && f.Dia <= hasta)
            .GroupBy(f => f.Dia)
            .OrderBy(g => g.Key)
            .Select(g => new DiaValor(g.Key, AnalisisWidgetRunner.Agregar(fn, g.Select(x => x.Valor)) ?? 0m))
            .ToList());

    /// <summary>KPI con su variacion y tendencia a partir del plan y las filas (fecha, valor).</summary>
    public static EscalarShape Kpi(string etiqueta, PlanPeriodos plan, IReadOnlyList<(DateOnly Dia, object? Valor)> filas, ReportAggregateFunction fn)
    {
        var actual = AgregarEn(plan.Actual, filas, fn);
        var anterior = AgregarEn(plan.Anterior, filas, fn);
        return new EscalarShape(actual, etiqueta, KpiPeriodos.Variacion(actual, anterior))
        {
            ValorAnterior = anterior,
            Comparacion = plan.Comparacion,
            Tendencia = plan.Serie.Select(v => new PuntoSerie(v.Etiqueta, AgregarEn(v, filas, fn))).ToList()
        };
    }

    private static DateOnly? DeTexto(string s, TimeZoneInfo? zona)
    {
        var t = s.Trim();
        if (t.Length >= 10 && DateOnly.TryParseExact(t[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            && (t.Length == 10 || t[10] is 'T' or ' '))
        {
            // Texto con hora y offset explicito: se lleva a la zona; sin offset, vale el dia calendario.
            if (t.Length > 10 && DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto) && TieneOffset(t))
            {
                return Dia(dto, zona);
            }

            return d;
        }

        return DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? DateOnly.FromDateTime(dt) : null;
    }

    private static bool TieneOffset(string t) =>
        t.EndsWith('Z') || (t.Length > 19 && (t[^6] is '+' or '-') && t[^3] == ':');
}
