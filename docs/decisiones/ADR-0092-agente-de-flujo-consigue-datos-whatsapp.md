# ADR-0092: El agente de flujo consigue datos por WhatsApp (pregunta y reanuda) para llenar formularios

**Estado:** Propuesta
**Fecha:** 2026-09-05
**Deciders:** Alexander Cuartas (orquestador)
**Relacionados:** ADR-0091 (Colmena web + llamada Retell como herramientas de consecucion de datos), ADR-0090
(agentes de IA en nodos de flujo: llenar formulario, ola C), integracion WhatsApp (YCloud BSP) + agente
conversacional (SARA).

## Contexto

Con ADR-0091 el agente de un paso Task ya consigue datos que no estan en el contexto por dos vias:
`buscar_web` (Colmena, sincrono) y `llamar_telefono` (Retell, asincrono con pausa/reanudacion). Falta la via
mas usada en la operacion real: **preguntar por WhatsApp** a una persona (un cliente, un proveedor) para
obtener o confirmar un dato antes de diligenciar el formulario.

Naturaleza de WhatsApp (inventario del codigo):

- ENVIO saliente: `IWhatsAppConnectorService.SendTestAsync(lineId, phone, text, ...)` enruta por proveedor
  (`WhatsAppLine.Provider`): YCloud (BSP real), Cloud (Meta), Evolution, Emulator. El texto LIBRE de YCloud
  (`IYCloudApiClient.SendTextAsync`) NO estaba gateado por plantillas.
- ENTRADA: `POST /webhooks/ycloud` -> `ChatIngestService.IngestTrustedAsync` correlaciona por
  `(TenantId, WhatsAppLineId, ContactPhone)` -> persiste `Message` (Inbound) en una `Conversation` y encola
  `AgentReplyDispatcher` (el agente conversacional). ESTE es el gancho de reanudacion.
- CONVERSACIONAL: `AgentConversationService.RunAsync` -> `AiInferenceService.RespondAsync` es el cerebro de
  SARA (responde WhatsApp de forma autonoma). No tiene "captura de dato estructurado + fin" tipo Retell.

Dos hechos marcan el diseno:

1. **WhatsApp es asincrono Y multi-turno** (a diferencia de Retell, que responde de un golpe por webhook).
2. **Candado de 24 horas de Meta**: un primer mensaje EN FRIO (la persona no te ha escrito en 24h) exige una
   PLANTILLA aprobada; el texto libre solo vale con la ventana abierta. En el codigo NO existia una ruta para
   ENVIAR plantillas por YCloud (solo crear/listar).

## Decision

Agregar una TERCERA herramienta de consecucion de datos, `preguntar_whatsapp`, con el mismo patron de
pausa/reanudacion de la llamada Retell, pero conducida por el PROPIO agente del flujo (no se delega a SARA), y
con soporte de plantilla para el primer contacto en frio. Permiso EXPLICITO por nodo.

### 1. Quien conversa: el agente del flujo (espejo de Retell), no SARA

El mismo agente que llena el formulario emite UNA pregunta, el paso se PAUSA, y al llegar la respuesta el
agente REANUDA con ese texto en el contexto; si le falta algo, vuelve a preguntar (otro ciclo pausa/reanuda).
Reusa su tool-calling; no se construye la captura estructurada + senal de fin que faltaria para delegar en el
motor conversacional. Consistente con `llamar_telefono`/`buscar_web`.

### 2. Configuracion por nodo (permiso explicito)

`WorkflowNodeAgent` gana campos OPCIONALES (migracion dual aditiva):
- `WhatsAppLineId` (FK a `WhatsAppLine`, nullable): habilita `preguntar_whatsapp` desde esa linea. Null = sin
  herramienta.
- `WhatsAppTemplateName` + `WhatsAppTemplateLang` (nullable): plantilla aprobada para el PRIMER contacto en
  frio; su unica variable de cuerpo `{{1}}` recibe la pregunta del agente. Sin plantilla, la herramienta solo
  funciona con la ventana de 24h abierta (texto libre).

### 3. Envio de plantilla (candado de 24h)

