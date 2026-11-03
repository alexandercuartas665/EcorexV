# ADR-0146: El soporte que el cliente envia cae en la tarea CORRECTA (ADR-0125 v2)

Fecha: 2026-11-03
Estado: Aceptada
Extiende: ADR-0125 (la media del cliente en hold cae en la tarea del flujo)

## Contexto

ADR-0125 engancho la media entrante (comprobante de pago, documento) como adjunto de la tarea del
flujo SOLO cuando un paso retenia la conversacion (`waitingSteps`, resueltos por `FlowHoldStepId` o
`PendingWhatsAppConversationId`). En produccion aparecio un caso real donde el comprobante NO quedo
adjunto: el cliente lo envio FUERA de la ventana de hold (los pasos que retuvieron la conversacion ya
estaban Skipped; los vigentes tenian `pending_conv = NULL`). Los soportes llegan de forma asincrona,
casi siempre cuando el flujo NO esta esperando, asi que atar la captura al hold es demasiado estrecho.

Ademas, una conversacion de WhatsApp es por (linea + telefono) y se reutiliza para TODOS los negocios
del cliente en el tiempo: el mismo hilo puede cubrir la tarea A (cerrada) y la tarea B (nueva). "Adjuntar
a cualquier instancia viva" rutearia el soporte a la tarea equivocada. Hay que decidir de forma
DETERMINISTICA a que tarea cae. Y un detalle agravante: el mismo contacto estaba partido en DOS hilos
(`573166578604` con indicativo, del entrante, y `3166578604` sin el, que guardaba el saliente del agente),
porque la ingesta y el envio normalizaban distinto.

## Decision

1. **Tarea dueña del hilo** (`Conversation.CurrentTaskItemId`): el motor la fija cada vez que un paso
   ENGANCHA al cliente por esa conversacion -al TOMAR la linea (`WorkflowAgentStepRunner`, ambos holds) y
   al NOTIFICAR al cliente (`NodeNotifyService`)-. El ultimo caso que engancha gana la titularidad, asi un
   negocio nuevo se queda con los soportes que lleguen despues.
2. **Ruteo en `ChatIngestService`** cuando llega media:
   - con hold -> a la(s) tarea(s) de los pasos que retienen (comportamiento ADR-0125);
   - sin hold -> a la tarea DUEÑA si sigue abierta; si no, a la UNICA tarea ABIERTA ligada al hilo
     (`TaskItem.ConversationId`); si hay varias, a la mas reciente con NOTA de ambiguedad en la bitacora;
   - NUNCA a una tarea Done/Closed (un negocio cerrado no absorbe un pago nuevo);
   - sin candidata abierta -> el archivo queda en el chat marcado `ClientSupportStatus.Pending` = BANDEJA
     de soportes sin tarea, para que un humano lo archive (no se adivina en una cerrada, no se pierde).
   De-dup por (tarea, url) como en v1.
3. **Bandeja de soportes sin tarea** (pestana Conversaciones): lista los `Message` Pending con su contacto;
   un humano los **archiva** en una tarea (por numero) o los **descarta**. Estado del soporte en
   `Message.SupportStatus` (None/Pending/Routed/Dismissed) + `Message.SupportTaskItemId`.
4. **Re-archivar siempre posible**: en el detalle de la tarea (Documentos) se puede MOVER un adjunto a otra
   tarea por numero (`ITaskItemService.MoveAttachmentAsync`), por si el ruteo automatico se equivoco.
5. **Normalizar al ingerir (sin fusionar)**: la llave del hilo es el MSISDN canonico compartido
   (`Ecorex.Application.Common.PhoneNumber`), usado por la ingesta Y por el envio del agente. El lookup
   casa TODAS las formas equivalentes (canonica y nacional) y toma el hilo mas activo, asi el trafico
   NUEVO aterriza en UN solo hilo. NO se fusionan ni migran los dos hilos viejos ya partidos (no destructivo).

## Por que `Message.SupportStatus` y no un agregado nuevo

El adjunto YA es un `Message` con `MediaUrl`. Marcar el ruteo sobre el propio mensaje (2 columnas: estado
+ tarea) evita un agregado/tabla nuevos y hace la bandeja una simple consulta `SupportStatus == Pending`.
Migracion dual minima (PG + SQL Server): 3 columnas + 1 indice. El default de las filas existentes es
"None" (NO "": el conversor enum<->string haria fallar la lectura de mensajes viejos con valor vacio).

## Consecuencias

- Un soporte enviado fuera de hold cae en el negocio abierto correcto (dueña actual), no en uno cerrado
  ni en varios; si no hay destino, queda visible en la bandeja y recuperable, nunca perdido.
- El mismo contacto deja de partirse en dos hilos para el trafico nuevo (sin tocar el historico).
- Un humano puede corregir un ruteo equivocado moviendo el adjunto.
- El camino conversacional (SARA) sigue adjuntando por su cuenta; el `waitingSteps` de v1 queda intacto.
- Corre con el webhook SIN tenant ambient (`IgnoreQueryFilters`), igual que ADR-0125.
