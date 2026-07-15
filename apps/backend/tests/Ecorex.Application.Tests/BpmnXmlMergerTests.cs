using System.Xml.Linq;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests del merge del XML del graficador. Lo que se protege aqui es el BUG que existia: al editar un nodo
/// desde el panel, el XML se REGENERABA desde las tablas del motor y se llevaba por delante TODAS las
/// figuras de documentacion (objeto de datos, almacen, grupo, subproceso, pool, anotaciones).
/// </summary>
public class BpmnXmlMergerTests
{
    private static readonly XNamespace Bpmn = BpmnProcessParser.Bpmn;
    private static readonly XNamespace BpmnDi = BpmnProcessParser.BpmnDi;

    /// <summary>XML como el que produce bpmn-js: el grafo del motor MAS figuras de documentacion.</summary>
    private const string XmlConDecoraciones = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                          id="Definitions_1" targetNamespace="http://bpmn.io/schema/bpmn">
          <bpmn:process id="Process_DEMO" isExecutable="true">
            <bpmn:startEvent id="Start_1" name="Inicio" />
            <bpmn:task id="Task_1" name="Revisar" />
            <bpmn:endEvent id="End_1" name="Fin" />
            <bpmn:sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="Task_1" />
            <bpmn:sequenceFlow id="Flow_2" sourceRef="Task_1" targetRef="End_1" />
            <bpmn:dataObjectReference id="DataObj_1" name="Cotizacion" />
            <bpmn:dataStoreReference id="DataStore_1" name="ERP" />
            <bpmn:group id="Group_1" />
            <bpmn:textAnnotation id="Note_1"><bpmn:text>Ojo con el IVA</bpmn:text></bpmn:textAnnotation>
            <bpmn:association id="Assoc_1" sourceRef="Task_1" targetRef="Note_1" />
          </bpmn:process>
          <bpmndi:BPMNDiagram id="Diagram_1">
            <bpmndi:BPMNPlane id="Plane_1" bpmnElement="Process_DEMO">
              <bpmndi:BPMNShape id="Start_1_di" bpmnElement="Start_1"><dc:Bounds x="100" y="100" width="46" height="46" /></bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Task_1_di" bpmnElement="Task_1"><dc:Bounds x="200" y="90" width="140" height="64" /></bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="End_1_di" bpmnElement="End_1"><dc:Bounds x="400" y="100" width="46" height="46" /></bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="DataObj_1_di" bpmnElement="DataObj_1"><dc:Bounds x="220" y="220" width="36" height="50" /></bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="DataStore_1_di" bpmnElement="DataStore_1"><dc:Bounds x="300" y="220" width="50" height="50" /></bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Group_1_di" bpmnElement="Group_1"><dc:Bounds x="180" y="60" width="300" height="240" /></bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Note_1_di" bpmnElement="Note_1"><dc:Bounds x="500" y="60" width="120" height="40" /></bpmndi:BPMNShape>
              <bpmndi:BPMNEdge id="Flow_1_di" bpmnElement="Flow_1"><di:waypoint x="146" y="123" /><di:waypoint x="200" y="123" /></bpmndi:BPMNEdge>
              <bpmndi:BPMNEdge id="Flow_2_di" bpmnElement="Flow_2"><di:waypoint x="340" y="123" /><di:waypoint x="400" y="123" /></bpmndi:BPMNEdge>
              <bpmndi:BPMNEdge id="Assoc_1_di" bpmnElement="Assoc_1"><di:waypoint x="340" y="110" /><di:waypoint x="500" y="80" /></bpmndi:BPMNEdge>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;

    private static List<BpmnWriterNode> GrafoNodos() =>
    [
        new("Start_1", "Inicio", WorkflowNodeType.StartEvent, 100, 100, 46, 46),
        new("Task_1", "Revisar", WorkflowNodeType.Task, 200, 90, 140, 64),
        new("End_1", "Fin", WorkflowNodeType.EndEvent, 400, 100, 46, 46),
    ];

