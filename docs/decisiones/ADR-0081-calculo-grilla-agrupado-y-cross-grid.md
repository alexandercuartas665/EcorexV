# ADR-0081: Calculo de grilla AGRUPADO (subtotales por grupo) y REFERENCIA entre grillas (cross-grid)

- Estado: Aceptado (en construccion por fases)
- Fecha: 2026-08-28
- Contexto: motor de calculo de grillas (ADR de formularios dinamicos), GridDetail con calc/agg/rollup
  (FormGridCalculator), lookup/resolve por columna (FormGridColumnLookup). Caso real: documento APU de
  costeo electrico (tenant EPRING) que hoy no se puede completar solo con configuracion porque el motor
  no tiene (1) subtotales por grupo dentro de una grilla y (2) referencias entre grillas del mismo registro.

## Contexto

El APU necesita, dentro del mismo registro de formulario:
- Subtotales por ITEM (Materiales + Mano de obra) y "valor total del item" en la grilla APU (SUMIF por
  columna clave), no solo el total de columna completa.
- Oferta.v_unitario = total del item calculado en APU (referencia APU -> Oferta).
- Margen.costo_directo = SUMA del 'parcial' del APU por 'grupo' (SUMIF cross-grid APU -> Margen).

El motor de hoy solo agrega columnas COMPLETAS (filtradas por aggWhen) y el evaluador de formulas solo ve
la fila y el encabezado ({col} y {#campo}); no hay agrupacion ni referencia entre grillas. Exigencia dura
del usuario: todo debe ser CONFIGURABLE desde la UI del disenador (nada de JSON crudo ni SQL).

## Decision

### CAP 1 - Subtotales AGRUPADOS (SUMIF por columna clave) dentro de una grilla

- `FormGridColumn` gana `GroupBy` (id de la columna clave). Si una columna tiene `Agg` != None y `GroupBy`,
  ademas del total completo (rollup) se calculan SUBTOTALES por grupo.
- `FormGridComputation` gana `GroupSubtotals`: `columnaId -> (claveDeGrupoNORMALIZADA -> subtotal)`.
- `FormGridCalculator.Compute` los calcula; helper publico `GroupAggregate(agg, rows, valueCol, groupByCol,
  aggWhen, header)` reutilizable. Clave normalizada por `NormalizeKey` (numero invariante si parsea, si no
  texto en minusculas) -> misma semantica de match que el cross-grid y el resolve.
- options_json de la columna: `"groupBy": "<idColumnaClave>"`.

### CAP 2 - Referencia ENTRE GRILLAS del mismo registro (VLOOKUP / SUMIF cross-grid)

- Nueva config de columna `crossGrid` (record `FormGridCrossRef`): trae un valor desde OTRA grilla del
  MISMO formulario (por su field code). Modos:
  - `vlookup`: empareja la PRIMERA fila origen por `match` (columnaOrigen -> ref de celda de esta fila) y
    devuelve `valueField`.
  - `sumif`: agrega `valueField` (`agg`, default Sum) sobre TODAS las filas origen que emparejan -> SUMIF
    por grupo. Reusa la normalizacion de clave de CAP 1.
- Resolver PURO `FormGridCrossRefResolver.Resolve(cross, sourceRows, currentRow, header)`; sin BD, lo llaman
  el recalculo del servidor (autoritativo) y el del cliente.
- options_json de la columna:
  `"crossGrid": {"grid":"lineas_apu","mode":"sumif","valueField":"parcial","agg":"Sum","match":{"grupo":"{grupo}"}}`.

### Orden de dependencia entre grillas (pendiente de fase 2)

El recalculo (cliente y servidor) debe ordenar las grillas por DEPENDENCIA: una grilla que tiene una columna
`crossGrid` hacia otra grilla se recalcula DESPUES de esa (grafo topologico; ciclos -> se corta y se deja la
ultima pasada). Hoy el recalculo es una sola pasada en orden de preguntas.

## Estado por fase

- FASE 1 (este commit): MOTOR. `FormGridColumn.GroupBy`, `FormGridComputation.GroupSubtotals`,
  `FormGridCalculator.Compute`/`GroupAggregate`/`NormalizeKey`, `FormGridCrossRef` + parse (`crossGrid`),
  `FormGridCrossRefResolver`. Verificado determinista contra la aritmetica del APU (SUMIF APU->Margen,
  APU->Oferta; VLOOKUP). Sin migracion (todo en options_json).
- FASE 2 (v0.15.112, este commit): CABLEADO. Nuevo `FormGridDependency.Order` (Kahn estable: grillas
  referenciadas primero; ciclos/aristas ausentes se ignoran y quedan al final en orden de entrada). El
  recalculo del servidor (`FormResponseService`) y el del cliente (`DynamicFormRenderer.RecomputeGrids`)
  ahora: (a) arman el meta de cada grilla (columnas + extras), (b) ordenan por dependencia cross-grid,
  (c) resuelven las columnas `crossGrid` (`FormGridCrossRefResolver`) contra las filas YA computadas de la
  grilla origen antes de correr `Compute`, (d) guardan las filas por field code para las grillas que las
  referencian. UI del disenador (`FormDesigner.razor`, editor de columnas de grilla): selector "Agrupar
  subtotales por" (CAP 1, solo si la columna tiene Agg) y bloque "Traer de otra grilla (cross-grid)" con
  grilla origen, modo (SUMIF/VLOOKUP), columna a traer, operacion (para SUMIF) y 1 par de emparejamiento
  (columna origen = valor de esta fila). Helpers `SetGridColumnGroupByAsync`, `SetGridCrossGridAsync`,
  `PatchGridCrossFieldAsync`, `PatchGridCrossMatchAsync` (mutan `groupBy`/`crossGrid` del options_json;
  `RawColumnExtras` los preserva en el round-trip de columnas). Nada de JSON crudo ni SQL. Sin migracion.
- FASE 3 (CAP 3): render agrupado (bloques por grupo + filas de subtotal) sobre RenderGrid.

## Consecuencias

- Todo persiste en el OptionsJson de la pregunta GridDetail (mismo store); sin esquema nuevo ni migracion.
  El servidor sigue siendo la fuente de verdad del calculo. Multi-tenant intacto.
- El match/agrupacion es EXACTO con normalizacion unica (numero o texto case-insensitive), consistente con
  el resolve existente.

## Pendientes / supuestos

- Fase 3 (CAP 3): render agrupado (bloques por grupo + filas de subtotal).
- SUMIF/VLOOKUP con clave COMPUESTA soportado por el resolver (match multi-columna); la UI de fase 2 expone
  solo 1 clave (el motor acepta varias si se editan a mano o en una futura UI).
- Ciclos entre grillas: `FormGridDependency.Order` los corta (una sola pasada ordenada, sin iteracion a punto
  fijo); los nodos en ciclo quedan al final en orden de entrada. No hay tope de pasadas porque no se itera.
