# ADR-0032: Roles de permisos dinamicos con matriz Modulo x Accion (Ola B1)

- Estado: Aceptado
- Fecha: 2026-07-07
- Contexto: Ola B1 del sistema de permisos. Inspirado en el hermano Visal (Rol + RolPermiso).

## Contexto

ECOREX necesita permisos finos configurables por el tenant, sin codigo: cada tenant define sus
propios roles y, por cada modulo real del sistema, marca que puede hacer un usuario con ese rol
(Ver / Crear / Editar / Eliminar). El backbone ya trae `TenantRole` (Owner/Admin/Supervisor/
Advisor), pero ese enum modela **poder organico** (quien gobierna la cuenta), no permisos finos.

## Decision

### Modelo (tenant-scoped)

- `Rol : TenantEntity` — `Name` (unico por tenant), `Description?`, `IsActive` (default true),
  `IsSystem` (protege el rol "Administrador" de borrado y renombrado).
- `RolPermiso : TenantEntity` — `RolId` (FK -> Rol, **cascade**), `ModuleKey` (string = Route del
  MenuNode Item), `CanView` / `CanCreate` / `CanEdit` / `CanDelete`. Unico `(RolId, ModuleKey)`.
- `TenantUser.RolId` (Guid?, nullable, FK **NO ACTION**) — rol de permisos del usuario. Null = sin
  rol fino.

Indices: `Rol` unico `(TenantId, Name)` + `(TenantId, IsActive)`; `RolPermiso` unico
`(RolId, ModuleKey)` + `(TenantId, RolId)`; `TenantUser` indice en `RolId`.

DAL dual: FK `Rol -> RolPermiso` **cascade** (el permiso vive y muere con su rol); FK
`TenantUser -> Rol` **NO ACTION** (borrar un rol no arrastra al usuario; el servicio bloquea el
borrado de un rol con usuarios). Migracion dual `AddRoles` (Ecorex.Infrastructure +
Ecorex.Infrastructure.SqlServer), aplicada y verificada en PostgreSQL (5442) y SQL Server (1443).

### Catalogo de modulos derivado del menu

La matriz **no** tiene un catalogo hardcodeado clinico (como Visal). El catalogo sale de los nodos
`MenuNode` `Kind=Item`, `State=Ready`, con `Route`, de la vista `IsDefault` del tenant:
`Key = Route`, `Label = Name`, `Grupo = nombre de la Section ancestro`. Un modulo real = un Route
unico. Si el tenant aun no tiene menu, cae a `ModuleCatalogFallback` (catalogo minimo). Asi la
matriz siempre refleja las paginas reales que existen, sin listas paralelas que se desincronicen.

### `TenantRole` (poder organico) vs `Rol` (permisos finos)

Son ejes distintos y **coexisten**. La resolucion de permisos efectivos
(`ResolveEffectivePermissionsAsync`) es:

- `TenantRole` Owner/Admin -> `AllowAll` (mandan por gobierno, ignoran el rol de permisos).
- Con `RolId` -> set del rol (mapa `ModuleKey -> {View,Create,Edit,Delete}`, helper `Can(mod, acc)`).
- Sin `RolId` -> vacio (todo denegado).

La logica pura vive en `PermissionResolver` / `EffectivePermissions` (sin EF, testeable).

### Enforcement = Ola B2 (deuda explicita)

B1 entrega **modelo + servicio + UI + resolucion efectiva**, pero **no** aplica los permisos en el
backend. Las policies siguen exigiendo solo `tenant_id` (paso 1). El claim `Permissions` del token
(`AuthService.BuildToken`) NO se pobla todavia. La Ola B2 usara `ResolveEffectivePermissionsAsync`
para hacer cumplir el set por modulo (policies/endpoints) y poblar el token.

## Consecuencias

- La pagina `/roles-permisos` (policy `RolesPermisos.Administrar`, item de menu 000198 en
  "Sistema . General") permite CRUD de roles, la matriz con utilidades marcar/desmarcar
  fila/columna/grupo, y asignacion de rol a usuario. `AdmUsuarios` gana una columna "Rol de permisos".
- Seed demo: rol de sistema "Administrador" (todos los modulos, todo en true) + "Asesor limitado"
  (Ver general + Crear en tareas/inventario, sin Eliminar) asignado a `simple@sky-system.local`.
- Un rol de sistema no se borra ni se renombra; un rol con usuarios no se borra (mensaje claro).
