# ADR-0017: Dependencias (organigrama) y Modulos web (module registry) - FASE 5

- Estado: aceptada
- Fecha: 2026-07-03
- Relacionada con: ADR-0001 (DAL dual), ADR-0013 (nucleo TaskItem, resultados tipados),
  ADR-0016 (RulesEngine, patron de resultados)

## Contexto

FASE 5 de la hoja de ruta: los dos modulos de sistema del vault.

- **Dependencias (legacy 000850)**: el organigrama del tenant (areas, equipos,
  responsables y miembros). En el legacy era una tabla plana con parent implicito y sin
  ninguna proteccion contra ciclos: era posible dejar el arbol corrupto (una dependencia
  ancestro de si misma) y los listados recursivos se colgaban.
- **Modulos web (legacy 000109)**: el registro de modulos del sistema. En el legacy la
  tabla de modulos vivia POR BASE de tenant, duplicada y desincronizada entre
  sucursales: el mismo modulo tenia metadatos distintos en cada base y no habia forma de
  saber que tenia habilitado cada tenant sin entrar a su base.

## Decision

### 1. OrgUnit / OrgUnitMember: arbol tenant-scoped con validacion de ciclos tipada

- `OrgUnit` (TenantEntity): `Name(150)`, `Kind` enum `OrgUnitKind { Area, Team }`,
  `ParentId` self-FK con **NO ACTION** (una unidad con hijos no se borra por cascada),
  `ResponsibleTenantUserId?`, `Description?(600)`, `SortOrder`, `IsArchived`. Nunca hay
  DELETE fisico: se archiva (regla 5 del repo), y archivar exige no tener hijas activas.
- `OrgUnitMember` (TenantEntity): FK a la unidad con **cascade** (el miembro vive y
  muere con su unidad), unico `(OrgUnitId, TenantUserId)`, `Role?(100)` funcional.
- **Ciclos**: `OrgUnitTree.WouldCreateCycle` es una funcion PURA (unit-testeable sin BD)
  que camina la cadena de ancestros del padre propuesto sobre el mapa `Id -> ParentId`
  del tenant, con set de visitados para que un arbol ya corrupto tambien se reporte como
  ciclo (fail-closed) en vez de colgar el servicio. `OrgUnitService.UpdateAsync` la
  ejecuta en cada cambio de padre y devuelve `Invalid` tipado; el legacy no validaba nada.
- KPIs del prototipo (Dependencias / Usuarios / Areas) se calculan en el servicio:
  usuarios = responsables + miembros DISTINTOS de unidades activas.

### 2. ModuleDefinition GLOBAL (sin TenantId, sin query filter) + TenantModule scoped

El catalogo `ModuleDefinition` es **global de plataforma a proposito**:

- Es un CATALOGO (codigo legacy 6 digitos unico, nombre, ruta, area, IsCore), no un dato
  operativo de negocio: la misma definicion vale para todos los tenants. Duplicarla por
  tenant reintroduciria exactamente la desincronizacion del legacy (metadatos distintos
  del mismo modulo por base).
- Por eso NO implementa `ITenantScoped` y NO recibe `HasQueryFilter` (el filtro global
  se aplica por reflexion SOLO a `ITenantScoped`; tener columna TenantId no es lo mismo
  que ser tenant-scoped, ver `ITenantScoped` en Domain.Common). Es el mismo trato que
  `SaasPlan` o `PlatformBranding`: administracion de plataforma.
- **Solo el PlatformAdmin edita el catalogo** (`UpsertDefinitionAsync`; cualquier UI o
  endpoint que lo exponga debe exigir la policy existente `SuperAdminOnly` /
  `PlatformOperator`). Los tenants lo LEEN (`ListCatalogAsync`).

Lo que SI es por tenant es el **estado**: `TenantModule` (TenantEntity) con
`ModuleDefinitionId` (FK **Restrict**: borrar catalogo no arrastra estados),
`IsEnabled` y `SettingsJson` como documento JSON dual (jsonb en PostgreSQL,
nvarchar(max) en SQL Server, mismo patron de FormResponse.Data). Unico
`(TenantId, ModuleDefinitionId)`. Sin fila = modulo deshabilitado (opt-in explicito),
salvo que el seeder los habilite. Los modulos `IsCore` no se pueden deshabilitar.

### 3. GetEnabledModulesAsync: la semilla del menu por registry

`IModuleRegistryService.GetEnabledModulesAsync(tenantId)` devuelve los modulos
habilitados con ruta/area/codigo, pensado para que el menu de la consola se derive del
registry. Es **fail-closed**: con tenant ambiente distinto del pedido devuelve vacio;
solo sin tenant ambiente (PlatformAdmin / procesos de plataforma) consulta un tenant
explicito via `IgnoreQueryFilters` acotado por `TenantId`.

TODO(policies por modulo): cuando existan policies propias (ej. "Modulo.000850.Usar"),
el NavMenu se construira desde este metodo filtrando por policy; hoy los toggles de la
UI /modulos-web se limitan a owner/admin del tenant (claim `tenant_role`) o a un
operador de plataforma (claim `platform_role`), y las paginas siguen bajo `TenantMember`.

### 4. Resultados tipados y UI

`OrgResult<T>` / `OrgServiceStatus` siguen el patron de ADR-0013/0016 (Ok / NotFound /
Invalid / Conflict, sin excepciones hacia la presentacion). La UI (/dependencias con
arbol de cards CSS puro + panel de detalle, /modulos-web con tabla del catalogo, toggle
por tenant y modal de settings JSON validado) replica las capturas del prototipo
(04, 04b y 05) con los tokens existentes (`--surface/--ink/--line/--t-*`) y fallback a
las variables legacy, sin tocar app.css.

## Consecuencias

- Migracion dual `AddOrgAndModuleRegistry` (PG + SQL Server): `org_units`,
  `org_unit_members`, `module_definitions` (indice unico por `legacy_code`),
  `tenant_modules` (indice unico `(tenant_id, module_definition_id)`).
- Seeders idempotentes de Development: organigrama demo de 5 unidades para SKY SYSTEM
  (Direccion General > Comercial / Tecnologia > Desarrollo / Gestion Humana, owner
  responsable de la raiz, miembros demo) y catalogo global de 11 modulos reales
  (000038, 000042, 000636, 000889, 000291, 000131, 000802, 000850, 000109, 000788
  placeholder, 000867 placeholder) todos habilitados para SKY SYSTEM.
- Tests: 8 unit (ciclos puros) + 4 de integracion x 2 motores (arbol CRUD + ciclo
  rechazado; aislamiento cross-tenant OrgUnit/TenantModule; catalogo global visible
  desde ambos tenants con estado aislado; toggle por tenant + proteccion IsCore).
- Deuda: el menu sigue estatico (se derivara del registry al existir policies por
  modulo); el ETL de FASE 6 debe mapear las dependencias reales del tenant 01 (BITCODE).
