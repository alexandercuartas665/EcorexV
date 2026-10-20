# ADR-0125: La media que el cliente envia durante un hold de flujo cae en la tarea del flujo

Fecha: 2026-10-20
Estado: Aceptada

## Contexto

Cuando la conversacion esta "tomada por el flujo" (un nodo-agente, p.ej. "Agente seguimiento
inteligente", retiene la linea esperando la respuesta del cliente) y el cliente envia un
comprobante de pago u otro documento/imagen por WhatsApp, el archivo se persistia como `Message`
con `MediaUrl` pero NO quedaba adjunto a la tarea. El camino del flujo
(`WorkflowAgentContextBuilder`) solo lee el TEXTO del mensaje (`m.Body`); no toca la media. Solo
el camino conversacional adjuntaba (via el toolset, dentro de `AiToolRunContext`). Resultado: el
comprobante se perdia de la tarea y habia que buscarlo en la bandeja de conversaciones.

## Decision

Enganchar la media entrante a la tarea del flujo en `ChatIngestService.IngestTrustedAsync`, el
unico punto que tiene a la vez el inbound con su `MediaUrl` recien creado y la senal de que un
paso de flujo retiene la conversacion (`waitingSteps`, resueltos por `FlowHoldStepId` o por
`PendingWhatsAppConversationId`).

Cuando `message.MediaUrl != null` y hay `waitingSteps`:
1. Resolver la(s) tarea(s): `WorkflowInstances` de los `waitingSteps` (`InstanceId -> TaskItemId`).
   (`Conversation.FlowHandledTaskId` NO sirve durante el hold: se limpia al tomar la linea y solo
   se setea al cerrar.)
2. Insertar el `TaskItemAttachment` (URL, no bytes) + una `TaskItemActivity` de bitacora,
   atribuido a "Cliente (flujo)".
3. De-duplicar por `(TaskItemId, Url)` antes de insertar (el barrido de agentes puede re-correr el
   paso).

## Por que INLINE y no `ITaskItemService.AddAttachmentAsync`

`ChatIngestService` corre desde el webhook SIN tenant ambiente (por eso todo el metodo usa
`IgnoreQueryFilters`). `AddAttachmentAsync` (publico) consulta `_db.TaskItems` con el filtro global
de tenant, por lo que NO encontraria la tarea en este camino. Se replica su efecto (adjunto +
actividad) inline con `IgnoreQueryFilters`, fijando `TenantId` con el tenant del webhook.

## Consecuencias

- El comprobante enviado durante el seguimiento aparece como adjunto en la tarea que origino el
  flujo, sin duplicar en re-runs, y el flujo sigue procesando la respuesta normal.
- Solo cubre media recibida mientras la conversacion esta flow-held; el camino conversacional ya
  adjuntaba por su cuenta.
- No hay migracion (reusa `TaskItemAttachment`/`TaskItemActivity`).
