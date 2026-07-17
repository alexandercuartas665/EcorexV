using System.Globalization;
using System.Text;

namespace Ecorex.Application.Formulas;

/// <summary>Resultado de parsear una formula: o el arbol, o el motivo por el que no se pudo.</summary>
/// <param name="Node">Arbol listo para evaluar. Null si hubo error.</param>
/// <param name="Error">Mensaje en lenguaje llano, con la posicion. Null si todo bien.</param>
/// <param name="References">Claves de los campos referenciados, sin repetir. Sirve para validar y
/// para ordenar las dependencias entre calculados.</param>
public sealed record FormulaParseResult(FormulaNode? Node, string? Error, IReadOnlyList<string> References)
{
    public bool IsOk => Node is not null;

    internal static FormulaParseResult Fail(string message, int position)
        => new(null, $"{message} (posicion {position + 1}).", Array.Empty<string>());
}

/// <summary>
/// Motor de expresiones de los campos calculados (ADR-0029). Acotado a proposito: aritmetica sobre
/// campos propios y cinco funciones. No hay acceso a tipos, ni a I/O, ni a nada del sistema, asi que
/// una formula escrita por un usuario no puede hacer dano; esa es la razon de no usar una libreria
/// de expresiones generica.
///
/// Gramatica:
///   expr    := term (('+' | '-') term)*
///   term    := factor (('*' | '/') factor)*
///   factor  := '-' factor | primary
///   primary := numero | '{' clave '}' | funcion '(' args ')' | '(' expr ')'
///
/// Todo se evalua en decimal. Un campo vacio o no numerico vale 0, para que la ficha no se rompa
/// mientras se captura. Dividir por cero devuelve null (campo vacio), no una excepcion.
/// Solo ASCII.
/// </summary>
public static class FormulaEngine
{
    /// <summary>Funciones admitidas, con su aridad (min, max). int.MaxValue = variadica.</summary>
    private static readonly Dictionary<string, (int Min, int Max)> Functions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ROUND"] = (2, 2),
        ["MIN"] = (1, int.MaxValue),
        ["MAX"] = (1, int.MaxValue),
        ["ABS"] = (1, 1),
        ["SUM"] = (1, int.MaxValue),
    };

    public static IReadOnlyCollection<string> FunctionNames => Functions.Keys;

    /// <summary>Longitud maxima aceptada, alineada con la columna (ver EcorexDbContext).</summary>
    public const int MaxLength = 1000;

    public static FormulaParseResult Parse(string? formula)
    {
        if (string.IsNullOrWhiteSpace(formula))
        {
            return new FormulaParseResult(null, "La formula esta vacia.", Array.Empty<string>());
        }
        if (formula.Length > MaxLength)
        {
            return new FormulaParseResult(null, $"La formula supera los {MaxLength} caracteres.", Array.Empty<string>());
        }

        var parser = new Parser(formula);
        return parser.ParseAll();
    }

    /// <summary>
    /// Evalua el arbol. <paramref name="resolve"/> entrega el valor de cada campo referenciado
    /// (0 si esta vacio o no es numero). Devuelve null si la operacion no tiene resultado valido
    /// (division por cero), y el llamador deja el campo vacio.
    /// </summary>
    public static decimal? Evaluate(FormulaNode node, Func<string, decimal> resolve)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(resolve);
        try
        {
            return Eval(node, resolve);
        }
        catch (DivideByZeroException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    /// <summary>Interpreta un valor crudo como numero. Vacio o no numerico = 0.</summary>
    public static decimal ToNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return 0m; }

        // Invariante primero (asi es como se guarda). Si no, se limpia lo que teclea la gente:
        // separadores de miles y simbolos de moneda.
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)) { return n; }

        var cleaned = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (char.IsDigit(c) || c is '-' or '.' or ',') { cleaned.Append(c); }
        }
        var text = cleaned.ToString();
        if (text.Length == 0) { return 0m; }

        // "1.234,56" (es-CO) -> la coma es el decimal; "1,234.56" (invariante) -> el punto lo es.
        var lastDot = text.LastIndexOf('.');
        var lastComma = text.LastIndexOf(',');
        if (lastComma > lastDot)
        {
            text = text.Replace(".", string.Empty).Replace(',', '.');
        }
        else
        {
            text = text.Replace(",", string.Empty);
        }

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var m) ? m : 0m;
    }

    /// <summary>Serializa el resultado como se guarda: invariante, sin ceros de mas.</summary>
    public static string Format(decimal value)
        => value.ToString("0.##########", CultureInfo.InvariantCulture);

    private static decimal Eval(FormulaNode node, Func<string, decimal> resolve) => node switch
    {
        FormulaNumber n => n.Value,
        FormulaRef r => resolve(r.FieldKey),
        FormulaNegate g => -Eval(g.Operand, resolve),
        FormulaBinary b => EvalBinary(b, resolve),
        FormulaCall c => EvalCall(c, resolve),
        _ => throw new InvalidOperationException($"Nodo de formula desconocido: {node.GetType().Name}.")
    };

    private static decimal EvalBinary(FormulaBinary b, Func<string, decimal> resolve)
    {
        var left = Eval(b.Left, resolve);
        var right = Eval(b.Right, resolve);
        return b.Op switch
        {
            '+' => left + right,
            '-' => left - right,
            '*' => left * right,
            '/' => right == 0m ? throw new DivideByZeroException() : left / right,
            _ => throw new InvalidOperationException($"Operador desconocido: {b.Op}.")
        };
    }

    private static decimal EvalCall(FormulaCall c, Func<string, decimal> resolve)
    {
        var args = c.Args.Select(a => Eval(a, resolve)).ToList();
        return c.Name switch
        {
            "ROUND" => Math.Round(args[0], (int)Math.Clamp(args[1], 0, 28), MidpointRounding.AwayFromZero),
            "MIN" => args.Min(),
            "MAX" => args.Max(),
            "ABS" => Math.Abs(args[0]),
            "SUM" => args.Sum(),
            _ => throw new InvalidOperationException($"Funcion desconocida: {c.Name}.")
        };
    }

    /// <summary>Descenso recursivo sobre el texto. Guarda la posicion para poder senalar el error.</summary>
    private sealed class Parser(string text)
    {
        private readonly string _text = text;
        private readonly List<string> _refs = new();
        private int _pos;
        private string? _error;
        private int _errorPos;

        public FormulaParseResult ParseAll()
        {
            var node = ParseExpr();
            if (_error is not null) { return FormulaParseResult.Fail(_error, _errorPos); }

            SkipWhite();
            if (_pos < _text.Length)
            {
                return FormulaParseResult.Fail($"Sobra texto tras la formula: '{_text[_pos]}'", _pos);
            }
            return new FormulaParseResult(node, null, _refs.Distinct(StringComparer.Ordinal).ToList());
        }

        private FormulaNode? ParseExpr()
        {
            var left = ParseTerm();
            if (left is null) { return null; }

            while (true)
            {
                SkipWhite();
                if (_pos >= _text.Length) { return left; }
                var op = _text[_pos];
                if (op is not ('+' or '-')) { return left; }
                _pos++;
                var right = ParseTerm();
                if (right is null) { return null; }
                left = new FormulaBinary(op, left, right);
            }
        }

        private FormulaNode? ParseTerm()
        {
            var left = ParseFactor();
            if (left is null) { return null; }

            while (true)
            {
                SkipWhite();
                if (_pos >= _text.Length) { return left; }
                var op = _text[_pos];
                if (op is not ('*' or '/')) { return left; }
                _pos++;
                var right = ParseFactor();
                if (right is null) { return null; }
                left = new FormulaBinary(op, left, right);
            }
        }

        private FormulaNode? ParseFactor()
        {
            SkipWhite();
            if (_pos < _text.Length && _text[_pos] == '-')
            {
                _pos++;
                var operand = ParseFactor();
                return operand is null ? null : new FormulaNegate(operand);
            }
            return ParsePrimary();
        }

        private FormulaNode? ParsePrimary()
        {
            SkipWhite();
            if (_pos >= _text.Length) { return Error("Falta un valor al final de la formula"); }

            var c = _text[_pos];

            if (c == '(')
            {
                _pos++;
                var inner = ParseExpr();
                if (inner is null) { return null; }
                SkipWhite();
                if (_pos >= _text.Length || _text[_pos] != ')') { return Error("Falta cerrar el parentesis"); }
                _pos++;
                return inner;
            }

            if (c == '{') { return ParseRef(); }
            if (char.IsDigit(c) || c == '.') { return ParseNumber(); }
            if (char.IsLetter(c)) { return ParseCall(); }

            return Error($"No se esperaba '{c}'");
        }

        private FormulaNode? ParseRef()
        {
            var start = _pos;
            _pos++; // {
            var end = _text.IndexOf('}', _pos);
            if (end < 0) { return Error("Falta cerrar la llave del campo", start); }

            var key = _text[_pos..end].Trim();
            if (key.Length == 0) { return Error("Hay un campo sin nombre: {}", start); }

            _pos = end + 1;
            _refs.Add(key);
            return new FormulaRef(key);
        }

        private FormulaNode? ParseNumber()
        {
            var start = _pos;
            while (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] == '.')) { _pos++; }
            var raw = _text[start.._pos];
            if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            {
                return Error($"'{raw}' no es un numero valido (usa punto decimal)", start);
            }
            return new FormulaNumber(value);
        }

        private FormulaNode? ParseCall()
        {
            var start = _pos;
            while (_pos < _text.Length && char.IsLetter(_text[_pos])) { _pos++; }
            var name = _text[start.._pos];

            if (!Functions.TryGetValue(name, out var arity))
            {
                return Error($"No existe la funcion '{name}'. Disponibles: {string.Join(", ", Functions.Keys)}", start);
            }

            SkipWhite();
            if (_pos >= _text.Length || _text[_pos] != '(')
            {
                return Error($"Falta el parentesis de la funcion {name}", start);
            }
            _pos++;

            var args = new List<FormulaNode>();
            SkipWhite();
            if (_pos < _text.Length && _text[_pos] == ')')
            {
                _pos++;
            }
            else
            {
                while (true)
                {
                    var arg = ParseExpr();
                    if (arg is null) { return null; }
                    args.Add(arg);

                    SkipWhite();
                    if (_pos >= _text.Length) { return Error($"Falta cerrar la funcion {name}", start); }
                    if (_text[_pos] == ',') { _pos++; continue; }
                    if (_text[_pos] == ')') { _pos++; break; }
                    return Error($"Se esperaba ',' o ')' en la funcion {name}");
                }
            }

            var upper = name.ToUpperInvariant();
            if (args.Count < arity.Min || args.Count > arity.Max)
            {
                var esperado = arity.Max == int.MaxValue
                    ? $"al menos {arity.Min}"
                    : arity.Min == arity.Max ? $"{arity.Min}" : $"entre {arity.Min} y {arity.Max}";
                return Error($"{upper} espera {esperado} argumento(s) y recibio {args.Count}", start);
            }

            return new FormulaCall(upper, args);
        }

        private void SkipWhite()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) { _pos++; }
        }

        /// <summary>Registra el primer error y corta: el resto del arbol ya no importa.</summary>
        private FormulaNode? Error(string message, int? at = null)
        {
            if (_error is null)
            {
                _error = message;
                _errorPos = at ?? _pos;
            }
            return null;
        }
    }
}
