using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>
/// Contexto de una corrida del dron (motor de orquestacion, Ola 1). Guarda, por nombre, la SALIDA de cada
/// paso (ScrapeStep.OutputVar) ademas de las variables del flujo, y resuelve los placeholders de los pasos
/// siguientes contra ese contexto: sustituye <c>{{ruta}}</c> y <c>@@ruta@@</c> donde "ruta" admite acceso
/// con punto e indice a los JSON capturados, p.ej. <c>{{LISTADO.compras[0].btnDiligenciarId}}</c> o
/// <c>@@LISTADO.totalPaginas@@</c>. Un placeholder que NO se resuelve se deja TAL CUAL, para que los
/// scripts legacy que auto-defaultean (<c>if(x.indexOf('@@')!==-1){x='1';}</c>) sigan funcionando.
///
/// Los valores se guardan como texto; si son JSON se navegan al resolver. No es seguro para hilos (vive
/// dentro de una sola corrida/sesion de paso a paso).
/// </summary>
public sealed class ScrapeRunContext
{
    private readonly Dictionary<string, string> _vars;

    // {{ ... }} y @@ ... @@. El cuerpo es una ruta (no vacia, sin el delimitador correspondiente).
    private static readonly Regex Curly = new(@"\{\{\s*([^{}]+?)\s*\}\}", RegexOptions.Compiled);
    private static readonly Regex AtAt = new(@"@@\s*([^@]+?)\s*@@", RegexOptions.Compiled);

    public ScrapeRunContext(IReadOnlyDictionary<string, string>? seed = null)
        => _vars = seed is null ? new(StringComparer.Ordinal) : new(seed, StringComparer.Ordinal);

    /// <summary>Guarda (o reemplaza) un valor en el contexto. Vacio/null se ignora como nombre.</summary>
    public void Set(string? name, string? value)
    {
        if (string.IsNullOrWhiteSpace(name)) { return; }
        _vars[name.Trim()] = value ?? string.Empty;
    }

    /// <summary>Snapshot plano (nombre -> texto) para quien necesite las variables simples.</summary>
    public IReadOnlyDictionary<string, string> Flat => _vars;

    /// <summary>Sustituye en <paramref name="input"/> los <c>{{ruta}}</c> y <c>@@ruta@@</c> que se puedan
    /// resolver contra el contexto; deja intactos los que no.</summary>
    public string? Substitute(string? input) => SubstituteCore(input, escapeJs: false);

    /// <summary>
    /// Igual que <see cref="Substitute"/> pero para el JS de un paso: cada valor resuelto se ESCAPA para ir
    /// dentro de un literal de cadena JS (<c>'...'</c>, <c>"..."</c> o <c>`...`</c>). Todos los scripts usan
    /// los placeholders entre comillas (<c>var x='@@ROW.id@@'</c>, <c>txtClave: '{{Clave}}'</c>); sin escape,
    /// un valor con comilla o barra invertida (p.ej. una clave) rompia el script. Lo no resuelto queda igual.
    /// </summary>
    public string? SubstituteScript(string? input) => SubstituteCore(input, escapeJs: true);

    private string? SubstituteCore(string? input, bool escapeJs)
    {
        if (string.IsNullOrEmpty(input)) { return input; }
        var s = input;
        if (s.Contains("{{")) { s = Curly.Replace(s, m => TryResolve(m.Groups[1].Value, out var v) ? Out(v) : m.Value); }
        if (s.Contains("@@")) { s = AtAt.Replace(s, m => TryResolve(m.Groups[1].Value, out var v) ? Out(v) : m.Value); }
        return s;

        string Out(string v) => escapeJs ? JsEscape(v) : v;
    }

    /// <summary>
    /// Escapa un valor para insertarlo DENTRO de un literal de cadena JS de cualquier tipo de comilla:
    /// barra invertida, comillas simple/doble/backtick, saltos de linea, separadores U+2028/U+2029 y
    /// "&lt;/" (para no cerrar un &lt;script&gt; si el JS termina embebido en HTML). "${" queda inerte
    /// porque el "$" se escapa tambien.
    /// </summary>
    public static string JsEscape(string? value)
    {
        if (string.IsNullOrEmpty(value)) { return string.Empty; }
        var sb = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\'': sb.Append("\\'"); break;
                case '"': sb.Append("\\\""); break;
                case '`': sb.Append("\\`"); break;
                case '$': sb.Append("\\$"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case (char)0x2028: sb.Append("\\u2028"); break;
                case (char)0x2029: sb.Append("\\u2029"); break;
                case '<' when i + 1 < value.Length && value[i + 1] == '/': sb.Append("<\\/"); i++; break;
                default:
                    if (ch < ' ') { sb.Append("\\u").Append(((int)ch).ToString("x4")); }
                    else { sb.Append(ch); }
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Resuelve una ruta con punto/indice (p.ej. "LISTADO.compras[0].btnDiligenciarId" o "a.b.0.c")
    /// sobre el contexto. El primer segmento es el nombre de la variable; el resto navega su JSON.</summary>
    public bool TryResolve(string path, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(path)) { return false; }
        var segs = SplitPath(path);
        if (segs.Count == 0 || !_vars.TryGetValue(segs[0], out var raw)) { return false; }

        // Sin mas segmentos: el valor crudo.
        if (segs.Count == 1) { value = raw; return true; }
        if (string.IsNullOrWhiteSpace(raw)) { return false; }

        JsonElement cur;
        try { using var doc = JsonDocument.Parse(raw); cur = doc.RootElement.Clone(); }
        catch { return false; } // no es JSON: no se puede navegar.

        for (var i = 1; i < segs.Count; i++)
        {
            var seg = segs[i];
            if (cur.ValueKind == JsonValueKind.Array && int.TryParse(seg, out var idx))
            {
                if (idx < 0 || idx >= cur.GetArrayLength()) { return false; }
                cur = cur[idx];
            }
            else if (cur.ValueKind == JsonValueKind.Object && cur.TryGetProperty(seg, out var next))
            {
                cur = next;
            }
            else { return false; }
        }

        value = cur.ValueKind switch
        {
            JsonValueKind.String => cur.GetString() ?? string.Empty,
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Object or JsonValueKind.Array => cur.GetRawText(),
            _ => cur.ToString()
        };
        return true;
    }

    /// <summary>Parte "a.b[0].c" en ["a","b","0","c"] (normaliza [n] a segmento n).</summary>
    private static List<string> SplitPath(string path)
    {
        var norm = new StringBuilder(path.Length);
        foreach (var ch in path.Trim())
        {
            if (ch == '[') { norm.Append('.'); }
            else if (ch == ']') { /* omit */ }
            else { norm.Append(ch); }
        }
        return norm.ToString().Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}
