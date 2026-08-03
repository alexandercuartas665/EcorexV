# ADR-0060 - Scheduling en la Config API y corrida programada server-direct (Upsert)

- Estado: Aceptado
- Fecha: 2026-08-03
- Contexto: ECOREX.tareas, `apps/backend`. Sigue a ADR-0058 (Config API tenant-scoped) y ADR-0059
  (resolver JSON anidado compartido + `/run` server-direct). Fase 2 del scheduling.

## Contexto y problema

El `/run` MANUAL de la Config API ya corre server-direct (`ConnectorRunPlanner` ->
`ApiImportService.ImportAsync`): resuelve rutas anidadas (`NestedJsonResolver`) y toma
`mode`+`keyColumn` del body (ADR-0059). Pero la corrida PROGRAMADA (la que agenda el operador para que
un conector se refresque solo) tenia dos huecos:

1. El modo de re-carga NO se persistia en el proceso (`ImportProcess`): el disparo del scheduler iba
   FIJO en `Replace` (vaciar+recargar). No habia forma de programar un **Upsert** (reconciliar por
   una columna clave). Para el conector Siigo, que debe reconciliar por "Siigo Id", esto obligaba a
   correrlo a mano por `/run`.
2. El disparo del scheduler (`ProcessRunner`) despachaba TODO al agente Colmena (incluido RestApi) y
   no habia manera de exponer/gobernar la programacion por HTTP.

Ademas, el scheduling no estaba expuesto en la Config API: no se podia programar el conector Siigo de
punta a punta sin la UI Blazor.

## Decision

### 1. Persistir el modo de corrida en el proceso programado

Se agrega a `ImportProcess` (Domain) `Mode` (enum `ImportRunMode` = Append/Replace/Upsert) y
`KeyColumn` (string?, nombre de la columna clave para Upsert). `ImportRunMode` es un **espejo exacto**
(mismos valores y orden) de `ApiImportMode` (Application): Domain no puede referenciar Application
(Clean Architecture), asi que se declara en `Domain.Enums` y se **castea en el borde**
(`(ApiImportMode)process.Mode`). El orden esta protegido por un test de alineacion.

- **Default = Replace**: es el comportamiento historico del disparo (que era fijo). Las filas
  existentes se rellenan a `Replace` en la migracion, y el default CLR del entity es `Replace`.
- **Storage = string** (`HasConversion<string>()`, `nvarchar(16)`/`varchar(16)`): legible en la BD y
  estable ante reordenamientos del enum. Se usa `ValueGeneratedNever()` junto al `HasDefaultValue`
  para que EF envie SIEMPRE el valor en el INSERT y un `Append` explicito (que coincide con el default
  CLR del enum, 0) NO se sustituya por el default de columna.
- Migracion DUAL `AddImportProcessRunMode` (PostgreSQL + SQL Server), encadenada tras
  `AddContactWorkflows`. Solo agrega las dos columnas; sin cambios de esquema adicionales.

`Mode`/`KeyColumn` se propagan por `SaveImportProcessRequest` e `ImportProcessDto` (Application).
`KeyColumn` solo se conserva en modo Upsert (se limpia en otros modos).

### 2. La corrida PROGRAMADA de un conector RestApi usa el MISMO camino que `/run`

`ProcessRunner.RunNowAsync` (el runner que comparten el boton "Actualizar datos" y el scheduler) se
reestructura para ramificar por tipo de conector:

- **RestApi -> server-direct**: nuevo `RunRestServerDirectAsync` arma el plan con
  `ConnectorRunPlanner.Build(..., (ApiImportMode)process.Mode, process.KeyColumn)` (resolucion de
  rutas anidadas incluida) y ejecuta `IApiImportService.ImportAsync`. El servidor hace el GET (no un
  agente): el intercambio de token de 2 pasos y los headers estaticos ya viven en `ApiImportService`,
  asi que Siigo y compania funcionan sin agente. Deja una corrida en la bitacora (idempotente por el
  indice unico de la ventana) y la cierra sincronicamente.
