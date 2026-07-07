# ADR-0033: Enforcement real de permisos por rol (Ola B2)

- Estado: Aceptado
- Fecha: 2026-07-07
- Contexto: Ola B2 del sistema de permisos. La Ola B1 (ADR-0032) dejo el modelo (Rol/RolPermiso +
  TenantUser.RolId), la resolucion efectiva (`ResolveEffectivePermissionsAsync`) y la UI, pero NO
  aplicaba los permisos en el backend. Esta ola HACE CUMPLIR la matriz Modulo x Accion.

## Contexto

B1 dejaba las policies exigiendo solo `tenant_id` (paso 1): la matriz existia pero no restringia
nada. B2 debe aplicar el set efectivo por (ModuleKey, Accion) SIN bloquear a los usuarios que hoy
operan sin rol, y sin romper la consola si la resolucion de permisos falla.

## Decision

### Regla opt-in / back-compat (no negociable)

El enforcement es OPT-IN. `EffectivePermissions` gana un eje nuevo, `Unrestricted`:

- **Owner/Admin** (TenantRole) -> `AllowAll` (implica `Unrestricted`): mandan por gobierno.
- **Usuario SIN RolId** (o TenantUser no resoluble) -> `Unrestricted` (NO `AllowAll`): no hay matriz
  que aplicar, conserva el acceso del paso 1. **Cambia** respecto de B1, que devolvia `Empty()`
  (todo denegado). El motivo: no bloquear a operator/viewer/otros que hoy no tienen rol.
- **Usuario CON RolId** -> queda sujeto a su matriz; `Unrestricted=false`.

`Can(module, action)` y `For(module)` devuelven acceso total cuando `Unrestricted`. Solo un usuario
con rol ve `ModuleAccess.None` en un modulo ausente de su matriz. La logica pura vive en
`PermissionResolver` / `EffectivePermissions` (Ecorex.Application, sin EF, testeable).

### Autorizacion dinamica por permiso (via nueva, aditiva)

- `PermissionRequirement(moduleKey, action)` + `PermissionAuthorizationHandler`: concede si
  `Unrestricted` o si la matriz permite (ModuleKey, Accion). Consulta `ICurrentPermissions`.
- `PermissionPolicyProvider : IAuthorizationPolicyProvider`: para nombres `Perm:{moduleKey}:{action}`
  (action in View/Create/Edit/Delete) crea al vuelo una policy = `RequireClaim("tenant_id")` +
  `PermissionRequirement`. Para cualquier otro nombre DELEGA en el `DefaultAuthorizationPolicyProvider`,
  de modo que TODAS las policies existentes (Inventario.Ver, AdmUsuarios.Editar, TenantMember,
  PlatformOperator, ...) siguen intactas.
- Registro en `Program.cs`: provider (singleton), handler (scoped), `ICurrentPermissions` (scoped).
- Las policies clasicas NO se borran: es una via NUEVA.

### `ICurrentPermissions` (scoped, fail-open)

Resuelve UNA vez por circuito/scope el `EffectivePermissions` del usuario actual (claim
NameIdentifier = PlatformUserId), en un scope propio (patron de NavMenu con la marca/menu) para no
compartir el DbContext del circuito. Cachea el resultado. Expone
`CanView/CanCreate/CanEdit/CanDelete(moduleKey)` y `Unrestricted`. **Fail-OPEN**: si no hay usuario
o la resolucion lanza, devuelve `Unrestricted` (no bloquea la consola). Documentado a proposito: en
una pantalla de gobierno preferimos degradar a "como antes" antes que dejar sin acceso por un fallo
transitorio de datos.

### Menu filtrado por "Ver"

`MenuPermissionFilter` (Ecorex.Application, pure) poda del arbol resuelto los Item cuyo Route sea un
modulo con View=false, y oculta Section/Subgroup que quedan sin hijos visibles. QuickLinks e Item
sin Route no se tocan. `Unrestricted` (o permisos null) -> arbol intacto. NavMenu resuelve los
permisos en su scope y aplica el filtro sobre el resultado de `GetMenuForTenantUserAsync`.

