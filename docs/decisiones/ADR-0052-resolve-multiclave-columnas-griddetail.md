# ADR-0052 - Auto-resolucion multi-clave (VLOOKUP) en columnas de GridDetail

- Estado: Aceptado
- Fecha: 2026-07-31
- Contexto: Motor de formularios (Formularios avanzados). Cotizador de lamina (AGROMETALICAS).

## Contexto

El lookup de columna existente (ADR previo, ola F1) autollena una fila cuando el usuario ELIGE un valor
en una columna (`lookup` + `autofill` en `options_json`, dirigido por seleccion). El cotizador necesita
algo distinto: columnas de precio (corte, doblez, rolado) que se CALCULAN solas a partir de OTRAS celdas
ya llenas de la misma fila, matcheando una fila de una tabla de tarifas (Contenedor de datos) por 1+
claves y devolviendo una columna. Es el patron VLOOKUP/INDEX-MATCH, que el motor no soportaba.

## Decision

Se agrega una llave **`resolve`** al `options_json` de una columna de GridDetail:

```json
{ "id": "precio_corte", "type": "text",
  "resolve": {
    "source": "DataContainer", "sourceRef": "<containerId>",
    "match": { "Lamina": "{tipo_lamina}", "Espesor": "{espesor}" },
    "return": "Precio",
    "when": { "{rolado}": "SI" }            // opcional: solo resuelve si se cumple (exacto)
  } }
```

- `match`: columnaDeLaFuente -> ref de celda de la MISMA fila (`{campo}`) o literal. TODAS deben coincidir.
- `return`: columna de la fuente que se devuelve.
- `when` (opcional): guarda por valor exacto; si no se cumple, la celda queda vacia (0 en el calculo).
- Reusa la capa de lookup (`IFormLookupService` / `DataContainerLookupSource`); el match compuesto y el
  return son lo nuevo (`IFormLookupSource.MatchAsync`, default no-soportado). Tenant-safe por el filtro global.

### Match EXACTO (decision del usuario, 2026-07-31)

El match es **exacto**: numerico si ambos lados parsean como numero (`3 == 3.0`), si no texto
case-insensitive (asi `"<3" == "<3"`). Consecuencia operativa: el campo que aporta la clave (p.ej.
`espesor`) debe traer el valor LITERAL del tarifario (lista de umbrales `<3, 3, 4.5, ...`), no un espesor
real arbitrario. Se descarto el aproximado ("nearestNotExceeding") y los rangos desde/hasta.

## Consecuencias

- Read-only en la UI (como las columnas `calc`): se re-resuelve al cambiar sus dependencias de la fila
  (`SetGridCell`) y al cargar/restaurar un borrador (`ResolveAllGridsAsync`), antes del calculo.
- El SERVIDOR es autoritativo: `FormResponseService` re-resuelve antes de `FormGridCalculator.Recompute`
  al guardar; el cliente no es fuente de verdad para los precios.
- Sin cambio de esquema (todo vive en `options_json`): la sesion de DATOS engancha los `resolve` del COT
  sin recompilar.
- No debilita el sandbox del evaluador de formulas (el `resolve` es una consulta, no ejecucion de codigo).
