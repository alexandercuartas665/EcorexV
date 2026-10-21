# ADR-0126: Liberar la linea de WhatsApp apartada por el flujo al cerrar la tarea

Fecha: 2026-10-20
Estado: Aceptada

## Contexto

Cuando una tarea cae en un flujo y un nodo-agente necesita preguntarle algo al cliente por WhatsApp, el sistema
APARTA la linea (ADR-0122): estampa en la conversacion `FlowHoldStepId` + `FlowHoldNodeId`. Mientras este
apartada: un solo dueno (ningun otro paso la toma), la respuesta del cliente se enruta exactamente a ese paso, y
SARA (el agente conversacional de la linea) se calla.

Esa marca solo la soltaba el PROPIO paso-agente al resolver / volver a persona / fallar, dentro de
`WorkflowAgentStepRunner.ReleaseConversationHoldAsync`. Es decir, la liberacion dependia de que ese paso se
re-ejecutara. Si la tarea llegaba a CIERRE por otra via, la marca quedaba colgada:

- **Cierre manual** (arrastrar la tarjeta a una columna de cierre): `ActivityBoardService.MoveTaskAsync` pone la
  tarea en Done pero NO llama al motor, NO avanza el flujo y NO tocaba el hold. El paso-agente seguia
  `IsCurrent + Pending` y `FlowHoldStepId` seguia apuntandole.
- **Cierre por flujo / reabrir**: el motor apaga pasos (`IsCurrent=false`, barrido de cierre de instancia,
  reabrir aguas abajo) sin pasar por el runner, asi que tampoco soltaba el hold.

Consecuencia: la linea quedaba CAPTURADA indefinidamente (SARA muda, ninguna otra tarea podia tomarla) y los
entrantes del cliente REACTIVABAN un paso de una tarea ya cerrada (`ChatIngestService` enruta por
`FlowHoldStepId` y limpia `AgentAttemptedAt`).

## Decision

La linea NUNCA debe sobrevivir al paso-agente que la aparto. Se centraliza la liberacion en un helper
(`FlowLineRelease`) y se engancha en todos los caminos donde un paso retenido deja de ser el dueno activo sin
pasar por el runner:

1. **Cierre de la tarea (manual o por flujo)** -> `FlowLineRelease.OnTaskClosedAsync`:
   - suelta el hold (`FlowHoldStepId`/`FlowHoldNodeId` = null) de toda conversacion apartada por un paso de esa
     tarea;
   - deja la **marca de cierre-por-flujo** (`FlowHandledCloseAt` = ahora, `FlowHandledTaskId` = tarea) para que el
     candado de SARA (`AgentTaskIdempotency`, ventana 24h) NO recree un caso/tarea/lead duplicado;
   - ABANDONA el paso-agente retenido que siga vigente (`IsCurrent=false`, `Pending -> Skipped`, limpia la espera
     de WhatsApp y el reintento) para que el barrido y la ingesta dejen de apuntar a una tarea cerrada.
   Enganchado en `WorkflowEngine` (tras `CompleteInstance`) y en `ActivityBoardService.MoveTaskAsync` (al caer en
   columna de cierre).
2. **Pasos apagados por el motor sin runner** (barrido de cierre de instancia y reabrir aguas abajo) ->
   `FlowLineRelease.ReleaseHoldsForStepsAsync`: suelta el hold de esos pasos (sin marca de cierre: no siempre es
   un cierre de caso).

El avance NORMAL entre nodos-agente ya estaba cubierto: al resolver, el paso suelta su propio hold
(`ReleaseConversationHoldAsync`) antes de activarse el siguiente; y el relevo ADR-0122 (gana la tarea mas
reciente) libera al dueno viejo cuando otra tarea toma la misma linea.

Comportamiento tras liberar: la linea queda libre; el proximo entrante del cliente lo atiende SARA (agente
conversacional), que puede conversar pero NO crea duplicados por la marca de cierre.

## Por que un helper estatico sobre el mismo DbContext

El cierre ocurre en dos servicios distintos (`WorkflowEngine` y `ActivityBoardService`), cada uno con su propio
`IApplicationDbContext` y su propio `SaveChanges` transaccional. El helper es estatico, muta las entidades
rastreadas del contexto que recibe y NO llama `SaveChanges`: el que cierra guarda, en su misma transaccion. Mismo
patron que `AgentTaskIdempotency`.

## Consecuencias

- Cerrar una tarea (a mano o por flujo) libera la linea; SARA la retoma sin recrear el caso.
- Un paso-agente de una tarea cerrada deja de insistirle al cliente y de capturar la linea.
- Sin migracion: reusa columnas existentes (`FlowHoldStepId`, `FlowHoldNodeId`, `FlowHandledCloseAt`,
  `FlowHandledTaskId`).
- Alcance de la marca de cierre: se pone sobre la conversacion que estaba APARTADA al cerrar. El cierre por el
  agente que rutea una salida con nota de traspaso ya ponia la marca por su cuenta (`EmitRouteHandoffAsync`).
