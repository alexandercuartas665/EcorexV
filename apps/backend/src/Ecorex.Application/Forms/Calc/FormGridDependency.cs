namespace Ecorex.Application.Forms.Calc;

/// <summary>
/// CAP 2 (fase 2): ordena las grillas (GridDetail) de un formulario por DEPENDENCIA cross-grid, para que
/// el recalculo compute primero las grillas ORIGEN y luego las que las referencian. Una grilla G depende
/// de H si alguna columna de G tiene un `crossGrid` hacia H (por field code). Puro y compartido por el
/// recalculo del servidor (autoritativo) y el del cliente. Estable: respeta el orden de entrada cuando no
/// hay dependencia. Aristas hacia grillas ausentes y CICLOS se ignoran (los nodos en ciclo quedan al final,
/// en orden de entrada) para no colgar ni perder grillas.
/// </summary>
public static class FormGridDependency
{
    /// <summary>Devuelve los field codes en orden de dependencia (las referenciadas primero).</summary>
    public static IReadOnlyList<string> Order(IReadOnlyList<(string FieldCode, IReadOnlyList<string> DependsOn)> grids)
    {
        var present = new HashSet<string>(grids.Select(g => g.FieldCode), StringComparer.OrdinalIgnoreCase);
        // in-degree = cuantas dependencias (presentes) le faltan a cada grilla antes de poder computarse.
        var indegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var dependents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase); // dep -> quienes la usan
        foreach (var (code, _) in grids) { indegree[code] = 0; }
        foreach (var (code, dependsOn) in grids)
        {
            foreach (var dep in dependsOn.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!present.Contains(dep) || string.Equals(dep, code, StringComparison.OrdinalIgnoreCase)) { continue; }
                indegree[code]++;
                if (!dependents.TryGetValue(dep, out var list)) { list = new List<string>(); dependents[dep] = list; }
                list.Add(code);
            }
        }

        var order = grids.Select(g => g.FieldCode).ToList(); // orden de entrada, para estabilidad
        var result = new List<string>(order.Count);
        var doneSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Kahn estable: en cada vuelta toma, en orden de entrada, las de in-degree 0 aun no emitidas.
        bool progress = true;
        while (progress && result.Count < order.Count)
        {
            progress = false;
            foreach (var code in order)
            {
                if (doneSet.Contains(code) || indegree[code] != 0) { continue; }
                result.Add(code); doneSet.Add(code); progress = true;
                if (dependents.TryGetValue(code, out var deps))
                {
                    foreach (var d in deps) { if (indegree[d] > 0) { indegree[d]--; } }
                }
            }
        }
        // Ciclos: lo que quede (in-degree > 0 por dependencia mutua) se agrega en orden de entrada.
        foreach (var code in order) { if (!doneSet.Contains(code)) { result.Add(code); } }
        return result;
    }
}
