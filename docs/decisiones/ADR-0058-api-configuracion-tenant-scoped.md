# ADR-0058 - API REST de configuracion tenant-scoped (Contenedores / Conectores / Agentes)

- Estado: Aceptado
- Fecha: 2026-08-03
- Contexto: ECOREX.tareas, proyecto de prod `apps/backend/src/Ecorex.SuperAdmin`.

## Contexto y proposito

Se necesita configurar por HTTP, de punta a punta y SIN la UI Blazor, toda la maquinaria del
Contenedor de datos: Contenedores (modelos + tablas), Conectores REST (con su modelo completo:
TokenExchange de 2 pasos, headers arbitrarios como `Partner-Id`, paginacion, mapeo campo->columna,
modo Append/Replace/Upsert) y Agentes Colmena (`data_clients`). El caso guia es dejar operable el
conector Siigo del contenedor `siigo/clientes` de AGROMETALICAS: emitir credencial, crear el
conector, poner el secreto, hacer probe y correr un Upsert por `siigo_id`.

A diferencia de la API de gestion de agentes (ADR-0057), que es de GOBIERNO cross-tenant, esta es
una API de INQUILINO: cada llamada opera dentro de UN tenant y nunca puede cruzar a otro.

## Decision

Grupo Minimal API bajo `/api/config`, en `Ecorex.SuperAdmin` (el app que se despliega a prod;
`Ecorex.Api` no se despliega), definido en `Endpoints/ConfigApiEndpoints.cs` y mapeado en
`Program.cs`, siguiendo el patron de `AgentMgmtEndpoints`. Es una capa DELGADA: envuelve los
servicios de aplicacion existentes y solo mapea DTO<->HTTP. No reimplementa logica.

### Autenticacion: Bearer opaco per-tenant (nunca cross-tenant)

- Entidad nueva `TenantApiToken` (tenant-scoped): `TenantId`, `Name`, `TokenHash` (SHA-256 hex del
  token; NUNCA el valor en claro), `Scope`, `CreatedAt`, `RevokedAt?`, `LastUsedAt?`. El hash es
  UNICO GLOBAL: por el se resuelve el tenant del Bearer entrante. Migracion dual `AddTenantApiTokens`
  (PostgreSQL + SQL Server), encadenada tras `AddCiudadCatalog`.
- Resolucion: `Authorization: Bearer <token>` -> SHA-256 -> se busca (con `IgnoreQueryFilters`, unica
  lectura sin tenant activo, por hash opaco) un token activo (`RevokedAt == null`) -> su `TenantId` se
  fija con `AmbientTenantContext.Begin` en un scope de DI aislado (igual que el webhook entrante). A
  partir de ahi el filtro global de tenant aisla TODO. Sin match: 401. El token FIJA el tenant; es
  imposible por construccion tocar otro.
- Emision (bootstrap): `POST /api/config/tokens` esta gateado por la COOKIE de admin del tenant
  (claim `tenant_id` + `tenant_role` Owner/Admin). Devuelve el valor EN CLARO UNA sola vez y guarda
  solo el hash. Flujo: un admin entra al panel, emite el token, lo copia una vez, y la sesion externa
  lo usa como Bearer. `GET /api/config/tokens` lista sin el valor; `POST /api/config/tokens/{id}/revoke`
  lo revoca (idempotente).
