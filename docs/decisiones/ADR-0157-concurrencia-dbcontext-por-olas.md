# ADR-0157: Concurrencia del DbContext en Blazor Server, saneada por olas

**Status:** Proposed
**Date:** 2026-11-08
**Deciders:** Alexander Cuartas (dev principal)

## Contexto

La consola unificada (`Ecorex.SuperAdmin`) es **Blazor Server interactivo puro**
(`AddInteractiveServerComponents`). En ese modelo, el contenedor de DI vive **todo el
circuito** del usuario (no por request). `EcorexDbContext` / `IApplicationDbContext` estan
registrados como **Scoped**, asi que hay **UNA sola instancia de DbContext por circuito**,
compartida por los ~190 servicios Scoped de `Application` que lo inyectan.

EF Core **no es seguro para operaciones concurrentes sobre la misma instancia**. Si dos
operaciones asincronas del mismo circuito se solapan sobre ese unico DbContext, EF lanza:

```
System.InvalidOperationException: A second operation was started on this context instance
before a previous operation completed. This is usually caused by different threads
concurrently using the same instance of DbContext.
```

y, segun el momento, corrupciones de Npgsql derivadas ("Row's number of columns...",
"Source array was not long enough", lectores cerrados a media lectura).

**Veredicto de la auditoria (2026-11-08): P1 (estructural).** No es un bug puntual: es una
propiedad del registro Scoped + el modelo de circuito. Hoy solo esta mitigado en islas:

- `IApplicationDbContextFactory` (ADR-0138) lo usan **solo 3** servicios:
  `ConciliacionDianService`, `TerceroService`, `ConciliacionAsignacionHistorialService`.
- Un parche a mano adicional: `TaskItemService.LoadDetailFreshAsync` abre su propio
  `CreateScope`.

Lo demas sigue compartiendo el DbContext del circuito.

### Vectores de solape (donde se disparan las operaciones concurrentes)

- **Broadcasters de SignalR**: un evento entrante (tablero, notificacion) dispara un handler
  que lee/escribe mientras el usuario esta a mitad de otra accion.
- **Fire-and-forget** `_ = AlgoAsync()` y **`async void`**: lanzan trabajo que corre fuera del
  flujo del render y puede pisar una operacion en curso.
- **Sondeos / timers** de componentes que refrescan datos en segundo plano.
- Multiples invocaciones rapidas del usuario (doble click, navegacion) antes de que termine la
  anterior (ya vimos variantes de esto en [[crash-multiclick-tarjeta-circuito]]).

### Que NO esta en riesgo (descartado en la auditoria)

Workers (`Ecorex.Workers`), endpoints de API (`Ecorex.Api`), webhooks, el runtime del agente
(`AgentReplyDispatcher`) y la ingesta crean **su propio scope** por unidad de trabajo, asi que
cada operacion tiene su DbContext. Son de **bajo riesgo** y quedan como deuda documentada, no
como objetivo de migracion.

### Evidencia

- **Historica**: el crash real que motivo ADR-0138 (contexto efimero para lectura) es
  exactamente esta familia.
- **En vivo**: en los logs de prod (docker logs / stdout, sin Serilog ni sink de archivo) hubo
  **0 firmas de "second operation"** en la ventana viva (~15 h desde el ultimo deploy), pero es
  **inconcluso**: la retencion es corta y las lineas no llevan timestamp, asi que no se puede
  afirmar "no ocurre", solo "no se observo en esa ventana". Medir de verdad exige primero
  arreglar la observabilidad (ver Action Items).

## Decision

Migrar al patron **"contexto corto por operacion"** mediante el factory ya existente
(`IApplicationDbContextFactory`, ADR-0138), pero hacerlo **por olas** de riesgo decreciente, en
PRs separados y verificables, en vez de un cambio masivo y arriesgado sobre ~190 servicios.
Como mitigacion inmediata y barata, la Ola 0 **serializa** los puntos de solape del circuito
sin tocar la capa de datos.

### Ola 0 - Mitigacion inmediata (sin tocar la capa de datos)

- Eliminar los `_ = ...Async()` fire-and-forget y los `async void` de los componentes calientes
  (p.ej. `TaskDetailModal`, `DynamicFormRenderer`): volverlos `await` dentro del flujo, o
  encolarlos.
