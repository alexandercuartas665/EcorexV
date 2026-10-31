# ADR-0136: Alcance por usuario en tableros (default "mis tareas" + quien ve todo)

**Estado:** Aceptado
**Fecha:** 2026-10-27
**Version:** v0.16.237
**Deciders:** usuario (producto) + agente de desarrollo

## Contexto

Los tableros de actividades (ADR-0020, gestor unificado 000636) abrian por defecto en el
alcance "Todas del equipo": cada usuario veia TODAS las tareas del tablero. El usuario pidio que,
por tablero, cada quien **vea solo su carga** por defecto, y poder **configurar que usuarios SI
pueden ver todas las tareas**.

Ya existia el alcance por usuario DENTRO del tablero (chips "Todas del equipo / Pendientes mios /
No asignados / Terminados", `ActivityBoardScope`), y el patron de restriccion por usuario a nivel
de VISIBILIDAD del tablero (`TaskBoard.AllowedUserIdsJson`, ADR-0117). Faltaba una restriccion de
ALCANCE: que el default sea "mis tareas" y que el ver-todo quede limitado a una lista.

## Decision

Nuevo campo por tablero `TaskBoard.SeeAllUserIdsJson` (arreglo JSON de TenantUserId), gemelo de
`AllowedUserIdsJson` pero para el alcance interno:

1. **Null/vacio = sin restriccion**: comportamiento clasico (default "equipo", todos ven todo).
2. **Con usuarios = restriccion ACTIVA**:
   - El tablero abre por defecto en **"Pendientes mios"** para cada usuario.
   - Pueden ver TODO (alcances "equipo"/"terminados") **solo** los usuarios de la lista, **mas
     Owner/Admin** (bypass, igual que AllowedUserIds).
   - Los demas quedan con **bloqueo duro**: solo "Pendientes mios" + "No asignados" (para tomar
     trabajo libre). Decidido con el usuario: ve mias + no asignadas, nunca las de otros.

El enforcement es **en el servidor** (`ActivityBoardService.GetBoardDetailAsync`): si el que
consulta no puede ver todo, el alcance pedido se coacciona a "mis tareas" aunque el cliente pida
"equipo"/"terminados", y los contadores de equipo/terminados no se calculan ni se revelan. La UI
ademas oculta esos chips (`@if (_canSeeAllTasks)`), pero la seguridad no depende de la UI.

Config **self-serve** en el administrador (`/tableros`): segundo selector de usuarios "Quien ve
todas las tareas", clon del selector "Visible para".

### Detalle tecnico

- `TaskBoard.SeeAllUserIdsJson` (jsonb PG / nvarchar(max) SQL Server). Migracion dual
  `AddBoardSeeAllUsers` (solo la columna, nullable).
- DTOs: `SeeAllUserIds` en Create/Update/Summary; `SeeAllRestrictionOn` + `CanSeeAllTasks` en el
  detalle para que el componente decida default y chips sin re-derivar.
- `ActivityBoardDetail`: computa el permiso desde el resumen del tablero (que ya trae
  SeeAllUserIds) antes del primer load -> default "mis tareas" cuando la restriccion esta activa;
  sincroniza con el detalle (autoritativo) tras cada recarga; `SetScopeAsync` ignora
  equipo/terminados si no puede ver todo.
- Reutiliza los helpers `ParseUserIds`/`SerializeUserIds` (null/vacio = sin restriccion).

## Consecuencias

- Tableros nuevos y existentes **no cambian** mientras la lista este vacia (rollout seguro, sin
  siembra).
- Al poblar la lista, el tablero pasa a "cada quien ve su carga" + algunos ven todo, sin codigo.
- El bloqueo es por construccion en el servidor; una peticion directa con otro alcance no evade la
  restriccion.
- Owner/Admin siempre ven todo (para administrar), coherente con ADR-0117.
- Relacionado con, pero independiente de, ADR-0117 (ese controla QUIEN ABRE el tablero; este
  controla QUE TAREAS ve dentro).

## Validacion

Build verde, 1322 tests unitarios verdes, migracion PG aplicada al dev local. E2E en navegador
(AGROMETALICAS, Owner): el selector guarda `see_all_user_ids_json`; un tablero con la restriccion
activa abre en "Pendientes mios"; el Owner conserva los 4 chips (bypass). El caso del usuario
Member (chips equipo/terminados ocultos) queda para validar con un usuario no-admin real.
