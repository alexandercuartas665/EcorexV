# ADR-0082: Acceso a SECCIONES de un formulario por CARGO (solo-lectura para no autorizados)

- Estado: Aceptado
- Fecha: 2026-08-29
- Contexto: formularios dinamicos (ADR-0015) con contenedores (FormContainer). Ya existen permisos por
  CAMPO por rol (ola F6: FieldVisibilityJson hide/readonly). Falta poder restringir SECCIONES completas
  por CARGO del organigrama (modulo Dependencias, OrgUnit Classifier==Cargo, ADR-0035), como en la
  asignacion por cargo de Actividades.

## Decision

Una SECCION (FormContainer) puede declarar los CARGOS autorizados a OPERARLA. Un usuario cuyo cargo NO
este en la lista VE la seccion pero en SOLO-LECTURA (deshabilitada). Sin cargos declarados = todos la
operan (comportamiento actual, sin regresion). Owner/Admin del tenant OPERAN todas las secciones (bypass
de administradores).

### Persistencia

- `FormContainer.AllowedCargosJson` (string?, arreglo JSON de Guids de OrgUnit-Cargo). Null/vacio = sin
  restriccion. Se eligio JSON (no tabla join) por simplicidad y por el precedente FieldVisibilityJson.
  Columna DUAL: PG `jsonb`, SQL Server `nvarchar(max)` (via `jsonColumnType` del DbContext).
- Migracion dual: `20260829132944_AddFormContainerAllowedCargos` (PG) y
  `20260829133053_AddFormContainerAllowedCargos` (SQL Server). Aditiva, nullable, sin default.
- Propagado por `FormContainerDto` y `SaveFormContainerRequest` (campo al final, con default null),
  mapeado en create/update del `FormDefinitionService` (Normalize) y en el round-trip de import/export.

### Modelo de cargo (reusa lo existente, sin entidad nueva)

- Cargo = `OrgUnit` con `Classifier==Cargo`. Un usuario se vincula a un cargo por TRES vias (union):
  (1) `OrgUnit` Funcionario hijo del cargo con `TenantUserId`; (2) `OrgUnitMember` con OrgUnitId=cargo;
  (3) `ResponsibleTenantUserId` del cargo. Espejo del resolver directo
  `ActividadCatalogoService.ListEncargadoUserIdsAsync`.
- Resolver INVERSO nuevo `IOrgUnitService.ListCargoIdsForUserAsync(tenantUserId)` (usuario -> ids de sus
  cargos, mismas 3 vias, solo cargos no archivados). Opciones para el picker:
  `IOrgUnitService.ListCargoOptionsAsync()` (OrgUnit Classifier==Cargo, no archivados). Tenant-scoped por
  el filtro global.
- Puente claim->TenantUser: `ITenantUserService.ResolveTenantUserIdAsync(platformUserId)`.

### Renderer (DynamicFormRenderer)

- En la carga (OnParametersSetAsync, dentro del gate del circuito) se CACHEA `_currentUserCargos` =
  `OrgUnitsSvc.ListCargoIdsForUserAsync(TenantUsersSvc.ResolveTenantUserIdAsync(platformUserId))`.
- `IsSectionReadonly(container)` = AllowedCargosJson no vacio && rol != Owner/Admin && el usuario NO tiene
  ninguno de los cargos autorizados.
- Gate en `RenderContainerChildren` (unico choke point: cubre preguntas directas + subcontenedores + el
  cuerpo de Tabs): si `IsSectionReadonly`, el cuerpo se envuelve en `<fieldset disabled class="dfr-ro
  dfr-section-ro">` (mismo patron que el readonly por campo). El cuerpo se extrajo a
  `RenderContainerChildrenInner` para poder envolverlo.
- NO se pierden datos: `<fieldset disabled>` no dispara `@onchange`, y `_values` se carga desde la
  respuesta al abrir; los campos de la seccion en solo-lectura conservan su valor al guardar (idem al
  readonly por campo ya existente).

