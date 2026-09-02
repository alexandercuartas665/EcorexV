# ADR-0066 - Renderizador de panel GENERICO por spec (autonomia de reportes sin codigo)

- Estado: ACEPTADA (2026-08-12). Solicitado por la SESION DE REPORTES (worktree informes/siigo) para
  dejar de depender de la sesion de codigo + un deploy por cada panel nuevo. Implementada sobre
  `fase-0/clon-backbone` (rama `feat/renderizador-panel-spec-adr0066`).
- Fecha: 2026-08-12
- Rama / worktree: propuesta desde `feat/reporte-siigo-agro`; implementada sobre `fase-0/clon-backbone`.
- Relacionado: [ADR-0051 Motor de Reportes], [ADR-0062 plantillas entre tenants], [ADR-0063/0064
  conector externo]; paneles de referencia: OcsDashboardPanel, TareasDashboardPanel/ActivitiesDashboardPanel,
  SiigoVentasDashboardPanel.

## Contexto

Hoy cada panel (OCS, Tareas, SIIGO) es un COMPONENTE Blazor compilado + una linea de despacho en la
galeria (`panel:xxx -> Componente`). Agregar un panel = codigo + recompilar + DESPLEGAR, lo que obliga a
la sesion de codigo y a un deploy cada vez. La parte de DATOS (ReportDefinition, plantillas, concesiones,
contenedores) ya no necesita codigo; lo que falta es que el PANEL mismo sea dato.

Objetivo: que la sesion de reportes cree/edite/publique paneles COMO DATOS (un spec JSON) sin tocar
codigo ni desplegar. Requisito duro del usuario: **NO perder la calidad** de los paneles actuales.

## Decision

Construir UNA sola vez un **renderizador de panel generico** que lee un `PanelSpec` (JSON) y pinta
KPIs/graficas/tabla/filtros usando LOS MISMOS bloques que ya usan los paneles a mano (CSS `rpt-*` +
los mismos patrones de ECharts). A partir de ahi, cada panel es un `ReportDefinition.SpecJson` (dato).

Los dos enfoques CONVIVEN (la galeria ya despacha por SourceKey): generico por spec para el caso comun;
componente a medida como ESCAPE para el reporte ultra-especifico. Asi no se pierde calidad.

### PanelSpec (esquema, derivado de los 3 paneles reales)

```jsonc
{
  "title": "Ventas y Facturacion (SIIGO)",
  "sources": {
    "main":   { "container": "facturas" },
    "lookups": [ { "container": "clientes", "key": "Identificacion", "bring": { "Nombre": "ClienteNombre" } } ]
  },
  "join": { "mainKey": "Cliente NIT", "lookup": "clientes" },     // main.ClienteNIT -> clientes.Identificacion
  "derived": [                                                    // buckets de fecha, etc.
    { "name": "Anio", "from": "Fecha", "op": "year" },
    { "name": "Mes",  "from": "Fecha", "op": "yyyymm" }
  ],
  "filters": [                                                    // se auto-pueblan (distinct) o por tipo
    { "field": "Anio", "control": "dropdown" },
    { "field": "Vendedor", "control": "dropdown" },
    { "field": "Estado DIAN", "control": "dropdown" },
    { "field": "ClienteNombre", "control": "text" }
  ],
  "kpis": [
    { "label": "Ventas",   "agg": "sum",           "field": "Total", "format": "moneyM" },
    { "label": "Facturas", "agg": "count",                           "format": "int" },
    { "label": "Clientes", "agg": "countDistinct", "field": "Cliente NIT", "format": "int" },
    { "label": "Ticket promedio", "agg": "avg", "field": "Total", "format": "money" },
    { "label": "Saldo",    "agg": "sum",           "field": "Saldo", "format": "moneyM" }
  ],
  "widgets": [
    { "type": "line",   "title": "Ventas mensuales (M)", "dim": "Mes", "agg": "sum", "field": "Total", "scale": 1000000, "width": "full" },
    { "type": "pareto", "title": "Pareto de clientes",   "dim": "ClienteNombre", "agg": "sum", "field": "Total", "scale": 1000000, "topN": 20, "cumulative": true, "width": "full" },
    { "type": "bar",    "title": "Por vendedor (M)",     "dim": "Vendedor", "agg": "sum", "field": "Total", "scale": 1000000, "topN": 10, "orientation": "horizontal" },
    { "type": "donut",  "title": "Por estado DIAN",      "dim": "Estado DIAN", "agg": "count" },
    { "type": "matrix", "title": "SO x Bits",            "rowDim": "X", "colDim": "Y", "agg": "count", "heatmap": true },   // caso OCS/Tareas
    { "type": "table",  "title": "Top 15 clientes", "groupBy": "ClienteNombre", "topN": 15,
       "columns": [ {"label":"Cliente","field":"ClienteNombre"}, {"label":"Facturas","agg":"count"},
                    {"label":"Ventas","agg":"sum","aggField":"Total","format":"money"} ] }
  ]
}
```

