# ADR-0031: Modulo Administracion de usuarios del tenant (000073)

Fecha: 2026-07-07
Estado: Aceptado

## Contexto

El item de menu "Administracion de usuarios" (legacy 000073, seccion "Sistema . General")
existia como stub `modulo/administracion-de-usuarios`. El backbone SaaS YA traia casi todo
el backend de usuarios del tenant: la entidad `TenantUser` (vinculo PlatformUser <-> tenant
con rol interno, estado y vista de menu asignada), el servicio `ITenantUserService`
(`ListAsync`, `InviteAsync`, `ChangeRoleAsync`, `SetStatusAsync`), el hasher PBKDF2 y el
`IMenuConfigService.AssignUserToViewAsync`. Faltaban la pagina Blazor, la policy, un par de
metodos de servicio y enganchar el menu a la pantalla real.

Esta ola NO construye roles/permisos dinamicos (queda para una ola posterior): los roles del
tenant son el enum fijo `TenantRole` (Owner/Admin/Supervisor/Advisor).

## Decision

Reusar el backend existente y agregar SOLO lo minimo:

### Servicio ampliado (`ITenantUserService` / `TenantUserService`)

Dos metodos nuevos, con el mismo patron transaccional + `IAuditWriter` que `InviteAsync`,
tenant-scoped por el filtro global de consulta:

- `ResetPasswordAsync(tenantUserId, newPassword, actorUserId)`: el admin del tenant fija una
  clave nueva para un usuario. Hashea con PBKDF2, actualiza `PlatformUser.PasswordHash` y, si
  el usuario estaba `Invited`, lo pasa a `Active`. Valida clave no vacia y de minimo 6
  caracteres (`ArgumentException`). La auditoria registra el hecho y si reactivo la cuenta,
  **nunca la clave en claro**.
- `UpdateProfileAsync(tenantUserId, displayName, actorUserId)`: edita el `DisplayName` del
  `PlatformUser` (null/vacio lo deja sin nombre). Audita el cambio.

La asignacion de vista de menu NO se duplica: usa el `IMenuConfigService.AssignUserToViewAsync`
existente. `TenantUserDto` ya traia `DisplayName` opcional; el `Map` se amplio para poblarlo.

### Pagina Blazor

`Ecorex.SuperAdmin/Components/Pages/AdmUsuarios.razor`, `@page "/admin-usuarios"`,
`@attribute [Authorize(Policy = "AdmUsuarios.Editar")]`, render InteractiveServer, tokens
ECOREX (breadcrumb + module-head + tabla + modales, patron de `EquipoPlataforma.razor` y las
paginas de tenant recientes). Tabla con Usuario/Email/Rol/Estado (badges)/Vista de menu/
Acciones. Modales: Nuevo usuario (`InviteAsync`; vacio -> Invited, con clave -> Active; vista
opcional -> `AssignUserToViewAsync` tras crear), Editar (DisplayName/Rol/Estado/Vista),
Cambiar clave (`ResetPasswordAsync`, con confirmar y boton Generar). Toast de exito. Inyecta
`ITenantUserService`, `IMenuConfigService`, `ITenantContext` (el actor sale de
`ITenantContext.UserId`, con fallback al claim NameIdentifier).

### Policy

`AdmUsuarios.Editar` en `Program.cs`. Paso 1: `RequireClaim("tenant_id")` (igual que
TenantMember, no cambia el acceso). Paso 2 (pendiente): restringir a Owner/Admin del tenant
derivandolo del `tenant_role`, sin tocar la pagina.

### Menu (seed idempotente + reconciliacion)

`EnsureMenuConfigDemoAsync` ahora siembra el item 000073 apuntando a la pagina real
`admin-usuarios` (State Ready) en vez del stub. Como el seeder es idempotente por existencia
de vistas (no re-siembra si el tenant ya tiene vistas), se agrego un paso de **reconciliacion
idempotente** (`ReconcileMenuNodesAsync`, tenant-scoped): si la vista ya existe, actualiza (solo
si difieren) el Route/State/Name de los nodos con LegacyCode **000073** (-> `admin-usuarios`,
Ready) y **000194** (-> `configuracion-menu`, "Administrador de Menu", Ready). Asi un demo ya
sembrado refleja ambas paginas reales sin recrear la vista ni perder asignaciones de usuarios.

## Alternativas consideradas

- **Reescribir el backend de usuarios**: descartado; el backbone ya lo tenia funcional y
  auditado. Se reusa.
- **Migracion nueva**: no hizo falta. `TenantUser`/`PlatformUser` ya tenian todos los campos
  (rol, estado, MenuViewId, PasswordHash). Sin cambios de esquema.

## Consecuencias

- El tenant administra sus usuarios (invitar, rol, estado, clave, vista de menu) desde una
  pantalla real, con auditoria y aislamiento multi-tenant (verificado en Integration.Tests
  dual PG + SQL Server).
- Deuda:
  - Roles/permisos dinamicos (ola siguiente); hoy `TenantRole` es un enum fijo.
  - Invitacion por correo real (hoy la clave la fija el admin; el token de invitacion de
    `TenantUser` no se explota aun) y self-service de clave por el usuario.
  - Paso 2 de la policy `AdmUsuarios.Editar` (restringir a Owner/Admin via `tenant_role`).
  - Salvaguarda "no dejar el tenant sin ultimo Owner" NO implementada en esta ola.
