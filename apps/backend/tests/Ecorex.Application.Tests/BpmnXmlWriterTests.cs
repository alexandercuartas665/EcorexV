using Ecorex.Application.Workflows;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests del writer BPMN del editor canvas (ADR-0022). El contrato clave es el
/// ROUND-TRIP: BpmnProcessParser.Parse(BpmnXmlWriter.Write(grafo)) reproduce el mismo
/// grafo (ids, nombres, tipos, condiciones Y coordenadas del bpmndi), porque el editor
/// regenera el XML en cada mutacion y ese XML debe seguir siendo importable (motor)
/// y portable (bpmn.io, ADR-0014).
/// </summary>
public class BpmnXmlWriterTests
{
    private static readonly List<BpmnWriterNode> Nodes =
    [
        new("Start_1", "Inicio", WorkflowNodeType.StartEvent, 80, 150, 46, 46),
        new("Task_1", "Registrar requerimiento", WorkflowNodeType.Task, 190, 141, 140, 64),
        new("Gw_1", "Aprueba oferta?", WorkflowNodeType.ExclusiveGateway, 390, 145, 56, 56),
        new("Task_2", null, WorkflowNodeType.Task, 510, 141, 150, 66),
        new("End_1", "Fin", WorkflowNodeType.EndEvent, 720, 150, 46, 46)
    ];

    private static readonly List<BpmnWriterEdge> Edges =
    [
        new("Flow_1", "Start_1", "Task_1", null, null),
        new("Flow_2", "Task_1", "Gw_1", null, null),
        new("Flow_3", "Gw_1", "Task_2", "Aprobada", "approval == 'Approved'"),
        new("Flow_4", "Gw_1", "End_1", "Rechazada", "approval == 'Rejected'"),
        new("Flow_5", "Task_2", "End_1", null, null)
    ];

    [Fact]
    public void RoundTrip_WriteThenParse_ReproducesSameGraph()
    {
        var xml = BpmnXmlWriter.Write("COT-COM", Nodes, Edges);
        var parsed = BpmnProcessParser.Parse(xml);

        Assert.True(parsed.IsValid, string.Join(" | ", parsed.Errors));
        Assert.Equal(Nodes.Count, parsed.Nodes.Count);
        Assert.Equal(Edges.Count, parsed.Edges.Count);

        foreach (var expected in Nodes)
        {
            var actual = Assert.Single(parsed.Nodes, n => n.BpmnElementId == expected.BpmnElementId);
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.NodeType, actual.NodeType);
            // Las coordenadas viajan en el bpmndi y regresan identicas (layout persistente).
            Assert.Equal(expected.X, actual.X);
            Assert.Equal(expected.Y, actual.Y);
            Assert.Equal(expected.W, actual.W);
            Assert.Equal(expected.H, actual.H);
        }

        foreach (var expected in Edges)
        {
            var actual = Assert.Single(parsed.Edges, e => e.BpmnElementId == expected.BpmnElementId);
            Assert.Equal(expected.SourceId, actual.SourceRef);
            Assert.Equal(expected.TargetId, actual.TargetRef);
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.ConditionExpression, actual.ConditionExpression);
        }
    }

    [Fact]
    public void RoundTrip_TwiceThroughWriter_IsStable()
    {
        // Import de lo exportado produce el mismo grafo: una segunda vuelta por el writer
        // con lo parseado genera un XML equivalente (mismos nodos/aristas/coordenadas).
        var first = BpmnXmlWriter.Write("COT-COM", Nodes, Edges);
        var parsed = BpmnProcessParser.Parse(first);
        Assert.True(parsed.IsValid);

        var reNodes = parsed.Nodes
            .Select(n => new BpmnWriterNode(n.BpmnElementId, n.Name, n.NodeType, n.X!.Value, n.Y!.Value, n.W!.Value, n.H!.Value))
            .ToList();
        var reEdges = parsed.Edges
            .Select(e => new BpmnWriterEdge(e.BpmnElementId!, e.SourceRef, e.TargetRef, e.Name, e.ConditionExpression))
            .ToList();
        var second = BpmnXmlWriter.Write("COT-COM", reNodes, reEdges);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Write_ConditionUsesStandardConditionExpression_ParsableByEngineParser()
    {
        var xml = BpmnXmlWriter.Write("GW-01", Nodes, Edges);
        Assert.Contains("bpmn:conditionExpression", xml);
        Assert.Contains("xsi:type=\"bpmn:tFormalExpression\"", xml);
        // El writer sanea el ProcessCode a NCName (GW-01 es valido tal cual).
        Assert.Contains("Process_GW-01", xml);
    }

    [Fact]
    public void Write_ProcessCodeWithInvalidChars_IsSanitizedToValidXmlId()
    {
        var xml = BpmnXmlWriter.Write("COT COM/2", Nodes, Edges);
        Assert.Contains("Process_COT_COM_2", xml);
        Assert.True(BpmnProcessParser.Parse(xml).IsValid);
    }

    [Fact]
    public void AutoLayout_IsDeterministic_AndAlignsColumnsLeftToRight()
    {
        List<(string Id, WorkflowNodeType Type, int Step)> nodes =
        [
            ("Start_1", WorkflowNodeType.StartEvent, 1),
            ("Task_1", WorkflowNodeType.Task, 2),
            ("End_1", WorkflowNodeType.EndEvent, 3)
        ];
        List<(string, string)> edges = [("Start_1", "Task_1"), ("Task_1", "End_1")];

        var first = WorkflowAutoLayout.Compute(nodes, edges);
        var second = WorkflowAutoLayout.Compute(nodes, edges);

        Assert.Equal(first, second);
        // Columnas BFS: cada nivel avanza hacia la derecha.
        Assert.True(first["Start_1"].X < first["Task_1"].X);
        Assert.True(first["Task_1"].X < first["End_1"].X);
        // Tamanos por defecto del prototipo segun tipo.
        Assert.Equal((46, 46), (first["Start_1"].W, first["Start_1"].H));
        Assert.Equal((140, 64), (first["Task_1"].W, first["Task_1"].H));
    }

    [Fact]
    public void Parser_ReadsBpmndiBounds_AsCanvasCoordinates()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                id="d1" targetNamespace="http://ecorex.local/bpmn">
              <bpmn:process id="P1">
                <bpmn:startEvent id="Start_1" name="Inicio" />
                <bpmn:endEvent id="End_1" name="Fin" />
                <bpmn:sequenceFlow id="F1" sourceRef="Start_1" targetRef="End_1" />
              </bpmn:process>
              <bpmndi:BPMNDiagram id="Dia_1">
                <bpmndi:BPMNPlane id="Plane_1" bpmnElement="P1">
                  <bpmndi:BPMNShape id="Start_1_di" bpmnElement="Start_1">
                    <dc:Bounds x="156.5" y="82" width="36" height="36" />
                  </bpmndi:BPMNShape>
                </bpmndi:BPMNPlane>
              </bpmndi:BPMNDiagram>
            </bpmn:definitions>
            """;

        var parsed = BpmnProcessParser.Parse(xml);
        Assert.True(parsed.IsValid);

        // Con DI: coordenadas redondeadas del Bounds; sin DI: nulls (auto-layout despues).
        var start = Assert.Single(parsed.Nodes, n => n.BpmnElementId == "Start_1");
        Assert.Equal(157, start.X);
        Assert.Equal(82, start.Y);
        Assert.Equal(36, start.W);
        Assert.Equal(36, start.H);
        var end = Assert.Single(parsed.Nodes, n => n.BpmnElementId == "End_1");
        Assert.Null(end.X);
        Assert.Null(end.Y);
    }
}
