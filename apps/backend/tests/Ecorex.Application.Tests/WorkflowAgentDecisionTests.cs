using Ecorex.Application.Workflows;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// Parser de la decision del agente de flujo (ADR-0090). Fija que:
/// - Un Task devuelve 'resultado'; una compuerta devuelve 'ruta' (ola B). El parser lee AMBOS y no impone
///   cual: el runner exige el que corresponde al tipo de nodo.
/// - Sin resultado NI ruta (o con puede_resolver=false) el agente "no pudo" y el paso vuelve a una persona.
/// Puro, sin BD.
/// </summary>
public class WorkflowAgentDecisionParserTests
{
    [Fact]
    public void Task_ParsesResultado()
    {
        var r = WorkflowAgentDecisionParser.Parse("""{"puede_resolver": true, "resultado": "Approved", "comentario": "ok"}""");
        Assert.True(r.Ok);
        Assert.Equal("Approved", r.Result);
        Assert.Null(r.Route);
        Assert.Equal("ok", r.Comment);
    }

    [Fact]
    public void Gateway_ParsesRuta()
    {
        var r = WorkflowAgentDecisionParser.Parse("""{"puede_resolver": true, "ruta": "Task_Facturacion", "comentario": "monto alto"}""");
        Assert.True(r.Ok);
        Assert.Equal("Task_Facturacion", r.Route);
        Assert.Null(r.Result);
    }

    [Fact]
    public void Ruta_IsNotClippedTo20Chars()
    {
        // La ruta es una clave (BpmnElementId) o el nombre del destino: puede pasar de 20 caracteres.
        var key = "Task_RevisionComercialYAprobacionDeDescuento";
        var r = WorkflowAgentDecisionParser.Parse($$"""{"puede_resolver": true, "ruta": "{{key}}", "comentario": "x"}""");
        Assert.Equal(key, r.Route);
    }

    [Fact]
    public void NeitherResultNorRoute_IsFailed()
    {
        var r = WorkflowAgentDecisionParser.Parse("""{"puede_resolver": true, "comentario": "no se"}""");
        Assert.False(r.Ok);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void CannotResolve_IsFailed_WithReason()
    {
        var r = WorkflowAgentDecisionParser.Parse("""{"puede_resolver": false, "comentario": "falta el monto"}""");
        Assert.False(r.Ok);
        Assert.Contains("falta el monto", r.Error);
    }

    [Fact]
    public void NonJson_IsFailed()
    {
        var r = WorkflowAgentDecisionParser.Parse("no soy json");
        Assert.False(r.Ok);
    }
}

/// <summary>
/// Serializador del contexto del agente (ADR-0090 ola B): una compuerta debe MOSTRAR sus rutas al modelo,
/// con la clave del destino, para que pueda elegir una. Puro, sin BD.
/// </summary>
public class WorkflowAgentContextSerializerRoutesTests
{
    private static WorkflowAgentContextDto GatewayContext(IReadOnlyList<WorkflowAgentRouteDto>? routes)
    {
        var node = new WorkflowAgentNodeDto(
            Guid.NewGuid(), "Gateway_1", "Decide compra", "Si el cliente compra, factura; si no, cierra.",
            WorkflowNodeType.ExclusiveGateway, null, Form: null, Routes: routes);
        return new WorkflowAgentContextDto(
            Guid.NewGuid(), Guid.NewGuid(), node,
            new WorkflowAgentPriorDataDto(Array.Empty<WorkflowAgentPriorFormDto>(), false),
            Task: null,
            new WorkflowAgentHistoryDto(Array.Empty<WorkflowAgentHistoryStepDto>(), 0, false),
            Assignment: null);
    }

    [Fact]
    public void Gateway_ListsRoutesWithKeys()
    {
        var routes = new[]
        {
            new WorkflowAgentRouteDto("Task_Facturacion", "Facturacion", "compra", "approval == 'Compra'"),
            new WorkflowAgentRouteDto("End_NoCompra", "Fin sin compra", null, null)
        };
        var text = WorkflowAgentContextSerializer.ToText(GatewayContext(routes));

        Assert.Contains("Rutas de la compuerta", text);
        Assert.Contains("Task_Facturacion", text);
        Assert.Contains("Facturacion", text);
        Assert.Contains("approval == 'Compra'", text);
        Assert.Contains("End_NoCompra", text);
        Assert.Contains("sin condicion", text); // la rama por defecto se anuncia
    }

    [Fact]
    public void NoRoutes_DoesNotEmitRouteSection()
    {
        var text = WorkflowAgentContextSerializer.ToText(GatewayContext(routes: null));
        Assert.DoesNotContain("Rutas de la compuerta", text);
    }
}
