# ADR-0026: Administracion de empresas (000072) como ficha de gobierno de tenants, aditiva y segura

- Estado: aceptada
- Fecha: 2026-07-05
- Modulo: NEWFRONT_adm_empresas (000072) -> pagina `/admin/empresas` en Ecorex.SuperAdmin
- Relacionada con: Capa 1 (Gestion de Empresas - Admin multi-tenant, los 9 errores),
  ADR-0011 (backbone SaaS: Tenant/Plan/Suscripcion ya existentes),
  ADR-0023 (estructura del concepto por-modulo + tokens del workspace),
  ADR-0025 (mismo molde de pagina: reemplaza stub + migracion dual pequena + seeder + E2E +1)

## Contexto

El legacy `NEWFRONT_adm_empresas.aspx` es la consola multi-tenant de ECOREX: crea/edita
empresas (SUCURSAL) y concentra TODA su configuracion secundaria en acordeones (modulos,
actividades, parametros, integraciones, usuarios, reglas) MAS utilidades peligrosas para
clonar datos entre empresas (copiar `sys.tables` con columna SUCURSAL via `INSERT..SELECT`
con blacklist hardcodeada), copiar formularios (5+ INSERT sueltos con remapeo de IDs por
subqueries a `db3dev.dbo.*`) y traer datos de una BD externa arbitraria. La spec de Capa 1
documenta que este modulo concentra varios de los 9 errores heredados: aislamiento por
disciplina, SQL concatenado, operaciones multi-tabla sin transaccion, DELETE sin cascada,
secretos en texto plano, `db3dev` y blacklists hardcodeadas.

El backbone SaaS del repo (heredado de CUBOT.nails) YA corrige esos errores por
construccion: `Tenant` global con `Status`/`Kind`, `HasQueryFilter` multi-tenant + RLS,
transacciones, auditoria inmutable (`SuperAdminAuditLog` via `IAuditWriter`), secretos con
DataProtection, y una consola PlatformAdmin operativa con paginas `/tenants` (alta,
listado, cambio de estado, suscripcion), `/plans`, `/facturacion` y servicios
`ITenantAdminService` / `IOnboardingService` / `ISubscriptionAdminService` /
`IPlanAdminService`. NO hay que reconstruir el CRUD de tenants: ya existe.

## Decision de AREA: PlatformAdmin, no tenant

La ficha 000072 es GOBIERNO multi-tenant (crear/editar la ficha de CUALQUIER empresa, ver
sus usuarios cross-tenant). Segun la Capa 1, eso es responsabilidad del operador de
plataforma (rol formal `PlatformAdmin`), no de un miembro del tenant. Por eso:

- La pagina vive en el AREA PlatformAdmin de la consola unificada, junto a `/tenants` y
  `/plans`. Ruta `/admin/empresas`.
- Se protege con la policy nueva `AdmEmpresas.Ver` = `RequireClaim("platform_role")`
  (mismo requisito que `PlatformOperator`), NO con `tenant_id`.
- El item 000072 del NavMenu se MOVIO del menu del tenant (grupo "Sistema - General",
  donde estaba mal con policy `TenantMember`) al bloque SUPER ADMIN SAAS como
  "Ficha de empresa 000072", junto a "Empresas". El contador del grupo del tenant paso de
  8 a 7 y su stub se retiro del registro de `Modulo.razor`.

Se reutiliza `Tenants.razor` (listado) para el alta ("+ Nueva empresa" enlaza ahi): la
ficha es la vista de DETALLE enriquecida que faltaba, no un segundo CRUD.

## Decision de ALCANCE: mapear a lo REAL, placeholders para el resto

### Campos REALES implementados (de verdad)

Ficha basica editable, mapeada al modelo `Tenant` existente + una migracion aditiva PEQUENA:

| Campo de la ficha | Origen legacy | Destino real |
|---|---|---|
| Razon social | SUCURSAL.NOMBRE_REAL | `Tenant.LegalName` (ya existia) |
| Nombre comercial | SUCURSAL.NOMBRE | `Tenant.Name` (ya existia) |
| NIT / TaxId | SUCURSAL.NIT | `Tenant.TaxId` (ya existia) |
| Pais / Moneda | - | `Tenant.Country` / `Currency` (ya existian) |
| Estado | SUC_ESTADOS | `Tenant.Status` via `ChangeStatusAsync` (maquina de estados existente, auditada) |
| Ciudad | SUCURSAL.CIUDAD | `Tenant.City` (**migracion AddTenantProfile**) |
| Direccion | SUCURSAL.DIRECCION | `Tenant.Address` (**migracion AddTenantProfile**) |
| Telefono | SUCURSAL.TELS | `Tenant.Phone` (**migracion AddTenantProfile**) |
| Email de contacto | SUCURSAL.EMAIL | `Tenant.Email` (**migracion AddTenantProfile**) |
| Usuarios del tenant | USUARIO.SUCURSAL | `TenantUser` (solo lectura: email, rol, estado) |

