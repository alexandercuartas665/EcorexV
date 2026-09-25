# ADR-0111: Tabla del renderizador de reportes - formato de fecha, scroll/sticky y export a Excel con valores reales

**Status:** Accepted
**Date:** 2026-09-25
**Deciders:** Alexander Cuartas (owner), agente de desarrollo
**Extiende:** ADR-0066 (renderizador generico de paneles por spec), ADR-0068 (fuente de reporte por formulario)

## Contexto

El reporte "Registro de cotizaciones (GESTION COMERCIAL)" (tenant AGROMETALICAS, ~129
filas x 10 columnas, fuente `form:{code}`) quedaba inservible por tres motivos del MOTOR
del renderizador (no del spec del reporte):

1. **Fechas ilegibles.** Las columnas de CAMPO DIRECTO (sin `Agg`) se mostraban con
   `PanelDataEngine.Norm(raw)`, que ignora `col.Format`. Una fecha `DateTimeOffset`
   se renderizaba como `09/18/2026 19:26:54 +00:00` (con offset y en formato US).
2. **Tabla incomoda.** `<table class="rpt-table">` sin contenedor de scroll: una tabla
   ancha/larga empujaba el scroll horizontal a TODA la pagina y el encabezado se perdia
   al bajar.
3. **Export a Excel como texto.** El export ya existia (`SpecPanelRenderer.BuildExcelBytes()`
   + boton "Generar Excel" en la galeria, via ClosedXML `ReportExcelExport`), PERO para
   las tablas capturaba las celdas YA formateadas (strings). En Excel los numeros y fechas
   entraban como texto y no se podian ordenar ni sumar.

## Decision

Tres cambios acotados al motor, sin tocar el contrato del spec ni la BD:

1. **Formato de fecha en columnas.** Se agregan los formatos validos `date` (`yyyy-MM-dd`)
   y `datetime` (`yyyy-MM-dd HH:mm`) a `PanelSpecValidator.KnownFormats`. Se agrega
   `PanelDataEngine.FormatValue(object? raw, string? format)`: aplica `col.Format` a
   cualquier valor crudo (fechas via `AsDate`, numericos via `Format`), con cultura
   invariante; si el valor no encaja con el formato, cae a `Norm` (texto); `null` -> celda
   vacia. El renderer usa `FormatValue` para las columnas de campo directo (antes `Norm`).

2. **Aspecto de tabla.** La `<table>` se envuelve en `<div class="rpt-table-wrap">`
   (`overflow:auto; max-height:60vh; border; radius`). El encabezado es sticky
   (`thead th { position:sticky; top:0; background:#fff; ... }`), la tabla toma el ancho
   de su contenido (`width:auto; min-width:100%`) y las celdas no parten linea
   (`white-space:nowrap`). El scroll (vertical y horizontal) queda DENTRO del contenedor;
   la pagina nunca scrollea en horizontal. El componente `rpt-*` es siempre claro (todos
   los fondos son `#fff` fijos, sin tokens dark), asi que el header sticky usa `#fff` como
   el resto del panel.

3. **Export con valores reales.** Se agrega `PanelDataEngine.ExportValue(object? raw, string? format)`
   que devuelve el valor NATIVO (numero/fecha) segun `col.Format`. El renderer captura, en
   el MISMO recorrido de la tabla, dos filas paralelas: la de texto (lo que ve la tabla) y
   la de export (valores reales). `ReportExcelExport.SetCell` ahora maneja `DateTime`/
   `DateTimeOffset` para escribirlos como fecha real. El boton ya existente exporta las
   filas ACTUALES (respeta los filtros del tablero: usa `_filtered`), una hoja por widget de
   tabla/serie/matriz + hoja de indicadores; el nombre del archivo es `{reporte} {yyyy-MM-dd}.xlsx`.

## Alternativas consideradas

- **Convertir la fecha a la zona del tenant en el display.** Descartado: el spec pide
  `yyyy-MM-dd`/`yyyy-MM-dd HH:mm` sin conversion; se muestra el valor tal cual (UTC) para
  no introducir sorpresas. Si se requiere zona del tenant, sera un formato adicional.
- **Mostrar el boton de Excel solo si hay >=1 widget de tabla.** Se dejo el boton siempre
  disponible en el menu de Acciones (ya exportaba KPIs + series + tablas), con guardas: solo
  en la vista Tablero y con mensaje amable si no hay datos con los filtros actuales. Es mas
  util que restringirlo y no obliga al padre a conocer los widgets del renderer.

## Consecuencias

- Las columnas de fecha de cualquier reporte por spec ahora son legibles con `Format:"date"`
  o `"datetime"`, sin cambios de codigo por reporte.
- Las tablas anchas/largas son navegables sin romper el layout de la pagina.
- El Excel exportado es ORDENABLE y SUMABLE (numeros como numero, fechas como fecha).
- Cambio de motor, reutilizable por todos los paneles `rpt-*`; no altera el JSON del spec.

## Action Items

1. [x] `FormatValue`/`ExportValue` + `date`/`datetime` en `KnownFormats`.
2. [x] Aplicar `col.Format` a columnas de campo directo en `SpecPanelRenderer`.
3. [x] Wrap + sticky + nowrap + width:auto en `.rpt-table`.
4. [x] Captura de export con valores reales + `SetCell` maneja fechas.
5. [x] Tests: `FormatValue`/`ExportValue`, export de fecha/numero real, `date`/`datetime` en el validador.
6. [ ] (Config, no codigo) Marcar el form COT "REGISTRO COTIZACIONES" como reportable en prod.
