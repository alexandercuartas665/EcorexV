# ADR-0024: Modulo Conceptos (000270) fiel al concepto por-modulo sin migrar el modelo

- Estado: aceptada
- Fecha: 2026-07-04
- Relacionada con: ADR-0013 (nucleo TaskItem / ActivityType), ADR-0014 (workflow
  engine), ADR-0019 (E2E Playwright), ADR-0023 (tokens del workspace sobre paleta
  del concepto por-modulo)

## Contexto

El catalogo de tipos de actividad (ActivityType: Category, Name, Description,
SortOrder, IsArchived, WorkflowDefinitionId?, RequiresForm) era funcional pero solo
se consumia desde el wizard/tableros; la opcion "Conceptos" (000270) del menu era un
placeholder (/modulo/conceptos). El concepto por-modulo de Capa 6
(`proto_tar_conceptos.html`) y la spec `NEWFRONT_tar_conceptos` definen la
experiencia objetivo: topbar con breadcrumb + MOD 000270, tabs Actividades/Detalle,
split categorias (340px) / detalle (1fr) con KPIs y grid, y un modal grande de
sub-categoria con acordeones (RQ07, procesos, formularios, permisos, notificaciones).

## Decisiones

### 1. Tokens del workspace sobre la paleta teal del concepto (misma regla que ADR-0023)

Se replican ESTRUCTURA y MEDIDAS del proto (topbar 14x24, container 1400/20x24x60,
tabs 10x18 borde 2px, split 340/1fr gap16, card-header 12x16, buscador 12x16 input
7x10, list-item 10x16 icono 32x32 r6, detail-head 20 titulo 18/600, KPIs grid4
valor 20/600, filter-bar 12x20, tabla th 10x16 11.5/600 upper, modal 860 r10,
acordeon head 12x14 body 14, field-row 160px/1fr, flags grid2) con los tokens del
workspace en `Conceptos.razor.css`: accent teal -> --brand(+soft), muted/muted-2 ->
--ink-2/3, card/paper -> --surface/--bg, border/soft -> --line(-2), badges on/off ->
--t-green/--t-rose(+bg) y los iconos i-teal/i-blue/... del proto como rotacion
estable de los tonos --t-* por categoria.

### 2. La jerarquia legacy TIPO_TAR / TIPO_TAR_R se proyecta sobre UNA entidad

No hay entidad Categoria: `ActivityType.Category` (string agrupador) ES la
categoria. El modulo la trata como ciudadano de primera clase sin migrar:

- Crear categoria = agrupador local pendiente que se persiste al crear su primer
  concepto (no existe fila de categoria vacia).
- Renombrar categoria = `RenameCategoryAsync` mueve TODOS los conceptos validando
  colisiones (TenantId, Category, Name) en un solo SaveChanges.
- FLAG_INA de TIPO_TAR = `SetCategoryArchivedAsync` (archiva/restaura todos los
  conceptos de la categoria); una categoria "archivada" es la que tiene todos sus
  conceptos archivados.

### 3. Campos de la spec SIN respaldo en el modelo: visibles y deshabilitados (SIN migrar)

Regla de la tarea: no crear migraciones. El modal muestra los controles del proto
que el modelo no puede persistir con `disabled` + tooltip "Pendiente":
Codigo (se muestra un derivado CN-XXXXXXXX de los ULTIMOS 8 del Guid: los primeros
8 de un Guid v7 son timestamp y colisionan visualmente), icono, sedes
(TIPO_TAR_EMPRESA), RQ07 completo (FLAG_INICIA_MODULO, FLAG_BOTON_CIERRE,
TITULO_AUTO, DETALLE_AUTO), FLAG_CLIENTE, lista de chequeo (CHEQUEO), formulario
especifico + modo (solo existe el flag RequiresForm), nodo inicial, permisos por
cargo y notificaciones (TIPO_TAR_N/NR). El gap queda declarado aqui y en
PROGRESO.md; el coordinador decide si se migra.

### 4. Proceso vinculado = WorkflowDefinitionId validado contra flujos publicados

El combo "proceso vinculado" (TIPO_TAR_R_PRO legacy era N:M; el modelo es 1:0..1)
ofrece SOLO definiciones publicadas no archivadas (`ListWorkflowOptionsAsync`) y
Create/Update rechazan ids no publicados o inexistentes con error tipado (la FK es
NO ACTION: sin esta validacion un id invalido revienta en SaveChanges).

### 5. Backend aditivo minimo sobre IActivityTypeService (sin migracion)

`CreateActivityTypeRequest`/`UpdateActivityTypeRequest` ganan parametros opcionales
(WorkflowDefinitionId, RequiresForm) - compatibles con los llamadores existentes -
y el servicio gana: `ListWorkflowOptionsAsync`, `GetUsageAsync` (conteo total/
abiertas de TaskItems por tipo, analogo CANT_USADO del grid Detalle legacy),
`SetArchivedAsync` (toggle con Invalid en doble toggle), `RenameCategoryAsync`,
`SetCategoryArchivedAsync` y `MoveAsync` (permuta SortOrder con el vecino
normalizando empates, un solo SaveChanges). DeleteAsync conserva la regla previa:
en uso -> archiva, sin uso -> borra.

### 6. El check-all de grids y el grid anidado de notificaciones NO se portan

El proto/spec traen seleccion multiple para borrar sub-categorias y un grid anidado
de notificaciones; sin respaldo en el modelo (notificaciones) y con archivado por
fila + borrado en modal (mas seguro que borrado masivo), se omiten. Deuda declarada.

## Consecuencias

- /conceptos reemplaza al placeholder; el NavMenu (item 000270) apunta a la pagina
  real con policy propia `Conceptos.Editar` (patron paso 1: claim tenant_id).
- El wizard de actividades ofrece los conceptos nuevos de inmediato (mismo
  IActivityTypeService.ListAsync, archivados excluidos).
- Nuevos tests: integracion dual ActivityTypeCatalogTests (6 x PG + SQL Server) y
  E2E ConceptosTests (crear concepto -> grid -> tab Detalle -> combo del wizard).
- Deuda declarada (gaps del modelo, SIN migrar): Code, IconClass, sedes por
  concepto, flags RQ07 + titulo/detalle auto, FLAG_CLIENTE, checklist,
  FormDefinitionId especifico, procesos N:M, permisos por cargo/usuario,
  notificaciones por concepto, componentes fijos y formacion.