### Disenador (FormDesigner)

- En el panel de ajustes de la seccion (gate a ContainerType Section/Segment) un picker
  "Cargos que pueden operar esta seccion" (checkbox grid de `ListCargoOptionsAsync`), que guarda los Guids
  en `AllowedCargosJson` via `PatchContainerAsync(with { AllowedCargosJson = ... })`. Sin marcar ninguno =
  null (todos operan).

## Consecuencias

- Multi-tenant intacto: cargos y usuarios se resuelven bajo el filtro global (tenant activo).
- La restriccion es de PRESENTACION/operacion en el renderer; el servidor no rechaza escrituras de una
  seccion restringida (defensa a nivel UI, como el readonly por campo). Si luego se requiere gating
  autoritativo en el submit, es un cambio aparte.
- El gate del renderer aplica a CUALQUIER contenedor; el disenador solo expone el picker en Section/Segment
  (se puede ampliar a Tabs/Modal si se necesita).

## Pendientes / supuestos

- Tenants cuyos usuarios son TODOS Owner/Admin no muestran el estado solo-lectura (bypass): para validarlo
  se necesita un usuario Supervisor/Advisor sin el cargo.
- Clave COMPUESTA no aplica (es una lista simple de cargos).

## Rework v0.15.125: acceso por DEPENDENCIA/CARGO (organigrama), sin control por rol

Cambio de criterio: el acceso a una seccion NO depende del rol de tenant. El bypass Owner/Admin en
IsSectionReadonly hacia que el candado nunca aplicara donde todos los usuarios son Admin (p.ej.
AGROMETALICAS). Ahora es puro por Dependencia/Cargo del organigrama, con el MISMO selector que la
asignacion de los flujos (ADR-0035).

- Datos: se REUSA `form_containers.allowed_cargos_json` (sin migracion) pero los ids son OrgUnit de
  clasificador Dependencia O Cargo (no solo Cargo); los ids de cargo ya guardados siguen validos.
- Render (DynamicFormRenderer.IsSectionReadonly): se ELIMINA el bypass Owner/Admin y la comparacion contra
  cargos-del-usuario. Nuevo criterio: una seccion con unidades es solo-lectura si el TenantUser actual NO es
  CANDIDATO de ninguna de esas unidades, resuelto con `OrgAssigneeTree.ResolveForUnits` (funcionarios
  descendientes + miembros + responsable). Se carga el grafo (IOrgUnitService.LoadAssigneeGraphAsync) y el
  TenantUser una vez por carga; memoizado por contenedor. Sin unidades = todos operan. `<fieldset disabled>`
  sigue siendo el unico choke point.
- LOCKOUT (intencional): sin bypass por rol, una seccion cuyas unidades no incluyan al usuario queda
  read-only aunque sea Admin/Owner. Recuperable: el FormDesigner se gobierna por permiso de NAVEGACION (no
  por las unidades de la seccion), asi que un admin siempre puede reabrir el disenador y reasignar. No hay
  override por Owner.
- Disenador (FormDesigner): el picker de cargos (checkboxes) se reemplaza por el patron de los flujos:
  filas de unidades asignadas (punto por clasificador + nombre + badge Dependencia/Cargo + "N cand." + x),
  <select> "-- Dependencia / Cargo --" (IWorkflowNodePolicyService.ListAssignableUnitsAsync, indentado por
  Depth) + boton "+ Asignar". Conteo por unidad con IWorkflowNodePolicyService.CountCandidatesAsync. Se
  muestra para CUALQUIER contenedor (no solo Section/Segment). Etiqueta: "Dependencias / Cargos que pueden
  operar esta seccion".
- Verificado (AGROMETALICAS, todos Admin): cargo "Gerente Administrativo" resuelve proyectos@ (edita) y NO
  direccionventas@ (solo-lectura). Sin migracion.
