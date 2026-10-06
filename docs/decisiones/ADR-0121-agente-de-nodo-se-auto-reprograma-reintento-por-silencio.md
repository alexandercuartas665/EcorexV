# ADR-0121: El agente de nodo se auto-reprograma (reintento por silencio del cliente)

**Status:** Aceptado
**Date:** 2026-10-05
**Deciders:** Alexander Cuartas
**Extiende:** ADR-0092 (preguntar por WhatsApp), ADR-0119 (ejecucion diferida), ADR-0120 (el agente usa su medio)

## Contexto

Un agente de nodo puede preguntarle al cliente por WhatsApp y el paso queda EN ESPERA de la
respuesta (ADR-0092). Si el cliente NO responde, hoy el paso solo espera hasta `AgentDeadlineAt`
(6h por defecto) y el reaper lo cierra (vuelve a persona / ruta de contingencia). No hay un
**recordatorio intermedio**: un cliente que "se durmio" simplemente se pierde hasta el timeout.

`FailureRetries` no sirve para esto: es para fallos del PROVEEDOR de IA, no para el silencio del
cliente.

## Decision

En vez de una config fija (intervalo + maximo) con un barrido aparte, **el propio agente se
reprograma** con una herramienta, igual que tendria un MCP para agendar su siguiente iteracion:

1. **Herramienta `programar_reintento`** (ofrecida cuando el nodo tiene linea de WhatsApp): el
   agente la llama JUNTO con `preguntar_whatsapp` ("le escribo ahora y me reprogramo en X por si no
   responde"). Argumento: `en_minutos` (acotado a [5 min, 30 dias]).
2. Al pausar el paso por la pregunta (ADR-0092), el runner ademas estampa
   `WorkflowStepHistory.AgentNextRetryAt = ahora + en_minutos`.
3. **Despertar programado**: el barrido de agentes toma tambien los pasos cuyo `AgentNextRetryAt`
   ya vencio (aunque esten "esperando"), limpia `AgentAttemptedAt`/`AgentNextRetryAt` y re-corre al
   agente. El contexto le dice "el cliente NO ha respondido desde {hora}", asi que **el agente
   redacta el recordatorio** (otra `preguntar_whatsapp`) o, si ya insistio bastante, se rinde.
4. **Si el cliente responde ANTES** del reintento, la ingesta (ChatIngestService) reanuda el paso y
   limpia `AgentNextRetryAt`: el reintento queda sin efecto (ya no hace falta insistir).
5. **Al resolver** (ruta/resultado/form) o devolver a persona/timeout, se limpia `AgentNextRetryAt`.

Tope: el recordatorio reusa `MaxWhatsAppAsks` (4 preguntas por conversacion); agotado, el paso
vuelve a una persona. Asi el agente no insiste infinitamente.

## Por que agente-dirigido y no un barrido con config

- El agente decide **cuando** y **que** reintentar segun el caso (no un intervalo rigido igual para
  todos). Un seguimiento de cotizacion no espera lo mismo que un recordatorio de cita.
- Reusa la infraestructura que ya existe (worker que sondea, campos del paso, ejecucion diferida de
  ADR-0119). No agrega un servicio nuevo.
- El recordatorio lo redacta el agente en su re-corrida: natural y contextual, no un texto fijo.

## Consecuencias

- Un cliente dormido recibe uno o mas recordatorios (hasta el tope) en vez de perderse hasta el
  timeout; mas cotizaciones/seguimientos se cierran.
- Costo acotado: cada recordatorio es una re-corrida del agente, limitada por `MaxWhatsAppAsks`.
- `AgentNextRetryAt` es independiente de `StartAt` (arranque, ADR-0119) y de `AgentDeadlineAt`
  (timeout duro): arranque < pregunta < [reintentos] < deadline.

## Action Items

1. [x] Campo `WorkflowStepHistory.AgentNextRetryAt` (migracion dual).
2. [x] Herramienta `programar_reintento` + prompt (rutas de formulario y de decision/compuerta).
3. [x] Runner: estampar `AgentNextRetryAt` al pausar; limpiarlo al resolver/reanudar/fallar.
4. [x] Dispatcher: despertar pasos con `AgentNextRetryAt <= ahora`.
5. [x] Contexto: avisar al agente "el cliente no ha respondido desde {hora}".
6. [ ] E2E: preguntar -> sin respuesta -> reintento programado -> recordatorio redactado por el agente.
