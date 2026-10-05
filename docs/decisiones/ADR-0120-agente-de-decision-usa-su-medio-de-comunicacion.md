# ADR-0120: El agente de decision/compuerta usa su medio de comunicacion para decidir

**Status:** Aceptado
**Date:** 2026-10-05
**Deciders:** Alexander Cuartas
**Extiende:** ADR-0090 (agentes en nodos), ADR-0091 (voz), ADR-0092 (preguntar por WhatsApp)

## Contexto

Un nodo de flujo puede tener un agente de IA. Hay tres formas de atender un paso:

- **Formulario** (ADR-0090 ola C): el agente LLENA el formulario con un bucle de
  herramientas (`ver/fijar/enviar` + `buscar_web`, `llamar_telefono`,
  `preguntar_whatsapp`, `enviar_correo`). Multi-turno.
- **Decision** (Task sin formulario): el agente fija un `resultado`.
- **Compuerta** (ExclusiveGateway): el agente elige una `ruta`.

Las dos ultimas corrian en **un solo tiro** (`CompleteAsync`, sin herramientas):
el agente leia el contexto y respondia un JSON `{puede_resolver, resultado|ruta,
comentario}`. Si el contexto NO alcanzaba, su unica salida era `puede_resolver=false`
y el paso volvia a una persona.

El problema real (caso AGRO T00075, seguimiento de cotizacion): el nodo tiene una
**linea de WhatsApp configurada** y el agente razono por si mismo *"debo preguntar
al cliente si aprueba"*, pero como la compuerta es de un solo tiro **no tenia como
preguntar**: termino eligiendo la ruta a ciegas. Se le dio un medio de comunicacion
y no podia usarlo.

Ademas, cuando un agente de nodo pregunta por WhatsApp, su mensaje saliente no
quedaba en `/bitacora-agente` (solo en la conversacion), asi que al reiniciarse el
agente conversacional de la linea (SARA) no tenia rastro de lo que el flujo pregunto.

## Decision

1. **Un agente de decision/compuerta que tiene un medio de comunicacion corre un
   bucle de herramientas**, no un solo tiro. Se le ofrecen las mismas herramientas de
   comunicacion que al llenado (`buscar_web`, `llamar_telefono`, `preguntar_whatsapp`,
   `enviar_correo`) y el prompt lo **empuja**: *"se te dio un medio de comunicacion; si
   te falta un dato del cliente para decidir, PREGUNTASELO por ese medio antes de
   rendirte; no respondas puede_resolver=false si puedes conseguir el dato"*. El agente
   termina devolviendo el MISMO contrato JSON de decision; el parser y el runner no
   cambian.

2. **Si el nodo NO tiene ningun medio, la ruta de un solo tiro queda intacta.** Cero
   cambio de comportamiento para los flujos que ya funcionan: un gateway sin linea
   decide igual que antes. Y aunque haya medio, si el contexto alcanza el modelo
   responde el JSON en la primera ronda sin llamar herramientas -> equivalente al
   single-shot.

3. **Reanudacion**: cuando el agente pregunta por WhatsApp, el paso queda EN ESPERA
   (`PendingWhatsAppConversationId`, ADR-0092). La respuesta del cliente reanuda el
   paso (ChatIngestService limpia `AgentAttemptedAt`) y el agente la recibe en su
   contexto (`## Respuesta por WhatsApp`), ahora SI pudiendo decidir la ruta.

4. **Al RESOLVER** (elegir ruta / fijar resultado), el paso limpia
   `PendingWhatsAppConversationId` / `PendingVoiceCallId`: deja de "poseer" la
   conversacion y el agente de la linea vuelve a atender.

5. **El envio del agente queda en `/bitacora-agente`**: `WorkflowAgentWhatsApp.AskAsync`
   escribe un `AiAgentRunLog` (Kind=Info) en la conversacion, atribuido al agente ligado
   a la linea, para que SARA tenga el rastro de lo que el flujo pregunto.

## "Lista negra automatica" — ya existe y es mejor que una permanente

El usuario planteo el riesgo: si el agente del nodo pregunta por la linea y el cliente
responde, el **agente de la linea (SARA)** podria responder primero y chocar con el
agente del flujo. La solucion NO es meter el numero en `TenantBlockedNumbers` (eso lo
silenciaria PARA SIEMPRE con ese cliente).

El motor ya resuelve esto con el guard **`ownedByStep`** en `AgentConversationService`:
mientras un paso vigente "posee" la conversacion (`PendingWhatsAppConversationId ==
conv && IsCurrent`), **SARA se calla** y la respuesta la procesa el agente del flujo.
Es un bloqueo *scoped* a exactamente el tiempo que el paso espera la respuesta; al
resolver el paso (punto 4) SARA retoma. No hay carrera: ChatIngestService conserva
`PendingWhatsAppConversationId` al reanudar, asi que el guard sigue activo durante la
reanudacion.

## Consecuencias

- Un agente de compuerta/decision con linea **consigue el dato faltante** en vez de
  rendirse: mas pasos se resuelven solos.
- El costo sube (bucle de herramientas vs un tiro) solo cuando hay medio; acotado por
  el tope de rondas y el tope de preguntas por conversacion (ADR-0092).
- Riesgo contenido: sin medio, nada cambia; con medio y contexto suficiente, el modelo
  decide en la primera ronda igual que antes.

## Action Items

1. [x] Invoker: `RunDecisionWithToolsAsync` (bucle) cuando el nodo de decision/compuerta
       tiene medio; single-shot si no.
2. [x] Runner: al resolver decision/ruta, limpiar `PendingWhatsAppConversationId` /
       `PendingVoiceCallId`.
3. [x] `WorkflowAgentWhatsApp.AskAsync`: escribir `AiAgentRunLog` (bitacora del agente).
4. [ ] E2E en AGRO T00075: el agente de compuerta pregunta al cliente, el cliente
       responde, SARA calla, el agente decide la ruta con la respuesta.
