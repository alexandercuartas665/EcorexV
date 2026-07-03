# ADR-0013: Nucleo TaskItem de primera clase (FASE 3, ola 1)

- Estado: aceptada
- Fecha: 2026-07-03
- Relacionada con: ADR-0001 (DAL dual), ADR-0011 (limpieza del backbone), ADR-0012 (.NET 10)

## Contexto

ECOREX Sistema de Tareas necesita tareas de primera clase: numero consecutivo por tenant,
ciclo de vida con estados propios, prioridad, solicitante, proyectos con ACL, etiquetas,
worklogs, comentarios y adjuntos. El backbone CRM heredado trae un modulo Kanban
(TaskBoard/TaskBoardColumn/TaskCard/...) donde la "tarea" es una tarjeta cuyo estado es la
columna en la que vive: util como tablero visual, insuficiente como nucleo del dominio.

## Decision

### 1. TaskItem primera clase; TaskBoard/TaskCard queda como kanban generico CRM

Se crea un nucleo nuevo e independiente (`TaskItem` + `ActivityType`, `Project`,
`ProjectMember`, `TaskItemTag`, `TaskItemTagAssignment`, `TaskWorkLog`, `TaskItemActivity`,
`TaskItemAttachment`, `TenantSequence`), todo TENANT-SCOPED bajo el HasQueryFilter global.
El estado de la tarea es un enum (`TaskItemStatus`) gobernado por la maquina de estados
`TaskItemStateMachine` (Ecorex.Domain.Rules):

```
Pending    -> Active | InProgress | Suspended
Active     -> InProgress | Suspended | Done
InProgress -> Done | Suspended
Suspended  -> InProgress | Active
Done       -> Closed | InProgress   (reabrir)
Closed     -> (terminal, solo lectura)
```

Transicion invalida => resultado tipado `InvalidTransition` (nunca excepcion cruda).
`ClosedAt` se estampa al entrar a Closed.

El modulo heredado TaskBoard/TaskCard NO se toca: queda como kanban generico del CRM,
con destino a decidir (candidatos: vista kanban sobre TaskItem, o eliminacion en una
fase de limpieza). Las etiquetas del nucleo (`TaskItemTag`) son catalogo POR TENANT,
a diferencia de `TaskCardTag` que es por tablero.

### 2. Concurrencia optimista: columna Version portable (elegida sobre xmin/rowversion)

Regla inviolable del proyecto: TaskItem y Project llevan concurrencia optimista. Se
evaluaron dos estrategias:

- xmin (PostgreSQL) + rowversion (SQL Server), condicionadas por `isNpgsql`: el token
  nativo difiere en tipo (uint vs byte[]), contamina DTOs/API con tipos por motor y
  complica el par de migraciones/snapshots.
- **Columna `Version` (long) como ConcurrencyToken portable (ELEGIDA)**: mismo modelo,
  mismas migraciones logicas y mismo token (long) en ambos motores.

Implementacion: interfaz `IVersioned` (Ecorex.Domain.Common); `AuditableTenantInterceptor`
incrementa `Version` en cada entidad modificada (el negocio nunca la escribe); EF usa el
valor ORIGINAL en el WHERE del UPDATE, por lo que una escritura concurrente lanza
`DbUpdateConcurrencyException`. Los servicios ademas comparan el token que envia el
cliente (`request.Version != entity.Version`) y devuelven `TaskCoreResult.Conflict`
tipado; la excepcion de EF tambien se traduce a `Conflict`. Cobertura: token viejo del
cliente (check explicito) y carrera leer-guardar (token de EF).

### 3. TenantSequence para consecutivos (reemplazo del MAX+1 legacy)

El legacy calculaba consecutivos con MAX+1, que duplica numeros bajo concurrencia.
Se reemplaza por la tabla `TenantSequence` (TenantId, Code, NextValue; unico
TenantId+Code) y `SequenceService`:

- `NextAsync(code, prefix, padding)` emite `T00042` con un UPDATE condicional atomico
  (compare-and-swap con retry) via `ExecuteUpdateAsync` LINQ: `WHERE id = X AND
  next_value = leido`. Sin SQL crudo, portable a ambos motores. Bajo READ COMMITTED el
  perdedor de la carrera bloquea hasta el commit del ganador, re-evalua el predicado,
  afecta 0 filas y reintenta con el valor fresco: la emision se serializa sobre la fila
  sin duplicados.
- El incremento participa en la transaccion del caso de uso (`CreateAsync` de TaskItem
  abre transaccion explicita): un rollback devuelve el numero (sin huecos por fallos).
- `EnsureSequenceAsync(code)` se llama ANTES de abrir la transaccion: la carrera de
  creacion de la fila (violacion del indice unico) se tolera fuera de la transaccion
  principal, porque en PostgreSQL cualquier error envenena la transaccion en curso.
- Codigo del consecutivo de tareas: "T05" (prefijo "T", padding 5).

Verificado con test de integracion dual: 10 creaciones CONCURRENTES (Task.WhenAll)
producen T00001..T00010 exactos, sin duplicados, en PostgreSQL y SQL Server.

### 4. Servicios y resultados tipados

`ISequenceService`, `IActivityTypeService`, `IProjectService`, `ITaskItemService`
(Ecorex.Application/Tenancy, patron interfaz + impl + DTOs, registrados en
DependencyInjection). Errores de negocio via `TaskCoreResult<T>` con estados
Ok/NotFound/Invalid/Conflict/InvalidTransition/Forbidden. `IApplicationDbContext` expone
`BeginTransactionAsync` para casos de uso multi-paso.

ACL de proyecto: owner (edicion total) + `ProjectMember.CanEdit`;
`IProjectService.CheckAccessAsync` responde (CanView, CanEdit).

### 5. Hooks FASE 4 (workflow)

`ActivityType.WorkflowDefinitionId` (Guid?, SIN FK todavia) y `ActivityType.RequiresForm`
son placeholders. Cuando exista el motor de flujos, el estado de las tareas de un tipo
con workflow NO sera libre: lo dictara la definicion del flujo y `TaskItemStateMachine`
quedara como fallback para tipos sin flujo. El formulario requerido se anclara a
`RequiresForm`.

## Consecuencias

- (+) Modelo, migraciones y DTOs identicos entre motores; el token de concurrencia viaja
  como long por la API.
- (+) Consecutivos correlativos sin duplicados bajo concurrencia, sin SQL por proveedor.
- (-) La emision del consecutivo serializa las creaciones de tareas del tenant sobre una
  fila (aceptable para el volumen esperado; si algun dia duele, se puede pasar a bloques
  reservados por instancia).
- (-) `Version` requiere que TODA modificacion pase por el DbContext con el interceptor
  (los `ExecuteUpdate` masivos no la incrementan; hoy ningun caso de uso del nucleo los
  usa sobre TaskItem/Project).
- El kanban CRM heredado sigue operativo y aislado; decision sobre su futuro pendiente.
