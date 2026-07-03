using System.Xml;
using System.Xml.Linq;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Workflows;

/// <summary>Nodo BPMN parseado (aun sin persistir).</summary>
public sealed record ParsedBpmnNode(string BpmnElementId, string? Name, WorkflowNodeType NodeType, int StepNumber);

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
/// sequenceFlow. Ignora DI (diagrama), anotaciones y asociaciones: son visuales. El XML
/// original NUNCA se modifica (se persiste tal cual para round-trip con bpmn.io).
/// Validaciones: exactamente 1 startEvent, al menos 1 endEvent, ids unicos y aristas
/// que apuntan a nodos existentes.
/// </summary>
public static class BpmnProcessParser
{
    /// <summary>Namespace del modelo BPMN 2.0 (el prefijo bpmn:/bpmn2: es irrelevante).</summary>
    public static readonly XNamespace Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";

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
                nodes.Add(new ParsedBpmnNode(id, Normalize((string?)element.Attribute("name")), type, step));
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
        var nodeIds = nodes.Select(n => n.BpmnElementId).ToHashSet(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (!nodeIds.Contains(edge.SourceRef))
            {
                errors.Add($"El sequenceFlow '{edge.BpmnElementId}' sale de un nodo inexistente: '{edge.SourceRef}'.");
            }
            if (!nodeIds.Contains(edge.TargetRef))
            {
                errors.Add($"El sequenceFlow '{edge.BpmnElementId}' llega a un nodo inexistente: '{edge.TargetRef}'.");
            }
        }

        return new ParsedBpmnProcess(nodes, edges, errors);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