    private static List<BpmnWriterEdge> GrafoAristas() =>
    [
        new("Flow_1", "Start_1", "Task_1", null, null),
        new("Flow_2", "Task_1", "End_1", null, null),
    ];

    private static XDocument Merge(List<BpmnWriterNode> nodes, List<BpmnWriterEdge> edges)
        => XDocument.Parse(BpmnXmlMerger.Merge(XmlConDecoraciones, "DEMO", nodes, edges));

    private static string[] Ids(XDocument doc, string localName)
        => doc.Descendants(Bpmn + localName).Select(e => (string)e.Attribute("id")!).ToArray();

    [Fact]
    public void Merge_PreservesDocumentationShapes_WhenTheEngineGraphIsUnchanged()
    {
        var doc = Merge(GrafoNodos(), GrafoAristas());

        // Las figuras de documentacion SIGUEN AHI (esto es lo que se perdia antes).
        Assert.Contains("DataObj_1", Ids(doc, "dataObjectReference"));
        Assert.Contains("DataStore_1", Ids(doc, "dataStoreReference"));
        Assert.Contains("Group_1", Ids(doc, "group"));
        Assert.Contains("Note_1", Ids(doc, "textAnnotation"));
        Assert.Contains("Assoc_1", Ids(doc, "association"));

        // Y sus formas del diagrama tambien.
        var shapes = doc.Descendants(BpmnDi + "BPMNShape")
            .Select(s => (string)s.Attribute("bpmnElement")!).ToArray();
        Assert.Contains("DataObj_1", shapes);
        Assert.Contains("Group_1", shapes);
        Assert.Contains("Note_1", shapes);
    }

    [Fact]
    public void Merge_AddsANewEngineNode_WithoutTouchingTheDecorations()
    {
        var nodes = GrafoNodos();
        nodes.Add(new BpmnWriterNode("Gw_1", "Decision", WorkflowNodeType.ExclusiveGateway, 350, 95, 56, 56));
        var edges = GrafoAristas();
        edges.Add(new BpmnWriterEdge("Flow_3", "Task_1", "Gw_1", null, null));

        var doc = Merge(nodes, edges);

        // El nodo nuevo entra con su forma...
        Assert.Contains("Gw_1", Ids(doc, "exclusiveGateway"));
        var shape = doc.Descendants(BpmnDi + "BPMNShape")
            .Single(s => (string?)s.Attribute("bpmnElement") == "Gw_1");
        Assert.NotNull(shape.Descendants().FirstOrDefault(e => e.Name.LocalName == "Bounds"));
        Assert.Contains("Flow_3", Ids(doc, "sequenceFlow"));

        // ...y las decoraciones siguen intactas.
        Assert.Contains("DataObj_1", Ids(doc, "dataObjectReference"));
        Assert.Contains("Group_1", Ids(doc, "group"));
    }

    [Fact]
    public void Merge_RemovesTheEngineNodeAndItsShape_ButKeepsTheDecorations()
    {
        // El usuario borra la tarea desde el panel: se va la tarea y sus dos flechas.
        var nodes = GrafoNodos().Where(n => n.BpmnElementId != "Task_1").ToList();
        var edges = new List<BpmnWriterEdge>();

        var doc = Merge(nodes, edges);

        Assert.DoesNotContain("Task_1", Ids(doc, "task"));
        Assert.Empty(Ids(doc, "sequenceFlow"));
        var shapes = doc.Descendants(BpmnDi + "BPMNShape")
            .Select(s => (string)s.Attribute("bpmnElement")!).ToArray();
        Assert.DoesNotContain("Task_1", shapes);

        // Las decoraciones NO se van con el.
        Assert.Contains("DataObj_1", Ids(doc, "dataObjectReference"));
        Assert.Contains("DataStore_1", Ids(doc, "dataStoreReference"));
        Assert.Contains("Group_1", Ids(doc, "group"));
        Assert.Contains("Note_1", shapes);
    }

