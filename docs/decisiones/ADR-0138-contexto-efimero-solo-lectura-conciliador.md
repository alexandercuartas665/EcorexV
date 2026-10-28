# ADR-0138: Contexto EF efimero por consulta para lecturas que comparten circuito (fix "second operation")

**Estado:** Aceptado
**Fecha:** 2026-10-28
**Version:** v0.16.239
**Deciders:** usuario (producto) + agente de desarrollo

## Contexto

En /conciliacion-dian, al cambiar el usuario asignado de una tarea desde el modal de detalle
(TaskDetailModal) abierto sobre la pagina, el circuito Blazor se caia con:

> System.InvalidOperationException: A second operation was started on this context instance before a
> previous operation completed. This is usually caused by different threads concurrently using the same
> instance of DbContext.

El stack apuntaba a `ConciliacionDianService.ContarPorEstadoAsync` (disparado por el `ReloadAsync` del
padre, conectado a `OnChanged` del modal).

**Causa raiz:** el `IApplicationDbContext` (EcorexDbContext) es SCOPED al circuito y se COMPARTE entre
la pagina y sus componentes hijos (el modal). Cuando una recarga de la pagina (lecturas del conciliador)
se solapa con el trabajo del modal (asignar, que escribe en el MISMO contexto via TaskItemService), dos
operaciones corren a la vez sobre la misma instancia de DbContext y EF lanza la excepcion. Es
pre-existente; no proviene del commit c621e6d8.

## Decision

Las operaciones de SOLO LECTURA del conciliador usan un **DbContext efimero y propio por consulta**, en
lugar del contexto scoped compartido. Se introduce una abstraccion en la capa Application (para no acoplar
los servicios a Infrastructure ni romper Clean Architecture):

- `IApplicationDbContextFactory.Create()` -> `IScopedDbContext` (`IApplicationDbContext` + `IAsyncDisposable`).
- Implementacion en Infrastructure (`ApplicationDbContextFactory`), registrada **Scoped** en cada rama de
  proveedor (DAL dual, ADR-001): construye el contexto del proveedor ACTIVO (EcorexDbContext para Postgres,
  SqlServerEcorexDbContext para SQL Server) con las `DbContextOptions` del scope y el **mismo
  `ITenantContext` del circuito**, por lo que el **filtro global multi-tenant se conserva intacto**
  (regla inviolable #1).
- `ConciliacionDianService` inyecta la fabrica y envuelve cada lectura en
  `await using var db = _dbFactory.Create();` (`ListDocumentosAsync`, `GetDocumentoAsync`,
  `ListRenglonesAsync`, `ContarPorEstadoAsync`, `ListPorAutorizarAsync`, `IsColumnValueUsedAsync`).
- Las ESCRITURAS siguen en el `IApplicationDbContext` scoped (unidad de trabajo / transaccion del circuito):
  Crear/Importar/Cruzar/Update*/Set*/Toggle*/Radicar*/SeedDummy no cambian.

Se eligio esta via (equivalente al "Fix A - robusto" del diagnostico) sobre un `SemaphoreSlim` gate en la
UI (Fix B) porque elimina la comparticion de raiz y no depende del timing del render.

## Consecuencias

- Las lecturas del conciliador ya no colisionan con el trabajo en vuelo del contexto compartido: el
  "second operation" en ese flujo desaparece.
- La fabrica es **reutilizable**: cualquier servicio con el mismo patron (lecturas que corren mientras un
  modal/hijo usa el contexto del circuito) puede inyectar `IApplicationDbContextFactory`.
- Cada lectura abre/cierra su propia conexion (coste marginal; los volumenes del conciliador son chicos).
- Los contextos efimeros son de solo lectura (AsNoTracking): no escriben, no abren transacciones.
- RESIDUAL: si el mismo crash reaparece en OTRO servicio llamado dentro de `ReloadAsync` (p.ej. el de
  archivos o el de config de tareas) habria que darle el mismo tratamiento; el punto observado
  (ContarPorEstado/ListRenglones) queda resuelto.

## Validacion

Build verde (Infrastructure + Application); 1144 tests unitarios de Application en verde. Multi-tenancy:
el contexto efimero recibe el mismo ITenantContext del scope, asi que el HasQueryFilter global sigue
aplicando (no se introduce via cross-tenant). Pendiente E2E en /conciliacion-dian (tenant SOLDARCO) y la
integracion al worktree 5263 via prompt.
