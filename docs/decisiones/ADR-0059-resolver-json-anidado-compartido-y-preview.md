# ADR-0059 - Resolver de rutas JSON anidadas compartido y preview del mapeo (Config API /run)

- Estado: Aceptado
- Fecha: 2026-08-03
- Contexto: ECOREX.tareas, `apps/backend/src/Ecorex.Application/DataContainers` y la Config API
  (`apps/backend/src/Ecorex.SuperAdmin/Endpoints/ConfigApiEndpoints.cs`). Sigue a ADR-0058.

## Contexto y problema

El `/run` de la Config API despacha el importador REST server-direct
(`ConnectorRunPlanner` -> `ApiImportService.ImportAsync`). Ese importador resolvia el mapeo
columna->campo con `JsonElement.TryGetProperty(field)`, que SOLO mira el primer nivel del objeto.
Pero las rutas del mapeo de un conector son ANIDADAS/INDEXADAS: `id_type.name`, `name[0]`,
`address.city.city_name`, `phones[0].number`, `contacts[0].email`, `metadata.created`. Con
`TryGetProperty` esas rutas quedaban VACIAS. En modo **Upsert** el vacio SOBRESCRIBIA la data
existente (borrado silencioso). Reproducible con el conector Siigo
`019fc83c-5876-744c-a717-9a448da0b281` (AGROMETALICAS). El runner via AGENTE Colmena no tenia el
bug: su `RestExecutor` ya aplana rutas con `Ecorex.Agent.Core.Services.RestJson` (TryResolve +
Scalar), que resuelve `a.b.c`, `arr[0]`, `arr[0].x`, objeto-indexado y clave vacia.

## Decision

### 1. Resolver anidado en el importador in-process (replica, no referencia)

Se agrega `NestedJsonResolver` en `Ecorex.Application` con la logica de resolucion de rutas
IDENTICA a la del agente (`TryResolve` + `Scalar` + `ParseSegments`), mas un `ProjectRow` que
materializa una fila (ruta -> valor escalar). `ApiImportService.ImportAsync` deja de usar
`TryGetProperty` plano y proyecta cada elemento con `NestedJsonResolver.ProjectRow`.

Se REPLICA en vez de compartir por referencia de proyecto porque hoy no es viable una unica
referencia:

- El agente vive en OTRA solucion (`apps/agent`), apunta a `net10.0-windows` (store DPAPI, crypt32)
  y NO forma parte de `apps/backend/Ecorex.sln`.
- `Ecorex.Application` es `net10.0` multiplataforma y corre en la matriz dual de CI (incluye Linux):
  referenciar `Ecorex.Agent.Core` arrastraria el TFM Windows.
- Al reves, que el agente referencie `Ecorex.Application` arrastraria EF Core al agente.

La replica mantiene UNA sola SEMANTICA (mismos casos: dot-path, indice, indice+dot, ausencia -> no
resuelve, JSON null -> resuelve con valor null). Follow-up (ver abajo): promover el helper a un
proyecto leaf (`Ecorex.Shared`) al que ambas soluciones puedan apuntar y borrar la copia del agente.

### 2. Regla de no-sobrescribir-con-vacio en Upsert

`NestedJsonResolver.ProjectRow` OMITE del diccionario de la fila las rutas que NO resuelven (no
escribe cadena vacia). El nucleo de ingesta (`RowIngestService`, sesion Upsert) ahora, para una fila
existente, SALTA los campos ausentes del diccionario (`if (!src.ContainsKey(field)) continue;`): asi
conserva el valor existente en vez de borrarlo. Distincion deliberada: una ruta que resuelve a JSON
`null` SI viaja en la fila (con valor `null`) y limpia la celda (borrado explicito del origen), a
diferencia de la ruta ausente. El runner via agente no cambia: su `MergeRow` siempre incluye todas
las columnas mapeadas, de modo que el nuevo `continue` nunca se dispara para el (comportamiento
preservado).

### 3. Preview del mapeo con VALORES aplicados

Nuevo endpoint `POST /api/config/connectors/{id}/preview` (mismo tenant-scoping + Bearer que el
resto de `/api/config`). Como el probe, hace el GET (auth + headers + una pagina), pero para la
PRIMERA fila de muestra devuelve el mapeo YA aplicado columna -> valor resolviendo cada ruta con
`NestedJsonResolver`, e indica por columna si la ruta `resolved`. Asi el bug (ruta que queda vacia)
se ve ANTES del run. Respaldado por `IApiImportService.PreviewAsync` +
`ApiPreviewResult`/`ApiPreviewField`. El endpoint reusa `ConnectorRunPlanner.Build` (modo Append,
sin clave) para obtener el mapeo persistido del conector y lo invierte a columna(nombre) -> ruta.

## Consecuencias

- El `/run` server-direct queda desbloqueado para conectores con rutas anidadas (Siigo).
- Upsert ya no borra data cuando una ruta no resuelve.
- El preview permite validar el mapeo sin escribir nada.
- Persiste una duplicacion controlada del resolver (agente vs Application) hasta el follow-up.

## Follow-up (B) - despachar el /run al agente Colmena conectado (disenado, no implementado)

Objetivo del usuario: que el `/run` despache al agente Colmena conectado del tenant cuando exista
(su `RestExecutor` ya resuelve rutas anidadas y corre en la LAN del cliente), enviando el
`RestFetchSpec`; con fallback a server-direct (ya con el fix de este ADR). Diseno propuesto:

1. En el endpoint `/run`, si el tenant tiene un agente activo/presente (via `IAgentClientService` +
   ultima actividad, como ya lo calcula `GET /agents`), construir el `RestFetchSpec` (igual que
   `ProcessRunner.BuildRestSpec`) y llamar `IAgentImportService.DispatchFetchAsync` con
   `correlationId` propio; registrar la corrida en `ConfigRunStore`/bitacora ANTES de despachar.
2. Si no hay agente, usar la ruta server-direct actual (`ApiImportService.ImportAsync`).
3. Unificar el resolver: mover `NestedJsonResolver`/`RestJson` a un proyecto leaf (`Ecorex.Shared`,
   `net10.0`) referenciado por `Ecorex.Application` y por `Ecorex.Agent.Core`, eliminando la copia.

No se implementa aqui por alcance/riesgo; queda como siguiente paso.
