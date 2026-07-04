# ADR-0020: Tableros de actividades unificados (las tarjetas SON TaskItem)

- **Fecha**: 2026-07-04
- **Estado**: Aceptada
- **Contexto previo**: ADR-0013 (nucleo TaskItem), ADR-0014 (WorkflowEngine),
  prototipo corregido `ECOREX.dc.html` del vault (fuente unica, 2026-07-04).

## Contexto

El prototipo corregido unifica "Administrar actividades" (modulo legacy 000636) con el
gestor de tableros: el "menu rapido" del rail y la bandeja de administracion son EL MISMO
sistema. Las tarjetas de los tableros no son un kanban generico aparte: son las
actividades del nucleo (TaskItem, con numero T####, prioridad, flujo BPMN y worklog),
enriquecidas con checklist, columna por tablero y multiples asignados.

El backbone ya traia un kanban CRM heredado (TaskBoard/TaskBoardColumn/TaskCard) usado
por paginas existentes. Habia que decidir: crear entidades de tablero nuevas o extender
las existentes sin romper el CRM.

## Decision

1. **TaskItem entra a los tableros; TaskCard queda como kanban CRM heredado.**
   TaskItem gana `BoardId` (FK a TaskBoard, NO ACTION), `ColumnId` (FK a TaskBoardColumn,
   NO ACTION), `BoardSortOrder` y `StartDate` (vista Gantt). La coherencia
   ColumnId-pertenece-a-BoardId se valida en Application (TaskItemService/
   ActivityBoardService). Las FKs sin cascada son deliberadas: borrar un tablero exige
   desacoplar las tareas primero (`ActivityBoardService.DeleteBoardAsync` lo hace
   explicito con `ExecuteUpdate`); las actividades NUNCA mueren con el tablero.

2. **TaskBoard extendido con `Kind` para no romper el CRM heredado.** Enum
   `TaskBoardKind { CrmLegacy = 0, Activities = 1 }` con default CrmLegacy: los tableros
   existentes del CRM no cambian de semantica ni de UI. Los tableros de actividades
   (`Kind = Activities`) agregan `Code` (nullable, "PRY-####" via TenantSequence "PRY",
   unico por tenant con indice filtrado), `Status` (`TaskBoardStatus { OnTime, InProgress,
   AtRisk, Completed }`, rotulo gerencial manual, default InProgress) y `DueDate`.
   `IActivityBoardService` opera SOLO sobre `Kind = Activities`; `ITaskBoardService`
   sigue intacto para el CRM.

3. **Encargado single + asignados M:N.** El `AssigneeTenantUserId` existente se conserva
   como RESPONSABLE (encargado) unico de la actividad; la nueva `TaskItemAssignment`
   (unica por TaskItemId+TenantUserId, cascade con la tarea) agrega el resto del equipo
   (avatares de la tarjeta). Los filtros "asignado" y el alcance "mias" matchean
   encargado O asignacion M:N. El alcance "no asignadas" exige sin encargado Y sin
   asignados.

4. **Columna != estado, con transicion OPORTUNISTA al mover a IsDone.** La columna del
   tablero es ubicacion visual; el estado sigue gobernado por TaskItemStateMachine
   (ADR-0013). Al mover una tarjeta a una columna `IsDone`, `MoveTaskAsync` intenta la
   transicion `Status -> Done` SOLO si la maquina la permite (ej. Active/InProgress ->
   Done); si no (ej. Pending -> Done), la tarjeta se mueve igual, el estado queda intacto
   y el resultado lo reporta en `MoveTaskResultDto.StatusNote` (nunca falla por eso).
   Mover FUERA de una columna final NO reabre la tarea (reabrir es decision humana via
   ChangeStatus). Todo movimiento registra TaskItemActivity.

5. **Checklist propio del TaskItem.** `TaskItemChecklistItem` (Text 500, IsCompleted,
   CompletedAt/CompletedByTenantUserId informativos sin FK dura, SortOrder, cascade con
   la tarea, indice TaskItemId+SortOrder). Completar un item registra actividad;
   desmarcar no (es correccion, no hito).

6. **Progreso del indice (documentado):** si las tareas del tablero tienen checklist,
   progreso = items completados / totales; si no hay checklist, cae a tareas en columna
   final (IsDone) / tareas totales; sin tareas = 0. KPI "en riesgo" del indice: tableros
   con `Status = AtRisk` O con al menos una tarea vencida (DueDate pasada y fuera de
   columna IsDone). KPI "completadas": tareas en columna IsDone. Los KPIs se calculan
   sobre el conjunto FILTRADO del indice (los filtros afectan las tarjetas y los KPIs a
   la vez, como en el prototipo). Logica pura en `ActivityBoardCalculations` (unit-tested).

7. **Filtros server-side.** Indice: usuario miembro / etiqueta / tipo de actividad (sobre
   las tareas del tablero) y rango de fechas (sobre el DueDate del tablero). Detalle:
   columnas[], asignados[], fecha limite (hoy/manana/con-fecha, corte de dia en UTC -
   el corte por zona del tenant es deuda de la ola de UI), tags[] y alcance
   (team/mine/unassigned), todos combinables AND y traducidos a SQL via LINQ sobre el
   query filter global de tenant. Los contadores por alcance se calculan con los demas
   filtros aplicados, ignorando el alcance seleccionado.

8. **QuickCreate delega en CreateAsync.** `CreateTaskItemRequest` gana
   `StartDate/BoardId/ColumnId` opcionales; la creacion rapida desde la columna reusa la
   MISMA transaccion atomica del nucleo (consecutivo T, etiquetas, actividad, arranque
   de flujo del ActivityType) y cuelga la tarea del board/columna al final de la columna.
   Si el quick-add no trae tipo de actividad, usa el primer tipo activo del tenant
   (el prototipo no lo pide; el detalle permite corregirlo).

9. **StartDate para Gantt.** Se persiste ya (create/update/DTOs) aunque la vista Gantt
   llegue en la ola de UI, para que el seed y el ETL puedan poblarla desde ahora.

## Consecuencias

- Migracion dual `AddActivityBoards` (PG + SQL Server): columnas nuevas en task_boards y
  task_items + tablas task_item_checklist_items / task_item_assignments. Sin rutas
  multiples de cascada en SQL Server: las FKs tarea->tablero/columna son NO ACTION y
  tenant_users no cascadea hacia task_items (el encargado es Restrict), asi que las
  cascadas de checklist/assignments son seguras (no hizo falta el patron ClientCascade).
- El CRM heredado no cambia: TaskCard/TaskBoardService intactos; los tableros viejos
  quedan `Kind = CrmLegacy` y sin Code.
- `TaskItemDetailDto` ahora incluye checklist y asignados; `TaskItemSummaryDto` expone
  StartDate/BoardId/ColumnId (parametros opcionales, sin romper llamadores).
- La ola de UI consumira `IActivityBoardService` tal cual (indice con KPIs, detalle con
  chips de filtros y alcances, move con nota de estado).

## Alternativas descartadas

- **Entidades de tablero nuevas (ActivityBoard aparte)**: duplicaba columnas/orden/labels
  y el patron de columnas default; el Kind discrimina con una sola columna.
- **Migrar TaskCard a TaskItem en esta ola**: fuera de alcance; el CRM heredado sigue
  operativo y se decidira su destino cuando el modulo 000636 reemplace esas pantallas.
- **Estado derivado de la columna (columna == estado)**: rompe la maquina de estados y
  los flujos BPMN del ADR-0014; la transicion oportunista conserva ambas verdades.
