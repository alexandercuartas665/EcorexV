# ADR-0006: Ingesta de chat por webhook (Evolution) e idempotencia

**Fecha:** 2026-05-20
**Estado:** Aceptado
**Corresponde a:** Hoja de ruta seccion 9.2 (Chat Omnicanal WhatsApp)

## Contexto

Los mensajes entrantes llegan desde Evolution API mediante webhooks. Estas peticiones NO traen el JWT propio de ECOREX.tareas, por lo que no aplican las politicas ni el `ITenantContext` basado en claims. Hay que resolver el tenant y aislar los datos de otra forma, y garantizar idempotencia (Evolution puede reenviar el mismo evento).

## Decision

1. **Endpoint por tenant:** `POST /webhooks/evolution/{tenantId}` (anonimo respecto al JWT). El tenant se toma de la ruta.
2. **Autenticacion del webhook:** header `X-Webhook-Token` validado contra el token Evolution del tenant (`TenantEvolutionConfig.ApiTokenEncrypted`, descifrado y comparado en tiempo constante). Sin config o token invalido -> 401. Asi el secreto ya gestionado en el modulo 1.3 sirve de credencial del webhook (MVP).
3. **Operacion fuera del filtro por tenant:** como no hay `ITenantContext` (sin JWT), el servicio de ingesta opera con `tenantId` explicito: lecturas con `IgnoreQueryFilters().Where(x => x.TenantId == tenantId)` y escrituras con `TenantId` asignado explicitamente. El webhook ES la frontera de tenant, validada por el secreto.
4. **Idempotencia:** cada mensaje entrante trae `ExternalId` (id de Evolution). Indice unico `(TenantId, ExternalId)` (filtrado a no nulos). Si llega repetido, no se inserta de nuevo.
5. **Conversacion:** una por `(TenantId, ContactPhone)` (find-or-create).
6. **Payload normalizado:** el endpoint recibe una forma normalizada (`instanceName`, `contactPhone`, `contactName?`, `externalMessageId`, `body`, `sentAt?`). El Evolution Connector real traducira el payload crudo de Evolution a esta forma en una fase posterior.

## Diferido (no en este commit)

- **Broadcast en tiempo real (SignalR):** publicar el mensaje al grupo del tenant para que el chat del asesor se actualice sin refrescar. Se anadira como hub + grupo por tenant; la persistencia idempotente de este ADR es el prerequisito.
- **Envio saliente real:** el endpoint autenticado de envio persiste el mensaje saliente; la llamada real a Evolution (Evolution Connector) y su confirmacion quedan para el connector.
- **Validacion de firma de Evolution** (si el proveedor la ofrece) ademas del token.

## Consecuencias

- La ingesta es segura (token por tenant), aislada (tenantId explicito) e idempotente (indice unico).
- Las lecturas de chat para asesores autenticados usan el flujo normal (politica TenantMember + filtro por tenant del JWT).
