# ADR-0066 - Renderizador de panel GENERICO por spec (autonomia de reportes sin codigo)

- Estado: PROPUESTA (2026-08-12). Solicitado por la SESION DE REPORTES (worktree informes/siigo) para
  dejar de depender de la sesion de codigo + un deploy por cada panel nuevo.
- Fecha: 2026-08-12
- Rama / worktree: propuesta desde `feat/reporte-siigo-agro`; se implementa sobre `fase-0/clon-backbone`.
- Relacionado: [ADR-0051 Motor de Reportes], [ADR-0062 plantillas entre tenants], [ADR-0063/0064
  conector externo]; paneles de referencia: OcsDashboardPanel, TareasDashboardPanel, SiigoVentasDashboardPanel.

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
  "join": { "mainKey": "Cliente NIT", "lookup": "clientes" },     // main.CladeNIT -> clientes.Identificacion
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
    { "type": "line",   "title": "Ventas mensuales (M)", "dim": "Mes", "agg": "sum", "field": "Total", "scale": 1000000, "width": "full", "sortDim": "asc" },
    { "type": "pareto", "title": "Pareto de clientes",   "dim": "ClienteNombre", "agg": "sum", "field": "Total", "scale": 1000000, "topN": 20, "cumulative": true, "width": "full" },
    { "type": "bar",    "title": "Por vendedor (M)",     "dim": "Vendedor", "agg": "sum", "field": "Total", "scale": 1000000, "topN": 10, "orientation": "horizontal" },
    { "type": "donut",  "title": "Por estado DIAN",      "dim": "Estado DIAN", "agg": "count" },
    { "type": "matrix", "title": "SO x Bits",            "rowDim": "X", "colDim": "Y", "agg": "count", "heatmap": true },   // caso OCS/Tareas
    { "type": "table",  "title": "Top 15 clientes", "groupBy": "ClienteNombre", "topN": 15,
       "columns": [ {"label":"Cliente","field":"ClienteNombre"}, {"label":"Facturas","agg":"count"},
                    {"label":"Ventas","agg":"sum","field":"Total","format":"money"} ] }
  ]
}
```

Cubre lo de los 3 paneles: KPIs (suma/conteo/distinct/promedio + formato dinero/M/%/int), serie temporal
(bucket mes/anio), **pareto** (barra + acumulado doble-eje), barra top-N (horizontal), dona, **matriz**
cruzada con heatmap, tabla con columnas computadas, filtros (dropdown distinct / rango fecha / texto), y
**join/lookup** por clave (codigo -> nombre). Todo se resuelve con UNA consulta tabular por fuente +
pivoteo en memoria (como ya lo hacen los paneles).

### Autonomia de autoria (lo que me libera de la sesion de codigo)

- `SpecPanelRenderer` (componente unico) que recibe el `PanelSpec` y renderiza. La galeria lo despacha
  cuando el `ReportDefinition` es tipo panel con SpecJson de PanelSpec (p.ej. SourceKey `panel:spec` o un
  flag), ademas de los `panel:ocs/tareas/siigo` a medida (fallback).
- Autoria como DATO: en la Galeria (o PlatformAdmin) un "Nuevo panel (spec)": Nombre + editor JSON del
  PanelSpec + Validar (contra el catalogo/contenedores del tenant) + Guardar como `ReportDefinition`
  Kind=Panel (SpecJson=spec). Aparece en la galeria y abre por `SpecPanelRenderer`. Sin recompilar, sin
  desplegar. Reusar el modelo de plantillas (ADR-0062) para ofrecerlo entre tenants.

## Consecuencias

Positivas:
- La sesion de reportes crea/edita/publica paneles como DATOS: **cero codigo, cero deploy** por reporte.
- Misma calidad (mismos bloques CSS/ECharts); el spec captura la logica (agregaciones, pareto, join, formato).
- Convive con paneles a medida (escape para lo ultra-especial): no se pierde nada.

Negativas / costos:
- Inversion inicial: construir el renderizador + el editor/validador de spec (una vez).
- El spec debe cubrir los casos reales; criterio de aceptacion = reproducir OCS/Tareas/SIIGO 1:1.
- Validacion importante: el spec solo referencia campos del catalogo tenant-safe (limite de seguridad).

## Alternativas consideradas

- **Seguir con un componente por reporte**: calidad ok pero cada reporte necesita codigo + deploy
  (dependencia permanente de la sesion de codigo). Rechazada como modelo por defecto.
- **Renderizador generico pobre** (solo 1-2 tipos de grafico): perderia calidad. Rechazada; por eso el
  spec se disena desde los 3 paneles reales y el criterio es reproducirlos 1:1, con fallback a medida.

## Criterio de aceptacion (garantia de calidad)

El renderizador debe **reproducir 1:1 desde spec** los paneles OcsDashboardPanel, TareasDashboardPanel y
SiigoVentasDashboardPanel (incluye pareto con acumulado, join NIT->Nombre, formato en millones, matriz
con heatmap, filtros). Si un widget no se puede reproducir exacto, ese caso queda como componente a medida.
