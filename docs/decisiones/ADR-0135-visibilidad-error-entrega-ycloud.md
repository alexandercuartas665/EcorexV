# ADR-0135: Visibilidad del error de entrega de WhatsApp (YCloud)

**Estado:** Aceptado
**Fecha:** 2026-10-27
**Version:** v0.16.236

## Contexto

Cuando un mensaje SALIENTE de WhatsApp no llega al cliente, YCloud ACEPTA el envio (asigna
external_id) y Meta devuelve `failed`/`undelivered` segundos despues por el webhook de estado
(`whatsapp.message.updated`). El handler dejaba una nota en la conversacion, pero el motivo
quedaba como "sin detalle de Meta" porque el parser solo leia `error.{code,message/title}` o
`errors[0]`, y el detalle real de Meta suele venir mas anidado. Sin el codigo real
(1013 "no es usuario WhatsApp", 131026 "undeliverable", 131047 "ventana cerrada", ...) cada
"no llego" obligaba a entrar a la BD a adivinar.

Premisa del reporte original (data-ops): "Serilog va a archivo, no a stdout". Al revisar, el
proyecto NO usa Serilog: usa el logging por defecto de .NET, que ya escribe a stdout (y Docker
lo captura en `docker compose logs`). La premisa era incorrecta; el hueco real era el detalle
anidado y el payload crudo.

## Decision

1. **Parser tolerante extendido** (`YCloudWebhookParser.ParseStatus`): leer el detalle de Meta
   tambien en las formas anidadas (`error_data.details`, `errorData.details`/`.detail`, `href`)
   y el codigo numerico anidado (`error_data.code`/`errorData.code`), ademas de las directas.
2. **Persistir el error CRUDO**: nuevo campo `YCloudStatusUpdate.ErrorRaw` con el subarbol
   `error`/`errors` tal cual, para ajustar el parser con certeza si aparece una forma nueva.
3. **Motivo visible con codigo**: la nota y `Message.DeliveryError` muestran
   "codigo 131026 - Message undeliverable" cuando Meta entrega ambos; "codigo X" o el texto si
   solo viene uno; "sin detalle de Meta" solo cuando de verdad no llega nada.
4. **Log con payload crudo**: el `LogWarning` del fallo incluye `errorRaw` (JSON del subarbol de
   error), no solo los campos parseados. No se agrega Serilog: el logger por defecto ya va a
   stdout.

## Consecuencias

- Un saliente que Meta rechaza deja trazabilidad accionable en la conversacion y en el log, sin
  entrar a la BD.
- Si una forma de error aun no reconocida aparece, `ErrorRaw`/`errorRaw` dan el JSON real para
  extender el parser con un caso de prueba.
- Si en prod el warning no se viera en `docker logs`, el problema seria un override del nivel de
  consola por variable de entorno del contenedor (`Logging__LogLevel__*`), no un sink a archivo.
- Alcance: proveedor YCloud. El proveedor Cloud API, si tiene el mismo hueco, aplica el mismo
  criterio por separado.