La edicion pasa por los servicios EXISTENTES: `ITenantAdminService.UpdateProfileAsync`
(extendido con los 4 campos nuevos) para el perfil y `ChangeStatusAsync` para el estado;
ambos escriben `SuperAdminAuditLog` en su transaccion. NO se duplico CRUD. Se agrego
`ITenantAdminService.ListUsersAsync(tenantId)`: acceso cross-tenant ACOTADO al tenant
pedido (`IgnoreQueryFilters` + `Where(TenantId == id)`), permitido solo al operador de
plataforma (por policy). Es el unico cross-tenant que introduce este modulo.

`AddTenantProfile` es una migracion DUAL puramente aditiva (4 columnas `nullable`, sin
drops de datos), generada y aplicada en PostgreSQL (5442) y SQL Server (1443) y verificada
en ambos motores.

### Secciones PLACEHOLDER (visible-deshabilitadas, tooltip "Pendiente")

Para preservar la estructura del proto sin reconstruir lo que el modelo aun no soporta ni
los flujos peligrosos, las siguientes secciones quedan visibles, atenuadas y con
explicacion. NO son navegacion muerta: comunican el gap.

| # | Seccion | Por que es placeholder |
|---|---|---|
| 02 | Modulos habilitados | La asignacion por empresa ira con un servicio transaccional (ya existe `TenantModule` + registro de modulos; falta la UI de asignacion). |
| 03 | Actividades | `TIPO_TAR_EMPRESA` -> servicio propio por empresa, pendiente. |
| 04 | Cargar datos | **Error heredado**: copiar `sys.tables` via SQL con blacklist. Se reemplaza por plantillas versionadas transaccionales; NO se reconstruye el flujo peligroso. |
| 05 | Copiar formularios | **Error heredado**: 5+ INSERT sueltos + remapeo de IDs por subqueries a `db3dev`. Ira como plantillas de formulario transaccionales. |
| 06 | Datos externos | **Error heredado**: cadena de conexion arbitraria en un TextBox (inyeccion/SSRF). Requiere un conector gobernado. |
| 07 | Reglas | Se integrara con el RulesEngine existente en fase dedicada. |
| 09 | Configuraciones (parametros) | `SUCURSAL_PAR` -> `TenantParameter` con enforcement de plan (error #9). |
| 10 | Integraciones | Requiere boveda de secretos cifrados por empresa (error #6): plugins con secretos en DataProtection/Key Vault, nunca en texto plano. |
| C1 | Contador y revisor fiscal | El modelo `Tenant` aun no tiene esas columnas; se agregaran como entidad owned cuando se prioricen. |

## Por que NO se reconstruyen los flujos SQL peligrosos

Las utilidades "Cargar datos", "Copiar formularios" y "Datos externos" del legacy son
exactamente los 9 errores en accion: SQL concatenado, sin transaccion, con blacklists y
`db3dev` hardcodeados, y una cadena de conexion externa controlada por el usuario. La
CLAUDE.md del repo (reglas inviolables) y la Capa 1 los prohiben. Reconstruirlos "tal cual"
seria heredar los errores que toda esta migracion existe para eliminar. La ruta correcta
(documentada como deuda) es plantillas de tenant versionadas y transaccionales, con remapeo
de IDs por el ORM, sin literales de entorno ni blacklists. Por eso hoy son placeholders con
la explicacion a la vista, no botones que ejecutan SQL.

## Relacion con la consola PlatformAdmin existente

La ficha COMPLEMENTA, no reemplaza, `/tenants`:
- `/tenants`: listado + alta (onboarding) + cambio de estado rapido + suscripcion.
- `/admin/empresas`: DETALLE por empresa (ficha editable, usuarios, plan/estado en contexto,
  mapa de lo pendiente). Reutiliza los mismos servicios; no hay CRUD duplicado.

## Estructura visual (proto + tokens del workspace, ADR-0023)

Se replican las MEDIDAS del proto `proto_adm_empresas.html` con los TOKENS del workspace
(regla ADR-0023, mismo mapeo que ADR-0024/0025): `accent -> --brand`, `success -> --ok/--t-green`,
`warn -> --t-amber`, `danger -> --danger/--t-rose`, `muted -> --ink-2/--ink-3`,
`card -> --surface`, `border -> --line`. Topbar 14x24 con breadcrumb + MOD 000072, layout
grid 300px/1fr, sidebar sticky selector de empresa, header-card r10 con avatar gradiente,
KPIs 5 columnas, secciones colapsables (details/summary nativo con chevron) numeradas.
Como el CSS usa exclusivamente tokens del workspace, el tema claro/oscuro conmuta por
construccion (verificado). Los KPIs de modulos/actividades/reglas y las secciones no
soportadas llevan el tag ambar "Pendiente".

## Consecuencias

- Positivas: la ficha del operador existe y edita datos reales de forma segura y auditada;
  la estructura del proto queda fiel sin heredar los flujos peligrosos; migracion minima y
  aditiva en ambos motores; sin duplicar CRUD de tenants.
- Costos / deudas: las 9 secciones placeholder son promesas visibles; cada una necesita su
  propia ola (asignacion de modulos/actividades/parametros/integraciones por empresa,
  contador/revisor como entidad owned, plantillas transaccionales para clonar datos y
  formularios). La policy `AdmEmpresas.Ver` hoy solo exige `platform_role` (paso 1: nombre
  estable); el paso 2 (derivar del rol real / MFA para acciones criticas) queda pendiente
  como en el resto de policies del proyecto.
