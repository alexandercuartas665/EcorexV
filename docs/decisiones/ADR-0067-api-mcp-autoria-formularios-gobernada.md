# ADR-0067: API/MCP de autoria de formularios gobernada

Fecha: 2026-08-20
Estado: Aceptada

## Contexto

Hoy la creacion de formularios dinamicos, plantillas de impresion, enlaces publicos y
modulos para un tenant se hacia por SQL directo: no gobernado, sin auth ni auditoria, y
sin validacion de negocio. Queremos que un agente de IA (o un usuario via un cliente MCP)
haga TODO eso "conectandose al sistema" con autenticacion, seleccion de tenant, validacion,
transaccion y auditoria, SIN tocar SQL. Toda la logica ya existe en los servicios de
aplicacion (IFormDefinitionService, IFormTokenService, IQuoteTemplateService,
IRuleDocumentService, IFormResponseService, IMenuConfigService); habia que EXPONERLA, no
reimplementarla.

## Decision

1. **Toolset nuevo `FormAuthoringToolset : IAgentToolset`** (Ecorex.Application/Tenancy),
   registrado igual que los otros toolsets (3 lineas en DependencyInjection.cs). Expone ~34
   herramientas (JSON-Schema inline) que DELEGAN en los servicios de aplicacion existentes:
   descubrimiento (describe_components auto-descriptivo, list_tenants/forms/templates/
   data_containers/tercero_fields/menu_views/menu_nodes), autoria de formulario (create/import/
   export, add/update/move contenedores y preguntas -incluye grillas GridDetail con columnas
   lookup/resolve/calc, lookup de campo, calc y format-, set_transactional, set_sequence_next,
   set_module, set_custom_css, activate/deactivate/archive), plantillas (create/update/
   set_default + wire_print_button que arma documento+regla IMPRIMIR_PLANTILLA+boton+enlace en
   una operacion), enlaces compartidos (create_share_link -> /f/{token}) y registros
   (create_record + get_render_urls) para probar de punta a punta.

2. **Superficie externa REST reusando /api/mgmt** (ADR-0057): dos endpoints nuevos en
   AgentMgmtEndpoints.cs:
   - `GET  /api/mgmt/agent/tools` (solo AUTH): catalogo COMPLETO nombre+descripcion+JSON-Schema
     (schema embebido como objeto, no string), para que un agente construya sin leer codigo.
   - `POST /api/mgmt/agent/tools/{tool}?tenant={guid}` (AUTH + tenant): ejecuta la tool en ese
     tenant. Cuerpo = argumentos JSON. Despacha al toolset dueno de la tool (por nombre).

3. **Auth / tenant / auditoria (no negociable)**:
   - Autenticacion por header `X-Ecorex-Mgmt-Key` (env ECOREX_MGMT_API_KEY, compare en tiempo
     constante) + allowlist de IP opcional. Sin key -> 404 (no revela el endpoint).
   - Tenant obligatorio por `?tenant={guid}`, validado; TODO corre dentro de
     `AmbientTenantContext.Begin(tenantId)` + scope de DI nuevo (patron Run/RunBody), de modo que
     los servicios scoped ven el filtro global del tenant (multi-tenant real, sin fuga
     cross-tenant). La unica lectura cross-tenant sigue siendo /api/mgmt/tenants (y list_tenants,
     que usa IgnoreQueryFilters solo para el descubrimiento).
   - Cada MUTACION exitosa escribe una entrada inmutable en super_admin_audit_logs
     (IAuditWriter, actorType=System, reason "mgmt-api", action "mgmt-api.agent-tool.{tool}",
     con un resumen truncado de los argumentos). Las tools de SOLO LECTURA no auditan
     (FormAuthoringToolset.ReadOnlyTools).

4. **Errores como resultado estructurado**: los fallos de validacion de los servicios
   (FormResult.Invalid/ValidationFailed/Conflict/NotFound, RuleResult, codigo duplicado,
   expiracion fuera de rango, "solo un formulario activo puede publicarse", "un modulo solo
   cuelga de una seccion/subgrupo", etc.) se devuelven como `{ok:false,status,error,field_errors}`,
   nunca como 500.

## Consecuencias

- Un agente/cliente MCP reproduce de punta a punta lo que antes se hacia por SQL: crear un
  formulario con Text + Select + grilla GridDetail con columna resolve, marcarlo transaccional
  Sequence con prefijo, crear una plantilla, cablear su boton imprimir, emitir un enlace
  /f/{token} publico y promoverlo a modulo /m/{code}; todo en el tenant de `?tenant`, con
  auditoria por mutacion. Verificado contra el tenant AGROMETALICAS (record ADEMO000001,
  /f/{token} HTTP 200, /m/{code} publicado; artefactos demo archivados tras la prueba).
- La capacidad es privilegiada y cross-tenant (equivalente a PlatformAdmin): depende de rotar
  la clave y restringir su uso por IP. La traza registra QUE/CUANDO/tenant/tool, no QUIEN (clave
  compartida, sin identidad de usuario; actorUserId de los servicios = Guid.Empty/System).
- No se anadio esquema de BD ni migraciones: todo es lectura + delegacion. DAL dual intacto.
- FASE 2 (adaptador MCP JSON-RPC nativo modelado sobre AgentMcpServer): pendiente; el puente
  solo traduciria protocolo -> estas mismas llamadas, sin duplicar logica.
