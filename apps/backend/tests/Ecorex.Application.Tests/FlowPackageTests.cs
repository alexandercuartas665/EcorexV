using System.Text.Json;
using System.Text.Json.Serialization;
using Ecorex.Application.Workflows;

namespace Ecorex.Application.Tests;

/// <summary>
/// Round-trip del paquete PORTABLE de flujo (ADR-0097 Ola B1): un FlowPackage con todas las piezas
/// (nodos con formularios embebidos, cargos y agente/reglas por nombre, conexiones) debe serializar a
/// JSON y volver SIN perder informacion, para que el snapshot del marketplace sea estable. Enums viajan
/// como texto (Tipo/AssigneeSource/Autonomy/OnFailure ya son strings).
/// </summary>
public class FlowPackageTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static FlowPackage Sample() => new(
        FormatVersion: 1,
        Name: "Cotizacion con agente",
        Description: "Flujo de ejemplo",
        Category: "Comercial",
        Nodes: new List<FlowPackageNode>
        {
            new("Start_1", "startEvent", "Inicio", 0, 0, 40, 40, "Policy", null, null, null,
                Forms: new List<FlowPackageForm>(),
                CargoNames: new List<string>(),
                Agent: null,
                Rules: new List<FlowPackageRule>()),
            new("Task_1", "task", "Requerimiento", 120, 0, 150, 90, "InheritStart", null, "#6b4bd8", "post-it",
                Forms: new List<FlowPackageForm>
                {
                    new("{\"formatVersion\":1,\"definition\":{\"title\":\"Req\"}}", 0, true, true)
                },
                CargoNames: new List<string> { "DIRECTOR", "COMERCIAL" },
                Agent: null,
                Rules: new List<FlowPackageRule>()),
            new("Task_2", "task", "Buscar productos", 320, 0, 150, 90, "Policy", null, null, null,
                Forms: new List<FlowPackageForm>(),
                CargoNames: new List<string> { "DIRECTOR" },
                Agent: new FlowPackageAgent("Clasificador de contactos", "Autonomous", "usa buscar_web", true, "Retry", 2, null, null),
                Rules: new List<FlowPackageRule> { new("Notificar cierre", false, 0) }),
            new("End_1", "endEvent", "Fin", 520, 0, 40, 40, "Policy", null, null, null,
                Forms: new List<FlowPackageForm>(),
                CargoNames: new List<string>(),
                Agent: null,
                Rules: new List<FlowPackageRule>()),
        },
        Edges: new List<FlowPackageEdge>
        {
            new("Start_1", "Task_1", null, null),
            new("Task_1", "Task_2", "sigue", null),
            new("Task_2", "End_1", "ok", "estado == 'ok'"),
        });

    [Fact]
    public void RoundTrip_PreservesHeaderAndCounts()
    {
        var json = JsonSerializer.Serialize(Sample(), Json);
        var back = JsonSerializer.Deserialize<FlowPackage>(json, Json);

        Assert.NotNull(back);
        Assert.Equal(1, back!.FormatVersion);
        Assert.Equal("Cotizacion con agente", back.Name);
        Assert.Equal("Comercial", back.Category);
        Assert.Equal(4, back.Nodes.Count);
        Assert.Equal(3, back.Edges.Count);
    }

    [Fact]
    public void RoundTrip_PreservesNodeConfigAndForms()
    {
        var json = JsonSerializer.Serialize(Sample(), Json);
        var back = JsonSerializer.Deserialize<FlowPackage>(json, Json)!;

        var req = back.Nodes.Single(n => n.BpmnElementId == "Task_1");
        Assert.Equal("task", req.Tipo);
        Assert.Equal("InheritStart", req.AssigneeSource);
        Assert.Equal("#6b4bd8", req.Color);
        Assert.Equal("post-it", req.Note);
        Assert.Single(req.Forms);
        Assert.True(req.Forms[0].IsRequired);
        Assert.Contains("\"title\":\"Req\"", req.Forms[0].ExportJson);
        Assert.Equal(new[] { "DIRECTOR", "COMERCIAL" }, req.CargoNames);
    }

    [Fact]
    public void RoundTrip_PreservesAgentByName()
    {
        var json = JsonSerializer.Serialize(Sample(), Json);
        var back = JsonSerializer.Deserialize<FlowPackage>(json, Json)!;

        var busca = back.Nodes.Single(n => n.BpmnElementId == "Task_2");
        Assert.NotNull(busca.Agent);
        Assert.Equal("Clasificador de contactos", busca.Agent!.AgentName);
        Assert.Equal("Autonomous", busca.Agent.Autonomy);
        Assert.Equal("usa buscar_web", busca.Agent.ExtraPrompt);
        Assert.True(busca.Agent.CanSendEmail);
        Assert.Equal("Retry", busca.Agent.OnFailure);
        Assert.Equal(2, busca.Agent.FailureRetries);
        Assert.Single(busca.Rules);
        Assert.Equal("Notificar cierre", busca.Rules[0].RuleName);
        Assert.False(busca.Rules[0].IsAutonomous);
    }

    [Fact]
    public void RoundTrip_PreservesEdgesWithConditions()
    {
        var json = JsonSerializer.Serialize(Sample(), Json);
        var back = JsonSerializer.Deserialize<FlowPackage>(json, Json)!;

        var cond = back.Edges.Single(e => e.From == "Task_2");
        Assert.Equal("End_1", cond.To);
        Assert.Equal("ok", cond.Name);
        Assert.Equal("estado == 'ok'", cond.Condition);
    }

    [Fact]
    public void Package_DoesNotCarryTenantIds()
    {
        // Ninguna referencia por Guid de tenant: cargos/agentes/reglas van por NOMBRE.
        var json = JsonSerializer.Serialize(Sample(), Json);
        Assert.DoesNotContain("OrgUnitId", json);
        Assert.DoesNotContain("AiAgentId", json);
        Assert.DoesNotContain("ColmenaClientId", json);
        Assert.Contains("Clasificador de contactos", json);
        Assert.Contains("DIRECTOR", json);
    }
}
