# ADR-0061 - Despacho de conectores RestApi via agente Colmena en la Config API

- Estado: Aceptado
- Fecha: 2026-08-03
- Contexto: ECOREX.tareas, `apps/backend`. Follow-up B de ADR-0059/0060. Sigue a ADR-0054 (conector
  RestApi con headers arbitrarios + TokenExchange) y a ADR-0060 (scheduling en la Config API,
  corrida programada server-direct).

## Contexto y problema

ADR-0060 dejo los conectores RestApi corriendo SIEMPRE server-direct (el servidor hace el GET via
`ConnectorRunPlanner` + `IApiImportService.ImportAsync`) y ELIMINO del `ProcessRunner` la rama
RestApi-via-agente junto con su helper `BuildRestSpec`. Eso cubre bien las APIs alcanzables desde el
servidor (caso Siigo, API publica), pero cerro explicitamente el hueco de una API REST que solo sea
alcanzable desde la LAN del cliente: para esas, el GET debe hacerlo un agente Colmena on-prem.

Ademas, entre-tanto el `RestExecutor` del agente gano soporte de headers arbitrarios + TokenExchange
(commit `12adfc4`), asi que el agente YA sabe ejecutar el `RestFetchSpec` COMPLETO (login de 2 pasos +
Partner-Id + paginacion + rutas anidadas). Lo que faltaba era volver a ARMAR ese spec en el servidor y
volver a despacharlo, esta vez en su version post-TokenExchange, y exponerlo por la Config API.

El caso motor es el conector Siigo de AGROMETALICAS: se quiere poder programarlo (o dispararlo) para
que lo ejecute el agente `cli_...`, reconciliando por "Siigo Id" (Upsert), sin duplicar.

## Decision

### 1. Re-habilitar el camino RestApi-via-agente como OPCION (no como reemplazo)

`ProcessRunner.RunNowAsync` ramifica el RestApi por presencia de cliente remoto:

- **RestApi + `process.ClientId` puesto -> via agente**: nuevo `RunRestViaAgentAsync`. Arma el
  `RestFetchSpec` COMPLETO, descifra la credencial del login (viaja en `ConnectorSpec.Secret`,
  ADR-0040) y despacha por el hub con `IAgentImportService.DispatchFetchAsync`. Simetrico al camino
  Database: abre la corrida en la bitacora ANTES de despachar y, si el agente esta offline, la marca
  `PendingOffline` + parquea (`PendingSince`) para reintentar al reconectar.
- **RestApi sin `ClientId` -> server-direct**: sin cambios, el camino de ADR-0060.

No se re-introduce el modo fijo `Replace` de la vieja rama: el disparo via agente honra el
`Mode`/`KeyColumn` PERSISTIDOS del proceso, igual que el server-direct.

### 2. `RestSpecBuilder`: armado del `RestFetchSpec` COMPLETO (restaurado y completado)

El viejo `BuildRestSpec` (privado del runner, eliminado en ADR-0060) se restaura como clase propia
`Ecorex.SuperAdmin.Agents.RestSpecBuilder` (publica, unit-testeable sin hub ni BD). Arma el spec ENTERO
desde el conector persistido, SIN duplicar el modelo de mapeo:

- `baseUrl`, `arrayPath`, `paging`, `fields` (mapeo ANIDADO: `name.0`, `address.city.name`) salen de
  `connector.MappingJson`, que es el MISMO `RestFetchSpec` que escribe la Config API y que lee
  `ConnectorRunPlanner` para el server-direct.
- `baseUrl`/`httpMethod`/`authKind` caen a los campos normales del conector
  (`EndpointUrl`/`HttpMethod`/`AuthKind`) si el JSON no los trae.
- `Headers` (ej. `Partner-Id`) y `TokenExchange` (login de 2 pasos) son autoritativos desde las
  columnas dedicadas `HeadersJson`/`TokenExchangeJson` (reusa `ConnectorRestConfig.Parse*`).

La diferencia con la version pre-TokenExchange que quedo muerta: aquella no poblaba `Headers` ni
`TokenExchange` de forma completa. Esta si, por lo que el agente puede autenticar Siigo (login ->
`access_token` -> `Authorization: Bearer`) y enviar `Partner-Id` en toda llamada.

