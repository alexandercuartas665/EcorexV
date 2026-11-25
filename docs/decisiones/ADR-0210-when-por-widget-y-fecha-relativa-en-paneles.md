# ADR-0210: When por WIDGET y fecha relativa (lastDays) en el motor de paneles

**Estado:** Aceptado
**Fecha:** 2026-11-24
**Decisores:** sesion de reportes (panel "Pipeline comercial - monto por estado", GESTION COMERCIAL), ECOREX.desarrollo
**Extiende:** ADR-0066 (renderizador por spec), ADR-0068 (Where/When), ADR-0089 (percentOfTotal)

## Contexto

En un panel mixto (todas las etapas del tablero) se quieren graficas de un SUBCONJUNTO (solo Etapa = Cierre)
y una de ellas de "los ultimos 7 dias". Hoy todos los widgets agregan el mismo `_filtered` (Where global +
filtros de UI); solo los KPIs tienen `When`. Y `PanelWhere.Value` es literal: no hay fecha rodante.

## Decision

1. **`PanelWidget.When`** (`List<PanelWhere>`, mismo formato que `PanelKpi.When`): el widget agrega solo las
   filas del conjunto filtrado que cumplen todas las condiciones (AND). Vacio = todo el panel (compatible).
   Aplica a line/bar/donut/pareto/matrix/table. `PanelDataEngine.Subset` encapsula el filtro (testeable).
2. **Operador `lastDays`** en `PanelWhere` (opcion (a) del pedido, mas legible que un token `@today-7d`):
   `{Field, Op:"lastDays", Value:"7"}` = la fecha cae en los ultimos N dias CALENDARIO del tenant contando
   hoy (7 = hoy + 6 anteriores; fechas futuras no cuentan). Se evalua en la zona del tenant
   (`IAnalisisZonaHoraria`, la misma de Analisis/Bitacora). Una fecha sin hora (medianoche UTC, campos de
   solo fecha) se toma como dia calendario sin convertir, para no correrla al dia anterior en Bogota. Sirve
   en Where global, When de KPI y When de widget.
3. **`SortDim` en bar** (mejora opcional pedida): `"asc"`/`"desc"` ordena por la dimension (dias/meses en
   orden cronologico); sin SortDim sigue por valor desc (los paneles actuales no cambian). Con SortDim, TopN
   no recorta (igual que line).
4. **Validador**: acepta `lastDays` (exige entero > 0) y valida los campos/operadores del When de cada widget.

## Consecuencias

- Graficas por subconjunto sin crear otro reporte; specs existentes se comportan igual.
- Los buckets derivados (`Derive` year/yyyymm/date) siguen usando la fecha tal como llega (UTC para
  timestamptz): un cierre a las 20:00 de Bogota cae en el dia UTC siguiente al agrupar por "date". No se
  cambio aqui para no alterar paneles existentes; si molesta, evaluar derivados en la zona del tenant.
- No se agrego control de filtro de UI `lastdays` (no hacia falta para el caso; queda como evolucion).
- Sin migracion; tenant-safe intacto (no toca consultas, solo el pivoteo en memoria).
