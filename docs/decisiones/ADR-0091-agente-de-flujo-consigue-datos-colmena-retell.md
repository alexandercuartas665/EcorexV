# ADR-0091: El agente de flujo consigue datos (Colmena web + llamada Retell) para llenar formularios

**Estado:** Propuesta
**Fecha:** 2026-09-05
**Deciders:** Alexander Cuartas (orquestador)
**Relacionados:** ADR-0090 (agentes de IA en nodos de flujo: llenar formulario, ola C), ADR-0045 (cliente
Colmena como recurso transversal), ADR-0043 (orquestacion server-side del paso de IA / navegador on-prem),
integracion RetellAI+Telnyx (Llamadas de voz IA).

## Contexto

Con ADR-0090 (ola C) el agente de un paso Task diligencia el formulario con tool-calling
(ver_formulario / fijar_campos / enviar_formulario), llenando SOLO con datos ya presentes en el contexto.
Se quiere darle poder para CONSEGUIR datos que no estan en el contexto:
1. SCRAPEAR la web con un agente COLMENA (navegador on-prem, con perfiles logueados).
2. LLAMAR por telefono con RETELL para confirmar/obtener un dato de una persona.

Naturaleza de cada fuente (inventario del codigo):

- COLMENA: `IBrowserActionChannel.ExecuteAsync(clientId, request, timeout)` es SINCRONO/bloqueante
  (correlationId + TaskCompletionSource sobre el hub; ~45-60s por accion). Sirve como HERRAMIENTA dentro
  del bucle de llenado. Caveat de capas: el orquestador (`AiStepOrchestrator`) y el canal viven en
  `Ecorex.SuperAdmin`; la Application NO puede depender de SuperAdmin -> hace falta una COSTURA (interfaz en
  Application, implementada en SuperAdmin). El cliente se identifica por `DataClient.ClientId`; liveness por
  `IAgentRegistry.IsOnline`; el perfil logueado por `SessionKey` (perfiles en la maquina del agente).
- RETELL: `IRetellVoiceService.PlaceCallAsync` es ASINCRONO: devuelve un `CallId` en segundos; el transcript
  y los datos extraidos llegan MINUTOS despues por `POST /api/voice/retell/webhook` -> `RetellWebhookProcessor`
  escribe la fila `VoiceCall` (`Status: Registered->Ongoing->Ended->Analyzed`, `TranscriptText`,
  `AnalysisJson` con `custom_analysis_data`). Cuando `Objetivo == LlenarFormulario` ya vuelca los datos a un
  draft (`DumpCapturedFormsAsync`). NO se puede esperar en el bucle del agente.

## Decision

Extender el marco de ADR-0090 con dos herramientas de CONSECUCION DE DATOS, cada una con el patron que su
naturaleza exige, y un permiso EXPLICITO por nodo (el agente solo usa los recursos que se le configuraron).

### 1. Configuracion por nodo (permiso explicito)

`WorkflowNodeAgent` gana campos OPCIONALES (migracion dual aditiva):
- `ColmenaClientId` (FK a `DataClient`, nullable) + `ColmenaSessionKey` (texto, nullable): habilita
  `buscar_web` con ese cliente/perfil. Null = sin busqueda web.
- `VoiceAiAgentId` (FK a `AiAgent`, nullable): habilita `llamar_telefono` usando ese agente de voz (prompt).
  Null = sin llamada.

Sin estos campos, el agente NO tiene las herramientas (se ofrecen solo si el recurso esta configurado).

### 2. Colmena: herramienta SINCRONA en el bucle

- Costura Application: `IAgentBrowserFetch` con
  `Task<AgentBrowserFetchResult> FetchAsync(Guid clientId, string? sessionKey, string url, string? selector,
  Guid tenantId, CancellationToken)`. Implementacion en SuperAdmin sobre `IBrowserActionChannel`
  (Navigate + ExtractReadable, un solo request acotado); devuelve el contenido legible (JSON
  {title,url,text,items,links,images}). Verifica `IAgentRegistry.IsOnline` antes; offline -> resultado
  con error legible (no lanza).
- Herramienta `buscar_web(url, selector?)` en `RunFormFillAsync`: se ofrece SOLO si el nodo tiene
  `ColmenaClientId`. El agente decide la URL desde el contexto, lee el resultado y con eso hace
  `fijar_campos`. Acotada por el timeout del canal; el agente sigue teniendo su tope de rondas.

### 3. Retell: herramienta ASINCRONA (pausa / reanudacion)

Como la llamada tarda minutos y responde por webhook, NO se ejecuta dentro del bucle:

