namespace Ecorex.Application.Workflows;

/// <summary>
/// Bandeja operativa de flujos (runtime, ola F2, ADR-0036): "mis pasos pendientes" y las
/// acciones para atenderlos. Es la capa que une la asignacion por nodo (INodeAssigneeResolver,
/// ADR-0035) con el motor (IWorkflowEngine, ADR-0014): resuelve que pasos current+Pending de
/// instancias Running puede atender un usuario (asignado o candidato del cargo), permite
/// reclamar el paso (modelo "cualquiera lo toma"), reasignarlo (si el nodo lo permite) y
/// completarlo (con resultado de aprobacion para compuertas). Los pasos con formulario se
/// completan por el flujo de formularios (IFormResponseService.SaveAsync submit); este servicio
/// cubre los pasos SIN formulario o la decision de aprobacion. Tenant-scoped, resultados tipados.
/// </summary>
public interface IWorkflowInboxService
{
    /// <summary>
    /// Pasos current+Pending de instancias Running del tenant que el usuario puede atender:
    /// es el asignado (AssignedToTenantUserId == tenantUserId) o, si el paso esta sin asignar,
    /// es candidato de la policy del nodo (INodeAssigneeResolver). Ordenados por fecha.
    /// </summary>
    Task<IReadOnlyList<PendingStepDto>> GetMyPendingStepsAsync(Guid tenantUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reclama un paso sin asignar ("cualquiera lo toma"): si el usuario es candidato, fija
    /// AssignedToTenantUserId = tenantUserId. NotFound si el paso no existe/ya no es vigente;
    /// Invalid si el usuario no es candidato; Conflict si ya esta asignado a otro y el nodo no
    /// permite reasignacion.
    /// </summary>
    Task<WorkflowResult<bool>> ClaimStepAsync(Guid stepId, Guid tenantUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reasigna un paso a otro candidato. Solo si el nodo tiene AllowsAssignment y el destino
    /// es candidato de la policy del nodo. Auditado en la actividad de la tarea.
    /// </summary>
    Task<WorkflowResult<bool>> ReassignStepAsync(Guid stepId, Guid toTenantUserId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Completa un paso SIN formulario (o adjunta la decision de aprobacion en compuertas):
    /// valida que el usuario sea el asignado o candidato, resuelve instanceId/stepId y delega
    /// en IWorkflowEngine.CompleteStepAsync (que avanza el caso). Para pasos con formulario la
    /// UI usa el flujo de formulario (SaveAsync submit), que ya completa el paso.
    /// </summary>
    Task<WorkflowResult<WorkflowInstanceDto>> CompletePendingStepAsync(
        Guid stepId, Guid tenantUserId, string? approvalResult, string? approvalComment,
        CancellationToken cancellationToken = default);
}
