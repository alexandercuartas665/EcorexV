namespace Ecorex.Application.Workflows;

/// <summary>
/// Motor de flujos BPMN del tenant activo (FASE 4, ola 1: port del AdmWorkflow legacy).
/// Importa XML BPMN 2.0 estandar (guardado tal cual, portabilidad bpmn.io), publica
/// versiones, arranca instancias (opcionalmente ligadas a un TaskItem) y avanza casos
/// con la semantica heredada: tope de 50 iteraciones, historial append-only, ciclos por
/// RestartNodeId (CycleIndex+1) y hook de reglas autonomas (IWorkflowRuleHook).
/// Todo transaccional y con resultados tipados (patron TaskCoreResults); cero SQL crudo.
/// </summary>
public interface IWorkflowEngine
{
    /// <summary>
    /// Importa un XML BPMN 2.0 y materializa definicion + nodos + aristas. Valida
    /// exactamente 1 startEvent, al menos 1 endEvent, ids unicos y aristas coherentes.
    /// Si el ProcessCode ya existe, crea una version nueva (max+1) NO publicada.
    /// </summary>
    Task<WorkflowResult<WorkflowDefinitionDto>> ImportBpmnAsync(ImportBpmnRequest request, CancellationToken cancellationToken = default);

    /// <summary>Publica la definicion; despublica cualquier otra version del mismo ProcessCode.</summary>
    Task<WorkflowResult<WorkflowDefinitionDto>> PublishAsync(Guid definitionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Configura el destino de reinicio de un nodo (ID_REINICIO legacy). Los reinicios no
    /// forman parte del XML BPMN estandar, por eso se configuran tras importar.
    /// </summary>
    Task<WorkflowResult<bool>> SetRestartTargetAsync(Guid nodeId, Guid? restartNodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Arranca una instancia: crea el caso Running con el paso del startEvent (ciclo 0) y
    /// avanza automaticamente (los startEvent se completan solos), dejando current el/los
    /// siguientes. Si taskItemId viene, enlaza TaskItem.WorkflowInstanceId, pasa la tarea
    /// a Active respetando TaskItemStateMachine y registra la actividad "inicio flujo X".
    /// Detecta si el llamador ya abrio una transaccion y se une a ella.
    /// </summary>
    Task<WorkflowResult<WorkflowInstanceDto>> StartInstanceAsync(
        Guid definitionId, Guid? taskItemId = null, Guid? actorUserId = null, string actorName = "Sistema",
        CancellationToken cancellationToken = default);

    /// <summary>Pasos IsCurrent de la instancia con los datos de su nodo.</summary>
    Task<IReadOnlyList<WorkflowStepDto>> GetCurrentStepsAsync(Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Completa un paso current y Pending (con resultado/comentario de aprobacion para
    /// compuertas) y ejecuta el avance en cascada. StuckDetected si se alcanza el tope de 50.
    /// </summary>
    Task<WorkflowResult<WorkflowInstanceDto>> CompleteStepAsync(
        Guid instanceId, Guid stepId, Guid? executedByTenantUserId,
        string? approvalResult = null, string? approvalComment = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rechaza un paso current y REACTIVA el paso anterior creando una fila nueva Pending
    /// del nodo previo (historial append-only: el paso rechazado se conserva).
    /// </summary>
    Task<WorkflowResult<WorkflowInstanceDto>> RejectStepAsync(
        Guid instanceId, Guid stepId, Guid? tenantUserId, string reason,
        CancellationToken cancellationToken = default);
}