Cubre lo de los 3 paneles: KPIs (suma/conteo/distinct/promedio + formato dinero/M/%/int), serie temporal
(bucket mes/anio), **pareto** (barra + acumulado doble-eje), barra top-N (horizontal), dona, **matriz**
cruzada con heatmap, tabla con columnas computadas, filtros (dropdown distinct / rango fecha / texto), y
**join/lookup** por clave (codigo -> nombre). Todo se resuelve con UNA consulta tabular por fuente +
pivoteo en memoria (como ya lo hacen los paneles).

### Autonomia de autoria (lo que libera de la sesion de codigo)

- `SpecPanelRenderer` (componente unico) que recibe el `PanelSpec` y renderiza. La galeria lo despacha
  cuando el `ReportDefinition` es tipo panel con SpecJson de PanelSpec (SourceKey `panel:spec`), ademas
  de los `panel:ocs/system-activities` a medida (fallback).
- Autoria como DATO: en la Galeria un "Nuevo panel": Nombre + editor JSON del PanelSpec + Validar
  (contra el catalogo/contenedores del tenant) + Guardar como `ReportDefinition` Kind=Panel
  (SpecJson=spec). Aparece en la galeria y abre por `SpecPanelRenderer`. Editar/Duplicar/Archivar desde
  el visor. Sin recompilar, sin desplegar. Reusa el modelo de plantillas (ADR-0062) para ofrecerlo entre
  tenants (una `ReportTemplate` Kind=Panel con SourceKey `panel:spec` + SpecJson se activa por
  compatibilidad de contenedor y la instancia la pinta el mismo renderizador).

## Implementacion (2026-08-12)

Piezas nuevas:

- `Ecorex.Application/Reporting/Panels/PanelSpec.cs` - DTO del spec (JSON, enums como texto).
- `Ecorex.Application/Reporting/Panels/PanelSpecValidator.cs` - validador PURO contra el catalogo
  tenant-safe (resuelve fuentes por nombre de negocio; comprueba join/lookup/derivados/filtros/kpis/widgets).
- `Ecorex.Application/Reporting/Panels/PanelDataEngine.cs` - motor de pivoteo EN MEMORIA (join,
  derivados, agregaciones sum/count/countDistinct/avg, group-aggregate con escala/topN/orden, pareto con
  acumulado, matriz cruzada, tabla agrupada, formato dinero/M/%/int). Es el nucleo NUMERICO y es testeable
  sin Docker ni Blazor.
- `Ecorex.SuperAdmin/Components/Shared/Reporting/EChartBuilders.cs` - builders de "option" de ECharts
  extraidos (Line, VerticalBar, HorizontalBar, Donut, Pareto) para que paneles a medida y generico usen
  el mismo option.
- `Ecorex.SuperAdmin/Components/Shared/Reporting/SpecPanelRenderer.razor(.css)` - el UNICO componente.
- `IReportDefinitionService`: `GetSpecJsonAsync`, `ValidatePanelSpecAsync`, `SavePanelSpecAsync`,
  `UpdatePanelSpecAsync`, `DuplicatePanelSpecAsync` (tenant-scoped, validan contra el catalogo).
- `ReportGallery.razor`: despacho `panel:spec -> SpecPanelRenderer` (fallback vivo a medida) + modal
  "Nuevo panel" con Validar/Guardar + Editar/Duplicar/Archivar.

Migraciones: NINGUNA. Se reusa `ReportDefinition.SpecJson`/`SourceKey` existentes (sin flag nuevo).

### Que reproduce 1:1 y que queda como fallback