    [Fact]
    public void Merge_UpdatesNameAndPosition_OfAnExistingEngineNode()
    {
        var nodes = GrafoNodos();
        nodes[1] = new BpmnWriterNode("Task_1", "Revisar y aprobar", WorkflowNodeType.Task, 260, 130, 140, 64);

        var doc = Merge(nodes, GrafoAristas());

        var task = doc.Descendants(Bpmn + "task").Single(t => (string?)t.Attribute("id") == "Task_1");
        Assert.Equal("Revisar y aprobar", (string?)task.Attribute("name"));

        var bounds = doc.Descendants(BpmnDi + "BPMNShape")
            .Single(s => (string?)s.Attribute("bpmnElement") == "Task_1")
            .Descendants().Single(e => e.Name.LocalName == "Bounds");
        Assert.Equal("260", (string?)bounds.Attribute("x"));
        Assert.Equal("130", (string?)bounds.Attribute("y"));
    }

    [Fact]
    public void Merge_KeepsTheUsersEdgeRouting_AndTheResultStillParses()
    {
        var doc = Merge(GrafoNodos(), GrafoAristas());

        // El trazado que el usuario hizo en el lienzo se respeta (no se re-dibuja la arista).
        var flow1 = doc.Descendants(BpmnDi + "BPMNEdge")
            .Single(e => (string?)e.Attribute("bpmnElement") == "Flow_1");
        var firstWaypoint = flow1.Descendants().First(e => e.Name.LocalName == "waypoint");
        Assert.Equal("146", (string?)firstWaypoint.Attribute("x"));

        // Y el XML resultante sigue siendo valido para el motor (las decoraciones no lo rompen).
        var parsed = BpmnProcessParser.Parse(doc.ToString());
        Assert.Empty(parsed.Errors);
        Assert.Equal(3, parsed.Nodes.Count);
        Assert.Equal(2, parsed.Edges.Count);
    }

    [Fact]
    public void Merge_FallsBackToAFullWrite_WhenThereIsNoUsableXml()
    {
        // Flujo nuevo (sin XML previo): debe generar un documento valido igualmente.
        var xml = BpmnXmlMerger.Merge(null, "DEMO", GrafoNodos(), GrafoAristas());
        var parsed = BpmnProcessParser.Parse(xml);
        Assert.Empty(parsed.Errors);
        Assert.Equal(3, parsed.Nodes.Count);

        // XML corrupto: tampoco revienta, cae al generador desde cero.
        var fromBroken = BpmnXmlMerger.Merge("<no-es-xml", "DEMO", GrafoNodos(), GrafoAristas());
        Assert.Empty(BpmnProcessParser.Parse(fromBroken).Errors);
    }

    [Fact]
    public void Parse_ExplainsClearly_WhenADocumentationShapeIsWiredIntoTheFlow()
    {
        // El usuario mete un subproceso DENTRO del camino: Start -> SubProcess -> End.
        var xml = XmlConDecoraciones
            .Replace("<bpmn:group id=\"Group_1\" />",
                "<bpmn:group id=\"Group_1\" /><bpmn:subProcess id=\"Sub_1\" name=\"Sub\" />")
            .Replace("<bpmn:sequenceFlow id=\"Flow_2\" sourceRef=\"Task_1\" targetRef=\"End_1\" />",
                "<bpmn:sequenceFlow id=\"Flow_2\" sourceRef=\"Task_1\" targetRef=\"Sub_1\" />");

        var parsed = BpmnProcessParser.Parse(xml);

        var error = Assert.Single(parsed.Errors);
        Assert.Contains("Flow_2", error);
        Assert.Contains("subProcess", error);
        Assert.Contains("documentacion", error);
        // Y NO el mensaje criptico de antes.
        Assert.DoesNotContain("nodo inexistente", error);
    }
}
