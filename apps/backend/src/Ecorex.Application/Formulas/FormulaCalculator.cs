namespace Ecorex.Application.Formulas;

/// <summary>Un campo calculado, visto por el calculador: su clave y su formula.</summary>
public sealed record CalculatedField(string FieldKey, string Formula);

/// <summary>
/// Resuelve un CONJUNTO de campos calculados sobre unos valores (ADR-0029). Su razon de ser es lo
/// que el proyecto hermano no hace: un calculado puede depender de otro, asi que hay que evaluarlos
/// en orden de dependencia y rechazar los ciclos en vez de dar un resultado enganoso.
///
/// Es logica pura (no sabe de EF ni de HTTP), asi que la usan por igual Terceros e Items y se cubre
/// con tests unitarios. Solo ASCII.
/// </summary>
public static class FormulaCalculator
{
    /// <summary>
    /// Busca un ciclo entre los calculados. Devuelve null si no hay; si hay, el mensaje nombra el
    /// recorrido (ej. "total -> iva -> total") para que se pueda arreglar sin adivinar.
    /// </summary>
    public static string? FindCycle(IEnumerable<CalculatedField> fields)
    {
        var graph = BuildGraph(fields, out _);
        var state = new Dictionary<string, int>(StringComparer.Ordinal); // 0=sin ver, 1=en curso, 2=cerrado
        var path = new List<string>();

        foreach (var key in graph.Keys)
        {
            var cycle = Visit(key, graph, state, path);
            if (cycle is not null) { return cycle; }
        }
        return null;
    }

    /// <summary>
    /// Evalua todos los calculados y devuelve sus valores ya formateados, listos para guardar.
    /// <paramref name="values"/> son los valores crudos por clave (los capturados). Un calculado que
    /// no se puede resolver (division por cero, formula rota, ciclo) queda con null = campo vacio.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> EvaluateAll(
        IEnumerable<CalculatedField> fields,
        IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var graph = BuildGraph(fields, out var parsed);
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (parsed.Count == 0) { return result; }

        // Si hay un ciclo, ningun calculado del conjunto es de fiar: se dejan todos vacios en vez de
        // publicar un numero que depende del orden en que se recorrio el grafo.
        if (FindCycle(fields) is not null)
        {
            foreach (var key in parsed.Keys) { result[key] = null; }
            return result;
        }

        var computed = new Dictionary<string, decimal?>(StringComparer.Ordinal);

        decimal Resolve(string key)
        {
            // Referencia a otro calculado: se resuelve primero (el grafo no tiene ciclos aqui).
            if (parsed.ContainsKey(key))
            {
                if (!computed.TryGetValue(key, out var cached))
                {
                    cached = Compute(key);
                    computed[key] = cached;
                }
                return cached ?? 0m;
            }
            return FormulaEngine.ToNumber(values.GetValueOrDefault(key));
        }

        decimal? Compute(string key)
        {
            var node = parsed[key];
            return node is null ? null : FormulaEngine.Evaluate(node, Resolve);
        }

        foreach (var key in parsed.Keys)
        {
            if (!computed.TryGetValue(key, out var value))
            {
                value = Compute(key);
                computed[key] = value;
            }
            result[key] = value is decimal d ? FormulaEngine.Format(d) : null;
        }

        return result;
    }

    /// <summary>Grafo clave -> claves de las que depende, quedandose solo con aristas entre calculados.</summary>
    private static Dictionary<string, List<string>> BuildGraph(
        IEnumerable<CalculatedField> fields,
        out Dictionary<string, FormulaNode?> parsed)
    {
        ArgumentNullException.ThrowIfNull(fields);

        parsed = new Dictionary<string, FormulaNode?>(StringComparer.Ordinal);
        var refs = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var f in fields)
        {
            if (string.IsNullOrWhiteSpace(f.FieldKey)) { continue; }
            var r = FormulaEngine.Parse(f.Formula);
            parsed[f.FieldKey] = r.Node;              // null = formula rota: el campo queda vacio
            refs[f.FieldKey] = r.References;
        }

        var graph = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (key, deps) in refs)
        {
            // Solo importan las dependencias hacia otros calculados: las de campos normales son hojas.
            graph[key] = deps.Where(parsed.ContainsKey).Distinct(StringComparer.Ordinal).ToList();
        }
        return graph;
    }

    private static string? Visit(
        string key,
        Dictionary<string, List<string>> graph,
        Dictionary<string, int> state,
        List<string> path)
    {
        var current = state.GetValueOrDefault(key);
        if (current == 2) { return null; }
        if (current == 1)
        {
            // Ya estaba en el camino: se cerro el ciclo. Se recorta desde donde empezo.
            var from = path.IndexOf(key);
            var loop = path.Skip(from).Append(key);
            return string.Join(" -> ", loop);
        }

        state[key] = 1;
        path.Add(key);
        foreach (var dep in graph.GetValueOrDefault(key) ?? new List<string>())
        {
            var cycle = Visit(dep, graph, state, path);
            if (cycle is not null) { return cycle; }
        }
        path.RemoveAt(path.Count - 1);
        state[key] = 2;
        return null;
    }
}
