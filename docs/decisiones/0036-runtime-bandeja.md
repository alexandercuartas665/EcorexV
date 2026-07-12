# ADR-0036: Runtime operativo de flujos - bandeja "mis pasos" y atender (ola F2)

- Estado: aceptada, PARCIALMENTE SUPERSEDIDA por ADR-0038 (2026-07-12). Se conserva el
  motor y `IWorkflowInboxService` y la seccion "Flujo" del detalle de la tarea; se RETIRA
  la pagina/ruta `/mis-pasos`, el item de menu 000637 y la policy `MisPasos.Ver`. El
  descubrimiento de pasos pendientes pasa al TABLERO ("mis pendientes"), no a una bandeja.
- Fecha: 2026-07-08
- Relacionada con: ADR-0035 (asignacion por nodo, ola F1), ADR-0014 (workflow
  engine BPMN), ADR-0015 (formularios dinamicos + FormFlowLink), ADR-0022/0034
  (editor de flujos), ADR-0016 (rules engine).

## Contexto

El motor de flujos (ADR-0014) arranca instancias, avanza casos, resuelve compuertas
y cierra la tarea; la ola F1 (ADR-0035) definio QUIEN atiende cada nodo (policies
Dependencia/Cargo + `INodeAssigneeResolver`). Lo que faltaba para que los flujos
sean OPERATIVOS es la capa de USO: una bandeja donde el usuario ve "mis pasos
pendientes" y los atiende (diligenciar el formulario del nodo, o completar/aprobar),
haciendo AVANZAR el caso. El motor ya hacia casi todo (`CompleteStepAsync`,
`GetCurrentStepsAsync`, formularios que completan el paso al enviarse); esta ola es
sobre todo la QUERY de bandeja, la UI y el cableado. Cierra el objetivo de flujos
operativos y elimina la deuda declarada en ADR-0014 ("bandeja de pasos") que hasta
ahora solo el backdoor de E2E ejercitaba.

## Decisiones

### 1. IWorkflowInboxService: bandeja + acciones, tenant-scoped y tipado

Servicio nuevo en `Ecorex.Application/Workflows` (mismo patron de resultados tipados
`WorkflowResult<T>` del motor):

- `GetMyPendingStepsAsync(tenantUserId)`: pasos `IsCurrent && Status==Pending` de
  instancias `Running` del tenant (filtro global) que el usuario puede atender.
  Candidato = `AssignedToTenantUserId == tenantUserId` OR (`AssignedToTenantUserId ==
  null` AND el usuario esta entre `INodeAssigneeResolver.ResolveCandidates(nodeId)`).
  Devuelve por paso: identificadores, numero/titulo de la tarea, proceso, nodo, estado
  de asignacion ("Sin reclamar"/"Tuyo"/"de Fulano"), `hasForm`, `isGatewayAhead` +
  las opciones de decision, ciclo y fecha. Ordena por fecha.
- `ClaimStepAsync(stepId, tenantUserId)`: modelo "cualquiera lo toma": si el paso esta
  sin asignar y el usuario es candidato, fija `AssignedToTenantUserId`. Rechaza si ya
  esta asignado a otro y el nodo no permite reasignacion (`Conflict`).
- `ReassignStepAsync(stepId, toTenantUserId, actorUserId)`: solo si el nodo tiene
  `AllowsAssignment` y el destino es candidato; auditado en la actividad de la tarea.
- `CompletePendingStepAsync(stepId, tenantUserId, approvalResult?, approvalComment?)`:
  valida que el usuario sea el asignado o candidato y delega en
  `IWorkflowEngine.CompleteStepAsync` (que avanza el caso). Para pasos CON formulario
  la UI usa el flujo de formulario (`IFormResponseService.SaveAsync` submit), que ya
  completa el paso vinculado en la misma transaccion (ADR-0015); este metodo cubre los
  pasos SIN formulario o la decision de aprobacion.

### 2. Gateway adelante y opciones de aprobacion (resolucion documentada)