- **Serializar** los handlers disparados por push de SignalR con un `SemaphoreSlim(1,1)` por
  circuito, para que un evento entrante no se solape con la accion en curso del usuario.
- Objetivo: matar los solapes mas probables ya, antes de tocar servicios.

### Ola 1 - Lecturas calientes al contexto efimero

Pasar al factory las **lecturas** de los servicios de las pantallas con broadcaster, siguiendo
el patron ya probado en `ConciliacionDianService`:
`TaskItemService` (lo que falta), `TaskBoardService`, `ActivityBoardService`,
`DashboardService`, `FormResponseService`, `ConciliacionRuleService`. Cada lectura abre su
contexto, **materializa dentro del `using`** y lo descarta.

### Ola 2 - Escrituras (tras decidir el diseno)

Antes de migrar escrituras hay que **decidir el modelo de unidad de trabajo**: o el factory
expone una UoW corta por operacion, o cada servicio abre su transaccion corta. Solo entonces se
migran las escrituras, **preservando la atomicidad** (regla inviolable #4: toda operacion
multi-tabla en transaccion, rollback total si un paso falla) y la **auditoria/outbox en el
MISMO contexto** que la escritura.

### Deuda documentada (NO se toca)

Workers, endpoints, webhooks, agente e ingesta: ya aislan su scope. Se dejan como estan.

### Checklist de trampas (que el compilador NO atrapa)

- **Materializar `IQueryable` dentro del `using`**: nunca devolver una consulta diferida que se
  evalue despues de que el contexto corto se haya descartado.
