# ADR-0075: el iniciador del flujo de una tarea se resuelve a TenantUser, no a PlatformUser

- **Estado:** Aceptado
- **Fecha:** 2026-08-24
- **Contexto previo:** ADR-0056 (origen del asignado por nodo; modo InheritStart)

## Contexto

Al crear una actividad ligada a un flujo, `TaskItemService.CreateAsync` invoca
`WorkflowEngine.StartInstanceAsync(wfDefId, task.Id, actorUserId, actorName, ...)`.
El motor guarda ese `actorUserId` en `WorkflowInstance.StartedByTenantUserId`, y el
modo de asignacion `InheritStart` (ADR-0056) lo hereda como encargado del paso.

Ese campo DEBE contener un `TenantUser` del tenant. Pero el `actorUserId` que llega
a `CreateAsync` es, en casi todos los caminos reales, el **id de PlatformUser** del
usuario autenticado:

- El wizard (`TaskWizard.ActorAsync`) y el resto de la UI leen el claim
  `ClaimTypes.NameIdentifier`, que el login (y el `/dev/login`) fija al `PlatformUser.Id`.
- El seeder de runtime de flujos pasa `owner.PlatformUserId`.
- Programaciones (`ScheduledJobDispatcher`, `job.CreatedBy`) y el motor de acciones
  (`ContactWorkflowDispatcher`) heredan el mismo id ambiguo.

`TaskItemActivity.ActorUserId` (log de actividad) SI espera el PlatformUser por diseno,
asi que el `actorUserId` es correcto ahi; el defecto era usar el MISMO valor como
tenant_user al arrancar el flujo.

### Evidencia

En datos reales (instancia de T00032, tenant AGROMETALICAS), `started_by_tenant_user_id`
quedo con `019f478d-6428-...`, que es un **PlatformUser** (calidad@agrometalicas.com),
no un `TenantUser`. `InheritStart` heredaba entonces un usuario fantasma que no ocupa
ningun cargo.

## Decision

1. **Raiz (choke point en `CreateAsync`):** antes de `StartInstanceAsync` se resuelve el
   `TenantUser` del actor con `ResolveActorTenantUserIdAsync`, y se pasa ESE id al motor.
   Resolucion robusta y tolerante al caller:
   - `Guid.Empty` / sistema -> `null`.
   - si el id ya es un `TenantUser` del tenant -> se usa tal cual (callers que ya mandan
     el tenant_user no se rompen).
   - si es un `PlatformUser` -> se mapea a su `TenantUser` en el tenant activo
     (`TenantUsers.Where(u => u.PlatformUserId == actorUserId)`).
   - si no hay membresia -> `null` (no se hereda un fantasma).
   El resto de usos de `actorUserId` (actividad, notificaciones, auditoria) sigue igual:
   son PlatformUser por diseno.

2. **Defensa en profundidad (motor):** `ResolveDynamicAssigneeAsync` valida que
   `StartedByTenantUserId` sea un `TenantUser` real antes de heredarlo en `InheritStart`;
   si no, cae al usuario del paso anterior (`InheritPrevious`). Protege a las instancias
   HISTORICAS que ya quedaron con un PlatformUser guardado (el fix de raiz solo corrige
   las nuevas).

## Consecuencias

- Una actividad nueva registra `StartedByTenantUserId` con un `TenantUser` valido del
  tenant. `InheritStart` hereda un encargado real.
- Sin cambio de esquema ni migracion.
- Verificado en dev (BD local `ecorex`, copia de prod): el seeder pasa
  `owner.PlatformUserId` (`019f2d5f-c74a-...`) y la instancia resultante (T00213, SKY
  SYSTEM) queda con `started_by_tenant_user_id = 019f2d5f-c763-...`, que ES el
  `TenantUser` de owner@sky-system.local (no el platform id). Antes del fix habria
  guardado el platform id, como T00032.

## Alternativas descartadas

- **Resolver el tenant_user en cada caller de UI (ActorAsync):** hay muchos puntos de
  entrada (wizard, detalle, quick-add, board, programaciones, motor de acciones); un solo
  choke point en `CreateAsync` cubre todos y no cambia el contrato publico del servicio.
- **Cambiar el claim NameIdentifier a tenant_user:** rompe todo lo que asume PlatformUser
  (auditoria, log de actividad, membresias cross-tenant del operador).
