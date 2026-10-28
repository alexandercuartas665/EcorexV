# ADR-0139: Consultar YCloud para recuperar el motivo REAL de un envio fallido ("sin detalle")

**Status:** Accepted
**Date:** 2026-10-28
**Deciders:** Equipo ECOREX (dev principal), a peticion del usuario

## Contexto

ADR-0135 hizo visible el estado de entrega de los salientes de WhatsApp (YCloud): el webhook de
estado del proveedor avanza `Message.DeliveryStatus` a "delivered"/"read"/"failed" y, cuando falla,
guarda el motivo en `Message.DeliveryError`. ADR-0137 agrego la pestana "Fallos de envio" en la
bitacora del agente para listarlos.

Problema observado en PROD (caso tenant AGROMETALICAS, tarea T00558, cliente 573182800618): varios
salientes quedaron "failed" con `DeliveryError` vacio -> la UI muestra **"sin detalle de Meta"**. El
webhook de estado de YCloud, para esos mensajes, NO trajo ni codigo ni texto de error, asi que no se
puede saber POR QUE no llegaron (p.ej. `131026 Message undeliverable` = el numero no tiene WhatsApp,
vs `131047` = fuera de ventana). Verificado ademas que:

- La decision plantilla-vs-texto por ventana de 24h **ya existe y funciona** (SARA en
  `WorkflowAgentWhatsApp.AskAsync` y `AgentReactivacionService`; el nodo NOTIFICACION del flujo en
  `NodeNotifyService` **siempre** usa plantilla). Para T00558 se envio por PLANTILLA (lo correcto en
  frio); YCloud la ACEPTO y luego el webhook de entrega la marco "failed". O sea: el fallo NO fue la
  ventana, fue el destino. Pero sin el codigo de Meta no se puede afirmar con certeza.

La API de YCloud expone el estado de UN mensaje por id (GET `/whatsapp/messages/{id}`), que SI incluye
el objeto de error de Meta (codigo + texto). Es lectura, no reenvia nada.

## Decision

Agregar una capacidad de **solo lectura** para, por el id de proveedor guardado al enviar
(`Message.ExternalId`), consultar YCloud y rellenar `DeliveryError` con el codigo + texto real de Meta
en los salientes "failed" que quedaron sin motivo. Se ofrece como accion manual en la pestana "Fallos
de envio": boton **"Actualizar motivos desde YCloud"**.

Restriccion de diseno pedida por el usuario: **NO multiplicar las "salidas" de la herramienta que
EMITE el mensaje.** Esta funcion no toca ninguna ruta de envio; es una consulta de estado aparte.

## Piezas

- `IYCloudApiClient.GetMessageAsync(apiKey, messageId)` -> `YCloudMessageStatusResult(IsSuccess,
  Status, ErrorCode, ErrorMessage, Error)`. Impl en `YCloudApiClient`: GET
  `/whatsapp/messages/{id}` con fallback a `?id=...&limit=1`; parser tolerante del error (objeto
  `error` o arreglo `errors`; `code` numerico o texto; detalle en `message`/`title`/`error_data.details`).
- `IWhatsAppConnectorService.ProbeDeliveryStatusAsync(lineId, externalId)` -> reusa la resolucion de
  la API key YCloud de la linea (ya existente, cifrada) y llama a GetMessageAsync. Solo lineas YCloud.
- `IAiAgentLineService.RefreshDeliveryFailureReasonsAsync(take=50)`: toma los salientes "failed" con
  `DeliveryError` vacio y `ExternalId` no nulo (tenant-scoped), consulta YCloud por cada uno y rellena
  `DeliveryError` con el mismo formato del webhook ("codigo X - texto"). Solo enriquece el motivo; NO
  cambia `DeliveryStatus`. Un solo `SaveChanges`. Devuelve cuantos se actualizaron.
- UI: boton en la pestana "Fallos de envio" (`BitacoraAgente.razor`) que corre el backfill y refresca.

Sin migracion (reusa `Message.DeliveryStatus`/`DeliveryError` de ADR-0135). Sin nueva ruta de envio.

## Alternativas consideradas

- **Consola de YCloud a mano**: cero codigo, inmediato, pero no deja la capacidad en la app ni es
  self-serve para el usuario. Se descarto como solucion permanente (sirve como verificacion puntual).
- **Mejorar el parser del webhook de estado** (Program.cs): no ayuda cuando el webhook llega SIN el
  objeto de error (el caso observado); hay que ir a buscar el detalle al proveedor.
- **Backfill automatico en un worker**: mas intrusivo y gasta llamadas a YCloud por cada fallo. Se
  dejo como accion MANUAL por ahora; se puede promover a worker si resulta util.

## Consecuencias

- La pestana "Fallos de envio" puede pasar de "sin detalle de Meta" a "codigo 131026 - Message
  undeliverable", haciendo accionable el diagnostico (numero sin WhatsApp vs ventana vs otro).
- Queda la base para un futuro paso B (validar el numero antes de disparar) y para mostrar el codigo
  por fila.
- La consulta depende de que el id guardado (`ExternalId`) sea resoluble en YCloud; si YCloud tampoco
  trae detalle, la fila se deja igual (no se inventa motivo).

## Pendientes

- [ ] Validar E2E con la API key real de AGRO (confirmar el codigo de 573182800618).
- [ ] (Posible paso B) Validacion de numero (contacto YCloud/Meta) antes del envio para no quemar
      disparos a numeros muertos (fijos, malformados).
- [ ] (Opcional) Consolidar en UN solo "portero de ventana" la decision plantilla-vs-texto hoy
      duplicada en `WorkflowAgentWhatsApp` y `AgentReactivacionService` (refactor, no arregla T00558).