- **Auditoria / outbox en el mismo `SaveChanges`**: `AuditableTenantInterceptor` / `AdminAuditLog`
  deben viajar en el mismo contexto corto que la escritura (si no, se pierde la atomicidad del
  ADR-0138 y de la regla #5).
- **No partir una operacion logica en dos contextos**: lecturas + escritura de una misma unidad
  deben compartir contexto, o se pierde la transaccion.
- **El query filter por tenant debe seguir aplicando** en los contextos creados por el factory
  (resolver el `TenantId` al crear el contexto; una query cross-tenant debe seguir siendo
  imposible por construccion - regla #1/#6).
- **Tokens de concurrencia optimista** (rowversion / xmin, regla #8): recargar la entidad en el
  MISMO contexto corto donde se va a guardar.
- **No capturar el DbContext del circuito** en una clausura que se pase a trabajo en segundo
  plano: capturar el factory, no la instancia.

## Opciones consideradas

### Opcion A: DbContext Transient
Romperia el seguimiento de cambios y la unidad de trabajo de una operacion logica; ademas, con
`@inject` cada inyeccion daria una instancia distinta. EF lo desaconseja. Descartada.

### Opcion B: factory + contexto corto en TODO de una sola vez
Correcta en destino pero inviable de revisar/verificar con seguridad en ~190 servicios en un
solo PR; alto riesgo de regresion silenciosa (consultas diferidas, atomicidad). Descartada como
ejecucion (el destino SI es el factory).

### Opcion C: un lock por circuito que serialice TODO servicio
Un `SemaphoreSlim` global por circuito alrededor de cada metodo mata la concurrencia y el
rendimiento, y no arregla nada fuera del circuito. Sirve solo como tactica acotada de la Ola 0
(handlers de push), no como fix global. Descartada como solucion principal.

### Opcion D (elegida): factory + migracion por olas, con serializacion de Ola 0 como mitigacion

| Dimension | Evaluacion |
|-----------|------------|
| Riesgo de regresion | Bajo (PRs chicos, verificables, reversibles ola a ola) |
| Alivio inmediato | Si (Ola 0 no toca datos y corta los solapes probables) |
| Reusa lo existente | Si (`IApplicationDbContextFactory`, ADR-0138, patron ya en prod) |
| DAL dual | Intacta (el factory ya resuelve el proveedor) |
| Atomicidad | Se preserva por diseno (decision explicita en Ola 2) |

## Consecuencias

- **Mas facil**: eliminar una clase de crashes intermitentes y dificiles de reproducir; aislar
  cada pantalla caliente sin esperar a reescribir todo.
- **Mas dificil**: cada servicio migrado debe cuidar las trampas de arriba (materializacion,
  atomicidad, tenant filter); la Ola 2 obliga a una decision de diseno de unidad de trabajo.
- **A revisar**: la metrica de aceptacion ("0 second operation en 24 h") **no es medible hoy**
  porque prod no tiene sink persistente ni timestamps. Arreglar la observabilidad es
  prerrequisito de la Ola 1 en adelante.

## Verificacion

- **Prerrequisito (observabilidad)**: sink persistente con timestamps en prod (y/o un contador
  de la excepcion), para poder medir la metrica.
- **Metrica de aceptacion**: **0 incidencias de "second operation"** (y de las corrupciones
  Npgsql derivadas) en una ventana de **24 h** de uso normal tras cada ola.
- Por ola: `dotnet build Ecorex.sln` verde; la matriz dual de integracion (PG + SQL Server,
  Testcontainers) + el test de aislamiento cross-tenant (gate del CI, ADR-0018) en el PR.
- Regresion manual de las pantallas calientes migradas (tableros, detalle de tarea, formularios,
  dashboard, conciliacion) con SignalR activo y doble-click/navegacion rapida.

## Estado de ejecucion

**Ola 0 - ejecutada (2026-11-08, v0.16.282).** La auditoria fina de los componentes calientes
mostro que la mayor parte de la serializacion YA estaba puesta de forma incremental (ADR-0138 y
sus seguimientos):

- Handlers de push de tableros (`TaskKanban`, `ActivityBoardDetail`, `ActivityBoardsIndex`):
  su `ReloadAsync` ya corre con `_reloadLock` + scope EF propio (`ScopeFactory.CreateAsyncScope`
  + `AmbientTenantContext`). OK, no se toco.
- `DynamicFormRenderer`: todas sus escrituras/lecturas disparadas fire-and-forget (`AutosaveTickAsync`
  via `SaveAsync`, `EnsureLookupDisplayAsync`, `EnsureGridListOptionsAsync`) ya pasan por el
  `CircuitFormGate` (`_dbGate`). OK, no se toco.
- `MainLayout.RefreshUnreadAsync` (push `NotificationAdded`): ya usa scope EF propio. OK.
- **Unico hueco real hallado y CORREGIDO**: `TaskDetailModal.RecalcCustomCalculatedAsync`, que se
  disparaba fire-and-forget desde un cambio de campo y llamaba `ITaskFieldService.ComputeCalculatedAsync`
  (que LEE `TaskFieldDefinitions` en BD, pese a un comentario que decia "no toca la BD") sobre el
  `_db` del circuito, sin gate. Se aislo con `_reloadLock` + scope EF propio + `AmbientTenantContext`,
  igual que el `ReloadAsync` del propio modal, con try/catch para no tumbar el circuito.
- Fire-and-forget restantes verificados SIN BD (seguros): `ClearCopiedAsync` (Task.Delay),
  `TaskToasts` (quita un toast). Sin `async void` en componentes calientes.

**Vectores nuevos detectados -> van a Ola 1** (no se parchean con medio-lock para no dar falsa
confianza): `Conversaciones`, `Pipeline` y `ClientChat` NO tienen gate por circuito y su handler de
push `BoardChanged` recarga de BD (`ReloadConversationsAsync` -> `ChatSvc`, etc.) sobre el `_db`
compartido; ademas el resto de la pagina usa ese mismo `_db` sin gate. Necesitan el tratamiento de
scope propio/gate completo (Ola 1), no un lock parcial.

**Ola 1 - primera tajada: componentes de chat (ejecutada, 2026-11-08, v0.16.283).** De los tres
componentes marcados, solo DOS tenian un push que lee BD sin gate:

- `Conversaciones.razor`: el push `BoardChanged` llamaba `ReloadConversationsAsync` (lee
  `ChatSvc.ListConversationsAsync` + `GetUnansweredByPhoneAsync`) sobre el `_db` del circuito. Se
  enruto por `_reloadLock` + `ScopeFactory.CreateAsyncScope` + `AmbientTenantContext.Begin` resolviendo
  un `IChatService` fresco (mismo patron que `TaskKanban.ReloadAsync`). El push `MessageAdded` solo
  agrega el DTO en memoria -> seguro, no se toco.
- `Pipeline.razor`: el push `BoardChanged` recalculaba `_pending = ChatSvc.GetUnansweredByPhoneAsync()`
  sobre el `_db` del circuito. Se extrajo a `ReloadPendingFromPushAsync()` con el mismo gate (lock +
  scope propio + ambient tenant). `MessageAdded` es en memoria -> seguro.
- `ClientChat.razor`: VERIFICADO sin cambio. Su unico handler de push es `MessageAdded` (agrega el DTO
  en memoria); NO tiene recarga de BD disparada por push. No requiere gate para este vector.

Trampas revisadas en esta tajada: las lecturas se materializan dentro del `using` (`.ToList()` /
diccionario materializado); el query filter por tenant se preserva via `AmbientTenantContext.Begin`
antes de crear el scope; no se parte ninguna operacion logica (son lecturas puras). Build verde.

**Ola 1 - segunda tajada: lecturas de servicios al factory (ejecutada, 2026-11-08, v0.16.284).** Se
migraron las LECTURAS puras de 4 de los 6 servicios al `IApplicationDbContextFactory` (ADR-0138),
patron `await using var db = _dbFactory.Create();` con materializacion dentro del `using`; las
ESCRITURAS siguen en el `_db` del circuito (su unidad de trabajo):

- `DashboardService`: 100% lectura -> inyecta SOLO el factory; `GetAsync` + `GetReportsAsync`. La
  subconsulta correlacionada (`db.Leads` dentro de la proyeccion de `db.PipelineStages`) usa el MISMO
  contexto corto.
- `TaskBoardService`: `ListBoardsAsync`, `ListColumnsAsync`, `ListBoardTagsAsync` (las 3 lecturas). El
  resto (CRUD de tableros/columnas/etiquetas) es UoW y queda en `_db`.
- `ConciliacionRuleService`: `ListAsync`, `ListContenedoresAsync`. El motor `AplicarReglasAsync` es una
  UoW TRACKEADA (lee + muta renglones/EAV + SaveChanges) y queda en `_db` (sus helpers de indice,
  tambien). Es trabajo de Ola 2.
- `ActivityBoardService` (pantalla con broadcaster): `ListBoardsAsync`, `GetBoardDetailAsync`,
  `ResolveScannedTaskOnBoardAsync`. Se enhebro el contexto corto por los helpers de SOLO LECTURA
  (`ApplyScope`, `GetRoutedTaskIdsAsync`, `LoadMemberInfosAsync`), que ahora reciben `IApplicationDbContext db`;
  el root del `IQueryable` y sus subconsultas correlacionadas comparten ese contexto. CRUD/MoveTask/
  Add-Remove/QuickCreate quedan en `_db`.

Verificado: build de solucion verde; integracion dual (PG + SQL Server por Testcontainers) de
ActivityBoard + TaskField 24/24 y Dashboard 1/1 en local.

**Ola 1 - tercera tajada: TaskItemService + FormResponseService (ejecutada, 2026-11-09, v0.16.285).**
Se enhebro el contexto por los helpers COMPARTIDOS entre lectura y escritura: `ToSummaryAsync`/
`ToSummariesAsync`/`LoadAssigneesAsync` y un nuevo `SumSecondsAsync` reciben ahora el `IApplicationDbContext`
a usar. Las ESCRITURAS (`ChangeStatus`/`Assign`/`Unassign`/`Archive`/`Restore`) pasan el `_db` del circuito
(la entidad viene rastreada y recien guardada); las LECTURAS abren el contexto efimero. En `TaskItemService`
se migraron `ListAsync`, `GetDetailAsync`, `ListWorkLogsAsync`, `TotalSecondsAsync` y las lecturas de
etiquetas. `GetDetailAsync` es seguro porque sus llamadores que escriben antes (`SetContactTercero`/
`UpdateCustomFields`/`Copy`) ya hicieron `SaveChanges` sin transaccion abierta -> la lectura fresca ve lo
commiteado.

En `FormResponseService` se migraron SOLO las lecturas AUTOCONTENIDAS (sin sub-servicio atado al mismo `_db`):
`GetAsync`, `ListRecords`, `ExportRecordsXlsx`, el maestro-detalle (`List/Find/CountRowChildren`),
`ResolveDefinitionIdByCode`, `GetDerivedRecord` y la familia **`Board*`** (`GetBoardForms`/`GetBoardTaskFormValues`/
`GetBoardGridSources`), que es la que solapa el push SignalR del tablero. Los helpers usados por estas lecturas
(`ToDto`/`ParseDocument`/`StripOrdinal`/`ParseCodeList`) son `static` puros.

Patron de call-site (ambos servicios), que respeta el fallback a `_db` en pruebas sin factory:
`await using var lease = _dbFactory?.Create(); var db = lease ?? _db;` y las consultas se materializan sobre
`db` dentro del `using`. El `_dbFactory` es un parametro OPCIONAL del ctor (null en tests -> `_db`; DI lo
inyecta en prod), siguiendo el patron ya usado para `_scopeFactory`/`_notificationBroadcaster`.

Integracion DUAL (PG + SQL Server, Testcontainers): se inyecto el factory real en `TaskCoreTests`,
`DynamicFormsTests` y `FormAutoCodeTests` para ejercitar el camino efimero; `TaskCore + TaskField +
ActivityBoard + DynamicForms + FormAutoCode = 84/84` verdes. Build Release de la solucion verde.

**DEFERIDO a Ola 2** (no autocontenido): las lecturas de `FormResponseService` que delegan en
`_workflowEngine`/`_lookup`/`_rules` (`GetTaskStepForms`, `GetTaskRelatedForms`, concepto/generos de la
tarea) siguen en `_db`: aislarlas exige que ESOS sub-servicios usen tambien un scope corto propio, lo que
se decide junto con el modelo de unidad de trabajo de las escrituras. Se mitiga porque su vector de solape
por push ya quedo acotado a nivel de componente (TaskKanban/ActivityBoardDetail/DynamicFormRenderer recargan
via `CreateAsyncScope`/`CircuitFormGate` -> servicio fresco).

**Ola 2 - escrituras + cierre (ejecutada, 2026-11-09, v0.16.286).** Antes de "migrar escrituras" se audito
que operacion de BD puede todavia tocar el `_db` del circuito DESDE UN CONTEXTO NO SERIALIZADO (la causa real
del "second operation": el dispatcher del circuito serializa los handlers de UI y el trabajo marshalado con
`InvokeAsync`, asi que dos escrituras de UI nunca se solapan de verdad; el bug aparece cuando algo corre FUERA
del dispatcher -fire-and-forget, callback de SignalR antes de `InvokeAsync`, timer/poll en hilo del pool- sobre
el MISMO `_db`). Barrido de los dos proyectos Blazor (`Ecorex.SuperAdmin` activo; `Ecorex.Web` sin estos
patrones) por `Task.Run`/`Timer`/`PeriodicTimer`/`ConfigureAwait(false)`/`_hub.On`:

- **UNICO vector de ESCRITURA off-dispatcher**: `Lineas.PollAsync` (timer de 3 s para el QR) llamaba
  `ConnectorSvc.RefreshAsync` (escribe el estado de la linea) sobre el `ConnectorSvc` del circuito en un hilo
  del pool -> podia solaparse con una accion del usuario (Refrescar/Desconectar). **CORREGIDO**: la consulta
  corre en un scope EF propio (`CreateAsyncScope` + `AmbientTenantContext.Begin`, `IWhatsAppConnectorService`
  fresco) y las mutaciones de estado + `StateHasChanged` se marshalan con `InvokeAsync`.
- `FormModule` (handler SignalR `FormRecord`) llama `ResponsesSvc.ListRecordsAsync` off-dispatcher: ya quedo
  mitigado por la tajada 3 (ese metodo abre su contexto efimero). Sin cambio.
- `ContactSearchConfig` (Task.Run) ya usaba scope propio + ambient tenant; `AnalizadorDocumentos`/`TaskToasts`
  marshalan con `InvokeAsync` y no tocan BD fuera del dispatcher; `DynamicFormRenderer` serializa TODA su BD
  (autosave/submit/reglas/lookups) por el `CircuitFormGate` por circuito; los demas `_hub.On` recargan via
  `InvokeAsync` + scope fresco (Ola 0/1). Sin cambios.

**Decision de unidad de trabajo (Action Item 4):** NO se adopta un factory-UoW por metodo ni se convierten las
escrituras de los ~190 servicios (eso era la Opcion B, descartada). Las ESCRITURAS permanecen como unidad de
trabajo sobre el `_db` del circuito -con su transaccion y su auditoria/outbox en el MISMO contexto (reglas #4/#5
intactas)- porque, tras Olas 0/1/2, el `_db` del circuito solo lo toca trabajo SERIALIZADO por el dispatcher:
todo lo que corre fuera del dispatcher usa un scope fresco (`CreateAsyncScope`) o el factory efimero. El patron
de aislamiento es "scope corto en el CALL-SITE no serializado", no "convertir cada metodo de escritura".

**Lecturas entangled de `FormResponseService`** (`GetTaskStepForms`/`GetTaskRelatedForms`/concepto+generos):
no requirieron cambio porque su superficie caliente (`TaskDetailModal`) ya las invoca por el scope FRESCO en
el camino normal (el `_db` del circuito solo en el fallback raro "sin tenant"); `MovilTablero.OnBoardChanged`/
`TaskWizard` son handlers de UI serializados. Quedan asi cubiertas; su conversion interna al factory se hara si
se migran los sub-servicios, sin urgencia.

**Pendiente real (no es codigo de esta ola):** la OBSERVABILIDAD en prod (Action Item 1) para poder MEDIR la
metrica "0 second operation en 24 h". El fix esta; la medicion necesita el sink.

## Action Items

1. [ ] Prerrequisito: observabilidad en prod (sink persistente + timestamps / contador de la excepcion).
2. [x] Ola 0 (v0.16.282): fix de `TaskDetailModal.RecalcCustomCalculatedAsync` (unico fire-and-forget sin gate que tocaba BD); el resto del camino caliente ya estaba serializado (verificado). Build verde.
3. [x] Ola 1: migrar las lecturas calientes al factory/scope propio.
   - [x] Componentes de chat (v0.16.283): `Conversaciones` (`ReloadConversationsAsync`) y `Pipeline`
     (`ReloadPendingFromPushAsync`) gateados con lock + scope propio + ambient tenant; `ClientChat`
     verificado sin vector (su push es solo `MessageAdded` en memoria). Build verde. SIN deploy.
   - [x] Servicios, tajada 1 (v0.16.284): lecturas al factory en `DashboardService`, `TaskBoardService`,
     `ConciliacionRuleService` (las dos lecturas puras) y `ActivityBoardService`. Integracion dual OK.
   - [x] Servicios, tajada 2 (v0.16.285): `TaskItemService` (todas sus lecturas + helpers enhebrados) y
     `FormResponseService` (lecturas autocontenidas, incl. familia `Board*`). Integracion dual 84/84.
     SIN deploy. Lo entangled con `_workflowEngine`/`_lookup`/`_rules` queda para Ola 2 (ver abajo).
4. [x] Decidir el modelo de unidad de trabajo para escrituras: las escrituras SIGUEN sobre el `_db` del
   circuito (transaccion + auditoria en el mismo contexto, reglas #4/#5); NO factory-UoW por metodo ni
   conversion masiva (Opcion B, descartada). El aislamiento es "scope corto en el call-site no serializado"
   (ver Ola 2 en Estado de ejecucion).
5. [x] Ola 2 (v0.16.286): auditoria de vectores off-dispatcher sobre el `_db` del circuito -> unico write
   real = `Lineas.PollAsync` (timer 3 s), CORREGIDO con scope propio + ambient tenant + `InvokeAsync`.
   `FormModule` off-dispatcher ya mitigado por la tajada 3. Lecturas entangled de `FormResponseService`
   cubiertas por el scope fresco de `TaskDetailModal`. Build SuperAdmin verde. SIN deploy.
6. [ ] Prerrequisito de medicion (bloquea la metrica, no el fix): observabilidad en prod (Action Item 1).
7. [ ] Reflejar este ADR en el vault (`04. Notas para desarrollador/ADRs`).

> Relaciona: [[dbcontext-efimero-lectura-factory]] (ADR-0138, contexto efimero de lectura),
> ADR-0156 (el fix de surrogates se hallo en esta misma auditoria, bug distinto),
> [[crash-multiclick-tarjeta-circuito]] (sintoma emparentado ya parcheado a mano).
