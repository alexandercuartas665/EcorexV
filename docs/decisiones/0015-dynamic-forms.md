# ADR-0015: DynamicFormRenderer (formularios dinamicos, port del constructor EAV) - FASE 4, ola 2

- Estado: aceptada
- Fecha: 2026-07-03
- Relacionada con: ADR-0001 (DAL dual), ADR-0013 (nucleo TaskItem), ADR-0014 (WorkflowEngine)

## Contexto

El legacy GestionMovil construye formularios con un motor EAV: la definicion vive en
catalogos de controles (19 tipos) y cada respuesta se persiste COMO FILAS en
`FORX_DATA` (una fila por campo). Los formularios se exigen en pasos del flujo BPMN y
tambien se publican por URL para diligenciamiento externo. Esta ola porta el nucleo del
constructor: modelo + servicios + renderer con controles Tier 1 + visor por token +
integracion con el flujo. El constructor visual completo (drag and drop) queda para una
ola posterior (esta ola entrega un builder basico por grid/modal).

## Decision

### 1. EAV -> documento JSON por respuesta

Se abandona el EAV por-fila: cada `FormResponse` guarda UN documento
`{ fieldCode: { value, type } }` en `Data` (jsonb en PostgreSQL, nvarchar(max) en SQL
Server; mismo patron dual de `TaskItem.CcEmails`). Razones: lecturas/escrituras 1-fila,
versionado y concurrencia triviales (IVersioned), y el renderer consume el documento tal
cual. La clave del documento es `FormQuestion.FieldCode` (unico por definicion, formato
identificador validado al guardar). El `type` acompaña al valor para que el documento
sea auto-descriptivo al exportar.

Consecuencia: no hay consultas SQL por-campo como en el legacy; cuando se necesiten
filtros por valor de campo se usara el indice GIN de jsonb en PG (y OPENJSON en SQL
Server) en una ola posterior.

### 2. Version de negocio (Revision) separada del token de concurrencia (Version)

`FormDefinition` es IVersioned (columna `Version` long, token de concurrencia que
incrementa el interceptor, ADR-0013). La version DE NEGOCIO del formulario se llama
`Revision` (int, default 1) A PROPOSITO para no chocar con esa columna: se incrementa en
cada cambio estructural (contenedores/preguntas) sobre una definicion Active (snapshot
logico). Las respuestas ya enviadas conservan su documento intacto.

### 3. Tier 1 primero; el enum completo desde ya

`FormControlType` incluye los 19 tipos del catalogo legacy, pero SOLO Tier 1 tiene
componente en `DynamicFormRenderer`: Text, TextArea, Heading, Select, MultiCheck, Radio,
Toggle, Number, Date, Literal. Los demas (Image, Photo, Audio, Signature, Gps, Button,
Chart, GridDetail, Html) existen en el enum para portar definiciones sin perder el tipo
y se renderizan como placeholder deshabilitado. La validacion es UNA sola
(`FormFieldValidator`, puro y sin EF): el renderer la usa para feedback inmediato en
cliente y `FormResponseService.SaveAsync(submit:true)` la re-ejecuta SIEMPRE en servidor
devolviendo errores por fieldCode (el cliente nunca es fuente de verdad).

### 4. Token opaco hasheado con expiracion, un-solo-uso y revocacion

La publicacion por URL emite un token opaco (32 bytes CSPRNG, base64url) que viaja EN
CLARO una unica vez; se persiste SOLO su SHA-256 (`FormToken.TokenHash`, hex 64, unico
por tenant). `ValidateAsync` aplica 4 verificaciones: existe por hash, no expirado
(`ExpiresAt`), no usado si `SingleUse` (`UsedAt`, se quema al primer submit) y no
revocado (`RevokedAt`). El visor `/f/{token}` muestra un mensaje NEUTRO ante cualquier
fallo (no distingue invalido/expirado/usado/revocado: no filtra informacion).

### 5. Cross-tenant acotado del visor anonimo (el UNICO permitido)

El visor publico no tiene tenant en contexto (el filtro global es fail-closed), asi que
`FormTokenService.ValidateAsync` es el UNICO punto del modulo que usa
`IgnoreQueryFilters()`: busca por la igualdad EXACTA del hash (inenumerable) y devuelve
el `TenantId` dueno del token. El visor fija ese tenant como ambient
(`AmbientTenantContext.Begin`) y TODO el resto del pipeline (definicion, borrador,
submit, marcar usado) corre tenant-scoped normal. Los tests duales verifican que el
DbSet de tokens sigue aislado entre tenants y que la validacion cross-contexto devuelve
el tenant del token, nunca el del contexto.

### 6. Integracion con el flujo: FormFlowLink + WorkflowNodeForm

- `WorkflowNodeForm` asigna a lo sumo UN formulario por nodo de flujo (indice unico por
  NodeId); `IFormDefinitionService.AssignToWorkflowNodeAsync` lo administra.
- Cuando una tarea con instancia de flujo tiene un paso current cuyo nodo exige
  formulario, `GetTaskStepFormsAsync` asegura (idempotente) el borrador de respuesta con
  `Reference` = numero de la tarea y su `FormFlowLink` Pending (unico por instancia +
  nodo + respuesta).
- Al enviar el formulario, `SaveAsync` marca el link Completed y completa el paso via
  `IWorkflowEngine.CompleteStepAsync` EN LA MISMA transaccion (el motor se une a la
  transaccion abierta, patron `HasActiveTransaction`): si el motor falla, rollback total.
- Mientras el link este Pending, el paso NO se completa manualmente desde la UI de la
  tarea (la seccion "Formularios del paso" del detalle solo ofrece diligenciar).

## Consecuencias

- Migraciones duales `AddDynamicForms` (7 tablas: form_definitions, form_containers,
  form_questions, form_responses, form_flow_links, form_tokens, workflow_node_forms).
- FKs con el mismo criterio anti-1785 de SQL Server del resto del modelo: cascada solo
  por la ruta de la definicion; ContainerId/ParentId/WorkflowNodeId en NO ACTION con
  limpieza explicita en el servicio.
- El builder de esta ola es deliberadamente basico (grid + modal + botones subir/bajar);
  el drag and drop y los tiers multimedia llegan en olas posteriores sin cambiar el
  modelo ni el documento de respuestas.
