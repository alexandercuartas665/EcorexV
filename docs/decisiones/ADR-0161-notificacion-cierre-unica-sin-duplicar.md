# ADR-0161: Notificacion de cierre del agente unificada (un solo mensaje al comercial)

**Status:** Accepted (Fase 4 - Agente IA)
**Date:** 2026-11-09
**Deciders:** Alexander Cuartas (dev principal)

## Contexto

Al cerrar una atencion, el agente puede notificar al comercial. Convivian DOS mecanismos, ambos en el
mismo `AiAgent.CierreJson` (`AgentCierreConfig`), con disparadores distintos:

- **`AvisosTarea`** ("Aviso de tarea asignada", ADR-0140): se dispara desde el TOOLSET cuando el agente
  CREA Y ASIGNA una actividad/tarea (`ActividadesToolset`/`TasksToolset` -> `AgentAvisoTareaService`).
  Conoce la tarea y su asignado; funciona bien (taskId, tokens `{tarea.*}`, binding de variables, destino
  `UsuarioTarea`, y deja rastro en `/bitacora-agente` + historia de la tarea).
- **`Alertas`** ("Alerta al cierre"): se dispara desde `AiInferenceService` al cerrar (sessionCompleted o
  marcador `[[cierre]]`) -> `AgentCierreService.HandleCloseAsync`.

Para el agente **crear la tarea == cerrar**, asi que ambas cajas eran "la notificacion de cierre". Problemas
detectados (verificados en codigo):

1. **Doble notificacion / ambiguedad.** Con ambas secciones pobladas, el comercial podia recibir dos
   mensajes equivalentes, y la UI obligaba al usuario a saber "cual gana".
2. **El camino `Alertas` fallaba, muchas veces en SILENCIO.** `DispatchAlertAsync` pasaba el `tokenMap`
   crudo sin aplicar el binding de variables de la plantilla; una plantilla con variables posicionales
   (`{{1}}{{2}}{{3}}`) salia vacia y el proveedor cortaba el envio
   (`NotificationChannelSender.FindEmptyTemplateParams`). El `WhatsAppSendOutcome` se ignoraba y el fallo
   se tragaba en un `try/catch` vacio: nada quedaba en "Fallos de envio".

## Decision

**Una sola notificacion de cierre, un solo lugar para configurarla.** No se "arregla" `Alertas` para que
duplique a `AvisosTarea`; se CONSOLIDA en el camino que ya funciona y se garantiza no duplicar.

1. **De-dup por turno (sin migracion, sin cambiar firmas).** `AiToolRunContext` (AsyncLocal, activo todo el
   turno de inferencia) gana un `HashSet<Guid> CloseNotifiedUsers` con `MarkCloseNotified(uid)` /
   `WasCloseNotified(uid)`. Cuando el AVISO de tarea notifica con exito al asignado (WhatsApp/correo), lo
   marca. La ALERTA de cierre, ANTES de enviar a un usuario, consulta `WasCloseNotified(uid)` y, si ya fue
   notificado, se OMITE (registrando el motivo). El cierre corre dentro del mismo scope del contexto
   (`AiInferenceService` abre `AiToolRunContext.Begin` antes del bucle de herramientas y el cierre ocurre
   antes del dispose), asi que el rastro esta disponible. El rastro se descarta al terminar el turno (no
   contamina al siguiente cliente).

2. **`Alertas` deja de fallar en silencio.** `AgentCierreService` ahora:
   - aplica `ApplyVariableBindings` (igual que `AgentAvisoTareaService`/`NodeNotifyService`) para que las
     plantillas con variables se llenen;
   - captura el `WhatsAppSendOutcome`/booleanos de cada canal y REGISTRA el resultado (exito o fallo + motivo)
     en `/bitacora-agente` (`AiAgentRunLog`), incluido el caso de de-dup ("omitido: ya se notifico por el
     aviso de tarea"). Se inyecta `INotifyTokenResolver`.

3. **UI: una sola seccion "Notificacion de cierre".** El panel del agente (`Agentes.razor`) fusiona los dos
   acordeones ("Cierre" + "Aviso de tarea asignada") en UNO, con dos sub-bloques rotulados por proposito y un
   solo boton Guardar:
   - **Mensaje al comercial asignado** (= `AvisosTarea`, el que recibe la persona al crear la tarea).
   - **Resumen del cierre** (= `Alertas`, para grupo/Telegram/correo o cierre SIN tarea).
   Ambos siguen viviendo en el mismo `CierreJson` (compatibilidad hacia atras).

## Opciones consideradas

- **Arreglar `Alertas` al 100% + exclusion mutua (mantener dos cajas).** Mas fiel al hand-off literal, pero
  deja dos motores y una UI con dos acordeones que el usuario debe reconciliar. Descartado por el usuario a
  favor de consolidar.
- **Arrancar `AvisosTarea`/ADR-0140 y meter todo en `HandleCloseAsync`.** Rechazado: ADR-0140 tambien sirve
  en asignaciones que NO son cierre; la unicidad aplica al caso de cierre del agente, no globalmente.
- **De-dup por BD (ventana por conversacion+destinatario+plantilla).** Mas pesado y con migracion; el rastro
  por turno (AsyncLocal) cubre exactamente el caso "crear == cerrar" en el mismo turno, sin estado durable.

## Consecuencias

- El comercial recibe UN solo mensaje al cierre; el usuario configura la notificacion en UNA seccion.
- Los fallos de la alerta de cierre ya son visibles (bitacora), no silenciosos.
- A vigilar: el de-dup es por turno/usuario. Si en el futuro el cierre debe notificar por VARIOS canales al
  mismo usuario a proposito, habria que afinar la llave de de-dup (hoy: por usuario destino).
- Sin migracion: todo es logica + un `HashSet` en el contexto ambiental; `CierreJson` no cambia de forma.

## Action Items

1. [x] `AiToolRunContext`: `CloseNotifiedUsers` + `MarkCloseNotified`/`WasCloseNotified`.
2. [x] `AgentAvisoTareaService`: marcar al asignado notificado (WhatsApp/correo con exito).
3. [x] `AgentCierreService`: binding de variables + de-dup + registro en bitacora (no silencioso).
4. [x] `Agentes.razor`: fusionar en un solo acordeon "Notificacion de cierre" con un solo guardado.
5. [x] Tests unitarios del de-dup (`AgentCierreDedupTests`).
6. [ ] Reflejar ADR-0161 en el vault.
7. [ ] Deploy a prod con OK explicito del usuario por version.
