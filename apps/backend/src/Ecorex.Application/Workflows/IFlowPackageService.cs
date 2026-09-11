namespace Ecorex.Application.Workflows;

/// <summary>
/// Empaqueta un flujo a un JSON PORTABLE y lo importa a otro tenant (ADR-0097 Ola B1). El export captura
/// el grafo + formularios embebidos + cargos/agentes/reglas por NOMBRE; el import crea un flujo BORRADOR
/// nuevo y remapea cargos/agentes/reglas por nombre (lo que no coincide queda sin asignar, con reporte).
/// </summary>
public interface IFlowPackageService
{
    /// <summary>Serializa un flujo (por su definicion) al paquete portable.</summary>
    Task<WorkflowResult<string>> ExportAsync(Guid definitionId, CancellationToken cancellationToken = default);

    /// <summary>Crea un flujo NUEVO (borrador) en el tenant activo a partir del paquete. Devuelve el reporte
    /// de que se mapeo y que quedo sin asignar.</summary>
    Task<WorkflowResult<FlowImportReport>> ImportAsync(string json, FlowImportOptions options, CancellationToken cancellationToken = default);
}
