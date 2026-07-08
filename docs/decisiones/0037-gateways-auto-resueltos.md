# ADR-0037: Compuertas exclusivas auto-resueltas en el motor (decision capturada en el paso previo)

- Estado: aceptada
- Fecha: 2026-07-08
- Relacionada con: ADR-0014 (workflow engine BPMN), ADR-0015 (formularios dinamicos +
  FormFlowLink), ADR-0036 (runtime/bandeja), ADR-0035 (asignacion por nodo).

## Contexto

El motor de flujos (ADR-0014) resolvia las compuertas exclusivas dejandolas como un
paso `Pending`-current: el humano las "completaba" con una decision (`approvalResult`)
y el motor enrutaba por el `ConditionExpression` de sus aristas. Esa logica de completar
el gateway vivia SOLO en `WorkflowInboxService.CompletePendingStepAsync` (la bandeja
no-form): tras completar el Task, buscaba el gateway current y lo completaba con el mismo
`approvalResult`.

El GAP: cuando el nodo previo (p.ej. "Cotizacion") tiene FORMULARIO, atender el paso
pasa por `FormResponseService.SaveAsync(submit)` -> `IWorkflowEngine.CompleteStepAsync`
SIN `approvalResult`. Ese camino NO pasaba por el inbox service, asi que el gateway
quedaba `Pending`-current para siempre: los gateways no tienen `WorkflowNodePolicy`, no
aparecen en ninguna bandeja y NADIE los resolvia. En la BD dev habia 25 instancias de
COT-COM con "Cotizacion" Completed y "Aprobacion" varado (0 gateways resueltos).

Un `exclusiveGateway` es una compuerta automatica del proceso: NUNCA debe esperar a un
humano. La decision no es del gateway, es del paso que ENTRA a el.

## Decisiones

### 1. El motor auto-resuelve el gateway en la misma cascada de `AdvanceAsync`

Al activar un `exclusiveGateway` (`ActivateNodeAsync`), el motor ya no lo deja `Pending`:
lo marca `Completed` en el acto y HEREDA el `ApprovalResult` del paso que lo activo (la
decision del paso previo). Como el bucle `while` de `AdvanceAsync` toma como `IsReady`
todo paso `Completed`+`IsCurrent`, el gateway se procesa en la MISMA pasada:
`ResolveOutgoing` evalua sus aristas contra ese `ApprovalResult` (o toma la arista
default). El gateway sigue siendo una fila de historial (auditoria, append-only) con su
decision heredada, pero nunca es un paso pendiente de atencion.

- Si ninguna condicion coincide y no hay arista default -> comportamiento `Stuck` actual.
- Se mantiene el tope de 50 iteraciones y el caracter append-only del historial.

La logica de completar el gateway se ELIMINA de `WorkflowInboxService`: es
responsabilidad unica del motor.

### 2. La decision se captura en el paso que entra al gateway

Para que el enrutado sea real, el `ApprovalResult` del paso previo (el Task) se fija al
completarlo, sin importar el camino:

- **Bandeja no-form**: `CompletePendingStepAsync` ya recibia `approvalResult` (opciones
  derivadas de los `Name` de las aristas del gateway, `WorkflowInboxProjection`); ahora se
  limita a completar el Task con esa decision y deja que el motor resuelva el gateway.
- **Formulario**: `IFormResponseService.SaveAsync` acepta un `approvalResult` opcional que
  propaga a `CompleteStepAsync` del paso vinculado. `GetTaskStepFormsAsync` calcula
  `IsGatewayAhead` + `ApprovalOptions` (misma logica pura que la bandeja) y los expone en
  `TaskStepFormDto`. El `DynamicFormRenderer` recibe `ApprovalOptions`: si vienen, muestra
  la decision (radio) JUNTO al formulario y deshabilita "Enviar" hasta elegir; al enviar
  propaga la decision. Si el nodo con form NO tiene gateway adelante, no pide decision.

### 3. Rechazo: los gateways se atraviesan

`RejectStepAsync` reactivaba "el nodo fuente del paso rechazado". Como los gateways ya no
son pasos humanos, la busqueda de fuente reactivable ATRAVIESA los `exclusiveGateway`
hacia sus propias fuentes (y sigue saltando `startEvent`) hasta llegar a un nodo humano
(`Task`). Evita ciclos con un conjunto de visitados.

### 4. Limpieza de datos varados (idempotente)

`DatabaseSeeder.ResolveStuckGatewaysAsync(engine)` (solo Development, ambient del tenant)
resuelve los gateways que ya quedaron `Pending`-current: hereda la decision del paso
Completed que entro al gateway (o toma la default) y delega en el motor. Sin varados es
no-op. Se corre tras `EnsureWorkflowRuntimeDemoAsync`.

## Consecuencias

- Un caso con formulario + gateway (COT-COM: Cotizacion con FRM-001) ahora enruta a
  Facturacion/reinicio al enviar el formulario con la decision; ya no se estanca.
- Un `exclusiveGateway` nunca es un paso de bandeja; su fila de historial queda
  `Completed` con la decision heredada.
- Los tests de motor que "completaban el gateway" se ajustan: la decision se captura en el
  paso previo (Task), no en el gateway.
- Se agrega cobertura dual (PG + SQL Server): Task con gateway adelante completado con
  decision (Aprobada -> Facturacion; Rechazada -> reinicio) y Task CON FORMULARIO con
  gateway adelante enviado con decision (enruta y el gateway queda resuelto).
