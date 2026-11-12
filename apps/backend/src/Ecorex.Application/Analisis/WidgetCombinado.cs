using System.Globalization;
using System.Text;
using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Sources;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Analisis;

/// <summary>Como se comparan las llaves de las series al cruzarlas.</summary>
public enum LlaveCombinado
{
    /// <summary>Texto: sin espacios sobrantes, sin tildes y en mayusculas ("Bogota " = "BOGOTA").</summary>
    Texto = 0,

    /// <summary>NIT: solo digitos y sin digito de verificacion ("900.123.456-7" = "900123456").</summary>
    Nit = 1,

    /// <summary>Exacta: tal cual llega de la fuente.</summary>
    Exacta = 2
}

/// <summary>Una serie del widget combinado: su propia fuente, medida y dimension (la llave del cruce).</summary>
public sealed record SerieCombinada
{
    public string Nombre { get; init; } = string.Empty;

    public string SourceKey { get; init; } = string.Empty;

    public ReportAggregateFunction Funcion { get; init; } = ReportAggregateFunction.Count;

    public string? CampoMedida { get; init; }

    /// <summary>Campo por el que se agrupa; sus valores son la llave con la que se alinea con las otras series.</summary>
    public string Dimension { get; init; } = string.Empty;

    public IReadOnlyList<WidgetFiltroDiseno> Filtros { get; init; } = Array.Empty<WidgetFiltroDiseno>();
}

/// <summary>Serie calculada entre series: letras A, B, C, D (en el orden de las series), numeros, + - * / y parentesis.</summary>
public sealed record FormulaCombinada(string Nombre, string Expresion);

/// <summary>
/// Widget COMBINADO (ADR-0158 Ola 5): varias series, cada una con su fuente, alineadas por la dimension con
/// una normalizacion de llave (cruce Directorio x Tareas x Contenedor por NIT, ciudad, etc.) y formulas
/// entre series. Se guarda en <see cref="WidgetConfig.Combinado"/>; el widget lleva
/// <see cref="IndicadorKey"/> como indicador.
/// </summary>
public sealed record CombinadoDiseno
{
    public const string IndicadorKey = "analisis.combinado";
    public const int MaxSeries = 4;
    public const int MaxFormulas = 3;

    /// <summary>Tope de llaves por serie: mas que esto se recorta y se avisa.</summary>
    public const int MaxLlavesPorSerie = 5_000;

    public IReadOnlyList<SerieCombinada> Series { get; init; } = Array.Empty<SerieCombinada>();

    public IReadOnlyList<FormulaCombinada> Formulas { get; init; } = Array.Empty<FormulaCombinada>();

    public LlaveCombinado Llave { get; init; } = LlaveCombinado.Texto;

    /// <summary>true = solo llaves presentes en TODAS las series (interseccion); false = union.</summary>
    public bool SoloComunes { get; init; }

    /// <summary>Errores de validacion contra el catalogo (vacio = valido).</summary>
    public IReadOnlyList<string> Validar(IReadOnlyList<ReportSourceDescriptor> fuentes)
    {
        var errores = new List<string>();
        if (Series.Count == 0)
        {
            errores.Add("Agrega al menos una serie.");
        }

        if (Series.Count > MaxSeries)
        {
            errores.Add($"Maximo {MaxSeries} series.");
        }

        for (var i = 0; i < Series.Count; i++)
        {
            var s = Series[i];
            var letra = Letra(i);
            var fuente = fuentes.FirstOrDefault(f => f.Key == s.SourceKey);
            if (fuente is null)
            {
                errores.Add($"Serie {letra}: elige una fuente.");
                continue;
            }

            if (fuente.FindField(s.Dimension) is not { CanGroup: true })
            {
                errores.Add($"Serie {letra}: elige el campo de cruce (agrupacion).");
            }

            if (s.Funcion != ReportAggregateFunction.Count && fuente.FindField(s.CampoMedida ?? string.Empty) is not { CanAggregate: true })
            {
                errores.Add($"Serie {letra}: elige un campo numerico para la medida.");
            }
        }

        if (Formulas.Count > MaxFormulas)
        {
            errores.Add($"Maximo {MaxFormulas} formulas.");
        }

        foreach (var f in Formulas)
        {
            if (string.IsNullOrWhiteSpace(f.Nombre))
            {
                errores.Add("Cada formula necesita un nombre.");
            }

            if (!FormulaSerie.EsValida(f.Expresion, Series.Count))
            {
                errores.Add($"Formula \"{f.Nombre}\": use letras A-{Letra(Math.Max(0, Series.Count - 1))}, numeros, + - * / y parentesis.");
            }
        }

        return errores;
    }

