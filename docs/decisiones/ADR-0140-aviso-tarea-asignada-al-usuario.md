# ADR-0140: Aviso de "tarea asignada" al usuario de la tarea (binding de tokens + bitacora)

**Estado:** Aceptado
**Fecha:** 2026-10-30
**Version:** (sin desplegar; bump al autorizar deploy)
**Deciders:** usuario (producto) + sesion data-ops AGROMETALICAS (diagnostico) + agente de desarrollo

## Contexto

El agente SARA debia avisar por WhatsApp al usuario asignado cuando CREA una actividad, pero no llegaba.
La sesion de data-ops de AGROMETALICAS diagnostico la causa en prod y la via usada (el CIERRE del agente,
`AgentCierreConfig` + `AgentCierreService.HandleCloseAsync`):

1. **Destinatario equivocado y vacio.** `CierreDestino.Asignado` resuelve al asignado de la LINEA
   (`WhatsAppLine.AssignedToTenantUserId`), NO al asignado de la TAREA. La linea no tenia asignado -> no
   enviaba. La etiqueta de UI "Usuario asignado" enganaba.
2. **Parametros de plantilla vacios.** La plantilla `aviso_tarea` pide variables posicionales
   (`{{1}}`=numero, `{{2}}`=titulo, `{{3}}`=solicitante). `AgentCierreService.BuildTokenMap` solo aporta
   cliente/telefono/agente/resumen, nunca 1/2/3; el guard anti-vacio (`FindEmptyTemplateParams`) bloqueaba.
   Ademas `HandleCloseAsync` recibe `(agentId, convId, summary)` y NO conoce la tarea creada, asi que
   numero/titulo eran imposibles de llenar desde ahi.
3. **Sin rastro.** El camino de cierre no escribia bitacora ni nota y tragaba la excepcion (`catch {}`).

Conclusion: la via CIERRE sirve para avisar del cierre de una conversacion con el CLIENTE (asignado de la
linea), no para "tarea asignada al usuario de la tarea".

## Decision

Una notificacion PROPIA de "tarea asignada" (Opcion B), separada del cierre:

- **Disparo donde se conoce la tarea Y el asignado:** se emite en `crear_actividad` (ActividadesToolset) y
  `crear_tarea` (TasksToolset), justo despues de `ITaskItemService.CreateAsync`, solo en la creacion REAL
  (no en los caminos idempotentes). Best-effort: un fallo de envio queda en la bitacora, nunca rompe la
  respuesta al cliente.
- **Destinatario = usuario de la TAREA.** Nuevo `CierreDestino.UsuarioTarea` (asignado de la tarea,
  `TaskItem.AssigneeTenantUserId`), distinto del `Asignado` (linea). Tambien admite "Otro usuario".
- **Binding de tokens por variable.** `AgentCierreAlerta` gana `Variables` (dict VARIABLE de la plantilla ->
  expresion de token), con el MISMO patron que `NodeNotifyRule.Variables`: la expresion se resuelve con
  `NotifyTokenResolver.Render` y se inyecta bajo el nombre de la variable (sin acentos, minuscula) para que
  el llenado por nombre tome ese valor. La UI del agente lee las variables de la plantilla elegida
  (`whats_app_templates.variables_json`) y pinta un campo por variable con su token.
- **Tokens disponibles.** `NotifyTokenResolver.BuildAsync` se amplia con `{usuario.nombre}` (DisplayName o
  Email), `{usuario.email}`, `{usuario.telefono}` del asignado de la tarea, ademas de los ya existentes
  `{tarea.*}`, `{tarea.enlace}` (deep-link) y `{sistema.*}`. Ejemplo para `aviso_tarea`:
  1->`{tarea.numero}`, 2->`{tarea.titulo}`, 3->`{tarea.cliente}`.
- **Bitacora + rastro.** Cada aviso registra en la historia de la tarea (`TaskItemActivity`) y en la
  bitacora del agente (`AiAgentRunLog`, Kind=Info en exito / Error en fallo con el motivo). Nada silencioso:
  sin telefono del asignado, sin plantilla, o parametro imposible -> NO se envia a ciegas y el motivo queda
  escrito. Se estampa el `WhatsAppSendOutcome.MessageId` (wamid) en el log para correlacion de entrega.
- **Servicio:** `IAgentAvisoTareaService.NotifyTaskAssignedAsync(agentId, taskId, conversationId, actor)`,
  reusando `INotificationChannelSender`, `INotifyTokenResolver` e `INotifyLinkBuilder`.

## Donde vive la configuracion (sin migracion)

Los avisos viven en el MISMO `AiAgent.CierreJson` (jsonb) que el cierre, bajo
`AgentCierreConfig.AvisosTarea` (lista de `AgentCierreAlerta`). No hay columna ni tabla nueva: solo se
amplia el contrato JSON (round-trip y compat. hacia atras cubiertos por tests). La UI del agente tiene un
acordeon aparte "Aviso de tarea asignada"; ambas secciones (Cierre y Aviso) persisten en el mismo blob y
cada guardado conserva la otra.

## Alternativas consideradas

- **Reparar el CIERRE** para que apunte a la tarea: rechazado, el cierre se dispara por `SessionCompleted`
  y no conoce la tarea; mezclar dos semanticas (cierre de conversacion vs tarea asignada) en un disparador.
- **Columna nueva `AvisoTareaJson`** en `AiAgent`: evita acoplar el guardado de ambas secciones, pero exige
  migracion DUAL (PG + SQL Server) + snapshots. Se prefirio el CierreJson (sin migracion) por simplicidad,
  aceptando que ambas secciones se guarden juntas.

## Consecuencias

- Al crear SARA una actividad asignada a un usuario con telefono, ese usuario recibe el WhatsApp con
  numero/titulo/solicitante correctos, y queda rastro en la bitacora y en la tarea.
- `{usuario.*}` queda disponible para CUALQUIER notificacion de nodo (el resolver es compartido).
- El mecanismo es generico (sirve a cualquier tenant/linea); el alcance inmediato es AGROMETALICAS/YCloud.
- La asignacion MANUAL (sin agente) no dispara avisos: el aviso es agent-scoped (la config vive en el
  agente). Cubrir la asignacion manual seria otra decision.
- **El codigo no trae la config por tenant:** hay que crear el/los aviso(s) en el agente (self-serve en
  `/agentes` -> seccion "Aviso de tarea asignada", o por datos) con la plantilla, la linea y el binding de
  variables. Sin eso, el agente no notifica nada.

## Validacion

Build verde (solucion completa). `Ecorex.Application.Tests` 1145/1145 OK, incluidos 3 tests nuevos de
round-trip de `AvisosTarea` + `Variables` + `UsuarioTarea` y compat. hacia atras del CierreJson viejo.
E2E (envio real por YCloud + config del agente en el tenant) a cargo de la sesion de data-ops de
AGROMETALICAS. Sin migracion de BD. Sin desplegar (pendiente de OK del usuario).