- Nuevo `IYCloudApiClient.SendTemplateAsync(apiKey, from, to, name, language, bodyParams, ct)` (POST
  /whatsapp/messages type=template, un componente body con los parametros de texto).
- Nuevo `IWhatsAppConnectorService.SendTemplateAsync(lineId, phone, name, language, bodyParams, actor, ct)`
  que enruta a YCloud (Emulator = exito sintetico; Cloud/Evolution = fuera de este corte).
- El seam `IWorkflowAgentWhatsApp.AskAsync` decide: si hay un entrante en las ultimas 24h -> texto libre; si
  no -> plantilla (si esta configurada) o error legible ("requiere plantilla: ventana de 24h cerrada").

### 4. Pausa / reanudacion (espejo de Retell)

- Herramienta `preguntar_whatsapp(numero, pregunta)` (ofrecida solo si el nodo tiene `WhatsAppLineId`): el
  agente la "pide"; el invoker NO envia (no escribe BD): registra un `WhatsAppRequest(Numero, Pregunta)` y el
  bucle TERMINA (los campos ya fijados se conservan).
- PAUSA (runner): `AskAsync` resuelve/crea la `Conversation` de `(tenant, linea, telefono)`, envia el mensaje
  (plantilla o libre) y persiste el saliente. El runner guarda `WorkflowStepHistory.PendingWhatsAppConversationId`
  (NUEVO, nullable), marca `AgentAttemptedAt`, deja el paso vigente. Nuevo outcome `WaitingForReply`.
- REANUDACION (webhook de entrada): `ChatIngestService` ya persiste el entrante; ADEMAS, si un paso vigente
  tiene `PendingWhatsAppConversationId == conversation.Id`, LIMPIA `AgentAttemptedAt` -> el barrido re-corre al
  agente. El contexto incluye la ultima respuesta entrante (`WhatsAppReplyResult`); el agente termina de llenar
  o vuelve a preguntar. Al enviar el formulario (o volver a humano), se limpia `PendingWhatsAppConversationId`.
- Tope de reintentos: el runner corta a un maximo de preguntas por conversacion (evita ciclos de costo).

### 5. Colision con el agente conversacional (SARA)

Mientras un paso "posee" la conversacion (`PendingWhatsAppConversationId == conv.Id` y vigente),
`AgentConversationService` se CALLA (guard nuevo, igual que el silencio cuando un asesor humano toma el chat):
una sola respuesta a la persona, la del flujo.

### 6. Guardarrailes (heredados + nuevos)

- Permiso explicito por nodo (linea + plantilla). Sin `WhatsAppLineId` no hay herramienta.
- Ventana cerrada sin plantilla, linea desconectada, o error de envio -> el paso vuelve a una persona
  (`ReturnToPerson`), nunca se atasca. Tope de preguntas por conversacion.
- Cupo de IA, `AiUsageLog`, autoria `executedByAiAgentId`. Multi-tenant por filtro global; la API key de la
  linea nunca se expone.

## Opciones consideradas

- **Agente del flujo vs delegar a SARA.** Se elige el agente del flujo (decision del usuario): reusa el
  tool-calling y el patron de Retell; delegar exigiria construir captura estructurada + senal de fin en el lado
  chat, que hoy no existe. Mas simple y consistente.
- **Contacto en frio con plantilla vs solo ventana de 24h.** Se elige CONSTRUIR el envio de plantilla (decision
  del usuario): el caso real es contactar a quien no te ha escrito. Con la ventana abierta se usa texto libre.
- **Correlacion por conversacion nueva vs reusar la conversacion (tenant, linea, telefono).** Se reusa la
  conversacion existente para que el webhook de entrada correlacione la respuesta sin hilos duplicados.

## Consecuencias

Mas facil: automatizar pasos donde una persona hoy escribe por WhatsApp para pedir un dato. Reusa la
infraestructura de WhatsApp (YCloud) y el patron async de ADR-0091 sin duplicar nada.

A vigilar: costo (tokens + conversaciones WhatsApp con costo por sesion), aprobacion de la plantilla en Meta
(se somete una vez), y la colision con SARA (resuelta con el guard de propiedad). Se acota con el permiso por
nodo, el tope de preguntas y el fallback a humano. El webhook `/webhooks/ycloud` aun no verifica firma
(deuda existente): la reanudacion de un paso desde un entrante hereda ese riesgo hasta que se agregue.

