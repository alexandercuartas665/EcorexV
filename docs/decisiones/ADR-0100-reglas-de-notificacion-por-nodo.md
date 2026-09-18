# ADR-0100: Reglas de notificacion por nodo de flujo (Ola 1)

**Status:** Accepted
**Date:** 2026-09-13
**Deciders:** sesion de flujos + desarrollo

## Contexto

El editor de flujos tenia un stub "Reglas de notificacion" (chips ilustrativos + "TODO: motor de
notificaciones por nodo pendiente"). Se pidio hacerlo real, REUSANDO lo que ya hace el agente en el Cierre
(ADR-0099): avisar por correo / WhatsApp (plantilla) / WhatsApp grupo / Telegram. Extras pedidos:
- Configurarlo en un MODAL (no inline).
- Mensajes por PLANTILLA con tokens de la TAREA ({tarea.contacto}, {tarea.numero}, ...) y de los
  FORMULARIOS empacados en el flujo ({form.<codigo_de_campo>}).
- Un ENLACE a la tarea, OPCIONAL por regla.

Decisiones del usuario: disparo **al LLEGAR el paso**; entrega **best-effort** (como el Cierre, sin
reintentos); enlace por **deep-link a la tarea** (ruta nueva + URL base por entorno).

## Decision

**Motor por nodo, determinista y server-side** (NO herramienta/MCP: cero tokens del modelo). Se dispara en el
UNICO punto de llegada del engine: `WorkflowEngine.ActivateNodeAsync`, cuando el paso queda `Pending`
(gemelo del board-target `MoveTaskToNodeTargetAsync`). Para no bloquear el avance con HTTP dentro de la
transaccion, las llegadas se **acumulan** durante la operacion y se **envian DESPUES del commit**
(`BroadcastTaskAsync` -> `FlushArrivalNotificationsAsync`), una vez por activacion. Cubre pasos manuales y
de agente (el cierre del agente re-entra por los mismos caminos).

**Config por nodo** en `WorkflowNode.NotifyJson` (jsonb), editable en un MODAL del editor (patron del modal
de Agente). CRUD via `IWorkflowDesignService.SetNodeNotifyAsync` (metadato, editable sobre publicada, no
regenera el XML). Cada regla: canal, destinatario (asignado del paso / usuario / grupo / chat), mensaje por
plantilla, y enlace opcional.

**Reuso del Cierre sin duplicar:** se extrajo `INotificationChannelSender` (correo / WhatsApp plantilla /
grupo Evolution / Telegram) que ahora usan TANTO el Cierre (`AgentCierreService`, refactorizado) COMO el
motor de nodo (`NodeNotifyService`). Centraliza el mapeo de variables HSM y la carga del token de Telegram.

**Tokens:** `NotifyTokenResolver` (server-side) arma {tarea.*} desde `TaskItem` y {form.*} leyendo las
respuestas ancladas a la tarea (`FormResponse.Reference == numero`). Sirve para texto libre (correo/grupo/
Telegram) y para llenar por NOMBRE las variables de una plantilla HSM de WhatsApp.

**Enlace a la tarea:** ruta nueva `/actividades?task={id}` (abre el detalle en modal) + `INotifyLinkBuilder`
(Infra) que arma la URL absoluta desde `Ecorex:PublicBaseUrl` o la env var `ECOREX_PUBLIC_BASE_URL`. Sin URL
base configurada, el mensaje sale sin enlace.

## Consecuencias

- Notificaciones por nodo sin costo de tokens del modelo; una vez por llegada; no bloquean el avance.
- Migracion DUAL aditiva `AddNodeNotify` (workflow_nodes.notify_json). Sin cambios en el XML BPMN.
- WhatsApp plantilla exige linea YCloud; grupo exige linea Evolution; Telegram usa el bot del tenant (se
  configura en la seccion Cierre del agente). El correo funciona con el SMTP del tenant.
- Para que el ENLACE funcione en prod hay que fijar `ECOREX_PUBLIC_BASE_URL` en el entorno (docker .env).
- Best-effort: un envio caido se ignora (no reintenta). Si luego se requiere entrega garantizada, el upgrade
  es un outbox transaccional (se evaluo y se pospuso).
- Ola siguiente posible: disparo tambien al CERRAR el paso; picker dinamico de campos de formulario en el modal.

## Referencias

- Codigo: `WorkflowEngine.cs` (buffer + FlushArrivalNotificationsAsync), `NodeNotifyConfig.cs`,
  `INodeNotifyService`/`NodeNotifyService.cs`, `INotifyTokenResolver`/`NotifyTokenResolver.cs`,
  `INotifyLinkBuilder` + `Infrastructure/Notifications/NotifyLinkBuilder.cs`,
  `Notifications/INotificationChannelSender`+`NotificationChannelSender.cs` (compartido con `AgentCierreService`),
  `WorkflowNode.NotifyJson`, `IWorkflowDesignService.SetNodeNotifyAsync`, UI `FlowEditor.razor` (modal),
  deep-link `Actividades.razor` (?task=).
- Tests: `NodeNotifyConfigTests`, `NotifyTokenResolverTests`.
- Relacionado: ADR-0099 (Cierre del agente), ADR-0056 (asignado del paso), ADR-0051 (diagrama de flujo).

## Nota v0.16.82 - binding de variables de plantilla WhatsApp + tokens de sistema

Hasta ahora una regla WhatsApp solo elegia la PLANTILLA; sus variables se llenaban por NOMBRE (la variable
"cliente" tomaba el token "cliente"), sin forma de atar una variable a otro dato. Se agrego:

- `NodeNotifyRule.Variables` (jsonb dentro de NotifyJson): mapa variableDeLaPlantilla -> expresion con tokens
  ("{tarea.numero}", "{form.total}", "{sistema.fecha}", texto fijo o mezcla). Null/vacio = llenado por nombre
  (compatibilidad hacia atras).
- UI (FlowEditor, modal de notificaciones): al elegir la plantilla se enumeran SUS variables (de
  WhatsAppTemplateDto.Variables) y por cada una un campo para escribir la expresion, con ayuda de tokens.
- Envio (NodeNotifyService, caso WhatsApp): cada binding se resuelve con NotifyTokenResolver.Render y se
  inyecta en el mapa de tokens bajo el nombre de la variable (normalizado como BuildTemplateParams: sin
  acentos, minusculas), asi el llenado posicional por nombre toma el valor atado. Sin cambiar la firma del
  sender. Las variables sin binding siguen por nombre.
- Tokens nuevos en NotifyTokenResolver: `{sistema.fecha}`/`{sistema.hora}`/`{sistema.fechahora}` (zona del
  tenant, UTC-5) y `{tarea.id}`. Siguen disponibles `{tarea.*}` y los de formularios de la ruta `{form.<campo>}`.

Sin migracion (todo va en NotifyJson). Tests: NotifyTokenResolverTests (Render de expresion mixta) +
FormExpressionEvaluatorTests (no relacionado). Build de la solucion verde.
