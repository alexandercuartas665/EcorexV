using System.Globalization;

namespace Ecorex.Application.Forms.Calc;

/// <summary>
/// Evaluador de expresiones de campo calculado (Formularios avanzados, ola F2; doc 01 D5).
/// SANDBOX TIPADO con allow-list: solo numeros, referencias a campos <c>{codigo}</c> y los
/// operadores + - * / con parentesis y menos unario. NO ejecuta codigo, NO usa reflexion, NO
/// llama funciones del host (se evita el RCE de facto del legacy). Es la MISMA logica que corre
/// en cliente (renderer, UX inmediata) y en servidor (revalidacion al guardar; fuente de verdad).
/// Un campo referenciado vacio/no numerico cuenta como 0; una expresion invalida devuelve null
/// (el llamador decide como mostrarlo) en vez de lanzar.
/// </summary>
public static class FormExpressionEvaluator
{
    /// <summary>
    /// Evalua <paramref name="expression"/> resolviendo cada <c>{codigo}</c> con
    /// <paramref name="values"/>. Devuelve el resultado o null si la expresion es invalida
    /// (parentesis desbalanceados, token desconocido, division por cero, etc.).
    /// </summary>
    public static decimal? Evaluate(string? expression, IReadOnlyDictionary<string, string?> values)
    {
        if (string.IsNullOrWhiteSpace(expression)) { return null; }
        try
        {
            var parser = new Parser(expression, values);
            var result = parser.ParseExpression();
            parser.ExpectEnd();
            return result;
        }
        catch (FormatException) { return null; }
        catch (DivideByZeroException) { return null; }
    }

    /// <summary>Extrae los codigos de campo referenciados (<c>{codigo}</c>) por la expresion.</summary>
    public static IReadOnlyList<string> ReferencedFields(string? expression)
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
                if (code.Length > 0 && !refs.Contains(code, StringComparer.Ordinal)) { refs.Add(code); }
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
        // Valores ficticios (0) solo para probar la forma; la division por cero no invalida la forma.
        var probe = ReferencedFields(expression).ToDictionary(c => c, _ => (string?)"1", StringComparer.Ordinal);
        try
        {
            var parser = new Parser(expression, probe);
            parser.ParseExpression();
            parser.ExpectEnd();
            return null;
        }
        catch (FormatException ex) { return ex.Message; }
        catch (DivideByZeroException) { return null; }
    }

    /// <summary>
    /// Parser de descenso recursivo minimo: expr := term (('+'|'-') term)*; term := factor
    /// (('*'|'/') factor)*; factor := number | '{'ref'}' | '(' expr ')' | '-' factor.
    /// </summary>
    private sealed class Parser
    {
        private readonly string _s;
        private readonly IReadOnlyDictionary<string, string?> _values;
        private int _pos;

        public Parser(string s, IReadOnlyDictionary<string, string?> values)
        {
            _s = s;
            _values = values;
        }

        public decimal ParseExpression()
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
                    if (divisor == 0m) { throw new DivideByZeroException(); }
                    value /= divisor;
                }
                else { break; }
            }
            return value;
        }

        private decimal ParseFactor()
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
            return ParseNumber();
        }

        private decimal ParseRef()
        {
            _pos++; // consume '{'
            var end = _s.IndexOf('}', _pos);
            if (end < 0) { throw new FormatException("Referencia de campo sin cerrar."); }
            var code = _s.Substring(_pos, end - _pos).Trim();
            _pos = end + 1;
            var raw = _values.TryGetValue(code, out var v) ? v : null;
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
            return decimal.Parse(token, NumberStyles.Any, CultureInfo.InvariantCulture);
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