    public static string Letra(int i) => ((char)('A' + i)).ToString();

    /// <summary>Spec agregado de una serie (un grupo + una medida), con filtros extra (hoja/contexto).</summary>
    public static ReportQuerySpec SpecDe(SerieCombinada s, IReadOnlyList<ReportFilter>? extra = null)
    {
        var filtros = s.Filtros
            .Where(f => !string.IsNullOrWhiteSpace(f.Campo))
            .Select(f => new ReportFilter(f.Campo, f.Operador, Valores(f)))
            .Concat(extra ?? Array.Empty<ReportFilter>())
            .ToList();
        var campo = s.Funcion == ReportAggregateFunction.Count ? string.Empty : s.CampoMedida ?? string.Empty;
        return new ReportQuerySpec
        {
            SourceKey = s.SourceKey,
            GroupBy = new[] { s.Dimension },
            Aggregates = new[] { new ReportAggregate(campo, s.Funcion) },
            Filters = filtros
        };
    }

    private static IReadOnlyList<string> Valores(WidgetFiltroDiseno f)
    {
        if (f.Operador == ReportFilterOperator.In)
        {
            return (f.Valor ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        return f.Operador == ReportFilterOperator.Between
            ? new[] { f.Valor ?? string.Empty, f.Valor2 ?? string.Empty }
            : new[] { f.Valor ?? string.Empty };
    }

    /// <summary>Normaliza una llave segun el modo (null = fila sin llave, se descarta).</summary>
    public static string? Normalizar(object? valor, LlaveCombinado modo)
    {
        var s = valor switch
        {
            null => null,
            DateTimeOffset d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => valor.ToString()
        };
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }

        return modo switch
        {
            LlaveCombinado.Nit => NitNormalizer.Normalize(s),
            LlaveCombinado.Exacta => s,
            _ => SinTildes(s.Trim()).ToUpperInvariant()
        };
    }

    private static string SinTildes(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return string.Join(' ', sb.ToString().Normalize(NormalizationForm.FormC).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary>Resultado del cruce: una fila por llave, una columna por serie y por formula.</summary>
public sealed record CombinadoTabla(IReadOnlyList<string> Columnas, IReadOnlyList<CombinadoFila> Filas, IReadOnlyList<string> Avisos);

public sealed record CombinadoFila(string Etiqueta, IReadOnlyList<decimal?> Valores);

/// <summary>Cruce PURO de las series ya consultadas (testeable sin BD).</summary>
public static class CombinadoMotor
{
    /// <summary>
    /// Alinea las series por llave normalizada (union o interseccion), suma los duplicados que la
    /// normalizacion junta y calcula las formulas fila por fila. La etiqueta visible es la primera
    /// forma original encontrada de cada llave.
    /// </summary>
    public static CombinadoTabla Combinar(CombinadoDiseno d, IReadOnlyList<ReportDataSet> datos)
    {
        var avisos = new List<string>();
        var etiquetas = new Dictionary<string, string>(StringComparer.Ordinal);
        var orden = new List<string>();
        var porSerie = new List<Dictionary<string, decimal?>>();

        for (var i = 0; i < datos.Count; i++)
        {
            var mapa = new Dictionary<string, decimal?>(StringComparer.Ordinal);
            var filas = datos[i].Rows;
            if (filas.Count > CombinadoDiseno.MaxLlavesPorSerie)
            {
                avisos.Add($"Serie {CombinadoDiseno.Letra(i)}: {filas.Count} valores; se usan los primeros {CombinadoDiseno.MaxLlavesPorSerie}.");
                filas = filas.Take(CombinadoDiseno.MaxLlavesPorSerie).ToList();
            }

            foreach (var r in filas)
            {
                if (r.Count < 2 || CombinadoDiseno.Normalizar(r[0], d.Llave) is not string llave)
                {
                    continue;
                }

                var v = InMemoryReportEngine.ToDecimal(r[1]);
                mapa[llave] = mapa.TryGetValue(llave, out var prev) ? Sumar(prev, v) : v;
                if (!etiquetas.ContainsKey(llave))
                {
                    etiquetas[llave] = r[0] is DateTimeOffset dt ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : r[0]!.ToString()!.Trim();
                    orden.Add(llave);
                }
            }

            porSerie.Add(mapa);
        }

        var llaves = d.SoloComunes
            ? orden.Where(k => porSerie.All(m => m.ContainsKey(k))).ToList()
            : orden;

        var columnas = d.Series.Select((s, i) => string.IsNullOrWhiteSpace(s.Nombre) ? CombinadoDiseno.Letra(i) : s.Nombre.Trim())
            .Concat(d.Formulas.Select(f => f.Nombre.Trim()))
            .ToList();

        var formulas = d.Formulas.Select(f => FormulaSerie.Compilar(f.Expresion)).ToList();
        var resultado = new List<CombinadoFila>(llaves.Count);
        foreach (var k in llaves)
        {
            var bases = porSerie.Select(m => m.TryGetValue(k, out var v) ? v : null).ToList();
            var valores = bases.Concat(formulas.Select(f => f?.Invoke(bases))).ToList();
            resultado.Add(new CombinadoFila(etiquetas[k], valores));
        }

        if (d.SoloComunes && resultado.Count == 0 && orden.Count > 0)
        {
            avisos.Add("Ninguna llave aparece en todas las series: revisa el campo de cruce o la normalizacion.");
        }

        return new CombinadoTabla(columnas, resultado, avisos);
    }

    /// <summary>Tabla combinada -> forma segun el tipo visual del widget.</summary>
    public static WidgetShape AForma(CombinadoTabla t, CombinadoDiseno d, AnalisisTipoWidget tipo, int top, string titulo)
    {
        if (WidgetDisenoMapper.EsEscalar(tipo))
        {
            // KPI: la PRIMERA formula evaluada sobre los TOTALES de cada serie (ej. tasa de conversion =
            // B / A * 100 con totales, no el promedio de las tasas); sin formula, el total de la serie A.
            var totales = Enumerable.Range(0, d.Series.Count).Select(c => SumaColumna(t, c)).ToList();
            var valor = d.Formulas.Count > 0 && FormulaSerie.Compilar(d.Formulas[0].Expresion) is { } f
                ? f(totales)
                : totales.FirstOrDefault();
            return new EscalarShape(valor is decimal v ? Math.Round(v, 4) : null, titulo);
        }

        var ordenadas = tipo is AnalisisTipoWidget.Linea or AnalisisTipoWidget.Area
            ? t.Filas.OrderBy(f => f.Etiqueta, StringComparer.OrdinalIgnoreCase).ToList()
            : t.Filas.OrderByDescending(f => f.Valores.FirstOrDefault() ?? decimal.MinValue).ToList();

        if (tipo == AnalisisTipoWidget.Tabla)
        {
            var cols = new List<ReportColumn> { new("Llave", "Llave", ReportFieldType.Text) };
            cols.AddRange(t.Columnas.Select(c => new ReportColumn(c, c, ReportFieldType.Decimal)));
            var filas = ordenadas.Select(f => (IReadOnlyList<object?>)new object?[] { f.Etiqueta }.Concat(f.Valores.Cast<object?>()).ToList()).ToList();
            return new TablaShape(cols, filas);
        }

        if (tipo is AnalisisTipoWidget.Dona or AnalisisTipoWidget.Semaforo or AnalisisTipoWidget.Embudo
            or AnalisisTipoWidget.BarrasHorizontales or AnalisisTipoWidget.Treemap)
        {
            // Una sola medida: la ultima columna (la formula, si hay).
            var c = t.Columnas.Count - 1;
            return new CategoriasShape(ordenadas.Take(Math.Max(1, top))
                .Where(f => f.Valores[c] is not null)
                .Select(f => new CategoriaItem(f.Etiqueta, f.Valores[c]!.Value)).ToList());
        }

        var visibles = tipo is AnalisisTipoWidget.Linea or AnalisisTipoWidget.Area ? ordenadas : ordenadas.Take(Math.Max(1, top)).ToList();
        return new SerieShape(
            visibles.Select(f => f.Etiqueta).ToList(),
            t.Columnas.Select((c, i) => new SerieDatos(c, visibles.Select(f => f.Valores[i]).ToList())).ToList());
    }

    private static decimal? SumaColumna(CombinadoTabla t, int c)
    {
        var vals = t.Filas.Select(f => f.Valores[c]).Where(v => v is not null).Select(v => v!.Value).ToList();
        return vals.Count == 0 ? null : vals.Sum();
    }

    private static decimal? Sumar(decimal? a, decimal? b) => a is null && b is null ? null : (a ?? 0) + (b ?? 0);
}

/// <summary>
/// Formulas entre series: parser recursivo minimo (sin eval, sin reflexion). Gramatica:
/// expr = term (('+'|'-') term)*; term = factor (('*'|'/') factor)*; factor = numero | letra | '(' expr ')' | '-' factor.
/// Un operando nulo o una division por cero dan null.
/// </summary>
public static class FormulaSerie
{
    public static bool EsValida(string? expresion, int series) =>
        Compilar(expresion) is not null && LetrasUsadas(expresion!).All(i => i < series);

    public static Func<IReadOnlyList<decimal?>, decimal?>? Compilar(string? expresion)
    {
        if (string.IsNullOrWhiteSpace(expresion) || expresion.Length > 200)
        {
            return null;
        }

        var p = new Parser(expresion);
        try
        {
            var f = p.Expr();
            p.SaltarEspacios();
            return p.Fin ? f : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static IEnumerable<int> LetrasUsadas(string e) =>
        e.Where(char.IsLetter).Select(c => char.ToUpperInvariant(c) - 'A');

    private sealed class Parser(string s)
    {
        private int _i;

        public bool Fin => _i >= s.Length;

        public void SaltarEspacios()
        {
            while (_i < s.Length && char.IsWhiteSpace(s[_i]))
            {
                _i++;
            }
        }

        public Func<IReadOnlyList<decimal?>, decimal?> Expr()
        {
            var izq = Term();
            while (true)
            {
                SaltarEspacios();
                if (Fin || (s[_i] != '+' && s[_i] != '-'))
                {
                    return izq;
                }

                var op = s[_i++];
                var der = Term();
                var a = izq;
                izq = op == '+' ? v => a(v) + der(v) : v => a(v) - der(v);
            }
        }

        private Func<IReadOnlyList<decimal?>, decimal?> Term()
        {
            var izq = Factor();
            while (true)
            {
                SaltarEspacios();
                if (Fin || (s[_i] != '*' && s[_i] != '/'))
                {
                    return izq;
                }

                var op = s[_i++];
                var der = Factor();
                var a = izq;
                izq = op == '*'
                    ? v => a(v) * der(v)
                    : v => der(v) is decimal d && d != 0 ? a(v) / d : null;
            }
        }

        private Func<IReadOnlyList<decimal?>, decimal?> Factor()
        {
            SaltarEspacios();
            if (Fin)
            {
                throw new FormatException();
            }

            var c = s[_i];
            if (c == '(')
            {
                _i++;
                var e = Expr();
                SaltarEspacios();
                if (Fin || s[_i] != ')')
                {
                    throw new FormatException();
                }

                _i++;
                return e;
            }

            if (c == '-')
            {
                _i++;
                var f = Factor();
                return v => -f(v);
            }

            if (char.IsLetter(c))
            {
                _i++;
                var idx = char.ToUpperInvariant(c) - 'A';
                if (idx is < 0 or >= CombinadoDiseno.MaxSeries)
                {
                    throw new FormatException();
                }

                return v => idx < v.Count ? v[idx] : null;
            }

            var ini = _i;
            while (_i < s.Length && (char.IsDigit(s[_i]) || s[_i] == '.'))
            {
                _i++;
            }

            if (_i == ini || !decimal.TryParse(s.AsSpan(ini, _i - ini), NumberStyles.Number, CultureInfo.InvariantCulture, out var n))
            {
                throw new FormatException();
            }

            return _ => n;
        }
    }
}