- Auditoria: toda mutacion (token/connector/secret/run/agent) escribe en `super_admin_audit_logs` via
  `IAuditWriter`, `actorType=System`, `reason="config-api"`, con `tenantId` (regla #5 de CLAUDE.md).
  Como el Bearer no lleva identidad de usuario, el actor es el sistema (Guid.Empty).

### Endpoints (FASE 1)

Tokens (cookie admin): `POST /tokens`, `GET /tokens`, `POST /tokens/{id}/revoke`.
Contenedores (Bearer, solo lectura): `GET /containers`, `GET /containers/{id}`.
Conectores (Bearer): `GET /connectors` (opcional `?model=`), `GET /connectors/{id}`,
`POST /connectors` (upsert idempotente por nombre), `PUT /connectors/{id}`, `DELETE /connectors/{id}`,
`PUT /connectors/{id}/secret`, `POST /connectors/{id}/probe`, `POST /connectors/{id}/run`,
`GET /runs/{id}`.
Agentes Colmena (Bearer): `GET /agents` (con `lastSeenAt` best-effort), `POST /agents` (registrar ->
`clientId` + `clientSecret` una vez).

### Reuso (no se duplica modelo ni logica)

- Contenedores: `IDataModelService` (`ListAsync`/`GetAsync` = modelo + tablas + columnas).
- Conectores: `IDataImportConfigService.SaveConnectorAsync`/`ListConnectorsAsync`/`DeleteConnectorAsync`.
  El body cubre el modelo RestApi completo reusando `ConnectorHeader`, `TokenExchangeConfig` y
  `ConnectorRestConfig` (headers + token exchange) y persiste el mapeo como el mismo RestFetchSpec
  (`MappingJson`) que consume el agente.
- Secreto: viaja en claro sobre TLS y lo cifra `SaveConnectorAsync` via `ISecretProtector`; nunca se
  devuelve (el DTO solo expone `HasCredentials`).
- Probe: `IApiImportService.ProbeAsync` (ya aplica headers + TokenExchange).
- Run: `IApiImportService.ImportAsync` (server-direct, sincrono). El nuevo helper puro y testeable
  `ConnectorRunPlanner` traduce el `MappingJson` persistido + las columnas destino + el modo/columna
  clave de la corrida en un `ApiImportRequest`. El estado se guarda en `ConfigRunStore` (singleton en
  memoria) y lo consulta `GET /runs/{id}`.
- Agentes: `IAgentClientService` (`ListAsync`/`SaveAsync`).

### Idempotencia

`POST /connectors` hace upsert por NOMBRE dentro del contenedor (re-ejecutable). El `run` acepta el
modo (Upsert por columna clave) como POLITICA de la corrida, de modo que un mismo conector se puede
correr en distintos modos y los scripts son re-ejecutables por clave estable (`siigo_id`).

### OpenAPI

Se agrega `Microsoft.AspNetCore.OpenApi` (built-in de ASP.NET Core 10): `AddOpenApi()` + `MapOpenApi()`
publican `/openapi/v1.json`; el grupo lleva `.WithTags("config-api")`.

## Alcance FASE 1 vs FASE 2

- FASE 1 (hecho): tokens (emitir/listar/revocar), contenedores (lectura), conectores (CRUD + secret +
  probe + run + estado), agentes (list + register). Tests unitarios del hasher del token y del
  `ConnectorRunPlanner` (incluido upsert por clave).
- FASE 2 (pendiente):
  - Contenedores de escritura (crear tablas/columnas por HTTP) si se requiere.
  - Agentes: rotate/revoke por HTTP (el servicio ya lo soporta).
  - Ejecucion via AGENTE (no solo server-direct) y rutas anidadas/fan-out: el importador in-process
    mapea por NOMBRE de propiedad PLANO; las rutas anidadas (`id_type.name`, `phones[0].number`) se
    declaran y persisten en el `MappingJson` pero solo el runner via agente las aplana hoy.
  - Corridas persistentes (hoy `ConfigRunStore` es en memoria; el historial durable ya vive en
    `ImportRun` para las programadas) y ejecucion asincrona real.
  - Gate opcional de habilitacion/allowlist de IP (como ECOREX_MGMT_API_KEY del ADR-0057) y
    antiforgery/CORS endurecidos para los endpoints con cookie.

## Consecuencias

- Una entidad y una migracion dual nuevas; ningun cambio a los servicios de dominio existentes.
- La unica lectura sin tenant activo es la resolucion del Bearer por hash opaco; el resto es
  tenant-scoped por el filtro global. El aislamiento cross-tenant se conserva por construccion.
