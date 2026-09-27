# ADR-0114: Plantillas de documento (grupos + editor con tokens) para tareas

**Status:** Accepted
**Date:** 2026-09-27
**Deciders:** Alexander Cuartas (producto), agente de desarrollo

## Contexto

Un proyecto hermano (PQRSD) tiene una herramienta para redactar documentos a partir de
plantillas. En ECOREX.tareas se quiere lo mismo, pero adaptado al modelo de tareas:

- En el **modulo de documentos de una tarea**, al editar, poder **agregar un documento a partir
  de plantillas precargadas** desde configuracion.
- En **conceptos de tarea (000270)** definir **que grupo(s) de plantillas** puede usar la tarea
  (pueden ser varios).
- En **configuracion** (todos los tenants) una **nueva opcion** para crear plantillas nuevas con
  grupos/categorias distintos.
- El **editor** usa el contexto rico de la tarea/contacto via tokens (`{tarea.contacto}`,
  `{tarea.numero}`, `{sistema.fecha}`...).
- Regla clave: el editor **ya NO envia correos**; solo **genera el documento y sostiene versiones**.
  El sistema inyecta en la tarea el documento con la **ultima version** o la **version activada**
  desde el gestor de plantillas.

Requisito de reuso: adaptar piezas existentes en lugar de codigo desde cero.

## Decision

Construir la feature en **3 olas**. Esta ADR cubre **Ola 1** (columna vertebral de configuracion
y binding), y deja planteadas las Olas 2 y 3.

### Piezas reusadas (no se parte de cero)

- **Motor de tokens**: `INotifyTokenResolver` (namespaces `{tarea.*}`, `{sistema.*}`, `{form.*}`)
  ya resuelve el contexto de una tarea. El editor de plantillas ofrece esos mismos tokens.
- **Gestor Documental** (`Documento`/`DocumentoVersion` inmutable, `VersionActualId`): sera el
  destino versionado del documento redactado (Ola 2), con `OrigenDocumento.Tarea`.
- **Patron de catalogo M:N por concepto**: se calca de `ActividadSubcategoriaTercero/Sede`
  (tabla hija Cascade con la subcategoria, FK al catalogo NO ACTION).
- **Patron de pagina de configuracion**: se calca de `PlantillasCorreo.razor` (lista + modal CRUD).

### Ola 1 (implementada)

Entidades nuevas (TENANT-SCOPED):

- `DocumentTemplateGroup` (grupo/categoria; unico por `(TenantId, Name)`).
- `DocumentTemplate` (HTML con tokens; FK a grupo Cascade).
- `ActividadSubcategoriaPlantillaGrupo` (M:N concepto<->grupo; Cascade con la subcategoria,
  Restrict hacia el grupo).

Persistencia dual (PG + SQL Server, migracion `AddDocumentTemplates` en ambos contextos).
Servicio `IDocumentTemplateService` (CRUD de grupos y plantillas + catalogo de tokens).
Pagina de configuracion `/plantillas-documentos` (item de menu en **Sistema - General** para todos
los tenants; backfill idempotente `EnsurePlantillasDocumentoMenuItemAsync` para BD ya sembradas).
Binding en `Conceptos.razor` (seccion "Plantillas de documento": el concepto elige uno o varios
grupos).

**Editor: TinyMCE 7 (GPL)** cargado **bajo demanda desde CDN jsDelivr** (interop
`ecorex-doc-editor.js`), con paleta de tokens. No se versiona TinyMCE en el repo publico (~30 MB);
prod tiene salida a internet. Se puede vendorizar a `wwwroot/lib/` mas adelante si se requiere
offline (como bpmn-js/echarts). El editor **no** tiene accion de envio de correo.

### Ola 2 (implementada, v0.16.153)

Puente tarea<->Gestor Documental: en la pestana Documentos de la tarea, "Redactar desde plantilla"
-> elegir plantilla habilitada por el concepto -> resolver tokens con `INotifyTokenResolver` -> editor
-> guardar como `Documento` versionado (`OrigenDocumento.Tarea`, `OrigenEntidadId = taskId`). El
documento se guarda como HTML (text/html) para ser re-editable (una version nueva parte del HTML de la
vigente). Activar version = `IDocumentoService.ActivarVersionAsync` (mueve `VersionActualId` a una
version ya existente, sin crear otra). Sin envio de correo. Servicio `ITaskDocumentComposerService`;
UI en el componente aislado `TaskTemplateDocs.razor` embebido en `TaskDetailModal`. Categoria destino
"Documentos de tareas" (creada on-demand).

### Ola 3 (implementada, v0.16.154)

Tokens `{tercero.*}` y su alias `{directorio.*}`. Se agrega `TaskItem.TerceroId` (FK Restrict, nullable):
el wizard ya capturaba el tercero elegido en el lookup (`_requesterTerceroId`) pero se perdia por no
tener columna; ahora se persiste (`CreateTaskItemRequest.TerceroId`). `NotifyTokenResolver.BuildAsync`,
cuando la tarea tiene `TerceroId`, carga el `Tercero` y expone sus columnas (`nombre`, `ciudad`,
`identificacion`/`nit`/`documento`, `email`, `telefono`, `sector`, `cargo`, `tipo`) y los campos de las
fichas dinamicas (jsonb) aplanados: `{directorio.<campo>}`/`{tercero.<campo>}`, p.ej.
`{directorio.direccion}`. Migracion dual `AddTaskItemTercero`. Los tokens nuevos se agregan a la paleta
del editor. Tareas sin tercero enlazado -> tokens vacios (sin regresion).

## Consecuencias

- **Mas facil**: crear/editar plantillas sin codigo; asignarlas por concepto; base lista para el
  editor de documentos de la tarea.
- **Mas dificil / a vigilar**: dependencia de CDN para TinyMCE en tiempo de ejecucion (mitigable
  vendorizando). Un grupo asignado a conceptos no se puede borrar hasta desvincularlo (evita
  conceptos huerfanos).
- **A revisitar**: en Ola 2 decidir la UX exacta del selector plantilla dentro de la tarea y el
  guardado de versiones.

## Action Items

1. [x] Entidades + migracion dual `AddDocumentTemplates`.
2. [x] `IDocumentTemplateService` + DTOs + DI.
3. [x] Pagina `/plantillas-documentos` + editor TinyMCE + paleta de tokens.
4. [x] Binding en `Conceptos.razor` + `ActividadCatalogoService`.
5. [x] Item de menu + backfill idempotente.
6. [ ] Ola 2: puente tarea<->Gestor Documental (versionado, activar version, sin correo).
7. [ ] Ola 3 (opcional): tokens `{directorio.*}`/`{tercero.*}`.
