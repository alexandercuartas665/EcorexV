namespace Ecorex.Application.Workflows;

/// <summary>
/// Crea una TAREA HIJA al saltar de un flujo a otro (ADR-0076): cuando un flujo llega a un evento de FIN
/// con <c>JumpToDefinitionId</c>, se crea una TaskItem nueva colgada del padre (<c>ParentId</c>) que corre
/// ese otro flujo, heredando la Entidad y los adjuntos del padre. Es un seam ESTRECHO que el WorkflowEngine
/// invoca perezosamente (via IServiceProvider) para NO acoplarse a TaskItemService y evitar el ciclo de DI
/// (TaskItemService ya depende de IWorkflowEngine). Corre dentro de la MISMA transaccion del avance del padre.
/// </summary>
public interface IChildTaskStarter
{
    /// <summary>
    /// Crea la tarea hija de <paramref name="parentTaskId"/> corriendo el flujo <paramref name="jumpDefinitionId"/>
    /// y le arranca ese flujo. Idempotente: si ya existe una hija del mismo padre corriendo ese flujo, no crea
    /// otra. Devuelve el id de la hija creada, o null si no aplica (flujo destino no publicado, ya existe, etc.).
    /// </summary>
    Task<Guid?> StartChildFromJumpAsync(
        Guid parentTaskId, Guid jumpDefinitionId, CancellationToken cancellationToken = default);
}
