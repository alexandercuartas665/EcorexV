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

**Pendiente de Ola 1 (su propia tajada)**: `TaskItemService` (1912 L) y `FormResponseService` (2194 L).
Sus lecturas calientes (`ListAsync`, `GetDetailAsync`, ...) construyen DTOs a traves de helpers
COMPARTIDOS (`ToSummaryAsync`/`ToSummariesAsync`/`LoadAssigneesAsync`/`TotalSecondsAsync`) que TAMBIEN
invocan los metodos de ESCRITURA tras su `SaveChanges`. Migrarlas con seguridad exige enhebrar el
contexto por esos helpers y actualizar cada call-site de escritura en todo el archivo -> cambio ancho
que debe ir en su PR, con la matriz dual del CI (riesgo: una escritura leyendo el detalle del contexto
equivocado). Se mitiga porque su vector de solape por push ya quedo acotado a nivel de componente
(TaskKanban/ActivityBoardDetail recargan via `CreateAsyncScope` -> servicio fresco).

## Action Items

1. [ ] Prerrequisito: observabilidad en prod (sink persistente + timestamps / contador de la excepcion).
2. [x] Ola 0 (v0.16.282): fix de `TaskDetailModal.RecalcCustomCalculatedAsync` (unico fire-and-forget sin gate que tocaba BD); el resto del camino caliente ya estaba serializado (verificado). Build verde.
3. [~] Ola 1: migrar las lecturas calientes al factory/scope propio.
   - [x] Componentes de chat (v0.16.283): `Conversaciones` (`ReloadConversationsAsync`) y `Pipeline`
     (`ReloadPendingFromPushAsync`) gateados con lock + scope propio + ambient tenant; `ClientChat`
     verificado sin vector (su push es solo `MessageAdded` en memoria). Build verde. SIN deploy.
   - [x] Servicios, tajada 1 (v0.16.284): lecturas al factory en `DashboardService`, `TaskBoardService`,
     `ConciliacionRuleService` (las dos lecturas puras) y `ActivityBoardService`. Integracion dual OK.
   - [ ] Servicios, tajada 2 (su propia PR): `TaskItemService` (resto) y `FormResponseService` -> exigen
     enhebrar el contexto por helpers compartidos entre lectura y escritura (ver Estado de ejecucion).
4. [ ] Decidir el modelo de unidad de trabajo para escrituras (factory-UoW vs transaccion corta por servicio).
5. [ ] Ola 2: migrar escrituras preservando atomicidad y auditoria en el mismo contexto.
6. [ ] Reflejar este ADR en el vault (`04. Notas para desarrollador/ADRs`).

> Relaciona: [[dbcontext-efimero-lectura-factory]] (ADR-0138, contexto efimero de lectura),
> ADR-0156 (el fix de surrogates se hallo en esta misma auditoria, bug distinto),
> [[crash-multiclick-tarjeta-circuito]] (sintoma emparentado ya parcheado a mano).
