# ADR-0113: La compuerta con enlaces de decision del cliente ESPERA (aunque no tenga asignacion humana)

**Status:** Accepted
**Date:** 2026-09-26
**Deciders:** Alexander Cuartas (owner), agente de desarrollo
**Extiende:** ADR-0037 (compuertas exclusivas auto-resueltas), ADR-0068 (compuerta/fin ATENDIDOS con asignacion),
ADR-0107 (enlace publico de decision /d/{token})

## Contexto

Una compuerta exclusiva puede ser un PUNTO DE DECISION donde quien elige la salida es el CLIENTE, por el
enlace publico `/d/{token}` que la regla de notificacion emite por cada salida (ADR-0107): el cliente recibe un
WhatsApp con un boton por rama (Aprobar/Rechazar) y su clic resuelve la compuerta (`ChooseGatewayRouteAsync`).
No decide un humano de la bandeja, ni el agente de IA — decide el cliente.

Pero el runtime solo tenia UN criterio para que una compuerta ESPERE (quede Pending -> envie la notificacion ->
espere la decision): `WaitsForHuman`, que para compuerta/fin es simplemente `AllowsAssignment` ("Permite asignacion
manual del paso"). Sin ese flag, la compuerta se AUTO-RESUELVE en el acto (hereda la decision o toma la arista
default) y **nunca dispara la notificacion** (solo se notifican pasos que quedan Pending).

Consecuencia (observada, T00184): al quitar la asignacion, la compuerta "Gestion del agente" se cerro sola por la
salida default y no envio el WhatsApp. Para que enviara habia que marcar "Permite asignacion manual" aunque NINGUN
humano decida — un desajuste conceptual: se fingia un decisor humano solo para que la compuerta esperara y notificara.

## Decision

Una compuerta exclusiva tambien ESPERA (queda Pending) cuando tiene **enlaces de decision** (una regla de
notificacion con `enlacesDecision` no vacio), aunque `AllowsAssignment` sea false. Asi:

- El paso queda Pending -> se dispara la notificacion (envia el link por cada salida).
- La compuerta se resuelve cuando el CLIENTE elige por su link (exclusiva: la salida elegida avanza, las demas se
  apagan/Skipped) — igual que una decision de bandeja, pero disparada por el enlace publico.
- No se resuelve asignado humano (no hace falta): `WaitsForHuman` sigue false, asi que el paso no entra a la bandeja
  como tarea humana; solo espera el link.

Implementacion (minima, `WorkflowEngine.ActivateNodeAsync`): la rama de auto-resolucion de la compuerta se condiciona
ademas a `!GatewayHasDecisionLinks(node)` (parsea `node.NotifyJson` con `NodeNotifyConfig`). Un gateway con enlaces
cae fuera de esa rama -> conserva su estado Pending -> notifica y espera. El bucle de avance no lo toca porque
`IsReady = IsCurrent && Completed` (un Pending no es "ready").

## Alternativas consideradas

- **Dejar "Permite asignacion manual" marcado** (workaround de config): funciona, pero es semanticamente incorrecto
  (no hay humano que decida) y fragil (cualquiera lo destilda y el flujo se cierra sin enviar). Se descarta como
  solucion; sirve solo como parche temporal.
- **Nuevo flag explicito en el nodo** ("espera decision del cliente"): mas ceremonia y una migracion; la presencia de
  `enlacesDecision` YA expresa la intencion sin metadato extra.

## Consecuencias

- Una compuerta "decide-el-cliente-por-link" funciona sin fingir asignacion humana: envia + espera + resuelve por link.
- Sin regresion: los gateways sin enlaces se comportan igual; los que ya tenian `AllowsAssignment` tampoco cambian
  (ya esperaban). El cambio solo agrega el caso "tiene enlaces pero no asignacion".
- Propiedad calculada, sin columna ni migracion.

## Pendiente (aparte)
El flujo "nuevo PROCESO COMERCIAL" tiene DOS compuertas en paralelo desde "Se prepara la cotizacion" (topologia
que deja una rama colgada). Es un tema de DISENO del flujo, no de este ADR.
