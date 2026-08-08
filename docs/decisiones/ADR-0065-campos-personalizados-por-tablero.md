# ADR-0065 - Campos personalizados de la tarea POR TABLERO

- Estado: ACEPTADA (2026-08-06). Implementado en la rama `feat/campos-personalizados-tarea`
  (worktree), sobre `origin/fase-0/clon-backbone`, PR a `fase-0/clon-backbone`.
- Fecha: 2026-08-06
- Rama / worktree: `feat/campos-personalizados-tarea` sobre `origin/fase-0/clon-backbone`.
- Relacionado: [ADR-0013 TaskItem nucleo], [ADR-0020 Tableros de actividades unificados],
  [ADR-0029 Campos calculados por formula]; patron de campos configurables del Directorio
  General (`TerceroFieldDefinition`) y del inventario (`ItemFieldDefinition`); motor compartido
  de listas del Contenedor de datos (`Ecorex.Application.DataLookups`).

## Contexto

Al trabajar una tarea hace falta capturar datos que el nucleo `TaskItem` no modela y que cambian
por equipo/proceso (un monto aprobado, una referencia externa, un numero de acta, una fila del
Contenedor de datos). El Directorio General ya resuelve exactamente esto con campos configurables
sin codigo: `TerceroFieldDefinition` (agrupados por ficha) e `ItemFieldDefinition` (agrupados por
tipo de item), ambos con el mismo set de tipos (`TerceroFieldType`: Text, Number, Currency,
TextArea, Select, Date, Phone, Separator, Calculated, Lookup), valores en un jsonb del agregado, y
la logica de formula (ADR-0029) y de listas del Contenedor (DataLookups) ya compartida.

La decision de producto: los campos nuevos se presentan y capturan en el MODAL de detalle de la
tarea (`TaskDetailModal`), y **solo existen en el TABLERO donde fueron creados** (alcance por
board). La configuracion se hace con la MISMA logica de "Directorio General" (un modal "Configurar
campos"), soportando TODO el set de tipos.

## Decision

Replicar el patron probado agrupando por **TaskBoard** en vez de por ficha/tipo, REUSANDO la
logica de dominio en vez de reimplementarla:

1. **`TaskFieldDefinition`** (TenantEntity, filtro global por tenant): `BoardId` (FK al TaskBoard,
   NO ACTION) como alcance, + FieldKey, Label, `FieldType` (**reusa `TerceroFieldType`**), Options,
   Column, SortOrder, Description, AllowMultiple, Formula, RepeatWithFieldKey, IsSystem. FieldKey
   unico por `(tenant, board)`. La config del tipo Lookup viaja serializada en `Options` (JSON), sin
   columnas nuevas, igual que en tercero/item.

2. **`TaskItem.CustomFieldsJson`** (jsonb PG / nvarchar(max) SQL Server): dict `FieldKey -> valor`,
   un solo nivel (a diferencia de `Tercero.FichasJson`, porque la tarea ya sabe su tablero).

3. **`ITaskFieldService` / `TaskFieldService`** (calcado de `ItemFieldService`): CRUD por board,
   reordenar, mover-a-otro-board, validar/calcular Calculated (reusa `FormulaEngine`/
   `FormulaCalculator`), resolver Lookup (reusa `IDataLookupService`). Multi-tenant safe: nunca
   filtra a mano por TenantId; el alta estampa el del contexto.

4. **`ITaskItemService.UpdateCustomFieldsAsync`**: persiste el JSON de valores; editable mientras la
   tarea no este Cerrada; registra actividad solo si el JSON cambia. `TaskItemDetailDto` expone
   `CustomFieldsJson`.

5. **UI** (replicada, no extraida): boton "Campos" en `ActivityBoardDetail` que abre el modal de
   configuracion del board; y una tarjeta "Campos del tablero" en `TaskDetailModal` que renderiza
   cada campo EDITABLE por tipo (reusa el componente compartido `DataLookupField` para Lookup),
   recalcula los Calculated (solo lectura) y guarda en `CustomFieldsJson`.

## Reuse vs. replicar

Se REUSA la logica de dominio (enum de tipos `TerceroFieldType`, motor de formulas de ADR-0029, y
el motor de listas del Contenedor `DataLookups` con su componente `DataLookupField`). Se REPLICA la
UI de configuracion y el editor de valor por tipo dentro de los componentes de Tareas, siguiendo la
convencion ya establecida en el repo: `InventarioItems` replico la UI de `DirectorioGeneral` en vez
de extraer un componente compartido. Extraer esas piezas (config de ~300 lineas + editor de valor de
~1800 lineas de `TerceroModal`) habria sido muy invasivo para el alcance; se prefiere replicar la UI
y reusar la logica, que es donde vive el riesgo real.

## Consecuencias

- Un campo vive y muere con su tablero; mover una tarea a otro tablero NO arrastra sus campos (los
  valores del JSON de claves ajenas quedan latentes, no se muestran). Es coherente con "los campos
  solo existen en el tablero donde se crearon".
- `AllowMultiple` / `RepeatWithFieldKey` existen en el esquema por paridad con el patron, pero la
  captura multi-valor/repetible aun NO se cablea en la UI (single-value + Calculated + Lookup +
  Select + Separator si estan completos end-to-end). Queda como trabajo futuro.
- Migraciones DUALES aditivas `AddTaskCustomFields` (PG + SQL Server): 1 tabla + 1 columna. La app
  las auto-aplica en el arranque.
- Tests de integracion en matriz dual (`TaskFieldTests`): alcance por board, round-trip de valores,
  recomputo de Calculated y aislamiento cross-tenant.
