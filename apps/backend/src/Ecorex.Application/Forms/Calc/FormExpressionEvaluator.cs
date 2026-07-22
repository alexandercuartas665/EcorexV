using System.Globalization;

namespace Ecorex.Application.Forms.Calc;

/// <summary>
/// Evaluador de expresiones de campo calculado (Formularios avanzados, ola F2; doc 01 D5).
/// SANDBOX TIPADO con allow-list: solo numeros, referencias a campos <c>{codigo}</c>, referencias
/// al encabezado <c>{#codigo}</c>, los operadores + - * / con parentesis y menos unario, los
/// comparadores &gt; &lt; &gt;= &lt;= = &lt;&gt; y una lista CERRADA de funciones puras
/// (SI, REDONDEAR, REDONDEAR.SUPERIOR, REDONDEAR.INFERIOR, MIN, MAX). NO ejecuta codigo, NO usa
/// reflexion, NO llama funciones del host (se evita el RCE de facto del legacy): la unica forma de
/// agregar una funcion es tocar este archivo. Es la MISMA logica que corre en cliente (renderer,
/// UX inmediata) y en servidor (revalidacion al guardar; fuente de verdad).
/// Un campo referenciado vacio/no numerico cuenta como 0; una expresion invalida devuelve null
/// (el llamador decide como mostrarlo) en vez de lanzar.
/// </summary>
public static class FormExpressionEvaluator
{
    /// <summary>
    /// Prefijo que marca una referencia al ENCABEZADO del formulario dentro de una formula de
    /// columna: <c>{#iva_pct}</c>. Se eligio '#' porque NO es un caracter valido en un codigo de
    /// campo, asi que ninguna formula ya escrita con <c>{campo}</c> cambia de significado.
    /// </summary>
    public const char HeaderPrefix = '#';

    /// <summary>
    /// Evalua <paramref name="expression"/> resolviendo cada <c>{codigo}</c> con
    /// <paramref name="values"/> y cada <c>{#codigo}</c> con <paramref name="headerValues"/>
    /// (si es null, toda referencia al encabezado vale 0). Devuelve el resultado o null si la
    /// expresion es invalida (parentesis desbalanceados, token desconocido, funcion fuera de la
    /// allow-list, aridad incorrecta, division por cero, desbordamiento, etc.).
    /// </summary>
    public static decimal? Evaluate(
        string? expression,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyDictionary<string, string?>? headerValues = null)
    {
        if (string.IsNullOrWhiteSpace(expression)) { return null; }
        try
        {
            var parser = new Parser(expression, values, headerValues);
            var result = parser.ParseExpression();
            parser.ExpectEnd();
            return result;
        }
        catch (FormatException) { return null; }
        catch (DivideByZeroException) { return null; }
        catch (OverflowException) { return null; }
    }

    /// <summary>
    /// Extrae los codigos de campo de FILA referenciados (<c>{codigo}</c>). Las referencias al
    /// encabezado (<c>{#codigo}</c>) se excluyen a proposito: viven en otro diccionario y se
    /// consultan con <see cref="ReferencedHeaderFields"/>.
    /// </summary>
    public static IReadOnlyList<string> ReferencedFields(string? expression)
        => ScanRefs(expression, header: false);

    /// <summary>Extrae los codigos del ENCABEZADO referenciados, ya SIN el prefijo '#'.</summary>
    public static IReadOnlyList<string> ReferencedHeaderFields(string? expression)
        => ScanRefs(expression, header: true);

    private static IReadOnlyList<string> ScanRefs(string? expression, bool header)
    {
        var refs = new List<string>();
        if (string.IsNullOrWhiteSpace(expression)) { return refs; }
        var i = 0;
        while (i < expression.Length)
        {
            if (expression[i] == '{')
            {
                var end = expression.IndexOf('}', i + 1);
                if (end < 0) { break; }
                var code = expression.Substring(i + 1, end - i - 1).Trim();
                var isHeader = code.Length > 0 && code[0] == HeaderPrefix;
                if (isHeader) { code = code[1..].Trim(); }
                if (code.Length > 0 && isHeader == header && !refs.Contains(code, StringComparer.Ordinal))
                {
                    refs.Add(code);
                }
                i = end + 1;
            }
            else { i++; }
        }
        return refs;
    }

