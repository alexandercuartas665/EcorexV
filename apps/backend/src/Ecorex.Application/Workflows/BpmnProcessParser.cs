using System.Xml;
using System.Xml.Linq;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Workflows;

/// <summary>
/// Nodo BPMN parseado (aun sin persistir). X/Y/W/H vienen del diagrama
/// (bpmndi:BPMNShape/dc:Bounds) y son null si el XML no trae DI para el elemento:
/// en ese caso el importador aplica auto-layout (ADR-0022).
/// </summary>
public sealed record ParsedBpmnNode(
    string BpmnElementId, string? Name, WorkflowNodeType NodeType, int StepNumber,
    int? X = null, int? Y = null, int? W = null, int? H = null);

/// <summary>Arista BPMN parseada (aun sin persistir; referencias por id de elemento).</summary>
public sealed record ParsedBpmnEdge(string? BpmnElementId, string SourceRef, string TargetRef, string? Name, string? ConditionExpression);

/// <summary>Resultado del parseo: o el grafo valido, o la lista de errores de validacion.</summary>
public sealed record ParsedBpmnProcess(
    IReadOnlyList<ParsedBpmnNode> Nodes,
    IReadOnlyList<ParsedBpmnEdge> Edges,
    IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Parser de XML BPMN 2.0 estandar (namespace OMG) para el WorkflowEngine. Reconoce el
/// subconjunto que ejecuta el motor: startEvent, task, exclusiveGateway, endEvent y
/// sequenceFlow. Del DI (diagrama) solo lee las coordenadas de los BPMNShape para el
/// canvas del editor (ADR-0022); anotaciones y asociaciones se ignoran. El XML original
/// NUNCA se modifica al importar (round-trip con bpmn.io); el editor propio lo REGENERA
/// completo (BpmnXmlWriter) al guardar cambios del grafo.
/// Validaciones: exactamente 1 startEvent, al menos 1 endEvent, ids unicos y aristas
/// que apuntan a nodos existentes.
/// </summary>
public static class BpmnProcessParser
{
    /// <summary>Namespace del modelo BPMN 2.0 (el prefijo bpmn:/bpmn2: es irrelevante).</summary>
    public static readonly XNamespace Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";

    /// <summary>Namespace del diagrama BPMN (bpmndi).</summary>
    public static readonly XNamespace BpmnDi = "http://www.omg.org/spec/BPMN/20100524/DI";

    /// <summary>Namespace de los Bounds del diagrama (dc).</summary>
    public static readonly XNamespace Dc = "http://www.omg.org/spec/DD/20100524/DC";

    /// <summary>Namespace de los waypoints del diagrama (di).</summary>
    public static readonly XNamespace Di = "http://www.omg.org/spec/DD/20100524/DI";

    public static ParsedBpmnProcess Parse(string? bpmnXml)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(bpmnXml))
        {
            return new ParsedBpmnProcess([], [], ["El XML BPMN esta vacio."]);
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(bpmnXml);
        }
        catch (XmlException ex)
        {
            return new ParsedBpmnProcess([], [], [$"XML invalido: {ex.Message}"]);
        }

        var process = doc.Descendants(Bpmn + "process").FirstOrDefault();
        if (process is null)
        {
            return new ParsedBpmnProcess([], [], ["El XML no contiene un bpmn:process (namespace BPMN 2.0)."]);
        }

        // Coordenadas del diagrama (bpmndi:BPMNShape/dc:Bounds) indexadas por bpmnElement.
        var bounds = new Dictionary<string, (int X, int Y, int W, int H)>(StringComparer.Ordinal);
        foreach (var shape in doc.Descendants(BpmnDi + "BPMNShape"))
        {
            var elementId = (string?)shape.Attribute("bpmnElement");
            var b = shape.Element(Dc + "Bounds");
            if (string.IsNullOrWhiteSpace(elementId) || b is null)
            {
                continue;
            }
            if (TryRound((string?)b.Attribute("x"), out var x)
                && TryRound((string?)b.Attribute("y"), out var y)
                && TryRound((string?)b.Attribute("width"), out var w)
                && TryRound((string?)b.Attribute("height"), out var h))
            {
                bounds[elementId] = (x, y, w, h);
            }
        }

        var nodes = new List<ParsedBpmnNode>();
        var edges = new List<ParsedBpmnEdge>();
        var step = 0;
        foreach (var element in process.Elements())
        {
            if (element.Name.Namespace != Bpmn) { continue; }

            var localName = element.Name.LocalName;
            var nodeType = localName switch
            {
                "startEvent" => WorkflowNodeType.StartEvent,
                "task" => WorkflowNodeType.Task,
                "exclusiveGateway" => WorkflowNodeType.ExclusiveGateway,
                "endEvent" => WorkflowNodeType.EndEvent,
                _ => (WorkflowNodeType?)null
            };

            if (nodeType is WorkflowNodeType type)
            {
                var id = (string?)element.Attribute("id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    errors.Add($"Un elemento {localName} no tiene atributo id.");
                    continue;
                }
                step++;
                var shape = bounds.TryGetValue(id, out var bb) ? bb : default((int X, int Y, int W, int H)?);
                nodes.Add(new ParsedBpmnNode(
                    id, Normalize((string?)element.Attribute("name")), type, step,
                    shape?.X, shape?.Y, shape?.W, shape?.H));
            }
            else if (localName == "sequenceFlow")
            {
                var source = (string?)element.Attribute("sourceRef");
                var target = (string?)element.Attribute("targetRef");
                if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
                {
                    errors.Add($"El sequenceFlow '{(string?)element.Attribute("id")}' no tiene sourceRef/targetRef.");
                    continue;
                }
                // Condicion estandar BPMN: <bpmn:conditionExpression> hijo del flow.
                var condition = Normalize(element.Element(Bpmn + "conditionExpression")?.Value);
                edges.Add(new ParsedBpmnEdge(
                    Normalize((string?)element.Attribute("id")), source, target,
                    Normalize((string?)element.Attribute("name")), condition));
            }
            // Otros elementos (textAnnotation, association, subProcess...) se ignoran:
            // el motor de esta ola solo ejecuta el subconjunto soportado.
        }

        // Ids unicos entre nodos.
        var duplicated = nodes.GroupBy(n => n.BpmnElementId, StringComparer.Ordinal)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        foreach (var id in duplicated)
        {
            errors.Add($"Id de nodo duplicado en el XML: '{id}'.");
        }

        var startCount = nodes.Count(n => n.NodeType == WorkflowNodeType.StartEvent);
        if (startCount != 1)
        {
            errors.Add($"El proceso debe tener exactamente 1 startEvent (tiene {startCount}).");
        }
        if (!nodes.Any(n => n.NodeType == WorkflowNodeType.EndEvent))
        {
            errors.Add("El proceso debe tener al menos 1 endEvent.");
        }

        // Toda arista apunta a nodos existentes del subconjunto soportado.
        //
        // Las figuras de DOCUMENTACION (objeto de datos, almacen, grupo, subproceso, evento intermedio,
        // pool...) se pueden dibujar y se conservan en el XML, pero el motor NO las ejecuta: no pueden ir
        // DENTRO del camino del flujo. Si el usuario las cablea con una flecha, se le dice exactamente eso
        // en vez del criptico "nodo inexistente".
        var nodeIds = nodes.Select(n => n.BpmnElementId).ToHashSet(StringComparer.Ordinal);
        var decorativeById = process.Elements()
            .Where(e => e.Name.Namespace == Bpmn
                && (string?)e.Attribute("id") is string id && id.Length > 0
                && !nodeIds.Contains(id)
                && e.Name.LocalName != "sequenceFlow")
            .ToDictionary(e => (string)e.Attribute("id")!, e => e.Name.LocalName, StringComparer.Ordinal);

        void CheckEnd(ParsedBpmnEdge edge, string elementId, string direction)
        {
            if (nodeIds.Contains(elementId))
            {
                return;
            }
            if (decorativeById.TryGetValue(elementId, out var localName))
            {
                errors.Add(
                    $"La flecha '{edge.BpmnElementId}' {direction} '{elementId}' ({localName}), que es una figura " +
                    "de documentacion: el motor no la ejecuta y no puede ir dentro del camino del flujo. " +
                    "Conectala con una asociacion, o usa solo inicio, tarea, compuerta y fin en el camino.");
                return;
            }
            errors.Add($"El sequenceFlow '{edge.BpmnElementId}' {direction} un nodo inexistente: '{elementId}'.");
        }

        foreach (var edge in edges)
        {
            CheckEnd(edge, edge.SourceRef, "sale de");
            CheckEnd(edge, edge.TargetRef, "llega a");
        }

        return new ParsedBpmnProcess(nodes, edges, errors);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Los Bounds del DI son decimales (dc:Bounds x="156.5"); el canvas usa int.</summary>
    private static bool TryRound(string? value, out int result)
    {
        if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            result = (int)Math.Round(parsed, MidpointRounding.AwayFromZero);
            return true;
        }
        result = 0;
        return false;
    }
}
