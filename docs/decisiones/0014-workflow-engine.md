# ADR-0014: WorkflowEngine propio (BPMN 2.0, port de AdmWorkflow) - FASE 4, ola 1

- Estado: aceptada
- Fecha: 2026-07-03
- Relacionada con: ADR-0001 (DAL dual), ADR-0012 (.NET 10), ADR-0013 (nucleo TaskItem)

## Contexto

El legacy GestionMovil ejecuta flujos de proceso con `AdmWorkflow.vb`: los diagramas BPMN
se materializan en `DOC_PROCESOS_R` (nodos) y `DOC_PROCESOS_RULES` (aristas), y cada caso
avanza sobre `TAR_SEGUIMIENTO_PROCESO` con un while de hasta 50 iteraciones
(`SiguienteEstado`), reinicios/loops por `ID_REINICIO` con un CTE recursivo
(`ProcesarReinicio`) y ciclos append-only (`ACTIVIDAD_CICLO`). El vault documenta el port
en `Capa 3 Flujos de Tareas BPMN` (fichas `AdmWorkflow - Motor de ejecucion` y
`Ejecucion - SiguienteEstado y Reinicios`).

## Decision

### 1. Motor propio, sin dependencia de un engine BPMN externo

Se construye `WorkflowEngine` (Ecorex.Application/Workflows) en vez de adoptar Camunda/
Elsa/etc.: el subconjunto ejecutable es pequeno (startEvent, task, exclusiveGateway,
endEvent, sequenceFlow), la semantica heredada (tope 50, ciclos append-only, reinicios)
no mapea a engines estandar, y el motor debe vivir bajo el filtro global multi-tenant y
el DAL dual con resultados tipados (patron TaskCoreResults).

### 2. XML BPMN 2.0 estandar, sin extensiones, guardado tal cual

`ImportBpmnAsync` parsea con XDocument el namespace OMG
(`http://www.omg.org/spec/BPMN/20100524/MODEL`, cualquier prefijo bpmn:/bpmn2:) y
materializa `WorkflowDefinition` + `WorkflowNode` + `WorkflowEdge`. El XML se persiste
SIN modificar: round-trip garantizado con bpmn.io/bpmn-js (el editor visual llega en la
proxima ola). Las condiciones de compuertas usan el elemento estandar
`bpmn:conditionExpression` con un formato simple ("approval == 'Approved'", vacio =
default) evaluado por `WorkflowConditionEvaluator` (fail-closed). El destino de reinicio
(`WorkflowNode.RestartNodeId`, el `ID_REINICIO` legacy) NO forma parte del estandar y se
configura tras importar (`SetRestartTargetAsync`); asi el XML permanece 100% portable.

Validaciones de import: exactamente 1 startEvent, al menos 1 endEvent, ids unicos y
aristas que apuntan a nodos existentes. Elementos no ejecutables (anotaciones,
asociaciones, DI) se ignoran.

### 3. Versionado de definiciones que FIJA la version por instancia

Reimportar un `ProcessCode` crea la version `max+1` NO publicada; `PublishAsync` deja a
lo sumo UNA version publicada por (TenantId, ProcessCode) despublicando la anterior. Las
instancias en curso quedan ancladas a su `DefinitionId` (version concreta): cambiar el
proceso nunca rompe casos vivos. Unicidad en BD: (TenantId, ProcessCode, Version).

### 4. Semantica de ejecucion heredada (reglas que se preservan exactamente)

- **Tope de 50 iteraciones** en el avance en cascada (`WorkflowEngine.MaxAdvanceIterations`,
  port del while de `SiguienteEstado`): al alcanzarlo, la instancia queda `Stuck` y el
  resultado tipado es `StuckDetected` (alimentara el KPI workflow_stuck_rate). Tambien se
  marca `Stuck` el caso degenerado "sin pasos vigentes y sin endEvent alcanzado" (rama
  muerta), para no dejar instancias Running zombis.
