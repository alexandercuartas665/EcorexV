using System.Text;
using System.Xml.Linq;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Workflows;

/// <summary>Nodo del grafo a serializar (coordenadas ya resueltas, nunca null).</summary>
public sealed record BpmnWriterNode(
    string BpmnElementId, string? Name, WorkflowNodeType NodeType, int X, int Y, int W, int H);

/// <summary>Arista del grafo a serializar (SourceId/TargetId son BpmnElementId de nodos).</summary>
public sealed record BpmnWriterEdge(
    string BpmnElementId, string SourceId, string TargetId, string? Name, string? ConditionExpression);

/// <summary>
/// Generador de XML BPMN 2.0 estandar para el editor de flujos propio (ADR-0022).
/// El editor del prototipo NO usa bpmn-js: muta nodos/aristas materializados y este
/// writer REGENERA el BpmnXml completo (bpmn:process + bpmndi con las coordenadas del
/// canvas) para conservar la portabilidad bpmn.io del ADR-0014. Round-trip garantizado
/// por test: BpmnProcessParser.Parse(Write(grafo)) reproduce el mismo grafo.
///
/// DEUDA / DEPRECACION PARCIAL (ADR-0034): el EDITOR migro a bpmn-js, que produce el XML
/// directamente; el guardado del editor (SaveBpmnAsync) ya NO pasa por este writer. Se
/// CONSERVA porque lo siguen usando: el seeder (backfill de layout), CreateDraftAsync
/// (borrador minimo Inicio->Fin), EnsureDraftAsync (regenerar el XML de la version fuente)
/// e ImportJsonAsync (import del formato JSON del prototipo, tambien deprecado). No borrar.
/// </summary>
public static class BpmnXmlWriter
{
    private static readonly XNamespace Bpmn = BpmnProcessParser.Bpmn;
    private static readonly XNamespace BpmnDi = BpmnProcessParser.BpmnDi;
    private static readonly XNamespace Dc = BpmnProcessParser.Dc;
    private static readonly XNamespace Di = BpmnProcessParser.Di;
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    public static string Write(string processCode, IReadOnlyList<BpmnWriterNode> nodes, IReadOnlyList<BpmnWriterEdge> edges)
    {
        var processId = "Process_" + Sanitize(processCode);

        var process = new XElement(Bpmn + "process",
            new XAttribute("id", processId),
            new XAttribute("isExecutable", "true"));

        foreach (var node in nodes)
        {
            var element = new XElement(Bpmn + LocalName(node.NodeType), new XAttribute("id", node.BpmnElementId));
            if (!string.IsNullOrWhiteSpace(node.Name))
            {
                element.Add(new XAttribute("name", node.Name));
            }
            process.Add(element);
        }

        foreach (var edge in edges)
        {
            var flow = new XElement(Bpmn + "sequenceFlow",
                new XAttribute("id", edge.BpmnElementId),
                new XAttribute("sourceRef", edge.SourceId),
                new XAttribute("targetRef", edge.TargetId));
            if (!string.IsNullOrWhiteSpace(edge.Name))
            {
                flow.Add(new XAttribute("name", edge.Name));
            }
            if (!string.IsNullOrWhiteSpace(edge.ConditionExpression))
            {
                // Condicion estandar (mismo formato que consume el motor y escribe bpmn.io).
                flow.Add(new XElement(Bpmn + "conditionExpression",
                    new XAttribute(Xsi + "type", "bpmn:tFormalExpression"),
                    edge.ConditionExpression));
            }
            process.Add(flow);
        }

        // Diagrama (bpmndi): shapes con las coordenadas del canvas y edges con waypoints
        // ortogonales simples (bpmn.io los reacomoda si el usuario los toca alla).
        var byId = nodes.ToDictionary(n => n.BpmnElementId, StringComparer.Ordinal);
        var plane = new XElement(BpmnDi + "BPMNPlane",
            new XAttribute("id", "BPMNPlane_1"),
            new XAttribute("bpmnElement", processId));
        foreach (var node in nodes)
        {
            plane.Add(new XElement(BpmnDi + "BPMNShape",
                new XAttribute("id", node.BpmnElementId + "_di"),
                new XAttribute("bpmnElement", node.BpmnElementId),
                new XElement(Dc + "Bounds",
                    new XAttribute("x", node.X), new XAttribute("y", node.Y),
                    new XAttribute("width", node.W), new XAttribute("height", node.H))));
        }
        foreach (var edge in edges)
        {
            if (!byId.TryGetValue(edge.SourceId, out var source) || !byId.TryGetValue(edge.TargetId, out var target))
            {
                continue;
            }
            plane.Add(new XElement(BpmnDi + "BPMNEdge",
                new XAttribute("id", edge.BpmnElementId + "_di"),
                new XAttribute("bpmnElement", edge.BpmnElementId),
                Waypoint(source.X + source.W, source.Y + source.H / 2),
                Waypoint(target.X, target.Y + target.H / 2)));
        }

        var definitions = new XElement(Bpmn + "definitions",
            new XAttribute(XNamespace.Xmlns + "bpmn", Bpmn.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "bpmndi", BpmnDi.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "dc", Dc.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "di", Di.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi.NamespaceName),
            new XAttribute("id", "Definitions_" + Sanitize(processCode)),
            new XAttribute("targetNamespace", "http://bpmn.io/schema/bpmn"),
            process,
            new XElement(BpmnDi + "BPMNDiagram", new XAttribute("id", "BPMNDiagram_1"), plane));

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), definitions);
        var sb = new StringBuilder();
        using (var writer = System.Xml.XmlWriter.Create(sb, new System.Xml.XmlWriterSettings
        {
            Indent = true,
            Encoding = Encoding.UTF8,
            OmitXmlDeclaration = false
        }))
        {
            doc.Save(writer);
        }
        // XmlWriter sobre StringBuilder declara utf-16; el estandar bpmn.io usa UTF-8.
        return sb.ToString().Replace("encoding=\"utf-16\"", "encoding=\"UTF-8\"");
    }

    /// <summary>Tamano por defecto del canvas segun tipo (medidas del prototipo).</summary>
    public static (int W, int H) DefaultSize(WorkflowNodeType type) => type switch
    {
        WorkflowNodeType.StartEvent or WorkflowNodeType.EndEvent => (46, 46),
        WorkflowNodeType.ExclusiveGateway => (56, 56),
        _ => (140, 64)
    };

    private static string LocalName(WorkflowNodeType type) => type switch
    {
        WorkflowNodeType.StartEvent => "startEvent",
        WorkflowNodeType.Task => "task",
        WorkflowNodeType.ExclusiveGateway => "exclusiveGateway",
        WorkflowNodeType.EndEvent => "endEvent",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Tipo de nodo BPMN no soportado.")
    };

    private static XElement Waypoint(int x, int y)
        => new(Di + "waypoint", new XAttribute("x", x), new XAttribute("y", y));

    /// <summary>Id XML valido (NCName) desde el ProcessCode (ej. "COT-COM" -> "COT_COM").</summary>
    private static string Sanitize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            sb.Append(char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_');
        }
        return sb.Length == 0 ? "1" : sb.ToString();
    }
}
