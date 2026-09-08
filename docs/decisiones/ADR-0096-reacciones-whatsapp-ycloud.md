# ADR-0096: Reacciones (emoji) en lineas WhatsApp YCloud (paridad con Evolution)

**Estado:** Aceptada
**Fecha:** 2026-09-07
**Deciders:** Alexander Cuartas (orquestador)
**Relacionados:** ADR-0095 (ingesta de media entrante YCloud), reaccion automatica del agente
(AiAgent.ReactionsEnabled) que corre en el dispatcher, ADR-0055 (remoteJid/LID en envio Evolution).

## Contexto

Las reacciones (responder con un emoji a un mensaje del cliente) estaban cableadas SOLO para lineas
**Evolution**: `WhatsAppConnectorService.SendReactionAsync` cortaba cualquier otra linea con
"Las reacciones por id solo aplican a lineas Evolution en este corte", y el cliente `IYCloudApiClient`
no tenia metodo de reaccion. Por eso el toggle "Reacciones" del agente (N de cada M mensajes) NO hacia
nada en una linea YCloud. YCloud es un BSP oficial sobre la WhatsApp Cloud API, que SI soporta enviar
reacciones (mensaje `type: "reaction"` con el wamid del mensaje + emoji), asi que solo faltaba
implementarlo de nuestro lado.

## Decision

Dar a YCloud **paridad con Evolution** en reacciones:

- `IYCloudApiClient` gana `SendReactionAsync(apiKey, fromPhone, toPhone, messageId, emoji)`, implementado
  en `YCloudApiClient` como un POST a `/whatsapp/messages` con `type=reaction` y
  `reaction: { message_id, emoji }` (mismo endpoint/estilo que SendText/SendMedia; un emoji vacio QUITA la
  reaccion, comportamiento nativo de WhatsApp).
- `WhatsAppConnectorService.SendReactionAsync` deja de bloquear YCloud: si la linea es YCloud, resuelve la
  API key (`YCloudApiKey`) y el emisor (`YCloudPhoneNumberId`) y enruta al cliente YCloud; si no, sigue el
  camino Evolution ya existente. El `externalMessageId` es el wamid del mensaje ENTRANTE (que ya guardamos
  como `Message.ExternalId` en la ingesta), asi que esta disponible tanto para la reaccion automatica del
  agente como para una manual.

Con esto, la reaccion automatica del agente (ReactionsEnabled) y una reaccion manual funcionan igual en
YCloud que en Evolution.

## Guardarrailes / notas

- Multi-tenant intacto: la linea y su API key se resuelven por la propia `WhatsAppLine` (tenant-scoped);
  la key se descifra con `YCloudApiKey` (ISecretProtector), nunca se expone.
- El cliente YCloud es interno y el codebase no lo testea a nivel unitario (convencion existente); el
  payload de reaccion es analogo al de media (ya probado). La validacion de runtime (una reaccion real
  llegando al chat del cliente) la hace el usuario.
- Nombre del campo: se envia `reaction.message_id` (estilo WhatsApp nativo, consistente con los campos de
  media que ya usa YCloud: body/link/caption/filename). Si el OpenAPI de YCloud exigiera `messageId`
  (camelCase), es un ajuste de una linea que revela la prueba en vivo.

## Consecuencias

Mas facil: el agente reacciona a los mensajes de los clientes tambien en lineas YCloud (no solo
Evolution). A vigilar: solo el naming exacto del campo `message_id` frente al OpenAPI v2 (se confirma en
la primera reaccion real).

## Verificacion

- Build de la solucion verde; Application 795/795 sin regresion (SuperAdmin 71/73 con 2 rojos preexistentes
  ajenos: AiStepOrchestrator). Entregado en v0.16.4.
- Runtime real (una linea YCloud conectada reaccionando a un mensaje entrante) lo valida el usuario en prod
  tras desplegar.
