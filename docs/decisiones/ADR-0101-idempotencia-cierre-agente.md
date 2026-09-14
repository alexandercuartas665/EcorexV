# ADR-0101: Idempotencia del cierre del agente (tareas duplicadas)

**Status:** Accepted
**Date:** 2026-09-13
**Deciders:** desarrollo + operacion de agentes

## Contexto

Los agentes de IA crean tareas DUPLICADAS al cerrar: crear_tarea (TasksToolset) y crear_actividad
(ActividadesToolset) se invocan varias veces -- en el mismo turno por el bucle de tool-calling, o en turnos
siguientes cuando el cliente escribe "ya quedo?" y el modelo vuelve a cerrar. Un prompt no lo resuelve de
forma confiable; hay que hacerlo idempotente en codigo.

REGLA DE ORO: NO deduplicar por conversacion. En WhatsApp la conversacion es unica y de por vida por numero;
el cliente puede, en el MISMO chat, pedir despues algo NUEVO y distinto, y eso DEBE crear una tarea nueva.
La deduplicacion es por CONTENIDO, no por conversacion.

## Decision

Idempotencia en dos capas, en un helper compartido (`AgentTaskIdempotency`) que usan ambos toolsets. NO se
toca `ITaskItemService.CreateAsync` (lo usan el wizard y otros modulos): la dedup vive en el camino de los
agentes.

- **Capa 1 - guardia INTRA-TURNO:** la primera creacion OK de una herramienta se recuerda en
  `AiToolRunContext` (dict por-turno, key = nombre de la herramienta). Una segunda llamada de ESA herramienta
  en el mismo turno devuelve el mismo ticket sin volver a insertar. El estado vive en el Scope del turno
  (se descarta al terminar; `Begin` crea uno nuevo por turno).
- **Capa 2 - dedup por CONTENIDO entre turnos:** antes de crear, se busca una tarea reciente que sea
  claramente la misma solicitud: mismo contacto (requester_phone; si no hay, requester_name) + mismo tablero
  (crear_tarea) o mismo concepto/SubcategoriaId (crear_actividad) + mismo titulo+descripcion NORMALIZADOS
  (trim + colapsar espacios + minusculas) + is_archived=false + created_at dentro de una ventana
  (`WindowMinutes = 45`). Si existe, se devuelve ese ticket; si no, se crea normal. Sin contacto NO se
  deduplica (evita falsos positivos entre clientes).

**Criterio elegido:** contacto + contenido + ventana, **SIN migracion** (task_items no gana conversation_id).
Se evaluo agregar conversation_id nullable a TaskItem (+ migracion dual) para key-ear por conversacion; se
descarto por innecesario (el contacto ya identifica al cliente) y para no tocar el esquema.

## Consecuencias

- Una solicitud NUEVA en la MISMA conversacion (titulo/descripcion distintos, o fuera de ventana) SI crea una
  tarea nueva (verificado por test en ambas herramientas). Se respeta la regla de oro.
- Beneficia a SARA (AGROMETALICAS) y MAURO (SKY SYSTEM) que usan crear_tarea, y a Ana (SOLDARCO) con
  crear_actividad.
- Sin migraciones; sin cambios de contrato (SaveImportProcessRequest/CreateTaskItemRequest intactos).
- Adjuntos y requester (contacto/telefono) se preservan; el reloj usado es TimeProvider.System.
- La normalizacion y la ventana son constantes claras (`AgentTaskIdempotency.WindowMinutes`).

## Referencias

- Codigo: `Ecorex.Application/Tenancy/AgentTaskIdempotency.cs` (helper), `AiToolRunContext.cs` (estado
  por-turno), `TasksToolset.cs` (crear_tarea), `ActividadesToolset.cs` (crear_actividad).
- Tests: `TasksToolsetBoardWhitelistTests` (+4) y `ActividadesToolsetTests` (+3): intra-turno,
  turno-posterior mismo contenido, contenido distinto crea nueva, fuera de ventana.
- Relacionado: ADR-0094 (whitelist por agente), ADR-0098 (crear_actividad).

## Revision 2 (2026-09-14, v0.16.65): re-llavear por CONVERSACION

La Capa 2 por contacto+contenido NO evito duplicados en pruebas: el agente puso TELEFONOS distintos
(573187148049 vs 573001234567) y REGENERO el resumen (hashes distintos), asi que ni el telefono ni el
contenido son llaves fiables. La unica estable es la CONVERSACION.

Cambios:
- `TaskItem.ConversationId` (Guid?, nullable) + migracion DUAL (PG `AddColumn conversation_id` + indice
  `(tenant_id, conversation_id)`; SQL Server equivalente). `CreateTaskItemRequest.ConversationId` y el
  servicio la estampan. Solo la puebla el camino del agente; el alta por wizard la deja null.
- crear_tarea / crear_actividad estampan `ConversationId = AiToolRunContext.ConversationId` al crear.
- Capa 2 re-llaveada: `AgentTaskIdempotency.FindRecentByConversationAsync(conversationId)` busca una tarea
  NO archivada de ESA conversacion dentro de una VENTANA CORTA (`ConversationWindowMinutes = 5`); si existe,
  devuelve ese ticket. Se QUITO la dependencia de requester_phone y del hash de contenido (y `Normalize`,
  `WindowMinutes` de 45). Sin ConversationId -> no aplica Capa 2 (se crea normal). La Capa 1 (intra-turno)
  queda igual.
- Telefono correcto: el `RequesterPhone` sale del `Conversations.ContactPhone` REAL (por
  `AiToolRunContext.ConversationId`); `cliente_telefono` del modelo solo se usa como respaldo cuando NO hay
  conversacion (antes el numero alucinado ganaba).

Regla de oro (preservada por la ventana CORTA): una solicitud NUEVA en el mismo chat, pasada la ventana,
crea tarea nueva; solo se colapsan re-cierres/confirmaciones inmediatas (segundos/minutos).

Tests (reescritos): misma conversacion en ventana -> UNA tarea (aunque cambie telefono/resumen); misma
conversacion FUERA de ventana -> tarea nueva; telefono real de la conversacion gana sobre el del modelo;
+ intra-turno. En ambos toolsets. Application.Tests 909/909 verdes.