### 3. Reconciliacion identica por agente (mismo `RowIngestService`)

Los chunks que devuelve el agente (`FetchResult`) se ingieren con el MISMO `IRowIngestService` que el
server-direct. `DispatchFetchAsync` recibe el `mode` + `keyColumnId` del proceso, y
`AgentImportService.OnFetchResultAsync` los pasa a `CreateSession(...)`. La convencion del agente es
`mapping` = columnaId -> NOMBRE de columna (el agente ya aplico el mapeo campo->columna del spec, asi
que sus filas vienen indexadas por nombre de columna); el `keyColumnId` es el Guid de la columna clave.
Resultado: Upsert por "Siigo Id" reconcilia (update de existentes, insert de nuevos) sin duplicar,
exactamente como el server.

### 4. Endpoints: `clientId`/`agent` OPCIONAL en `/schedule` y `/run`

En `ConfigApiEndpoints` (tenant-scoped por el Bearer opaco, auditados):

- `PUT /api/config/connectors/{id}/schedule` acepta ademas `clientId`/`agent`. Si viene, se resuelve el
  `DataClient` del tenant y su Guid se guarda como `ImportProcess.ClientId` (antes fijo en null); sin
  el, sigue server-direct. El scheduler lo dispara luego por `ProcessRunner` (camino via agente).
- `POST /api/config/connectors/{id}/run` acepta ademas `clientId`/`agent`. Si viene (y el conector es
  RestApi), el run NO corre server-direct: arma el `RestFetchSpec` completo y despacha por el hub; la
  ingesta async aterriza en el contenedor con el mismo Mode/KeyColumn. Devuelve `202` con el
  `correlationId` (`status="dispatched"`). Si el agente esta offline -> `409` claro.

`ResolveClientAsync` acepta el `clientId` en tres formas: el Guid de la fila, el ClientId publico
(`cli_...`) o el nombre del agente. Si no hay match -> `404`. El `ScheduleView` y la auditoria ahora
reflejan el `clientId`/`clientName`.

## Consecuencias

- Un conector RestApi que solo es alcanzable desde la LAN del cliente vuelve a poder ejecutarse, ahora
  con auth de 2 pasos + headers (lo que la vieja rama no cubria).
- Server-direct y via-agente comparten el modelo de mapeo (`RestFetchSpec` en `MappingJson`) y el
  nucleo de ingesta (`RowIngestService`): la reconciliacion Upsert es identica por ambos caminos.
- El operador elige el motor por dato (poner o no `clientId`), no por codigo. Sin `clientId` todo sigue
  como en ADR-0060 (server-direct): cambio retro-compatible.
- El `/run` via agente es asincrono (el resultado llega por el hub); su `ConfigRunStore` sincrono no
  aplica, se devuelve el `correlationId`. La verdad duradera de un run programado sigue en la bitacora
  (`ImportRun`), que el camino via agente cierra por `correlationId`.
- No hubo migracion: `ImportProcess.ClientId` (y su FK) ya existian.

## Pruebas

- `AgentRestSpecBuilderTests` (Ecorex.SuperAdmin.Tests): `RestSpecBuilder.Build` sobre el conector
  Siigo saca el spec ENTERO -> `baseUrl` + `arrayPath` + `paging` (Page, page/page_size) + `fields`
  ANIDADOS (`name.0`, `address.city.name`) + `Headers` (Partner-Id) + `TokenExchange`
  (login/access_key/access_token/Bearer). Casos borde: TokenExchange sin URL de token -> error;
  sin mapeo -> error.
- `ScheduledUpsertRunTests.Corrida_via_agente_en_Upsert_reconcilia_por_columna_sin_duplicar`
  (Ecorex.Application.Tests): el ingest con la convencion del agente (mapping columnaId -> NOMBRE,
  filas indexadas por nombre) en Upsert por "Siigo Id" reconcilia (1 update + 1 insert) sin duplicar.
- Build de la solucion verde; SuperAdmin.Tests 63/63, Application.Tests 615/615.