Para un paso de un nodo Task, se mira las aristas SALIENTES del nodo; si el target de
alguna es un `ExclusiveGateway`, se marca `isGatewayAhead` y las OPCIONES de decision
son los `Name` de las aristas SALIENTES DE ese gateway (p.ej. "Aprobada"/"Rechazada"),
distintos y no vacios. Esos nombres se pasan como `approvalResult` a `CompleteStep`,
donde el motor los evalua contra el `ConditionExpression` de cada arista del gateway
(la MISMA semantica que `WorkflowEngine.ResolveOutgoing`). Asi la UI ofrece exactamente
las salidas modeladas en el BPMN, sin adivinar. Esta logica (deteccion + extraccion de
opciones + regla de candidatura) se aisla en un proyector PURO
(`WorkflowInboxProjection`, sin EF) siguiendo el precedente de `OrgAssigneeTree`, para
verificarla en unit tests sin base de datos.

### 3. Pagina /mis-pasos + integracion en el detalle de tarea

`Components/Pages/MisPasos.razor` (`@page "/mis-pasos"`, InteractiveServer, tokens
ECOREX claro/oscuro, policy `MisPasos.Ver` = `RequireClaim(tenant_id)`: es la bandeja
del usuario, la ve cualquier miembro del tenant). Tarjetas de pasos con "Tomar" y un
panel "Atender" que renderiza el `DynamicFormRenderer` (si el nodo tiene formulario;
al enviar, el paso se completa y el flujo avanza) o botones Completar/Aprobar (segun
`isGatewayAhead` y sus opciones) + comentario, y "Reasignar" si el nodo lo permite.
Empty state y boton "Actualizar" (refresco por SignalR queda como deuda: NO bloquea la
ola). El item de menu "Mis pasos" (route `mis-pasos`, code 000637) se siembra en la
seccion "Mis Procesos" y se reconcilia en demos ya sembrados.

Ademas, el detalle de tarea (`TaskDetailModal.razor`) gana una seccion "Flujo" que,
si el usuario es candidato de algun paso current de la tarea, ofrece el mismo
Tomar/Completar/Aprobar reusando `IWorkflowInboxService` (los pasos con formulario se
siguen atendiendo por "Formularios del paso").

### 4. Seed demo para validar end-to-end

`EnsureWorkflowRuntimeDemoAsync` crea (idempotente) una TAREA del ActivityType
vinculado a COT-COM ("Direccion Comercial/Cotizacion") via el flujo normal
(`ITaskItemService.CreateAsync`), lo que arranca una `WorkflowInstance` Running con el
paso "Requerimiento" Pending y sin reclamar. Como el nodo Requerimiento tiene la policy
del cargo "Asesor Comercial" (ocupado por operator@), al entrar a /mis-pasos como
operator@ hay un paso listo para atender. Requerimiento no tiene formulario, asi que la
bandeja permite completarlo directo (al avanzar, el paso Cotizacion queda para el cargo
Aprobador; Cotizacion apunta a la compuerta, con opciones Aprobada/Rechazada).

## Consecuencias

- Los flujos quedan OPERATIVOS de punta a punta desde la UI del producto: crear tarea
  -> paso en la bandeja del candidato -> atender -> avanza -> siguiente cargo ->
  compuerta por `approvalResult`. El backdoor de E2E (ADR-0014) ya no es la unica via.
- Sin migracion: todo el modelo (WorkflowStepHistory con `AssignedToTenantUserId`,
  policies, node forms, edges) ya existia. La ola es query + UI + cableado.
- El modelo "cualquiera lo toma" (claim) mantiene el paso sin asignar visible para TODOS
  los candidatos hasta que uno lo reclama; la reasignacion queda gobernada por
  `AllowsAssignment` del nodo.

## Deudas

- Refresco en vivo por SignalR de la bandeja (hoy: boton "Actualizar"). No bloquea.
- La reasignacion usa como universo del selector todos los usuarios del tenant y el
  servicio valida candidatura real; un selector que muestre solo candidatos del nodo
  seria mas fino.
