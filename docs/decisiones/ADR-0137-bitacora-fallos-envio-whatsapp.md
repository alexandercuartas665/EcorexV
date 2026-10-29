# ADR-0137: Fallos de envio de WhatsApp visibles en la bitacora (sin SSH)

**Estado:** Aceptado
**Fecha:** 2026-10-27
**Version:** v0.16.238
**Deciders:** usuario (producto) + agente de desarrollo

## Contexto

Con ADR-0135, cuando un saliente de WhatsApp no llega (YCloud/Meta lo marca `failed`), se captura el
codigo + motivo real de Meta en `Message.DeliveryError` y se deja una nota en la conversacion + un
warning en el log del contenedor (`docker logs`). Pero para ver POR QUE no llego habia que (a) entrar
a cada conversacion, o (b) entrar al servidor por SSH a leer `docker logs`. El usuario pidio poder
verlo DENTRO de la app, consolidado: "si pasa un error, verlo en la bitacora".

Se decidio (con el usuario): alcance = SOLO fallos de envio de WhatsApp (no crashes del sistema ni
errores del agente, que ya estan en `/bitacora-agente`); ubicacion = dentro de `/bitacora-agente`.

## Decision

Nueva pestana **"Fallos de envio"** en `/bitacora-agente`, junto a "Atencion del agente". Lista los
mensajes SALIENTES con `DeliveryStatus == "failed"` del tenant, mas recientes primero, mostrando:
contacto (nombre/telefono) + linea, fecha, **motivo real de Meta** (`DeliveryError`, codigo + texto;
"sin detalle de Meta" si Meta no dio nada) y el texto del mensaje.

- **Sin datos nuevos ni migracion**: reusa `Message.DeliveryStatus`/`DeliveryError` (ADR-0135).
- Servicio: `IAiAgentLineService.ListDeliveryFailuresAsync` (tenant-scoped por el filtro global;
  query EF que se traduce a SQL, validada en vivo). DTO `AgentDeliveryFailureDto`.
- UI: pestanas con estado `_tab`; el boton "Limpiar bitacora y cache" (que borra logs del AGENTE)
  se oculta en la pestana de fallos para no confundir (no toca mensajes).
- No depende de deep-link a `/conversaciones` (esa pagina no lo soporta hoy): el detalle del fallo
  se muestra completo en la propia bitacora.

## Consecuencias

- El usuario ve los "no llego" con el codigo real de Meta sin entrar al servidor ni recorrer
  conversaciones.
- La lista solo se puebla con fallos REALES posteriores al deploy de ADR-0135 en prod (el estado de
  entrega lo avanza el webhook de estado; mensajes viejos no tienen `DeliveryStatus`).
- Alcance WhatsApp. Crashes/excepciones del sistema (p.ej. el circuito Blazor) NO entran aqui: si se
  quieren en la app, es otra decision (captura global de excepciones + tabla + UI), evaluada aparte.

## Validacion

Build verde; verificado E2E en navegador (AGROMETALICAS): se sembro un fallo de prueba en el dev
local, la pestana mostro el contador "(1)", la fila con linea + fecha + "codigo 131026 - Message
undeliverable" + el texto; luego se borro el dato de prueba. La pestana "Atencion del agente" sigue
igual (sin regresion). Sin migracion.
