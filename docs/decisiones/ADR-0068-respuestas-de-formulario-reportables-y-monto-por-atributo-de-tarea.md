# ADR-0068: Respuestas de formulario (modulos) como fuente reportable + monto por atributo de la tarea

**Status:** Accepted
**Date:** 2026-09-03
**Deciders:** Sesion de reportes (pide capacidad), sesion de codigo (implementa)

## Contexto

La sesion de reportes necesita un panel del tablero comercial que muestre, por ESTADO (columna del
tablero Kanban), el MONTO de la cotizacion que trae cada tarea. El dinero de la cotizacion vive en un
MODULO de formulario (form_definitions.code = 'COT', campo `tot_total`), enlazado a la tarea por
`form_responses.reference = task_items.number` (con posible sufijo de revision, ej. "T00016-1"). Hoy el
motor de reportes NO expone como dato el dinero de los formularios:

- `native:taskitem` ("Actividades") no tiene campo numerico (solo Count), y su "Etapa" es el ColumnId.
- El valor esta en `form_responses.data` (jsonb) envuelto como `{"value":"...","type":"Number"}`.
- `IReportCatalog` publica nativas + contenedores + externas, pero NO modulos/respuestas de formulario.

El objetivo es una CAPACIDAD reusable (cualquier modulo con campos numericos, no solo COT), no un reporte
a medida: la sesion de reportes arma el panel como dato (PanelSpec) despues del PR.

## Decision

1. **Nueva fuente reportable `FormResponseReportReader`** (Application/Reporting/Sources), analoga al
   `ContainerReportReader` pero sobre `_db.FormResponses`. UNA fuente por MODULO
   (form_definitions.is_module = true). Clave **`form:{code}`** (no el definitionId): el `code` es estable
   entre entornos (dev/prod), asi un PanelSpec autorizado funciona igual en ambos; el definitionId difiere
   por tenant/entorno. `IReportCatalog.GetSourcesAsync` la enumera como los contenedores; `ReportDataSource`
   despacha las claves `form:` a este lector. Tenant-safe: `FormResponses`/`FormDefinitions`/`FormQuestions`
   llevan el filtro global; un tenant solo ve SUS modulos y SUS respuestas.

2. **Campos** = las `FormQuestions` ESCALARES del modulo (`FormFieldValidator.IsCapture`: excluye
   estructura, multimedia placeholder, GridDetail y Subform). `Key = field_code`, `DisplayName = label`.
   Mapeo de tipo: **Number -> Decimal** (los montos son decimales; el tipo logico Number usa parseo entero
   y perderia el valor), Date/DateTime/Time -> Date, Toggle -> Boolean, resto -> Text. Solo los Decimal
   son agregables (Sum/Avg/Min/Max). Mas campos SINTETICOS siempre presentes: Reference, RecordNumber,
   Status (record_status), IsActive, TransactionDate, SubmittedAt, CreatedAt.

3. **Lectura del jsonb**: `FormResponseService.ParseDocument(data)` -> `dict[fieldCode] = {Value, Type}`;
   se toma `.Value` y se convierte al tipo del campo. **Vigencia**: se excluyen por defecto SOLO las
   respuestas anuladas (`record_status = Voided`); el estado y `IsActive` se exponen como campos para que
   el panel filtre a voluntad (no se fuerza `is_active = true`, que en la practica dejaria casi todo fuera
   por ser un flag de "formulario activo por defecto de la tarea", no de "vigente").

4. **PanelSpec (ADR-0066) extendido** con claves nuevas, todas JSON (sin migracion de esquema; reusa
   `ReportDefinition.SpecJson`):
   - **`Source`** en `Main` y en cada `Lookup`: la CLAVE de la fuente (ej. `native:taskitem`, `form:COT`),
     alternativa/preferente al `Container` (DisplayName). Los campos se referencian por DisplayName **o**
     por Key (tolerante), para specs con nombres tecnicos estables.
   - **`KeyTransform`** en el lookup: normaliza la clave antes de cruzar. `"beforeDash"` toma lo anterior
     al primer `-` (ej. "T00016-1" -> "T00016"), para cruzar `task.Number` con `response.Reference`.
   - **`Reduce`** en el lookup `{ By, Keep }`: dedupe antes de cruzar. `Keep="latest"` conserva la fila mas
     reciente por `By` (por TransactionDate/SubmittedAt/CreatedAt); `"first"` la primera. Evita contar
     varias revisiones de una misma cotizacion.
   - Un alias NUMERICO traido por lookup (Bring) queda AGREGABLE (Sum) como cualquier campo.
   - **`Where`** a nivel de spec: filtro FIJO `[{Field, Op, Value}]` que se aplica SIEMPRE (no es control de
     UI). Ops: eq | ne | contains | gt | gte | lt | lte. Cierra el "Where fijo" pendiente de ADR-0066.
   - **`When`** opcional en un KPI: agrega solo el subconjunto que cumple las condiciones (KPI condicional).

5. El `SpecPanelRenderer` aplica todo EN MEMORIA (reusa `PanelDataEngine`): Main -> lookups (con
   KeyTransform + Reduce) -> derivados -> Where -> filtros/KPIs/widgets. Las funciones nuevas
   (`TransformKey`, `Matches`) son puras y testeadas. El `PanelSpecValidator` valida las claves nuevas
   contra el catalogo tenant-safe.

## Consecuencias

- **Positivas**: cualquier "monto/valor/cantidad" capturado en un modulo se vuelve reportable y sumable por
  atributos de la tarea (estado del tablero, concepto, asignado), como dato, sin recompilar ni desplegar.
  El PanelSpec gana Where fijo, KPI condicional, cruce por clave normalizada y dedupe reusables.
- **Multi-tenant**: la fuente `form:{...}` corre sobre `_db.FormResponses` (filtro global); un tenant no
  puede leer respuestas ni modulos de otro. La cadena de datos jamas expone secretos.
- **Costo**: la lectura del jsonb se materializa y parsea en memoria (como el contenedor) para no depender
  de operadores jsonb especificos por proveedor (PG vs SQL Server). Cap de 50.000 filas por consulta.
- **A revisar**: el mapeo Number->Decimal cubre montos; si mas adelante se requieren enteros puros,
  distinguir por `format`/`numeral` de la pregunta. La agregacion es de un solo campo de agrupacion (v1).

## Verificacion

- Build verde (Application + SuperAdmin). Tests `PanelSpecValidatorTests` (incl. pipeline COT),
  `PanelDataEngineWhereTests` (TransformKey/Matches) y `FormResponseReportReaderTests` (clave/tipos).
- End-to-end en dev (copia de prod, tenant AGROMETALICAS): panel Main=`native:taskitem` + lookup `form:COT`
  (KeyTransform beforeDash + Reduce latest) sumando `tot_total` por Etapa. El resultado coincide AL PESO con
  la consulta SQL tenant-scoped de referencia (Completado $2.939.259, En progreso $840.735, Requerimiento
  $252.221, En revision $201.776; total ~$4,23M). Una consulta cruda cross-tenant inflaba el total con
  tareas de otro tenant que comparten numero: el panel las excluye (aislamiento por construccion).
