using System.Text;
using System.Xml;
using System.Xml.Linq;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Workflows;

/// <summary>
/// Sincroniza el grafo del MOTOR dentro de un XML BPMN ya existente, SIN destruir el resto del dibujo.
///
/// Por que existe: <see cref="BpmnXmlWriter"/> REGENERA el documento entero desde las tablas del motor,
/// asi que cualquier figura que el motor no modela (objeto de datos, almacen, grupo, subproceso, evento
/// intermedio, pool, anotaciones...) DESAPARECIA en cuanto el usuario tocaba un nodo desde el panel. Como
/// el graficador ahora ofrece esas figuras para DOCUMENTAR el proceso, el XML debe preservarlas.
///
/// Regla: este merge solo toca los elementos que el motor POSEE
/// (startEvent / task / exclusiveGateway / endEvent / sequenceFlow y sus formas del diagrama).
/// Todo lo demas se deja intacto, tal como lo dibujo bpmn-js.
/// </summary>
public static class BpmnXmlMerger
{
    private static readonly XNamespace Bpmn = BpmnProcessParser.Bpmn;
    private static readonly XNamespace BpmnDi = BpmnProcessParser.BpmnDi;
    private static readonly XNamespace Dc = BpmnProcessParser.Dc;
    private static readonly XNamespace Di = BpmnProcessParser.Di;
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    /// <summary>Elementos del proceso que el MOTOR posee (los unicos que este merge crea/borra/actualiza).</summary>
    private static readonly HashSet<string> EngineNodeNames = new(StringComparer.Ordinal)
    {
        "startEvent", "task", "exclusiveGateway", "endEvent"
    };

    /// <summary>
    /// Devuelve el XML con el grafo del motor sincronizado y el resto del dibujo intacto. Si no hay un XML
    /// previo utilizable (flujo nuevo, XML corrupto o sin diagrama), cae al generador desde cero.
    /// </summary>
    public static string Merge(
        string? existingXml, string processCode,
        IReadOnlyList<BpmnWriterNode> nodes, IReadOnlyList<BpmnWriterEdge> edges)
    {
        if (string.IsNullOrWhiteSpace(existingXml))
        {
            return BpmnXmlWriter.Write(processCode, nodes, edges);
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(existingXml);
        }
        catch (XmlException)
        {
            return BpmnXmlWriter.Write(processCode, nodes, edges);
        }

        var process = doc.Descendants(Bpmn + "process").FirstOrDefault();
        var plane = doc.Descendants(BpmnDi + "BPMNPlane").FirstOrDefault();
        if (process is null || plane is null)
        {
            // Sin proceso o sin diagrama no hay nada que preservar de forma fiable.
            return BpmnXmlWriter.Write(processCode, nodes, edges);
        }

        SyncNodes(process, nodes);
        SyncEdges(process, edges);
        SyncDiagram(plane, nodes, edges);

        return Serialize(doc);
    }

    // ---- Proceso ----

    private static void SyncNodes(XElement process, IReadOnlyList<BpmnWriterNode> nodes)
    {
        var wanted = nodes.ToDictionary(n => n.BpmnElementId, StringComparer.Ordinal);

        // 1) Borra los nodos DEL MOTOR que ya no existen en el grafo (las figuras decorativas ni se miran).
        foreach (var element in process.Elements().Where(IsEngineNode).ToList())
        {
            var id = (string?)element.Attribute("id");
            if (id is null || !wanted.ContainsKey(id))
            {
                element.Remove();
            }
        }

        // 2) Crea o actualiza los del grafo.
        foreach (var node in nodes)
        {
            var localName = LocalName(node.NodeType);
            var element = FindEngineNode(process, node.BpmnElementId);

            // Cambio de tipo (ej. task -> exclusiveGateway): se reemplaza el elemento conservando el id.
            if (element is not null && element.Name.LocalName != localName)
            {
                element.Remove();
                element = null;
            }
            if (element is null)
            {
                element = new XElement(Bpmn + localName, new XAttribute("id", node.BpmnElementId));
                process.Add(element);
            }
            SetOrRemove(element, "name", node.Name);

            // Las referencias incoming/outgoing son redundantes (bpmn-js deriva las conexiones de los
            // sequenceFlow) y quedarian COLGADAS si se borra una arista: se retiran de los nodos del motor.
            element.Elements(Bpmn + "incoming").Remove();
            element.Elements(Bpmn + "outgoing").Remove();
        }
    }

    private static void SyncEdges(XElement process, IReadOnlyList<BpmnWriterEdge> edges)
    {
        var wanted = edges.ToDictionary(e => e.BpmnElementId, StringComparer.Ordinal);

        foreach (var element in process.Elements(Bpmn + "sequenceFlow").ToList())
        {
            var id = (string?)element.Attribute("id");
            if (id is null || !wanted.ContainsKey(id))
            {
                element.Remove();
            }
        }

        foreach (var edge in edges)
        {
            var element = process.Elements(Bpmn + "sequenceFlow")
                .FirstOrDefault(e => (string?)e.Attribute("id") == edge.BpmnElementId);
            if (element is null)
            {
                element = new XElement(Bpmn + "sequenceFlow", new XAttribute("id", edge.BpmnElementId));
                process.Add(element);
            }
            element.SetAttributeValue("sourceRef", edge.SourceId);
            element.SetAttributeValue("targetRef", edge.TargetId);
            SetOrRemove(element, "name", edge.Name);

            element.Elements(Bpmn + "conditionExpression").Remove();
            if (!string.IsNullOrWhiteSpace(edge.ConditionExpression))
            {
                element.Add(new XElement(Bpmn + "conditionExpression",
                    new XAttribute(Xsi + "type", "bpmn:tFormalExpression"),
                    edge.ConditionExpression));
            }
        }
    }