### Alcance de paginas (referencia; resto = follow-up)

Migradas a la policy de permiso `Perm:{route}:View` (mantienen el gate de tenant via el
RequireClaim del provider):

- `InventarioItems.razor` -> `Perm:inventario-items:View` + botones Crear/Editar/Archivar gateados.
- Catalogos de inventario (`InventarioBodegas/Marcas/Grupos/Subgrupos/Tipos`) -> `Perm:{su-route}:View`.
- `AdmUsuarios.razor` -> `Perm:admin-usuarios:View` + Nuevo/Editar/Cambiar clave/Suspender y el
  selector de rol gateados.
- `RolesPermisos.razor` -> `Perm:roles-permisos:View` + Nuevo/Editar/Eliminar/Guardar/Asignar
  gateados.

El wiring del RESTO de modulos (Tareas, Proyectos, Flujos, Formularios, Reglas, Conceptos, etc.) a
su `Perm:{route}:View` es **follow-up mecanico**: basta cambiar el `[Authorize(Policy=...)]` de cada
pagina y (opcional) gatear sus botones. No requiere tocar el provider/handler.

### Seed demo del "Asesor limitado" (demostrable)

`EnsureRolesDemoAsync` ahora deriva la seccion de cada modulo (subiendo por ParentId hasta la
Section) y arma la matriz del "Asesor limitado":

- **SIN Ver** en Sistema . Desarrollo (dev), Sistema . CRM (syscrm) y CRM heredado (crm).
- **CON Ver** en el resto (Mis Procesos, Inventarios, Automatizacion, etc.).
- **Crear** solo en tareas/proyectos (`actividades`, `crear-actividad`, `proyectos`); NO en
  inventario (asi `/inventario-items` no muestra "Nuevo item" al Asesor aunque SI pueda verlo).
- Nunca Editar ni Eliminar.

Idempotente: si los roles ya existen, RECONCILIA (borra e reinserta) la matriz del Asesor limitado
para aplicar el recorte, sin tocar el rol de sistema "Administrador".

## Consecuencias

- owner/admin/operator/viewer (Owner/Admin o sin rol) NO pierden acceso: son Unrestricted. Solo
  `simple@sky-system.local` (Asesor limitado) queda sujeto a su matriz -> su sidebar se ve recortado
  y sin botones de Crear donde no aplica.
- Las policies clasicas quedan como estaban; conviven con la via `Perm:`.
- Deudas: (1) wiring del resto de paginas a `Perm:{route}:View`; (2) las policies clasicas
  (`Inventario.Ver`, etc.) quedan sin uso en las paginas migradas pero se conservan por compat;
  (3) el claim `Permissions` del token sigue sin poblarse (la consola resuelve por servicio, no por
  claim) - pendiente si algun consumidor externo lo necesita.

## Pruebas

- Unit (Application.Tests): `PermissionResolver` (sin rol -> Unrestricted; Owner/Admin -> AllowAll+
  Unrestricted; con rol -> no Unrestricted) y `MenuPermissionFilter` (poda por View, oculta secciones
  vacias, conserva QuickLinks/Item sin Route, filtra subgrupos anidados).
- Unit (SuperAdmin.Tests): `PermissionPolicy.TryParse`, `PermissionAuthorizationHandler`
  (Unrestricted/Owner allow; con rol respeta Can; deniega sin permiso) y `CurrentPermissions`
  (resuelve/cachea una vez; fail-open sin usuario y ante excepcion).
- Integracion DUAL (Integration.Tests, PG + SQL Server): un rol limitado -> el arbol resuelto
  EXCLUYE los modulos/secciones sin Ver; Owner y usuario-sin-rol obtienen el menu completo.
- E2E (E2E.Tests/PermissionEnforcementTests): simple@ no ve Desarrollo/CRM pero si Mis Procesos/
  Inventarios y `/inventario-items` sin boton de crear; owner@ ve todo con boton.
