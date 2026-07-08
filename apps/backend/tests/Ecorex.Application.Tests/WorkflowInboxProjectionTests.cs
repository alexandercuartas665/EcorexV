using Ecorex.Application.Workflows;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests unitarios de la logica PURA de la bandeja de pasos (ola F2, ADR-0036):
/// - CanAttend: un paso sin asignar es atendible por un candidato del cargo y NO por un
///   no-candidato; un paso asignado solo por su dueno (modelo "cualquiera lo toma").
/// - ResolveGatewayAhead: deteccion de compuerta adelante y extraccion de sus opciones de
///   decision (los Name de las aristas salientes del gateway, distintos, no vacios).
/// El cruce con EF (query real) se verifica en Integration.Tests (matriz dual).
/// </summary>
public class WorkflowInboxProjectionTests
{
    // ---- CanAttend (candidatura del paso) ----

    [Fact]
    public void CanAttend_UnassignedStep_TrueForCandidate_FalseForNonCandidate()
    {
        var candidate = Guid.NewGuid();
        var other = Guid.NewGuid();
        var nodeCandidates = new[] { candidate };

        Assert.True(WorkflowInboxProjection.CanAttend(candidate, assignedToTenantUserId: null, nodeCandidates));
        Assert.False(WorkflowInboxProjection.CanAttend(other, assignedToTenantUserId: null, nodeCandidates));
    }

    [Fact]
    public void CanAttend_AssignedStep_OnlyOwner_EvenIfOtherIsCandidate()
    {
        var owner = Guid.NewGuid();
        var otherCandidate = Guid.NewGuid();
        // Ambos son candidatos del nodo, pero el paso ya esta asignado al owner.
        var nodeCandidates = new[] { owner, otherCandidate };

        Assert.True(WorkflowInboxProjection.CanAttend(owner, assignedToTenantUserId: owner, nodeCandidates));
        Assert.False(WorkflowInboxProjection.CanAttend(otherCandidate, assignedToTenantUserId: owner, nodeCandidates));
    }

    // ---- ResolveGatewayAhead (compuerta adelante + opciones) ----

    [Fact]
    public void ResolveGatewayAhead_NodeIntoGateway_ExtractsGatewayOutgoingNamesAsOptions()
    {
        var taskNode = Guid.NewGuid();
        var gateway = Guid.NewGuid();
        var facturacion = Guid.NewGuid();
        var reinicio = Guid.NewGuid();

        var edges = new[]
        {
            new WorkflowInboxProjection.EdgeRow(taskNode, gateway, null),        // Task -> Gateway
            new WorkflowInboxProjection.EdgeRow(gateway, facturacion, "Aprobada"),
            new WorkflowInboxProjection.EdgeRow(gateway, reinicio, "Rechazada"),
        };
        var gatewayNodeIds = new HashSet<Guid> { gateway };

        var (isGatewayAhead, options) = WorkflowInboxProjection.ResolveGatewayAhead(taskNode, edges, gatewayNodeIds);

        Assert.True(isGatewayAhead);
        Assert.Equal(new[] { "Aprobada", "Rechazada" }, options);
    }

    [Fact]
    public void ResolveGatewayAhead_NodeIntoPlainTask_NoGatewayNoOptions()
    {
        var taskNode = Guid.NewGuid();
        var nextTask = Guid.NewGuid();

        var edges = new[]
        {
            new WorkflowInboxProjection.EdgeRow(taskNode, nextTask, null),
        };
        var gatewayNodeIds = new HashSet<Guid>(); // ningun gateway

        var (isGatewayAhead, options) = WorkflowInboxProjection.ResolveGatewayAhead(taskNode, edges, gatewayNodeIds);

        Assert.False(isGatewayAhead);
        Assert.Empty(options);
    }

    [Fact]
    public void ResolveGatewayAhead_DedupsAndIgnoresBlankNames()
    {
        var taskNode = Guid.NewGuid();
        var gateway = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        var edges = new[]
        {
            new WorkflowInboxProjection.EdgeRow(taskNode, gateway, null),
            new WorkflowInboxProjection.EdgeRow(gateway, a, "Aprobada"),
            new WorkflowInboxProjection.EdgeRow(gateway, b, "Aprobada"), // duplicado -> se colapsa
            new WorkflowInboxProjection.EdgeRow(gateway, c, "   "),      // en blanco -> se ignora
        };
        var gatewayNodeIds = new HashSet<Guid> { gateway };

        var (isGatewayAhead, options) = WorkflowInboxProjection.ResolveGatewayAhead(taskNode, edges, gatewayNodeIds);

        Assert.True(isGatewayAhead);
        Assert.Equal(new[] { "Aprobada" }, options);
    }
}