    // ---- Diagrama (bpmndi) ----

    private static void SyncDiagram(
        XElement plane, IReadOnlyList<BpmnWriterNode> nodes, IReadOnlyList<BpmnWriterEdge> edges)
    {
        var nodeById = nodes.ToDictionary(n => n.BpmnElementId, StringComparer.Ordinal);
        var edgeById = edges.ToDictionary(e => e.BpmnElementId, StringComparer.Ordinal);

        // Limpieza del diagrama con UNA regla segura: se retira la forma/arista cuyo elemento ya no existe
        // en NINGUNA parte del documento (o sea, la borro el motor, o quedo huerfana). Si el elemento sigue
        // existiendo -sea decorativo dentro del proceso o un participante dentro de <collaboration>- su
        // forma se conserva intacta. Asi el merge nunca se lleva por delante un pool ni una anotacion.
        var alive = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in plane.Document!.Descendants())
        {
            if (element.Name.Namespace != Bpmn) { continue; }
            if ((string?)element.Attribute("id") is string id && id.Length > 0)
            {
                alive.Add(id);
            }
        }
        // Los nodos/aristas del grafo actual siguen vivos aunque aun no esten escritos en el XML.
        alive.UnionWith(nodeById.Keys);
        alive.UnionWith(edgeById.Keys);

        foreach (var di in plane.Elements()
            .Where(e => e.Name == BpmnDi + "BPMNShape" || e.Name == BpmnDi + "BPMNEdge")
            .ToList())
        {
            var target = (string?)di.Attribute("bpmnElement");
            if (target is not null && !alive.Contains(target))
            {
                di.Remove();
            }
        }

        // Formas de los nodos del motor: se crean si faltan y se actualizan sus coordenadas.
        foreach (var node in nodes)
        {
            var shape = plane.Elements(BpmnDi + "BPMNShape")
                .FirstOrDefault(s => (string?)s.Attribute("bpmnElement") == node.BpmnElementId);
            if (shape is null)
            {
                shape = new XElement(BpmnDi + "BPMNShape",
                    new XAttribute("id", node.BpmnElementId + "_di"),
                    new XAttribute("bpmnElement", node.BpmnElementId));
                plane.Add(shape);
            }
            var bounds = shape.Element(Dc + "Bounds");
            if (bounds is null)
            {
                bounds = new XElement(Dc + "Bounds");
                shape.Add(bounds);
            }
            bounds.SetAttributeValue("x", node.X);
            bounds.SetAttributeValue("y", node.Y);
            bounds.SetAttributeValue("width", node.W);
            bounds.SetAttributeValue("height", node.H);
        }

        // Aristas del motor: si ya tienen trazado (el usuario la ruteo en el lienzo) se RESPETA; solo se
        // crean waypoints simples para las aristas nuevas nacidas del panel.
        foreach (var edge in edges)
        {
            var diEdge = plane.Elements(BpmnDi + "BPMNEdge")
                .FirstOrDefault(e => (string?)e.Attribute("bpmnElement") == edge.BpmnElementId);
            if (diEdge is not null)
            {
                continue;
            }
            if (!nodeById.TryGetValue(edge.SourceId, out var source)
                || !nodeById.TryGetValue(edge.TargetId, out var target))
            {
                continue;
            }
            plane.Add(new XElement(BpmnDi + "BPMNEdge",
                new XAttribute("id", edge.BpmnElementId + "_di"),
                new XAttribute("bpmnElement", edge.BpmnElementId),
                Waypoint(source.X + source.W, source.Y + source.H / 2),
                Waypoint(target.X, target.Y + target.H / 2)));
        }
    }

    // ---- Helpers ----

    private static bool IsEngineNode(XElement element)
        => element.Name.Namespace == Bpmn && EngineNodeNames.Contains(element.Name.LocalName);

    private static XElement? FindEngineNode(XElement process, string id)
        => process.Elements().FirstOrDefault(e => IsEngineNode(e) && (string?)e.Attribute("id") == id);

    private static void SetOrRemove(XElement element, string attribute, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            element.Attribute(attribute)?.Remove();
        }
        else
        {
            element.SetAttributeValue(attribute, value);
        }
    }

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

    private static string Serialize(XDocument doc)
    {
        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb, new XmlWriterSettings
        {
            Indent = true,
            Encoding = Encoding.UTF8,
            OmitXmlDeclaration = false
        }))
        {
            doc.Save(writer);
        }
        return sb.ToString().Replace("encoding=\"utf-16\"", "encoding=\"UTF-8\"");
    }
}
