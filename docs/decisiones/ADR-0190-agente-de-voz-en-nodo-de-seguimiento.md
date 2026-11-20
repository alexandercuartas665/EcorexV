# ADR-0190: Agente de voz (Retell) en el nodo de seguimiento

- Estado: ACEPTADA (en desarrollo por olas; worktree `voz-retell-fallback`)
- Fecha: 2026-11-01
- Numeracion: nacio como ADR-0142 en la rama worktree-voz-retell-fallback; renumerado a ADR-0190 al integrarlo
  al tronco (2026-11-19) porque ADR-0142 ya era "Datos principales como lista unica ordenable".
- Contexto relacionado: ADR-0090/0091/0092/0093 (marco de agente de nodo), ADR-0122/0126
  (hold/liberacion de linea), ADR-0124 (etiquetar tarea).

## Contexto

El nodo "Agente seguimiento inteligente" (SARA) hace hoy el seguimiento/cierre con el
cliente POR WHATSAPP. El marco de agente de nodo (ADR-0090/0091) ya trae la VOZ como
herramienta (`llamar_telefono`, pausa/reanudacion por `PendingVoiceCallId`), pero orientada
a "conseguir un dato para un formulario", no a CONDUCIR un seguimiento y decidir la ruta.

Se quiere que el MISMO nodo pueda hacer el seguimiento por VOZ (llamada IA de Retell),
conduciendo la llamada y usando el resultado (transcript + analisis) para decidir la ruta,
con los canales (voz / WhatsApp) LIMITABLES por el usuario.

El diseno completo y las decisiones viven en el vault:
`OBSIDIAN.tareas/01. Requerimiento/Capa 3a Flujos de Tareas voz/` (notas 00, 01, 02).

## Decision

Construir la capacidad POR OLAS, en un worktree, uniendo a main solo al final probado:

- **Ola 0 (cimientos):** registrar `webhook_url` POR LLAMADA en `/v2/create-phone-call`
  (para no depender de la config del dashboard) y poblar numero + variables dinamicas del
  contacto desde la tarea/tercero (hoy `ContactVariables` va vacio). La configuracion de
  que el webhook llegue a prod es responsabilidad del usuario (infra).
- **Ola 1:** objetivo `SeguimientoCierre` como MARCA (no vuelca a formulario), guia de
  reanudacion para la rama de decision, exponer `call_successful`/contacto en el contexto.
- **Ola 2:** `AllowedChannels` + `VoiceLineId` por nodo (migracion dual) + UI del modal.
- **Ola 3:** `DisconnectionReason` + politica de reintentos/ventana horaria (migracion dual).
- **Ola 4:** bitacora de voz (opcion A: `AiAgentRunLog.ConversationId` nullable +
  `VoiceCallId`), transcript en dos destinos (bitacora + Message para SARA), pestana
  "Grabacion de llamadas", candado de voz a nivel contacto.
- **Ola 5:** persona SARA-voz (guion; la config la aplica el usuario) + conducta base de la
  llamada de seguimiento y forma del agente Retell (codigo, ver Consecuencias).
- **Ola 6:** E2E con llamada real + merge a main.

Modelo de prompts (ADR-0091, confirmado): en modo COMPUESTO el prompt del agente Retell
es el `SystemPrompt` del `VoiceAiAgentId` (SARA) + instruccion por-llamada + directiva de
objetivo. El `ExtraPrompt` del nodo NO alimenta el prompt de voz: solo rutea leyendo la
transcripcion.

## Consecuencias

- Es sobre todo reuso + configuracion + cerrar huecos; no es un motor nuevo.
- Prerrequisito duro: el webhook `call_analyzed` debe llegar a prod o los flujos reales no
  reanudan (el sondeo get-call solo cubre el modal de prueba). Ola 0 mitiga registrando
  `webhook_url` por llamada; la entrega a prod la valida el usuario.
- Toda migracion (olas 2-4) es DUAL (PostgreSQL + SQL Server). Multi-tenant por filtro
  global. ASCII en archivos nuevos.
- Ola 4 (hecha): la bitacora de voz cuelga de `VoiceCall` (`AiAgentRunLog.VoiceCallId`,
  `ConversationId` nullable, check "exactamente uno"); el riel de `/bitacora-agente` agrupa
  por CONTACTO (ultimos 10 digitos del telefono) uniendo WhatsApp + voz. El candado de voz
  (4.6b) REUSA el hold de la conversacion de WhatsApp del contacto (`FlowHoldStepId`, que
  SARA ya respeta): se estampa al colocar la llamada y se suelta por DUENO del paso (cubre
  WhatsApp y voz). Sub-decision del transcript-para-SARA: se escribe el resumen como
  `Message` SOLO si el contacto ya tiene conversacion de WhatsApp; si el seguimiento fue
  solo-voz no se crea una conversacion (el transcript completo queda en `VoiceCall` y en la
  bitacora). Extender el context builder de SARA para leer transcripts de voz sin WhatsApp
  previo queda pendiente (no bloquea esta ola).
- Ola 5 (hecha en codigo; la persona es config): se separa en tres capas el prompt de la llamada
  de seguimiento. (1) PERSONA del tenant = `SystemPrompt` del agente de voz del nodo (guion
  SARA-voz en el vault, nota 03; se crea como `AiAgent` propio, NO se reusa la persona de
  WhatsApp). (2) Instruccion por llamada (la escribe el agente del nodo). (3) Directiva de
  objetivo `SeguimientoCierre` en codigo, igual para todo tenant: antes era vacia; ahora fija la
  conducta base (identidad IA, frases cortas, no inventar precio/valor/estado -> asesor,
  cortesia != rechazo, reprogramar con dia y hora, buzon -> colgar, decision dicha en voz alta
  para el ruteo, no hablar por el cliente, colgar al despedirse).
  Ademas, la llamada de un paso de flujo pide una FORMA de agente Retell (`RetellAgentOptions.
  WorkflowCall`): herramienta `end_call` en el LLM, `voicemail_option = hangup` (sin esto el
  agente le hablaba al buzon y Retell no reportaba el corte como buzon, asi que la politica de
  reintento de la Ola 3 no se disparaba), corte por 20 s de silencio y tope de 10 min (costo).
  Es OPCIONAL por peticion: los demas consumidores (recordatorio de agenda, cargador de
  contactos, prueba de /config-voz) no la piden y conservan su agente y su hash. La forma entra
  en el hash del agente, asi que las llamadas de flujo crean un agente Retell nuevo la primera
  vez (los viejos quedan huerfanos; limpieza pendiente historica). Solo aplica en modo
  COMPUESTO: con una linea en modo simple (agent_id fijo) Retell usa su propio prompt.
