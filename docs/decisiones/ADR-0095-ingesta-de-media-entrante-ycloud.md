# ADR-0095: Ingesta de media entrante en YCloud (paridad con Evolution)

**Estado:** Aceptada
**Fecha:** 2026-09-07
**Deciders:** Alexander Cuartas (orquestador)
**Relacionados:** ADR-0006 (webhook de chat / ingesta), ADR-0090..0093 (agentes en flujos), TasksToolset
(crear_tarea) que adjunta la media entrante a la tarea.

## Contexto

Cuando un cliente enviaba una IMAGEN o un ARCHIVO por una linea WhatsApp con proveedor **YCloud**, el
agente la VEIA (vision), pero la media NO se persistia como parte del mensaje entrante. Por eso
`TasksToolset.crear_tarea` (que adjunta a la tarea toda `Message` inbound con `MediaType != None` y
`MediaUrl != null`) no tenia nada que adjuntar: la tarea nacia con **0 adjuntos**. En **Evolution** si
funcionaba, porque su webhook descarga la media por id y setea `MediaType/MediaUrl/MediaMimeType`.

Causa raiz:
- `YCloudWebhookParser` no extraia la media: el record no tenia campos de media y para
  image/document/audio/video solo devolvia el caption o "(image)" (pendiente "fase 2").
- El endpoint `/webhooks/ycloud` construia el `IngestMessageRequest` SIEMPRE como `"text"`, sin media.
- El resto del pipeline (`IngestMessageRequest`/`ChatIngestService`, y el adjuntado en TasksToolset) YA
  soportaba media; no habia que tocarlo.

## Decision

Dar a YCloud **paridad con Evolution**: extraer la media del evento entrante, descargarla y persistirla
como adjunto local, para que el mensaje quede con `MediaType/MediaUrl/MediaMimeType` y el agente la
adjunte a la tarea automaticamente.

- `YCloudParsedMessage` gana `MediaLink`, `MediaMime`, `MediaKind` ("image"|"document"|"audio"|"video").
  El parser lee la media de `whatsappInboundMessage.<tipo>.{link|url}` y `.{mime_type|mimeType}`; el Body
  sigue siendo el caption o "(<tipo>)". Un mensaje de texto deja los campos de media en null.
- El endpoint `/webhooks/ycloud`, si hay `MediaLink`, **descarga** los bytes (HttpClient) y los guarda en
  `{WebRootPath}/uploads/chat/yc-{guid}{ext}` (ext segun mime), y arma el `IngestMessageRequest` con
  `MessageType = MediaKind`, `MediaType = map(MediaKind)`, `MediaUrl = /uploads/chat/...`, `MediaMimeType`.
  Si la descarga falla, **loguea WARNING** y hace fallback a texto (no silencioso).
- El enum `MessageMediaType` ya tenia Image/Video/Audio/Document/Location: **no hubo que ampliarlo**.
- Mejora colateral: el `catch` silencioso del webhook de Evolution pasa a `LogWarning` para que futuras
  fallas de descarga sean visibles.

## Guardarrailes / notas

- No se tocan `TasksToolset`, `ChatIngestService` ni `IngestMessageRequest` (ya soportan media).
- Multi-tenant intacto: la linea YCloud se resuelve por `YCloudPhoneNumberId` -> tenant, como antes.
- La media de YCloud es una URL publica temporal; por eso se descarga y se re-hospeda localmente (igual
  criterio que Evolution) en vez de guardar la URL externa.

## Consecuencias

Mas facil: un inbound de imagen/archivo por YCloud queda con `MediaUrl`/`MediaType` persistidos, y
`crear_tarea` lo adjunta a la tarea sin intervencion. A vigilar: tamano de las descargas y limpieza de
`uploads/chat` (compartido con Evolution; fuera de alcance de este cambio).

## Verificacion

- Unit: `YCloudWebhookParserMediaTests` (imagen con link/mime/kind + caption; documento con url/mimeType +
  Body "(document)"; texto sin media). 3/3.
- Build de la solucion verde. Entregado en v0.16.3. Runtime real (una linea YCloud con media entrante) lo
  valida el usuario en prod tras desplegar.
