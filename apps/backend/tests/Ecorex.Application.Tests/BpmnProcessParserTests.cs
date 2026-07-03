using Ecorex.Application.Workflows;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests unitarios del parser BPMN 2.0 del WorkflowEngine (FASE 4, ADR-0014): XML minimo
/// valido (con prefijos bpmn: y bpmn2:, ambos del namespace OMG), condiciones en aristas
/// y todas las validaciones (start unico, endEvent presente, ids unicos, aristas coherentes).
/// </summary>
public class BpmnProcessParserTests
{
    private const string ValidXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="d1" targetNamespace="http://ecorex.local/bpmn">
          <bpmn:process id="P1" isExecutable="false">
            <bpmn:startEvent id="Start_1" name="Inicio" />
            <bpmn:task id="Task_1" name="Hacer algo" />
            <bpmn:exclusiveGateway id="Gw_1" name="Decision" />
            <bpmn:endEvent id="End_1" name="Fin" />
            <bpmn:endEvent id="End_2" name="Fin alterno" />
            <bpmn:sequenceFlow id="F1" sourceRef="Start_1" targetRef="Task_1" />
            <bpmn:sequenceFlow id="F2" sourceRef="Task_1" targetRef="Gw_1" />
            <bpmn:sequenceFlow id="F3" sourceRef="Gw_1" targetRef="End_1">
              <bpmn:conditionExpression>approval == 'Approved'</bpmn:conditionExpression>
            </bpmn:sequenceFlow>
            <bpmn:sequenceFlow id="F4" sourceRef="Gw_1" targetRef="End_2" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    [Fact]
    public void Parse_ValidMinimalXml_ReturnsNodesEdgesAndConditions()
    {
        var parsed = BpmnProcessParser.Parse(ValidXml);

        Assert.True(parsed.IsValid, string.Join(" | ", parsed.Errors));
        Assert.Equal(5, parsed.Nodes.Count);
        Assert.Equal(4, parsed.Edges.Count);
        Assert.Equal(1, parsed.Nodes.Count(n => n.NodeType == WorkflowNodeType.StartEvent));
        Assert.Equal(1, parsed.Nodes.Count(n => n.NodeType == WorkflowNodeType.Task));
        Assert.Equal(1, parsed.Nodes.Count(n => n.NodeType == WorkflowNodeType.ExclusiveGateway));
        Assert.Equal(2, parsed.Nodes.Count(n => n.NodeType == WorkflowNodeType.EndEvent));

        // Nombres, numeracion de pasos y condicion estandar (bpmn:conditionExpression).
        var task = parsed.Nodes.Single(n => n.BpmnElementId == "Task_1");
        Assert.Equal("Hacer algo", task.Name);
        Assert.Equal(2, task.StepNumber);
        var approvedEdge = parsed.Edges.Single(e => e.BpmnElementId == "F3");
        Assert.Equal("approval == 'Approved'", approvedEdge.ConditionExpression);
        var defaultEdge = parsed.Edges.Single(e => e.BpmnElementId == "F4");
        Assert.Null(defaultEdge.ConditionExpression);
    }

    [Fact]
    public void Parse_Bpmn2Prefix_IsAcceptedByNamespace()
    {
        // El fixture real del vault usa el prefijo bpmn2: (mismo namespace OMG).
        const string xml = """
            <bpmn2:definitions xmlns:bpmn2="http://www.omg.org/spec/BPMN/20100524/MODEL" id="d2" targetNamespace="http://ecorex.local/bpmn">
              <bpmn2:process id="P1">
                <bpmn2:startEvent id="S" />
                <bpmn2:endEvent id="E" />
                <bpmn2:sequenceFlow id="F" sourceRef="S" targetRef="E" />
              </bpmn2:process>
            </bpmn2:definitions>
            """;
        var parsed = BpmnProcessParser.Parse(xml);
        Assert.True(parsed.IsValid, string.Join(" | ", parsed.Errors));
        Assert.Equal(2, parsed.Nodes.Count);
        Assert.Single(parsed.Edges);
    }

    [Fact]
    public void Parse_EmptyOrMalformedXml_IsInvalid()
    {
        Assert.False(BpmnProcessParser.Parse(null).IsValid);
        Assert.False(BpmnProcessParser.Parse("").IsValid);
        Assert.False(BpmnProcessParser.Parse("esto no es xml <<<").IsValid);
        Assert.False(BpmnProcessParser.Parse("<otro-xml/>").IsValid);
    }

    [Fact]
    public void Parse_WithoutStartEvent_IsInvalid()
    {
        var parsed = BpmnProcessParser.Parse(ValidXml.Replace("<bpmn:startEvent id=\"Start_1\" name=\"Inicio\" />", ""));
        Assert.False(parsed.IsValid);
        Assert.Contains(parsed.Errors, e => e.Contains("startEvent"));
    }

    [Fact]
    public void Parse_TwoStartEvents_IsInvalid()
    {
        var parsed = BpmnProcessParser.Parse(ValidXml.Replace(
            "<bpmn:startEvent id=\"Start_1\" name=\"Inicio\" />",
            "<bpmn:startEvent id=\"Start_1\" name=\"Inicio\" /><bpmn:startEvent id=\"Start_2\" />"));
        Assert.False(parsed.IsValid);
        Assert.Contains(parsed.Errors, e => e.Contains("exactamente 1 startEvent"));
    }

    [Fact]
    public void Parse_WithoutEndEvent_IsInvalid()
    {
        var parsed = BpmnProcessParser.Parse(ValidXml
            .Replace("<bpmn:endEvent id=\"End_1\" name=\"Fin\" />", "")
            .Replace("<bpmn:endEvent id=\"End_2\" name=\"Fin alterno\" />", ""));
        Assert.False(parsed.IsValid);
        Assert.Contains(parsed.Errors, e => e.Contains("endEvent"));
    }

    [Fact]
    public void Parse_DuplicatedNodeId_IsInvalid()
    {
        var parsed = BpmnProcessParser.Parse(ValidXml.Replace("id=\"End_2\"", "id=\"End_1\""));
        Assert.False(parsed.IsValid);
        Assert.Contains(parsed.Errors, e => e.Contains("duplicado"));
    }

    [Fact]
    public void Parse_EdgePointingToMissingNode_IsInvalid()
    {
        var parsed = BpmnProcessParser.Parse(ValidXml.Replace("targetRef=\"Task_1\"", "targetRef=\"NoExiste\""));
        Assert.False(parsed.IsValid);
        Assert.Contains(parsed.Errors, e => e.Contains("NoExiste"));
    }
}
