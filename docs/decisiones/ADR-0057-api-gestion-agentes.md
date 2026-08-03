# ADR-0057 - API REST de gestion de agentes de IA (gobierno cross-tenant)

- Estado: Aceptado
- Fecha: 2026-08-03
- Contexto: ECOREX.tareas, proyecto de prod `apps/backend/src/Ecorex.SuperAdmin`.

## Contexto y proposito

Se necesita que un operador externo (por ejemplo Claude, via WebFetch) pueda LEER y EDITAR
la estructura de los agentes de IA de CUALQUIER tenant, y LEER sus bitacoras, sin usar la
consola Blazor ni la cookie del panel. Es una API de GOBIERNO: opera cross-tenant y debe ir
bien protegida.

No se crea ninguna entidad ni tabla nueva: se reusan los servicios tenant-scoped existentes
(`IAiAgentService`, `IAiAgentCacheService`) y la lectura directa de `ai_agent_run_logs` via
`IApplicationDbContext`. NO hay migracion.

## Decision

Grupo de endpoints Minimal API bajo `/api/mgmt`, definidos en
`apps/backend/src/Ecorex.SuperAdmin/Endpoints/AgentMgmtEndpoints.cs`
(metodo de extension `MapAgentMgmtEndpoints(this WebApplication app)` llamado desde `Program.cs`
justo antes de `app.Run()`).

### Endpoints

| Metodo | Ruta                                   | Accion |
|--------|----------------------------------------|--------|
| GET    | `/api/mgmt/agents?tenant={guid}`       | Lista resumida (`IAiAgentService.ListAsync`). |
| GET    | `/api/mgmt/agents/{id}?tenant={guid}`  | Detalle: agente + prompts enrutados + recursos + definicion de datos cache (`GetAsync` + `IAiAgentCacheService.ListFieldsAsync`). |
| POST   | `/api/mgmt/agents?tenant={guid}`       | Crear agente (`CreateAsync`). 201. |
| PUT    | `/api/mgmt/agents/{id}?tenant={guid}`  | Actualizar agente completo (`UpdateAsync`). |
| PUT    | `/api/mgmt/agents/{id}/prompt?tenant={guid}` | Actualiza SOLO el system prompt (body `{ "systemPrompt": "..." }`; carga el agente y reusa `UpdateAsync` conservando los demas campos). |
| POST   | `/api/mgmt/agents/{id}/prompts?tenant={guid}` | Agregar prompt enrutado (`AddPromptAsync`). 201. |
| PUT    | `/api/mgmt/prompts/{promptId}?tenant={guid}`  | Actualizar prompt enrutado (`UpdatePromptAsync`). |
| DELETE | `/api/mgmt/prompts/{promptId}?tenant={guid}`  | Eliminar prompt enrutado (`DeletePromptAsync`). |
| GET    | `/api/mgmt/agents/{id}/bitacora?tenant={guid}&kind={Error\|...}&limit=50` | Bitacora del agente desde `ai_agent_run_logs`, orden desc por `OccurredAt`, filtro opcional por `kind`, `limit` acotado a 1..500 (default 50). |

Respuestas JSON con enums serializados como texto (`JsonStringEnumConverter`), que ademas
aceptan texto o numero al leer. `.DisableAntiforgery()` en POST/PUT/DELETE. Actor de auditoria
= `Guid.Empty` (actor de sistema), igual que el flujo del webhook/agente.

### Autenticacion (gate por clave, no cookie)

- Header `X-Ecorex-Mgmt-Key`, validado contra la env var `ECOREX_MGMT_API_KEY`.
- Si `ECOREX_MGMT_API_KEY` esta vacia o no seteada: TODOS los endpoints responden **404**
  (la API queda deshabilitada y no revela que existe).
- Si el header falta o no coincide: **401**. La comparacion es en tiempo constante
  (`CryptographicOperations.FixedTimeEquals` sobre bytes UTF8).
- Los endpoints son `.AllowAnonymous()` (implicito: no requieren autorizacion de cookie); el
  unico gate es la clave. NO se usa el esquema de cookie del panel.
- La clave es un SECRETO: se define en el `.env` de prod (NO versionada), misma politica que
  `ECOREX_SEED_ADMIN_PASSWORD`.

### Tenant scoping (cross-tenant)

Cada request lleva el tenant objetivo como query `?tenant={guid}` (obligatorio; **400** si
falta o es invalido). El tenant se fija con `AmbientTenantContext.Begin(tenantId)` dentro de un
scope de DI aislado (`IServiceScopeFactory.CreateScope`), y los servicios tenant-scoped se
resuelven DENTRO de ese scope. Es EXACTAMENTE el mismo mecanismo del pipeline del webhook
entrante (`AgentReplyDispatcher.ProcessAsync` y `/webhooks/evolution`): el `AsyncLocal` del
`AmbientTenantContext` fluye por la cadena async, de modo que el query filter global de EF
aisla al tenant correcto aunque no haya `HttpContext` autenticado.

## Consecuencias

- Superficie de gobierno cross-tenant potente: cualquiera con la clave puede editar y leer
  agentes de todos los tenants. Por eso el gate 404-si-no-hay-clave + 401 en tiempo constante
  y el secreto fuera del repo.
- No hay cambios de esquema: cero riesgo de migracion. Si en el futuro se quiere exponer datos
  cache por sesion o el historial de versiones de prompts, ya existen servicios para ello.
- El actor de auditoria es el actor de sistema (`Guid.Empty`); las acciones quedan registradas
  por los servicios subyacentes (auditoria existente) sin identificar a un usuario humano.

## Activacion

Setear la env var `ECOREX_MGMT_API_KEY` con un valor secreto fuerte en el `.env` de prod.
Sin ella, la API permanece deshabilitada (404).