- **Historial APPEND-ONLY** (`WorkflowStepHistory`, port de TAR_SEGUIMIENTO_PROCESO):
  nunca update destructivo ni delete; los reinicios agregan filas con `CycleIndex+1` e
  `IsCycleStart` y las de ciclos previos se conservan. Ramas vivas al completarse la
  instancia quedan `Skipped` (no se borran). `IsCurrent` es el FLAG_SIGUIENTE legacy.
- **Reinicios en LINQ/memoria, SIN SQL crudo ni CTE**: el `ProcesarReinicio` legacy usaba
  un CTE recursivo porque clonaba la subgraph en SQL; aqui el grafo de una definicion es
  pequeno (decenas de nodos), se carga completo a memoria y el reinicio es "crear el paso
  Pending del nodo destino con CycleIndex+1". Cero SQL crudo cumple la regla 2 del repo y
  el CTE dual (WITH RECURSIVE vs ;WITH) deja de ser necesario.
- **startEvent se completa solo** al arrancar la instancia; el gateway es el paso de
  aprobacion (FLAG_APROBACION legacy): se completa con `approvalResult` y sus aristas se
  evaluan contra ese valor; nodos no-gateway con varias salidas activan TODAS las ramas
  (paralelismo simple del legacy).
- **RejectStepAsync reactiva el paso anterior** como fila nueva (append-only), el
  rechazado queda `Rejected` con su comentario.

### 5. Hook de reglas para la siguiente ola (RulesEngine)

`IWorkflowRuleHook.OnNodeActivatedAsync` se invoca al activar cada nodo Task; si devuelve
`AutoComplete`, el paso se completa solo y la cascada continua (las "reglas autonomas"
del legacy). La implementacion registrada es `NoOpWorkflowRuleHook`; la ola RulesEngine
la reemplaza en DI sin tocar el motor.

### 6. Integracion con el nucleo TaskItem

- `WorkflowInstance.TaskItemId` (unico si no nulo) + `TaskItem.WorkflowInstanceId`
  (FKs sin cascada, referencia circular controlada); `ActivityType.WorkflowDefinitionId`
  pasa de placeholder a FK real NO ACTION.
- `TaskItemService.CreateAsync`: si el ActivityType tiene definicion PUBLICADA, arranca
  la instancia dentro de la MISMA transaccion (el motor detecta la transaccion abierta
  via `IApplicationDbContext.HasActiveTransaction` y se une). La tarea pasa a Active y al
  completarse el flujo a Done, siempre validando con `TaskItemStateMachine`; cada
  transicion registra `TaskItemActivity` y emite `ITaskBroadcaster.TaskChangedAsync`.
- Concurrencia optimista portable en `WorkflowInstance` (columna Version, patron
  IVersioned de ADR-0013): carreras de completado -> `Conflict` tipado.

### 7. Cascadas y DAL dual

`workflow_edges` tiene doble ruta de cascada potencial en SQL Server (definition->edges y
definition->nodes->edges): igual que `TaskCardTagAssignment`, las FKs hacia los nodos son
Cascade en PostgreSQL y ClientCascade (NO ACTION en BD) en SQL Server. `RestartNodeId` es
self-FK NO ACTION. Migracion `AddWorkflowEngine` en ambos proveedores.

## Consecuencias

- El editor visual (bpmn-js) y el RulesEngine llegan en olas siguientes sin cambios de
  esquema previstos: el XML ya se conserva intacto y el hook ya existe.
- Los gateways solo soportan decision por `approvalResult` en esta ola; condiciones sobre
  datos del formulario dinamico llegaran con el RulesEngine via el hook.
- Los subprocesos BPMN, eventos intermedios y userTask/serviceTask NO se ejecutan (se
  ignoran al importar los que no son del subconjunto; si estan conectados por
  sequenceFlow el import falla por arista a nodo inexistente, que es el comportamiento
  deseado: mejor rechazar que ejecutar a medias).
- Tests: parser y evaluador con unit tests; motor completo con integracion DUAL
  (import del fixture real 00001, lineal con TaskItem->Done, gateway Approved/Rejected,
  reinicio con CycleIndex+1, Stuck al tope de 50, aislamiento multi-tenant y
  append-only tras reinicio).
