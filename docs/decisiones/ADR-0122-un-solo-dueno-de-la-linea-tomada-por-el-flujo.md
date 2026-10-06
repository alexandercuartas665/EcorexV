# ADR-0122: Un solo dueno de la linea tomada por el flujo (anotar el nodo)

**Status:** Aceptado
**Date:** 2026-10-06
**Deciders:** Alexander Cuartas
**Extiende:** ADR-0092 (preguntar por WhatsApp), ADR-0120 (linea tomada por el flujo)

## Contexto

Cuando un agente de nodo le pregunta al cliente por WhatsApp, el paso queda EN ESPERA de la
respuesta (ADR-0092) y la conversacion queda "tomada por el flujo" (ADR-0120). Hasta ahora esa
posesion era IMPLICITA: se derivaba de `WorkflowStepHistory.PendingWhatsAppConversationId` (el paso
apunta a la conversacion). Al entrar la respuesta, `ChatIngestService` reanudaba **todos** los pasos
vigentes que apuntaban a esa conversacion.

Hueco: una misma conversacion (linea + telefono) puede ser apuntada por DOS pasos a la vez -ramas
paralelas con agente, o dos tareas del mismo cliente en la misma linea-. Entonces la respuesta del
cliente reanudaba a AMBOS y los dos agentes podian contestarle. La posesion no estaba acotada a un
unico dueno y la conversacion no sabia QUE nodo la estaba atendiendo.

## Decision

La **conversacion** anota quien la tomo: el PASO (`Conversation.FlowHoldStepId`) y el NODO
(`Conversation.FlowHoldNodeId`) del flujo. "El id del nodo que lo hizo", para atender la respuesta
desde ese flujo y ese nodo.

1. **Al tomar la linea** (el runner, en la pausa por WhatsApp): se anota `FlowHoldStepId`/
   `FlowHoldNodeId` en la conversacion.
2. **Un solo dueno**: antes de enviar, si la conversacion ya la tiene OTRO paso vigente, el nuevo
   paso NO la toma en paralelo: vuelve a una persona con el motivo (no se le escribe al cliente dos
   veces). Si el dueno anotado ya no esta vigente (paso cerrado), se puede retomar.
3. **Enrutar la respuesta** (`ChatIngestService`): la respuesta del cliente reanuda EXACTAMENTE el
   paso en `FlowHoldStepId` (no a todos). Sin marca (holds viejos) se cae al comportamiento anterior
   por `PendingWhatsAppConversationId` (retrocompatible).
4. **Liberar**: al resolver la salida, devolver a persona, enrutar por contingencia o fallar por
   timeout, el runner pone `FlowHoldStepId`/`FlowHoldNodeId` en null (solo si el dueno seguia siendo
   ese paso). SARA (agente de la linea) vuelve a atender.

El `NodeId` ya viajaba con el paso (el runner resuelve el agente por `step.NodeId`); lo nuevo es que
la CONVERSACION lo sabe, lo que da un unico dueno explicito y enrutamiento inequivoco.

## Consecuencias

- Dos nodos no pueden escribirle al mismo cliente por la misma linea a la vez; la respuesta no
  reanuda a varios pasos. Robusto ante ramas paralelas y multi-tarea.
- La conversacion es auditable: se sabe que nodo/flujo la atiende (base para la vista de ADR-0120).
- Retrocompatible: holds previos sin marca siguen funcionando por `PendingWhatsAppConversationId`.
- Un segundo nodo que necesite la linea mientras esta ocupada vuelve a una persona (no se encola);
  si se quisiera encolar, seria una extension futura.

## Action Items

1. [x] `Conversation.FlowHoldStepId` + `FlowHoldNodeId` (migracion dual).
2. [x] Runner: anotar al tomar; guard de un solo dueno; liberar al resolver/devolver/fallar.
3. [x] `ChatIngestService`: enrutar la respuesta al dueno (`FlowHoldStepId`).
4. [ ] E2E: dos pasos intentan la misma conversacion -> el segundo vuelve a persona; la respuesta
       reanuda solo al dueno.