    /// <summary>Valida que la expresion sea parseable (sin resolver valores). Null = ok; si no, mensaje.</summary>
    public static string? Validate(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) { return null; }
        // Valores ficticios (1) solo para probar la forma; la division por cero no invalida la forma.
        var probe = ReferencedFields(expression).ToDictionary(c => c, _ => (string?)"1", StringComparer.Ordinal);
        var probeHeader = ReferencedHeaderFields(expression).ToDictionary(c => c, _ => (string?)"1", StringComparer.Ordinal);
        try
        {
            var parser = new Parser(expression, probe, probeHeader);
            parser.ParseExpression();
            parser.ExpectEnd();
            return null;
        }
        catch (FormatException ex) { return ex.Message; }
        catch (DivideByZeroException) { return null; }
        catch (OverflowException) { return null; }
    }

    /// <summary>
    /// Parser de descenso recursivo minimo:
    /// expr := cmp; cmp := arith (('&gt;='|'&lt;='|'&lt;&gt;'|'&gt;'|'&lt;'|'=') arith)?;
    /// arith := term (('+'|'-') term)*; term := factor (('*'|'/') factor)*;
    /// factor := number | ref | funcion | '(' expr ')' | ('-'|'+') factor.
    /// Un comparador devuelve 1 (verdadero) o 0 (falso), como en el Excel de origen.
    /// </summary>
    private sealed class Parser
    {
        // Tope de anidamiento: el sandbox no puede permitir que una expresion guardada por un
        // usuario tumbe el proceso con StackOverflow (no es capturable en .NET).
        private const int MaxDepth = 64;

        private readonly string _s;
        private readonly IReadOnlyDictionary<string, string?> _values;
        private readonly IReadOnlyDictionary<string, string?>? _header;
        private int _pos;
        private int _depth;

        // Ramas de SI() que NO se toman: se PARSEAN (para seguir validando la forma) pero no
        // deben producir errores de ejecucion. Sin esto, SI({b}=0; 0; {a}/{b}) daria null.
        private int _skip;

        public Parser(
            string s,
            IReadOnlyDictionary<string, string?> values,
            IReadOnlyDictionary<string, string?>? header)
        {
            _s = s;
            _values = values;
            _header = header;
        }

        public decimal ParseExpression()
        {
            if (++_depth > MaxDepth) { throw new FormatException("Expresion demasiado anidada."); }
            try
            {
                var left = ParseArith();
                var op = MatchComparator();
                if (op is null) { return left; }
                var right = ParseArith();
                var ok = op switch
                {
                    ">" => left > right,
                    "<" => left < right,
                    ">=" => left >= right,
                    "<=" => left <= right,
                    "=" => left == right,
                    "<>" => left != right,
                    _ => throw new FormatException($"Comparador no soportado: '{op}'."),
                };
                return ok ? 1m : 0m;
            }
            finally { _depth--; }
        }

        /// <summary>Consume un comparador si viene. '&gt;=' '&lt;=' '&lt;&gt;' antes que '&gt;' '&lt;'.</summary>
        private string? MatchComparator()
        {
            SkipWs();
            if (_pos >= _s.Length) { return null; }
            if (_pos + 1 < _s.Length)
            {
                var two = _s.Substring(_pos, 2);
                if (two is ">=" or "<=" or "<>") { _pos += 2; return two; }
            }
            var one = _s[_pos];
            if (one is '>' or '<' or '=') { _pos++; return one.ToString(); }
            return null;
        }

        private decimal ParseArith()
        {
            var value = ParseTerm();
            while (true)
            {
                SkipWs();
                if (Match('+')) { value += ParseTerm(); }
                else if (Match('-')) { value -= ParseTerm(); }
                else { break; }
            }
            return value;
        }

        private decimal ParseTerm()
        {
            var value = ParseFactor();
            while (true)
            {
                SkipWs();
                if (Match('*')) { value *= ParseFactor(); }
                else if (Match('/'))
                {
                    var divisor = ParseFactor();
                    if (divisor == 0m)
                    {
                        // En una rama no tomada de SI() la division por cero es inofensiva.
                        if (_skip > 0) { value = 0m; continue; }
                        throw new DivideByZeroException();
                    }
                    value /= divisor;
                }
                else { break; }
            }
            return value;
        }

        private decimal ParseFactor()
        {
            if (++_depth > MaxDepth) { throw new FormatException("Expresion demasiado anidada."); }
            try
            {
                SkipWs();
                if (Match('-')) { return -ParseFactor(); }
                if (Match('+')) { return ParseFactor(); }
                if (Match('('))
                {
                    var value = ParseExpression();
                    SkipWs();
                    if (!Match(')')) { throw new FormatException("Falta ')'."); }
                    return value;
                }
                if (Peek() == '{') { return ParseRef(); }
                if (IsNameStart(Peek())) { return ParseFunction(); }
                return ParseNumber();
            }
            finally { _depth--; }
        }

        // ---- Funciones (allow-list CERRADA) ----

        private static bool IsNameStart(char c) => char.IsAsciiLetter(c);

        private static bool IsNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '.' || c == '_';

        private decimal ParseFunction()
        {
            SkipWs();
            var start = _pos;
            while (_pos < _s.Length && IsNameChar(_s[_pos])) { _pos++; }
            var name = _s.Substring(start, _pos - start).ToUpperInvariant();
            SkipWs();
            if (!Match('(')) { throw new FormatException($"Se esperaba '(' despues de '{name}'."); }

            // SI() es PEREZOSA: solo se evalua la rama tomada (la otra se parsea en modo skip).
            if (name == "SI")
            {
                var cond = ParseExpression();
                ExpectSeparator();
                var takeThen = cond != 0m;
                var thenValue = ParseArgument(evaluate: takeThen);
                ExpectSeparator();
                var elseValue = ParseArgument(evaluate: !takeThen);
                CloseCall(name);
                return takeThen ? thenValue : elseValue;
            }

            var args = new List<decimal> { ParseExpression() };
            while (TryMatchSeparator()) { args.Add(ParseExpression()); }
            CloseCall(name);

            return name switch
            {
                // El MULTIPLO es PARAMETRO explicito (decision del usuario: nada de 1000 fijo).
                // Sin segundo argumento el multiplo es 1, o sea redondeo al entero.
                "REDONDEAR.SUPERIOR" => RoundToMultiple(args, name, static (v, m) => Math.Ceiling(v / m) * m),
                "REDONDEAR.INFERIOR" => RoundToMultiple(args, name, static (v, m) => Math.Floor(v / m) * m),
                "REDONDEAR" => RoundToMultiple(args, name,
                    static (v, m) => Math.Round(v / m, MidpointRounding.AwayFromZero) * m),
                "MIN" => args.Min(),
                "MAX" => args.Max(),
                _ => throw new FormatException($"Funcion no permitida: '{name}'."),
            };
        }

        /// <summary>
        /// Familia REDONDEAR: (valor; multiplo) con multiplo opcional = 1. Multiplo 0 devuelve 0
        /// (misma convencion que el Excel de origen: no hay rejilla donde encajar), y del multiplo
        /// negativo se toma la magnitud para que el sentido lo fije la funcion, no el signo.
        /// </summary>
        private static decimal RoundToMultiple(List<decimal> args, string name, Func<decimal, decimal, decimal> op)
        {
            if (args.Count is < 1 or > 2) { throw new FormatException($"{name} espera (valor; multiplo)."); }
            var value = args[0];
            var multiple = args.Count == 2 ? Math.Abs(args[1]) : 1m;
            if (multiple == 0m) { return 0m; }
            return op(value, multiple);
        }

        /// <summary>Parsea un argumento; si <paramref name="evaluate"/> es false lo descarta (rama no tomada).</summary>
        private decimal ParseArgument(bool evaluate)
        {
            if (evaluate) { return ParseExpression(); }
            _skip++;
            try { ParseExpression(); }
            finally { _skip--; }
            return 0m;
        }

        private void CloseCall(string name)
        {
            SkipWs();
            if (!Match(')')) { throw new FormatException($"Falta ')' al cerrar {name}."); }
        }

        // Separador de argumentos: ';' (convencion Excel es-CO). Se acepta ',' por tolerancia:
        // no hay ambiguedad porque los numeros usan '.' como separador decimal.
        private bool TryMatchSeparator() => Match(';') || Match(',');

        private void ExpectSeparator()
        {
            if (!TryMatchSeparator()) { throw new FormatException("Se esperaba ';' entre argumentos."); }
        }

        // ---- Referencias ----

        private decimal ParseRef()
        {
            _pos++; // consume '{'
            var end = _s.IndexOf('}', _pos);
            if (end < 0) { throw new FormatException("Referencia de campo sin cerrar."); }
            var code = _s.Substring(_pos, end - _pos).Trim();
            _pos = end + 1;
            if (code.Length == 0) { throw new FormatException("Referencia de campo vacia."); }

            // {#codigo} apunta al ENCABEZADO del formulario (p.ej. el IVA de la cotizacion), no a
            // la fila. Un encabezado ausente vale 0, igual que un campo de fila ausente.
            IReadOnlyDictionary<string, string?>? source = _values;
            if (code[0] == HeaderPrefix)
            {
                code = code[1..].Trim();
                source = _header;
                if (code.Length == 0) { throw new FormatException("Referencia de encabezado vacia."); }
            }

            var raw = source is not null && source.TryGetValue(code, out var v) ? v : null;
            return ToNumber(raw);
        }

        /// <summary>Campo vacio o no numerico = 0 (contrato del motor, no se rompe).</summary>
        private static decimal ToNumber(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) { return 0m; }
            // Acepta miles/decimales flexibles: quita separadores de miles comunes.
            var cleaned = raw.Replace(" ", "").Replace(",", "");
            return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var num) ? num : 0m;
        }

        private decimal ParseNumber()
        {
            SkipWs();
            var start = _pos;
            while (_pos < _s.Length && (char.IsDigit(_s[_pos]) || _s[_pos] == '.')) { _pos++; }
            if (_pos == start) { throw new FormatException($"Se esperaba un numero en la posicion {start}."); }
            var token = _s.Substring(start, _pos - start);
            if (!decimal.TryParse(token, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            {
                throw new FormatException($"Numero invalido: '{token}'.");
            }
            return value;
        }

        public void ExpectEnd()
        {
            SkipWs();
            if (_pos != _s.Length) { throw new FormatException($"Token inesperado: '{_s[_pos]}'."); }
        }

        private void SkipWs() { while (_pos < _s.Length && char.IsWhiteSpace(_s[_pos])) { _pos++; } }
        private char Peek() { SkipWs(); return _pos < _s.Length ? _s[_pos] : '\0'; }
        private bool Match(char c) { SkipWs(); if (_pos < _s.Length && _s[_pos] == c) { _pos++; return true; } return false; }
    }
}