## Plan de accion

- [ ] Esquema: `WorkflowNodeAgent.{WhatsAppLineId, WhatsAppTemplateName, WhatsAppTemplateLang}` +
      `WorkflowStepHistory.PendingWhatsAppConversationId` + migracion dual aditiva.
- [ ] Envio de plantilla: `IYCloudApiClient.SendTemplateAsync` + `IWhatsAppConnectorService.SendTemplateAsync`.
- [ ] Seam `IWorkflowAgentWhatsApp.AskAsync` (crea/reusa conversacion, decide plantilla/libre, persiste saliente).
- [ ] Invoker: herramienta `preguntar_whatsapp` + `WhatsAppRequest` en el resultado.
- [ ] Runner: `PauseForWhatsAppAsync` (outcome `WaitingForReply`) + tope de preguntas; limpiar el pending al
      enviar / volver a humano.
- [ ] Reanudacion en `ChatIngestService` + guard de colision en `AgentConversationService`.
- [ ] Contexto: `WhatsAppReplyResult` en el context builder + serializer.
- [ ] Config por nodo (editor): selector de linea WhatsApp + plantilla.
- [ ] CI dual, solo ASCII, commit a fase-0/clon-backbone. Flujo de pruebas / validacion de runtime en prod.

## Nota (v0.15.182) - Implementado: esquema + envio + pausa/reanudacion + colision + UI

Entregado bajo el marco de arriba:
- Esquema (migracion DUAL aditiva): WorkflowNodeAgent.{WhatsAppLineId(FK WhatsAppLine), WhatsAppTemplateName,
  WhatsAppTemplateLang} + WorkflowStepHistory.PendingWhatsAppConversationId (indexado).
- Envio de plantilla: IYCloudApiClient.SendTemplateAsync (POST /whatsapp/messages type=template, un body con
  los parametros) + IWhatsAppConnectorService.SendTemplateAsync (solo YCloud/Emulator en este corte).
- Seam IWorkflowAgentWhatsApp/WorkflowAgentWhatsApp (Application, no SuperAdmin): resuelve/crea la conversacion
  de (tenant, linea, telefono), decide plantilla vs texto libre por la ventana de 24h, envia y persiste el
  saliente; devuelve el ConversationId.
- Invoker: tool 'preguntar_whatsapp(numero, pregunta)' (ofrecida si hay WhatsAppLineId; SI se ofrece en la
  reanudacion, es multi-turno); el bucle termina con un WhatsAppRequest.
- Runner: PauseForWhatsAppAsync -> envia por el seam, guarda PendingWhatsAppConversationId + AgentAttemptedAt,
  outcome WaitingForReply; tope MaxWhatsAppAsks=4 por conversacion; limpia el pending al enviar el formulario o
  volver a humano.
- Reanudacion: ChatIngestService, al persistir el entrante, limpia AgentAttemptedAt del paso que espera esa
  conversacion (indexado). Contexto: BuildWhatsAppReplyResultAsync inyecta el ultimo entrante; serializer lo
  imprime.
- Colision: AgentConversationService se calla si un paso vigente posee la conversacion (SARA no responde por
  encima del agente del flujo).
- Config por nodo (editor): selector de linea WhatsApp + plantilla (nombre/idioma) en el acordeon Agente de IA.

Verificado en dev (AGROMETALICAS): migracion aplica al arrancar (4 columnas nuevas); el editor persiste
WhatsAppLineId + plantilla + idioma por nodo (linea VENTAS_TEST, plantilla consulta_dato/es). Build Debug/Release
verde; integracion (matriz dual) 12/12 (con el nuevo ctor del runner + el fake de WhatsApp).

Pendiente de validacion de RUNTIME real (necesita una linea WhatsApp conectada, una plantilla aprobada en Meta
y una respuesta real): el ciclo pregunta -> pausa -> respuesta -> reanudacion se prueba en prod. El webhook
/webhooks/ycloud aun no verifica firma (deuda existente); la reanudacion hereda ese riesgo hasta cerrarla.