- **1:1 (numerico y visual):** KPIs (sum/count/countDistinct/avg + money/moneyM/percent/int); serie
  temporal (bucket yyyymm); **pareto con acumulado %** (topN + gran total sobre todas las categorias);
  barra top-N horizontal; dona; **matriz cruzada con heatmap**; tabla con columnas computadas; **join
  NIT->Nombre**; **formato en millones** (scale 1000000); filtros dropdown/daterange/text que recalculan
  en memoria. El SIIGO se reproduce 1:1 con el spec del apendice.
- **Fallback documentado:** los KPIs CONDICIONALES del panel de Tareas
  (`ActivitiesDashboardPanel`: "Abiertas" = Pending/Active/InProgress, "Cerradas" = Done/Closed,
  "Suspendidas") NO son expresables como una agregacion simple sobre un campo, asi que el spec de tareas
  reproduce los graficos (dona por estado, barra por prioridad, serie por dia, tabla reciente) y el KPI
  Total, pero para esos KPIs por-grupo se conserva `ActivitiesDashboardPanel` como componente a medida
  (SourceKey `panel:system-activities`, sigue vivo). El orden de filas de la matriz OCS usa el total de
  celda (no el conteo de equipos distintos): los valores y el heatmap coinciden; el orden de filas puede
  variar minimamente. Ambos casos son el ESCAPE previsto por esta ADR.

## Consecuencias

Positivas:
- La sesion de reportes crea/edita/publica paneles como DATOS: **cero codigo, cero deploy** por reporte.
- Misma calidad (mismos bloques CSS/ECharts); el spec captura la logica (agregaciones, pareto, join, formato).
- Convive con paneles a medida (escape para lo ultra-especial): no se pierde nada.

Negativas / costos:
- Inversion inicial: el renderizador + el editor/validador de spec (una vez).
- El spec debe cubrir los casos reales; criterio de aceptacion = reproducir OCS/Tareas/SIIGO 1:1.
- Validacion importante: el spec solo referencia campos del catalogo tenant-safe (limite de seguridad).

## Alternativas consideradas

- **Seguir con un componente por reporte**: calidad ok pero cada reporte necesita codigo + deploy
  (dependencia permanente de la sesion de codigo). Rechazada como modelo por defecto.
- **Renderizador generico pobre** (solo 1-2 tipos de grafico): perderia calidad. Rechazada; por eso el
  spec se disena desde los 3 paneles reales y el criterio es reproducirlos 1:1, con fallback a medida.

## Apendice - specs de ejemplo

Listos para pegar en "Nuevo panel". Los nombres de campo son los DisplayName del catalogo del tenant
(ajustar al nombre real del contenedor). Ver tambien `docs/decisiones/ADR-0066-ejemplos/`.

Ver los 3 archivos JSON (ocs / tareas / siigo) en `docs/decisiones/ADR-0066-ejemplos/`.

## Nota (v0.15.155) - fuentes EXTERNAS como Main del panel

Un PanelSpec puede usar como fuente principal (o lookup) un **dataset EXTERNO** del conector gobernado
(ADR-0064): el panel muestra los datos EN VIVO del servidor ajeno. El unico ajuste fue que
`PanelSpecValidator.FindSource` dejo de excluir `ReportSourceKind.External`; el resto ya estaba:
`ReportDataSource.QueryAsync` despacha External a `ExternalReportReader` (concesion + descifrado en
memoria + solo lectura) e `IReportCatalog` publica las externas concedidas/propias del tenant.

- Los campos salen de `fields_json` del ExternalDataSet; se declaran `CanFilter/Group/Aggregate=false`
  porque el panel filtra/agrupa/lista EN MEMORIA sobre las filas en vivo (como con cualquier Main).
- Parametros: el dispatch corre con inputs=null, asi que los parametros Input toman su `DefaultValue`
  (p.ej. `@limite`) y los Context vienen del contexto de confianza (tenant/usuario). El tope duro de
  filas lo aplica `ExternalQuery.MaxRows`.
- Tenant-safe: el catalogo solo expone datasets propios/concedidos; la cadena de conexion nunca se expone.
- Colision de nombre: las externas van al FINAL del catalogo, asi que ante DisplayName repetido gana la
  fuente nativa/contenedor previa (los paneles existentes no cambian).