- Herramienta `llamar_telefono(numero, objetivo)` (ofrecida solo si el nodo tiene `VoiceAiAgentId`): el
  agente la "pide"; el invoker NO coloca la llamada (no escribe BD ni bloquea). El bucle TERMINA y el
  resultado del invoker trae un `CallRequest(Numero, Objetivo)` (Fields puede venir parcial: lo ya fijado).
- PAUSA (runner): ante un `CallRequest`, el runner coloca la llamada con `IRetellVoiceService.PlaceCallAsync`
  (agente de voz = `VoiceAiAgentId`; whitelist = el formulario del paso; objetivo = LlenarFormulario),
  guarda el `CallId` en el paso (NUEVO `WorkflowStepHistory.PendingVoiceCallId`, nullable; migracion dual),
  marca `AgentAttemptedAt` (para que el barrido no lo re-tome de inmediato) y DEJA el paso vigente
  (Pending/current). Nuevo outcome `WaitingForCall`. El flujo NO avanza. Se anota en la tarea.
- REANUDACION (webhook): cuando `RetellWebhookProcessor` procesa `call_analyzed`, ademas de lo que ya hace,
  busca el `WorkflowStepHistory` con `PendingVoiceCallId == callId`; si existe, LIMPIA `AgentAttemptedAt` y
  `PendingVoiceCallId` -> el barrido de agentes re-corre el paso. En la re-corrida, el contexto del agente
  INCLUYE el transcript y los datos de la llamada (el context builder agrega la `VoiceCall` Analyzed
  asociada), y el agente termina de llenar/enviar el formulario. Idempotencia: si el paso ya no espera esa
  llamada (se cerro/reabrio), el webhook no hace nada.

### 4. Guardarrailes (heredados + nuevos)

- Permiso explicito: el agente SOLO usa el `ColmenaClientId`/`VoiceAiAgentId` del nodo. Nada de elegir
  recursos por su cuenta.
- Colmena offline, timeout, o llamada que no coloca / no arroja datos -> el paso vuelve a una persona
  (`ReturnToPerson`), nunca se atasca ni inventa. La reanudacion tiene un tope de tiempo razonable (si la
  llamada nunca llega a Analyzed, el paso queda para atencion humana con su nota).
- Cupo de IA (gate antes de gastar tokens), `AiUsageLog`, y el consumo de la llamada (VoiceCall.CostUsd)
  quedan registrados. Todo auditado como hecho por el AGENTE (`executedByAiAgentId`).
- Multi-tenant: `DataClient`/`AiAgent`/`RetellVoiceLine` tenant-scoped por filtro global; la cadena/secretos
  nunca se exponen.

## Opciones consideradas

- Llamada como HERRAMIENTA del agente (pausa/reanudacion) vs. como ACCION del nodo por fuera del agente.
  Se elige la HERRAMIENTA con pausa/reanudacion (decision del usuario): el agente decide cuando necesita la
  llamada y con que objetivo, y retoma con el resultado; es mas potente que una accion fija del nodo.
- Colmena via `AiStepOrchestrator` (bucle LLM anidado que maneja el navegador) vs. una sola instruccion
  Navigate+Extract. Se elige la instruccion ACOTADA (el agente del flujo decide la URL): evita un LLM
  anidado, es mas barato y mas predecible; si luego se quiere navegacion compleja, se abre a reusar el
  orquestador por la misma costura.

## Consecuencias

Mas facil: automatizar pasos que hoy requieren que una persona busque un dato en la web o llame para
confirmarlo. Reusa Colmena y Retell (ya en produccion) sin duplicar infraestructura.

A vigilar: costo (tokens + minutos de llamada), y la complejidad de la PAUSA/REANUDACION (un paso que
espera un evento externo). Se acota con el tope de tiempo, el permiso por nodo y el fallback a humano.

## Plan de accion

- [ ] Config por nodo: `WorkflowNodeAgent.{ColmenaClientId, ColmenaSessionKey, VoiceAiAgentId}` + migracion
      dual aditiva + UI en el editor (dentro del acordeon "Agente de IA").
- [ ] Colmena: costura `IAgentBrowserFetch` (Application) + impl SuperAdmin; herramienta `buscar_web`.
- [ ] Retell: `WorkflowStepHistory.PendingVoiceCallId` + migracion dual; `CallRequest` en el resultado del
      invoker; herramienta `llamar_telefono`; pausa en el runner; reanudacion en `RetellWebhookProcessor`;
      la `VoiceCall` de la llamada en el contexto del agente.
- [ ] Flujo de pruebas (tenant demo) y verificacion; nota de cierre en este ADR.
- [ ] CI dual, solo ASCII, commit a fase-0/clon-backbone.
