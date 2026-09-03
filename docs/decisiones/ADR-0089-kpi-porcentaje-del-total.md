# ADR-0089: KPI de porcentaje del total (percentOfTotal) para tasas de conversion

**Status:** Accepted
**Date:** 2026-09-03
**Deciders:** Usuario (Alexander), sesion de reportes (spec), sesion de codigo

> Nota de numeracion: la sesion de reportes lo pidio como "ADR-0069", pero ese numero ya lo ocupa
> otra decision (ADR-0069 formulario del inicio del flujo en el wizard). Se usa el siguiente libre
> real, ADR-0089. Extiende ADR-0066 (renderizador de panel generico por spec) y ADR-0068 (KPI
> condicional por When).

## Contexto

El renderizador generico de paneles (ADR-0066) calcula cada KPI con UNA sola agregacion
(`PanelDataEngine.Aggregate`: sum | count | countDistinct | avg) y la formatea. El KPI condicional
(ADR-0068) permite agregar SOLO el subconjunto que cumple un `When`, pero sigue siendo una sola
agregacion sobre ese subconjunto.

Una **tasa de conversion** ("18% de conversion" = cuanto del pipeline llega a "Cierre") no es una
agregacion: es una RAZON de dos agregados (numerador / denominador). Hoy no se puede expresar como
KPI; solo aparece como tooltip `{d}%` de una dona. El caso real es el panel "Pipeline comercial"
de AGROMETALICAS, que necesita conversion por CANTIDAD (cotizaciones en Cierre / total) y por MONTO
(monto en Cierre / monto total).

## Decision

Nueva agregacion de KPI **`percentOfTotal`** (SOLO KPI, no widgets en este alcance):

- Un KPI con `Agg: "percentOfTotal"` devuelve **numerador / denominador * 100**.
- **Numerador** = agregacion sobre las filas que cumplen `When` (el subconjunto que ya calcula ADR-0068).
- **Denominador** = la MISMA agregacion sobre el conjunto filtrado COMPLETO del panel (sin `When`).
- **Sub-agregacion** (la que se aplica a ambos lados): `sum` si el KPI declara `Field`, `count` si no.
  Asi el mismo verbo cubre "por cantidad" (sin campo) y "por monto" (con campo numerico).
- **Formato**: se reusa `Format: "percent"` (ya existente). El formato lo decide el spec, como en
  cualquier KPI.
- **Denominador 0 -> 0** (sin division por cero; no lanza).

El calculo vive en un helper PURO y testeable del motor:
`PanelDataEngine.PercentOfTotal(numerator, denominator, field)`. El renderer
(`SpecPanelRenderer.BuildKpis`) solo despacha: si `Agg == percentOfTotal`, pasa el subconjunto `When`
como numerador y el conjunto filtrado completo (`_filtered`) como denominador.

Ejemplos (expresables con el `When` que ya existe):

```json
{ "Label": "% Conversion (cant.)", "Agg": "percentOfTotal", "Format": "percent",
  "When": [ { "Field": "Etapa", "Op": "eq", "Value": "Cierre" } ] }

{ "Label": "% Conversion (monto)", "Agg": "percentOfTotal", "Field": "MontoCotizacion", "Format": "percent",
  "When": [ { "Field": "Etapa", "Op": "eq", "Value": "Cierre" } ] }
```

Validacion (`PanelSpecValidator`): `percentOfTotal` se acepta como agregacion valida SOLO en el bloque
de KPIs (no se agrega al set `KnownAggs`, que ya es exclusivo de KPIs pero se mantiene acotado a las 4
agregaciones clasicas para no habilitar el verbo en widgets/columnas por accidente). El `Field` es
OPCIONAL (se valida como `count`: si viene, debe existir en el catalogo; si falta, no se exige). El
`When` se valida como siempre.

## Alternativas consideradas

- **KPI `Agg: "ratio"` con `Numerator`/`Denominator` explicitos** (cada uno `{Agg, Field, When}`): mas
  general (permite numerador y denominador arbitrarios), pero mas verboso y no lo necesita el caso
  actual. `percentOfTotal` cubre exactamente "parte que cumple When / total del panel" con el minimo
  de llaves nuevas (cero: reusa `When`, `Field`, `Format`). Se elige `percentOfTotal`; `ratio` queda
  como extension futura si aparece la necesidad de un denominador con su propio filtro.
- **Extenderlo tambien a widgets/columnas**: fuera de alcance. Un widget ya expresa la razon como serie
  (dona con `{d}%`); el pedido es un KPI headline. Si luego se quiere, es otro cambio.

## Consecuencias

- **+**: la tasa de conversion (y cualquier "% del total") se expresa como KPI, solo con DATO (PanelSpec),
  sin codigo ni deploy por panel. Reusa `When`/`Field`/`Format` existentes: cero llaves nuevas en el JSON.
- **-**: si el autor olvida `Format: "percent"`, el numero sale sin el signo `%` (formato por defecto
  `int`). Se documenta; el resultado sigue siendo correcto (0..100).
- **Alcance acotado**: `percentOfTotal` en un widget cae silenciosamente a `count` (el motor no lo
  reconoce fuera del KPI). Aceptado; el validador no habilita el verbo en widgets.
- **Tenant-safe**: se hereda del panel (numerador y denominador son subconjuntos del mismo `_filtered`
  ya filtrado y tenant-safe). Puro: sin EF ni UI en el calculo.

## Verificacion

Tests unitarios (Application.Tests, sin Docker):
- `PanelDataEngineTests.PercentOfTotal_ByCount_BySum_AndZeroDenominator`: 3/4 = 75% (por conteo),
  18M/22M*100 (por monto), denominador vacio -> 0 con y sin Field.
- `PanelSpecValidatorTests.Kpi_PercentOfTotal_ByCount_And_ByAmount_ProducesNoErrors` y
  `Kpi_PercentOfTotal_OnNonExistingField_IsReported` sobre el catalogo del Pipeline comercial.

Build Release verde (Application + SuperAdmin). Los 2 KPIs de conversion del panel "Pipeline comercial"
de AGROMETALICAS los agrega la sesion de reportes como DATO; este cambio habilita el motor. No desplegado
(el deploy lo corre el usuario).