- **Database -> via agente** (sin cambios de mecanismo): la BD del cliente vive en su red, la trae el
  agente con SQL solo-lectura. Ahora tambien honra el `Mode`/`KeyColumn` persistidos (default Replace
  = comportamiento previo; Upsert resuelve el `keyColumnId` por nombre).

Antes el disparo RestApi iba al agente con `RestFetchSpec` y modo fijo `Replace`. Se ELIMINA esa rama
(y el helper `ProcessRunner.BuildRestSpec`): el RestApi programado ahora es server-direct, alineado
con la direccion de ADR-0058/0059 y con Siigo (API publica, alcanzable desde el servidor). Un RestApi
que solo fuera alcanzable desde la LAN del cliente ya no se cubre por esta via (no era el caso de uso
vigente); si reapareciera, se re-habilita el follow-up B de ADR-0059.

### 3. Endpoints de scheduling en la Config API

Cuatro endpoints nuevos en `ConfigApiEndpoints` (tenant-scoped por el Bearer opaco, auditados, en el
OpenAPI, mismo patron que el resto de `/api/config`). Un conector tiene a lo sumo una programacion:

- `PUT    /api/config/connectors/{id}/schedule` -> crea/edita el proceso (upsert por conector). Body:
  `{ scheduleKind, cronExpression?, intervalMinutes?, mode, keyColumn?, isActive }`. Reusa
  `SaveProcessAsync`. El proceso queda SIN cliente (server-direct).
- `GET    /api/config/connectors/{id}/schedule` -> estado (nextRunAt, lastRunAt, isActive,
  disabledReason, mode, keyColumn...).
- `GET    /api/config/connectors/{id}/runs?take=N` -> bitacora de corridas (reusa `ListRunsAsync`).
- `DELETE /api/config/connectors/{id}/schedule` -> quita el proceso (reusa `DeleteProcessAsync`).

### 4. Validacion del cron (hora del tenant)

No se cambia la convencion: el cron se interpreta en la zona del tenant con Cronos (ADR-0041).
`SaveProcessAsync` ya valida el horario con ese MISMO parser (`ImportRecurrence.ComputeNextRun`) y
lanza si es invalido; el `PUT /schedule` lo traduce a **400** con el motivo, sin activar una
programacion rota. Un cron que se rompe mas tarde (en un `Reschedule` del worker) sigue apagando el
proceso con `DisabledReason` a la vista (comportamiento existente).

## Consecuencias

- Se puede programar el conector Siigo por HTTP con `mode=Upsert, keyColumn="Siigo Id"`, y al
  dispararse reconcilia (Upsert por la clave, campos anidados poblados) en vez de duplicar/borrar.
- El disparo programado y el `/run` manual comparten EXACTAMENTE el mismo camino (planner + importador)
  para RestApi: no pueden divergir.
- La programacion es gobernable de punta a punta sin la UI Blazor.
- Se elimina el despacho RestApi-via-agente en el runner (menos codigo, una sola semantica). Database
  sigue via agente.
- Persiste la duplicacion de `ImportRunMode` (Domain) vs `ApiImportMode` (Application), acotada por el
  cast en el borde y un test de alineacion; se puede unificar si algun dia el enum sube a un leaf
  compartido (mismo espiritu que el follow-up de ADR-0059).

## Pruebas

`ScheduledUpsertRunTests` (Ecorex.Application.Tests): (a) el cast `ImportRunMode -> ApiImportMode` esta
alineado; (b) un proceso Cron con `Mode=Upsert`+`KeyColumn` produce, via `ConnectorRunPlanner`, un
`ApiImportRequest` en Upsert con la clave correcta y las rutas anidadas preservadas; (c) esa corrida,
sobre data existente, reconcilia (1 update + 1 insert) sin duplicar. Los tests del planner y del
nucleo de ingesta (`ConfigApiTests`, `RowIngestServiceTests`) siguen verdes.
