using Ecorex.Domain.Enums;

namespace Ecorex.Application.Workflows;

/// <summary>
/// Auto-layout de respaldo para XML BPMN sin DI (ADR-0022): distribuye los nodos por
/// niveles BFS desde el startEvent (columnas hacia la derecha, hermanos apilados),
/// con los tamanos por defecto del prototipo. Determinista: mismo grafo -> mismas
/// coordenadas (importa para el test de round-trip).
/// </summary>
public static class WorkflowAutoLayout
{
    private const int OriginX = 60;
    private const int OriginY = 90;
    private const int ColumnWidth = 190;
    private const int RowHeight = 120;

    /// <summary>
    /// Calcula (X, Y, W, H) por BpmnElementId. Nodos inalcanzables desde el start quedan
    /// en columnas posteriores al nivel maximo alcanzado (orden estable por StepNumber).
    /// </summary>
    public static IReadOnlyDictionary<string, (int X, int Y, int W, int H)> Compute(
        IReadOnlyList<(string Id, WorkflowNodeType Type, int Step)> nodes,
        IReadOnlyList<(string SourceId, string TargetId)> edges)
    {
        var level = new Dictionary<string, int>(StringComparer.Ordinal);
        var outgoing = edges
            .GroupBy(e => e.SourceId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.TargetId).ToList(), StringComparer.Ordinal);

        var start = nodes.FirstOrDefault(n => n.Type == WorkflowNodeType.StartEvent);
        var queue = new Queue<string>();
        if (start.Id is not null)
        {
            level[start.Id] = 0;
            queue.Enqueue(start.Id);
        }
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!outgoing.TryGetValue(current, out var targets))
            {
                continue;
            }
            foreach (var target in targets)
            {
                if (!level.ContainsKey(target))
                {
                    level[target] = level[current] + 1;
                    queue.Enqueue(target);
                }
            }
        }

        // Inalcanzables: columnas extra tras el nivel maximo, en orden de StepNumber.
        var maxLevel = level.Count > 0 ? level.Values.Max() : -1;
        foreach (var node in nodes.OrderBy(n => n.Step))
        {
            if (!level.ContainsKey(node.Id))
            {
                level[node.Id] = ++maxLevel;
            }
        }

        var result = new Dictionary<string, (int X, int Y, int W, int H)>(StringComparer.Ordinal);
        foreach (var group in nodes.GroupBy(n => level[n.Id]).OrderBy(g => g.Key))
        {
            var row = 0;
            foreach (var node in group.OrderBy(n => n.Step))
            {
                var (w, h) = BpmnXmlWriter.DefaultSize(node.Type);
                // Centro vertical comun por columna: los circulos/diamantes no quedan
                // desalineados frente a las tareas (mas altas).
                var x = OriginX + group.Key * ColumnWidth;
                var y = OriginY + row * RowHeight + (64 - h) / 2;
                result[node.Id] = (x, y, w, h);
                row++;
            }
        }
        return result;
    }
}
