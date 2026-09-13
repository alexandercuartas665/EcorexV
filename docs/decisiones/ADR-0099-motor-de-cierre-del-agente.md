# ADR-0099: Motor de "Cierre" del agente (olvidar cliente + alertas), Ola 1

**Status:** Accepted
**Date:** 2026-09-12
**Deciders:** sesion de agentes + desarrollo

## Contexto

Se pidio portar de CUBOT.travels la idea de "DESTINOS DE NOTIFICACION (PEDIDO)" pero EVOLUCIONADA a un
concepto de CIERRE de la atencion, con dos capacidades:
1. **Olvidar al cliente** al cerrar: que el agente salude desde cero la proxima vez.
2. **Alertas al cierre**: avisar al comercial (usuario asignado) u otro usuario por WhatsApp/correo.

Restriccion clave del usuario: que NO sea una herramienta/MCP que el modelo invoque (seria "una tragadera
de tokens"): quiere un GATILLO DE PROGRAMA determinista. Dato adicional: "el sistema casi siempre termina en
una actividad".

Hallazgos del mapeo:
- En CUBOT.travels `[[pedido]]` ya es marcador -> dispatch server-side (no function-calling).
- En ECOREX los agentes SI usan function-calling, y YA existe un punto de cierre determinista:
  `AgentToolResult.SessionCompleted` (lo devuelve `crear_actividad`/`crear_lead`), procesado una sola vez en
  `AiInferenceService` (bloque "Cierre del proceso", que hoy solo vaciaba la cache).
- Envios disponibles: WhatsApp plantilla HSM (`SendTemplateAsync`, YCloud), correo (`IEmailSender`, SMTP por
  tenant). NO existe envio a grupo de Evolution ni Telegram.
- Memoria por contacto: `Conversation` + `Message` (contexto = ultimos 30) + `AiAgentCacheValue` (sesion =
  conversationId) + `AiAgentRunLog` (bitacora).

## Decision

Un **motor de cierre server-side y determinista** (`IAgentCierreService`), disparado por DOS vias, sin tokens
extra del modelo:
- **SessionCompleted**: cuando `crear_actividad`/`crear_lead` cierran (la via principal; el agente ya la usa).
- **Marcador `[[cierre: resumen?]]`**: red de respaldo para cierres TACITOS (sin crear actividad). Se parsea
  y se quita del texto junto a `[[enviar:]]` en `AiInferenceService` (patron ya existente, sin MCP).

Al cerrar (solo en atencion real, con `conversationId`):
1. **Olvidar (reset NO destructivo)**: si esta configurado, se marca `Conversation.AgentContextResetAt`. El
   armado de contexto (`AgentConversationService`) ignora los mensajes anteriores a esa marca: el agente
   saluda desde cero, pero el historial NO se borra (sigue visible para humanos / auditoria).
2. **Alertas**: por cada alerta configurada se resuelve el destinatario (usuario asignado de la linea o un
   usuario elegido) y se envia por WhatsApp (plantilla HSM, variables por nombre {{cliente}}/{{telefono}}/
   {{resumen}}/{{agente}}) o por correo (HTML con el resumen del cierre). Best-effort: un fallo de envio no
   rompe la respuesta al cliente ni las demas alertas.

Config por agente en `AiAgent.CierreJson` (jsonb / nvarchar(max)); UI en la seccion "Cierre" de Agentes.

## Alcance de la Ola 1 y olas siguientes

- **Ola 1 (esta):** motor + `[[cierre]]` + reset no destructivo + alertas WhatsApp-plantilla y Correo, al
  usuario asignado o seleccionado. Reusa servicios existentes.
- **Ola 2 (hecha):** destino Grupo de Evolution (`@g.us`) por texto plano. En ECOREX el connector ya enruta
  al grupo (jid completo via `remoteJid` en el campo `number`), sin el digit-strip de CUBOT.travels. Canal
  `CierreCanal.WhatsAppGrupo` + `AgentCierreAlerta.GrupoJid`; sin migracion (va en el JSON).
- **Ola 3 (hecha):** canal Telegram. Bot por tenant (`TenantTelegramConfig`, token cifrado) + `chat_id` por
  alerta (`AgentCierreAlerta.ChatId`); envio via Bot API (`ITelegramClient`/`TelegramBotClient`). Nueva tabla
  (migracion dual `AddTenantTelegramConfig`). Config del bot en la propia seccion "Cierre" (nivel tenant).

## Consecuencias

- Cierre con costo CERO de tokens del modelo (cuelga del cierre que el agente ya hace).
- Migraciones DUALES aditivas `AddAgentCierre` (2 columnas nullable): `ai_agents.cierre_json` +
  `conversations.agent_context_reset_at`.
- La alerta WhatsApp exige linea YCloud (plantillas HSM solo YCloud en este corte); el Correo funciona con
  cualquier proveedor. Sin grupo/Telegram todavia.
- No se toca `crear_tarea`/`TasksToolset` ni la ejecucion de flujos.

## Referencias

- Codigo: `Ecorex.Application/Tenancy/AgentCierreConfig.cs`, `IAgentCierreService.cs`, `AgentCierreService.cs`,
  `AiInferenceService.cs` (marcador `[[cierre]]` + llamada al handler), `AgentConversationService.cs` (filtro
  por `AgentContextResetAt`); `Ecorex.Domain/Entities/AiAgent.cs` (CierreJson), `Conversation.cs`
  (AgentContextResetAt); UI `Ecorex.SuperAdmin/Components/Pages/Agentes.razor` (seccion Cierre).
- Tests: `tests/Ecorex.Application.Tests/AgentCierreConfigTests.cs`.
