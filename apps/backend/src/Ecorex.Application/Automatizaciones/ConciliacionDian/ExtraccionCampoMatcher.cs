namespace Ecorex.Application.Automatizaciones.ConciliacionDian;

/// <summary>
/// Valida contra un contenedor los valores CANDIDATOS que la IA extrajo del texto de una factura (ADR-0191).
/// La IA solo propone; esta clase decide que existe de verdad. Dos niveles de match:
/// 1) EXACTO: el candidato, dejando solo letras y numeros en mayusculas, es igual a la columna de busqueda
///    ("OMP-0012387" == "OMP0012387"). Si hay prefijos validos configurados, tambien lo acotan.
/// 2) POR NUMERO: si no hay exacto, compara los digitos sin ceros a la izquierda ("12387" == "OMP0012387"),
///    para cuando el proveedor omitio o escribio mal el prefijo. Se limita a los prefijos validos configurados y,
///    si el candidato trae un prefijo que existe en el contenedor, prefiere ese. Exige al menos 4 digitos para no
///    emparejar numeros cortos sueltos.
/// Devuelve los valores (columna a devolver) distintos: 0 = no existe, 1 = validado, varios = el usuario elige.
/// Pura y sin BD: el servicio la construye con las filas del contenedor (testeable).
/// </summary>
public sealed class ExtraccionCampoMatcher
{
    public const int MinDigitos = 4;

    private readonly Dictionary<string, List<(string Prefijo, string Valor)>> _exact = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(string Prefijo, string Valor)>> _num = new(StringComparer.Ordinal);
    private readonly HashSet<string> _prefijos;

    /// <summary>Filas del contenedor: (valor de la columna de busqueda, valor de la columna a devolver).</summary>
    public ExtraccionCampoMatcher(IEnumerable<(string? Busqueda, string? Devolver)> filas, string? prefijos)
    {
        _prefijos = ParsePrefijos(prefijos);
        foreach (var (busqueda, devolver) in filas)
        {
            var valor = (devolver ?? string.Empty).Trim();
            var key = Alnum(busqueda);
            if (valor.Length == 0 || key.Length == 0) { continue; }
            var (pre, dig) = Split(key);
            AddDistinct(_exact, key, (pre, valor));
            if (dig.Length >= MinDigitos)
            {
                if (!_num.TryGetValue(dig, out var lst)) { lst = new(); _num[dig] = lst; }
                if (!lst.Any(x => x.Valor == valor)) { lst.Add((pre, valor)); }
            }
        }
    }

    /// <summary>Cantidad de claves distintas indexadas (diagnostico).</summary>
    public int Claves => _exact.Count;

    /// <summary>Valores del contenedor que corresponden a los candidatos (distintos, en orden de aparicion).</summary>
    public IReadOnlyList<string> Match(IEnumerable<string?> candidatos)
    {
        var res = new List<string>();
        foreach (var c in candidatos ?? Array.Empty<string?>())
        {
            var key = Alnum(c);
            if (key.Length == 0) { continue; }
            if (_exact.TryGetValue(key, out var exactos))
            {
                // Los prefijos validos tambien acotan el match exacto (un OSE no pasa si solo se permite OMP).
                foreach (var x in exactos.Where(x => _prefijos.Count == 0 || _prefijos.Contains(x.Prefijo)))
                {
                    if (!res.Contains(x.Valor)) { res.Add(x.Valor); }
                }
                continue;
            }
            var (pre, dig) = Split(key);
            if (dig.Length < MinDigitos || !_num.TryGetValue(dig, out var lst)) { continue; }
            IEnumerable<(string Prefijo, string Valor)> sel = lst;
            if (_prefijos.Count > 0) { sel = sel.Where(x => _prefijos.Contains(x.Prefijo)); }
            if (pre.Length > 0)
            {
                var mismo = sel.Where(x => x.Prefijo == pre).ToList();
                if (mismo.Count > 0) { sel = mismo; }
            }
            foreach (var x in sel) { if (!res.Contains(x.Valor)) { res.Add(x.Valor); } }
        }
        return res;
    }

    /// <summary>Solo letras y numeros, en mayusculas.</summary>
    public static string Alnum(string? s) =>
        new string((s ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    /// <summary>Prefijo = letras iniciales; numero = todos los digitos sin ceros a la izquierda.</summary>
    public static (string Prefijo, string Numero) Split(string alnumUpper)
    {
        var i = 0;
        while (i < alnumUpper.Length && char.IsLetter(alnumUpper[i])) { i++; }
        var pre = alnumUpper[..i];
        var dig = new string(alnumUpper.Where(char.IsDigit).ToArray()).TrimStart('0');
        return (pre, dig);
    }

    private static HashSet<string> ParsePrefijos(string? s) =>
        (s ?? string.Empty).Split(new[] { ',', ';', ' ', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Alnum).Where(p => p.Length > 0).ToHashSet(StringComparer.Ordinal);

    private static void AddDistinct(Dictionary<string, List<(string Prefijo, string Valor)>> d, string key, (string Prefijo, string Valor) item)
    {
        if (!d.TryGetValue(key, out var lst)) { lst = new(); d[key] = lst; }
        if (!lst.Any(x => x.Valor == item.Valor)) { lst.Add(item); }
    }
}
