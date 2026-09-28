# ADR-0117: Tableros de actividades restringidos por usuario (vacio = todos)

**Status:** Accepted
**Date:** 2026-09-28
**Deciders:** Alexander Cuartas (producto), agente de desarrollo

## Contexto

Se pidio poder restringir un tablero de actividades (000636) a ciertos usuarios: solo ellos lo ven en la
bandeja/menu rapido. Si el tablero no define usuarios, queda **disponible para todos** (comportamiento
actual, no se rompe nada). Decisiones del usuario: (1) Owner/Admin ven SIEMPRE todos los tableros aunque
esten restringidos (para poder administrarlos); (2) la restriccion es **solo por usuarios** (no por
Dependencia/Cargo, a diferencia de las secciones de formulario, ADR-0082).

## Decision

1. **Modelo**: `TaskBoard.AllowedUserIdsJson` (arreglo JSON de `TenantUserId`). Null/vacio = sin
   restriccion (todos). jsonb (PG) / nvarchar(max) (SQL Server). Migracion dual `AddBoardAllowedUsers`.

2. **Evaluacion EN MEMORIA (portable dual)**: el indice (`ActivityBoardService.ListBoardsAsync`) ya carga
   los tableros a memoria; tras cargarlos se filtran con `IsBoardVisibleTo(json, currentTenantUserId)`
   (lista vacia -> visible; si no, visible solo si el usuario esta). No se usa consulta JSON en SQL (evita
   divergencias PG/SQL Server). El detalle (`GetBoardDetailAsync`) aplica el mismo chequeo y, si no pasa,
   responde NotFound (no revela la existencia).

3. **Bypass por rol en el LLAMADOR, no en el servicio**: el servicio no resuelve rol; los filtros
   (`ActivityBoardIndexFilter` / `ActivityBoardDetailFilter`) reciben `CurrentTenantUserId` y
   `CanSeeRestricted`. Cada pantalla pasa lo correcto:
   - Superficies de TRABAJO (ActivityBoardsIndex, ActivityBoardDetail, MovilTablero) pasan el
     `CurrentTenantUserId` del que mira y `CanSeeRestricted = (rol es Owner/Admin)`, leyendo el claim
     `tenant_role` y cruzando el claim `NameIdentifier` (PlatformUserId) contra `TenantUsersSvc.ListAsync()`.
   - Superficies de CONFIG/administracion (Tableros, FlowEditor picker, TaskDetailModal) pasan
     `CanSeeRestricted = true` (o el rol admin) para poder gestionar/mostrar el contexto.

4. **Self-serve**: en el administrador de tableros (`Tableros.razor`) el modal de crear/editar tiene la
   seccion "Visible para" con la lista de usuarios del tenant; vacio = disponible para todos. Persiste via
   `Create/UpdateActivityBoardRequest.AllowedUserIds` (null = no tocar; lista vacia = quitar restriccion).

## Opciones consideradas

- **A (elegida): campo JSON en el tablero + filtro en memoria + bypass en el llamador.** Simple, portable
  dual, sin tabla nueva, reusa la carga existente del indice. Contra: el bypass depende de que cada
  superficie pase el rol (documentado y con test del servicio).
- **B: tabla puente board_allowed_users (M:N).** Mas "normalizado" pero agrega entidad/joins y no aporta
  sobre una lista corta por tablero; descartada por complejidad.
- **C: restringir por Dependencia/Cargo (como ADR-0082).** Descartada: el requisito es explicitamente por
  usuario. (Se puede sumar despues reusando el picker del organigrama.)

## Consecuencias

- **Mas facil**: un tablero se limita a un grupo de usuarios sin codigo; los demas no lo ven ni pueden
  abrirlo por enlace directo; Owner/Admin siguen administrando todo.
- **A vigilar**: el gate de dominio es por usuario (TenantUser), no por rol/cargo. El bypass vive en el
  llamador; una pantalla nueva que liste tableros debe pasar `CurrentTenantUserId`+`CanSeeRestricted` o los
  tableros restringidos se ocultaran por defecto (fail-safe hacia lo restrictivo). No hay backfill: los
  tableros existentes quedan "para todos" hasta que se les configure una restriccion.

## Action Items

1. [x] `TaskBoard.AllowedUserIdsJson` + EF config + migracion dual `AddBoardAllowedUsers`.
2. [x] Filtro en `ListBoardsAsync` + bloqueo en `GetBoardDetailAsync` + persistencia Create/Update + helpers.
3. [x] DTOs: filtros (`CurrentTenantUserId`/`CanSeeRestricted`), requests (`AllowedUserIds`), summary.
4. [x] Cableado de llamadores (trabajo con usuario+rol; config con bypass).
5. [x] UI "Visible para" en `Tableros.razor` (vacio = todos).
6. [x] Test matriz dual (ActivityBoardTests) + PROGRESO. Build + tests 0 errores. NO desplegar (pedir OK).
