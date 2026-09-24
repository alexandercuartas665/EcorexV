# ADR-0108: Agente de flujo - visibilidad, corte manual y timeout

Fecha: 2026-09-23
Estado: Aceptado

## Contexto

Un nodo de flujo atendido por un agente de IA (ADR-0090/0091/0092/0093) podia quedarse "trabajando"
horas sin que el usuario supiera que estaba haciendo ni pudiera detenerlo:

- El bucle de una corrida SI estaba acotado (8 rondas, 1024 tokens/llamada), pero **no habia tope de
  reloj** de la corrida ni timeout del HttpClient del proveedor (100s por defecto x reintentos).
- Cuando el agente PAUSABA esperando una llamada (Retell) o una respuesta de WhatsApp, el paso quedaba
  EN ESPERA indefinidamente: si la respuesta no llegaba, **nada lo terminaba** (no habia reaper).
- La tarjeta mostraba "El agente esta trabajando..." mientras el paso fuera vigente y sin motivo de
  fallo, aunque en realidad estuviera idle esperando; y "en espera Xh" es el tiempo desde que llego el
  paso, no tiempo de computo. Resultado: parecia que "se comia los tokens" sin fin.
- **No se podia ver que penso/hizo** el agente por paso: los prompts no se guardaban, los tokens se
  registraban por agente (no por paso) y el "pensando..." en vivo era efimero (se perdia al recargar).
- **No habia forma de cortarlo** manualmente.

## Decision

Tres capacidades, sin cambiar el modelo de ejecucion (worker + barrido idempotente):

### A. Corte manual (persona)
`IWorkflowInboxService.CancelAgentStepAsync(stepId, user)` -> `IWorkflowAgentStepRunner.CancelAsync`.
Corta cualquier espera del agente (PendingVoiceCallId/PendingWhatsAppConversationId), marca el motivo
("Terminado manualmente por {email}") y DEVUELVE el paso a una persona (no sigue la ruta de
contingencia: el corte es para retomar el control). Boton "Terminar y devolver a una persona" en el
menu del nodo, con la MISMA autorizacion que "Retomar y cerrar" (asignado, o candidato si esta sin
asignar). Reusa la finalizacion de fallo del runner.

### B. Timeout / reaper (automatico)
- **Tope de reloj de la corrida**: `WorkflowAgentStepRunner` envuelve `InvokeAsync` en un
  `CancellationTokenSource.CancelAfter(RunTimeoutMinutes)` (env `ECOREX_AGENT_RUN_TIMEOUT_MIN`, 6 min);
  al vencer, el paso vuelve a una persona con el motivo. Ademas el HttpClient del proveedor pasa a 60s.
- **Reaper de esperas**: al pausar (llamada/WhatsApp) se estampa `WorkflowStepHistory.AgentDeadlineAt`
  = ahora + `WaitTimeoutHours` (env `ECOREX_AGENT_WAIT_TIMEOUT_HOURS`, 6 h). Cada ciclo del worker,
  el dispatcher barre cross-tenant los pasos de agente vigentes cuya espera vencio y llama
  `IWorkflowAgentStepRunner.TimeoutAsync`, que los cierra aplicando la politica OnFailure del nodo
  (ruta de contingencia o devolver a persona). Asi ningun paso queda colgado indefinidamente.

### C. Bitacora legible por paso (visibilidad)
`WorkflowStepHistory.AgentRunLog` (JSON) y `AgentTokensUsed` (int): el runner recolecta las fases que
el invoker reporta en vivo (callback de progreso) y ANEXA una entrada por corrida/reanudacion (hora,
intento, tokens, resultado y fases). Se muestra en el menu del nodo ("Ver actividad del agente") con
los tokens por corrida. Tipo compartido `WorkflowAgentRunLog` (parser tolerante, tope de 30 corridas).

## Consecuencias

- Un paso de agente nunca queda "trabajando" para siempre: o lo corta una persona, o lo cierra el
  reaper por timeout, siempre con motivo y sin cerrar en falso ni perder el caso.
- El usuario ve por paso que hizo el agente y cuantos tokens gasto, sin mirar logs tecnicos.
- Migracion dual `AddAgentStepRunLogAndDeadline` (3 columnas en workflow_step_histories, todas
  nullable, cambio aditivo). Sin nuevos estados en `WorkflowStepStatus` (el fallo sigue siendo el
  string `AgentFailureReason` sobre un paso Pending vigente, como en ADR-0090).
- Limites por entorno (no por nodo): un override por nodo queda como mejora futura.
