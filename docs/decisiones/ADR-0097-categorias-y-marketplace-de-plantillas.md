# ADR-0097: Categorias (etiquetas) de tarjetas + Marketplace de plantillas de flujos y formularios

**Status:** Proposed
**Date:** 2026-09-11
**Deciders:** Alexander (producto/PlatformAdmin), sesion de desarrollo

## Contexto

Los modulos Flujos (`/flujos`, `Flujos.razor`) y Formularios (`/formularios`, `Formularios.razor`)
listan las tarjetas de forma plana. Hoy:

- `WorkflowDefinition.Category` existe como UN string (usado como "tabs" en el prototipo de flujos).
- `FormDefinition` NO tiene categoria (tiene `Code` estable + `Version`, es `IVersioned`).
- No hay UI para CREAR categorias ni para agrupar/filtrar comodamente la galeria propia del tenant.

Ademas se quiere un **marketplace** de plantillas: el PlatformAdmin publica flujos/formularios (con
imagen + descripcion) a un catalogo que TODOS los tenants pueden explorar e IMPORTAR ("Traer") a su
propio tenant, gratis.

Piezas del codigo que YA existen y se reusan:

- **Formularios: import/export JSON portable** -- `FormDefinitionService.ImportExport.cs`
  (`ExportAsync(defId) -> json`, `ImportAsync(json) -> nueva definicion`). Sobre con `FormatVersion`.
- **Flujos: export/import JSON** -- `WorkflowDesignService.ExportJsonAsync` / `ImportJsonAsync`. OJO:
  el export de flujo actual SOLO captura el GRAFO (nodos: id/tipo/label/coords; conexiones + condicion).
  NO captura los formularios del nodo, las policies (cargo/dependencia), los agentes ni las reglas.
- **Mapeo por nombre factible**: `OrgUnit.Name` y `AiAgent.Name` existen (ambas `TenantEntity`).
- **Plataforma vs tenant**: las entidades de plataforma extienden `BaseEntity` (no `TenantEntity`, sin
  `TenantId`, sin filtro global). Ahi vive el catalogo del marketplace (visible a todos los tenants).

## Decisiones tomadas (con el usuario)

1. **Disenar todo (este ADR) antes de codear.**
2. **Categorias = ETIQUETAS: varias por tarjeta** (no una sola). Sets SEPARADOS por modulo (flujos vs
   formularios), no compartidos.
3. **Al "Traer" un flujo**: clonar el paquete y **mapear cargos/agentes por NOMBRE** si existen en el
   tenant destino; lo que no coincida queda **sin asignar** (con reporte).
4. **Publica solo el PlatformAdmin**, tomando un flujo/formulario **ya existente** y publicandolo con
   imagen + descripcion.

## Fase A -- Etiquetas por tarjeta (por tenant)

### Modelo (aditivo, DAL dual PG + SqlServer)

- `CardTag : TenantEntity` -- catalogo de etiquetas del tenant.
  - `Scope` (enum `CardTagScope { Flow, Form }`) -- separa etiquetas de flujos y de formularios.
  - `Name` (<= 60), `Color` (opcional, hex corto), `SortOrder` (int), auditoria estandar.
  - Unico por `(TenantId, Scope, Name)`.
- Relacion N:N contra la IDENTIDAD ESTABLE (no la version), para que las etiquetas sobrevivan al
  versionado:
  - `FlowTag`  = `(TenantId, ProcessCode, CardTagId)` -- una fila por (flujo logico, etiqueta).
  - `FormTag`  = `(TenantId, FormCode,    CardTagId)` -- idem para formularios (por `FormDefinition.Code`).
- Migracion de datos: por cada `WorkflowDefinition.Category` no vacio existente, crear un `CardTag`
  (Scope=Flow) y su `FlowTag`, para NO perder el agrupado actual. `WorkflowDefinition.Category` queda
  como campo legado (se deja de usar en la nueva UI; opcional retirarlo en una ola posterior).

### UI (Flujos.razor + Formularios.razor)

- **Vista agrupada**: secciones por etiqueta (titulo de la etiqueta + conteo, luego sus tarjetas). Una
  tarjeta con N etiquetas aparece bajo CADA una. Las tarjetas sin etiqueta van bajo "Sin categoria".
- **Filtros**: chips por etiqueta + busqueda por texto. Elegir chips acota (interseccion o union -- se
  define union por defecto: "muestrame las de estas etiquetas").
- **Gestionar categorias**: crear / renombrar / borrar / reordenar / color (modal o panel). Borrar una
  etiqueta solo la quita de las tarjetas (no borra flujos/formularios).
- **Asignar etiquetas** a un flujo/formulario: multi-select "Etiquetas" en la edicion (o accion rapida
  en la tarjeta).
- Es solo lectura/organizacion; NO toca el motor de flujos ni el de formularios.

## Fase B -- Marketplace de plantillas (cross-tenant)

### Modelo (PLATAFORMA, sin TenantId)

- `MarketplaceItem : BaseEntity`:
  - `Kind` (enum `MarketplaceItemKind { Flow, Form }`).
  - `Title`, `Description`, `ImageRef` (ver "Imagen" abajo).
  - `Category` (etiquetas de PLATAFORMA para explorar; independientes de las etiquetas por-tenant).
  - `SnapshotJson` (el paquete portable) + `SnapshotFormatVersion`.
  - `SourceCode` (ProcessCode o FormCode de origen, informativo), `Version` (version del item).
  - `PublishedByPlatformUserId`, `PublishedAt`, `IsActive`, `ImportCount` (metrica).
- Es de plataforma: **todos los tenants lo LEEN**; **solo el PlatformAdmin ESCRIBE**. El catalogo es el
  mismo para todos.

### Snapshot portable

- **Formulario**: se reusa `FormDefinitionService.ExportAsync` (JSON con `FormatVersion` + DetailDto).
  Autosuficiente (cabecera + contenedores + preguntas). Al importar, `ImportAsync` crea la definicion en
  el tenant activo.
- **Flujo (paquete NUEVO, RICO)**: se agrega `ExportFlowPackageAsync(defId) -> json` que ademas del grafo
  (reusa lo de `ExportJsonAsync`) captura, POR NODO y SIN ids de tenant:
  - Formularios del nodo (embebidos con el export de formulario, o referenciados dentro del paquete).
  - Policies como **nombres de cargo/dependencia** (`OrgUnit.Name`), no ids.
  - Agente del nodo como **nombre de agente** (`AiAgent.Name`) + su config portable (autonomia, politica
    de fallo, capacidades). El `colmena_client_id` NO viaja (es del tenant).
  - Reglas del nodo (si son autosuficientes) y ruta/compuertas.
  - Tablero destino por NOMBRE (si aplica).

### Publicar (SuperAdmin / PlatformAdmin)

- Accion "Publicar al marketplace" desde un flujo/formulario existente: toma el snapshot, pide
  `Title` / `Description` / `Image` / `Category`, crea el `MarketplaceItem`. Solo PlatformAdmin.

### Explorar (cada tenant)

- Una galeria "Marketplace" (pestana en Flujos y en Formularios, o pagina dedicada) que lista los
  `MarketplaceItem` (imagen + titulo + descripcion + categoria), con busqueda/filtros. Catalogo de solo
  lectura, igual para todos.

### Importar ("Traer") al tenant

- `ImportMarketplaceItemAsync(itemId)` escribe SOLO en el tenant activo (filtro global):
  - **Formulario**: reusa `FormDefinitionService.ImportAsync(snapshot)` -> nueva `FormDefinition`.
  - **Flujo**: `ImportFlowPackageAsync(package, options)`:
    0. **Asistente**: si el paquete trae formularios vinculados en los nodos, el asistente PREGUNTA al
       usuario si desea traer tambien esos formularios (migrar los formularios vinculados a cada nodo).
       Opciones: traer todos, ninguno (solo el grafo/estructura), o elegir cuales por nodo. Lo elegido
       va en `options` y gobierna el paso 2.
    1. Crea la `WorkflowDefinition` (grafo) como BORRADOR no publicado, con nuevo ProcessCode/FLW-xxx.
    2. Segun el asistente: importa los formularios del nodo (reusa import de formularios) y re-enlaza
       nodo -> formulario; si el usuario opto por no traerlos, el nodo queda sin formulario (lo cablea
       el tenant, o reusa uno propio existente por codigo si coincide).
    3. Policies: resuelve `OrgUnit` por NOMBRE en el tenant destino; si existe, crea la policy; si no,
       queda SIN asignar (el paso cae a la bandeja/manual).
    4. Agente: resuelve `AiAgent` por NOMBRE; si existe, crea el `WorkflowNodeAgent` con su config; si no,
       el nodo queda humano (sin agente).
    5. Reglas/tablero: se aplican si son resolubles; si no, se omiten con nota.
    6. Devuelve un **reporte de importacion**: que se mapeo y que quedo sin asignar, para que el tenant
       cablee lo que falte antes de PUBLICAR el flujo.
  - El flujo importado queda BORRADOR: el tenant revisa, cablea cargos/agentes faltantes y publica.
- **Sin costo** (gratis). "Traer" es un CLONE (fork) de una sola vez: cambios posteriores del item del
  marketplace NO actualizan las copias ya importadas (re-importar da una copia nueva).

## Sub-decisiones abiertas (a confirmar; propuestas por defecto)

- **Imagen del item**: propuesta = subir la imagen y guardarla como asset de plataforma (o `ImageRef`
  como URL). Tope de tamano (p.ej. 512 KB) y formatos jpg/png/webp. Se puede empezar con una URL simple.
- **De donde publica el PlatformAdmin**: desde cualquier flujo/formulario que administre en la consola
  unificada (tipicamente un tenant "plantillero" propio). El snapshot no lleva ids de tenant, asi que la
  fuente es indiferente.
- **Union vs interseccion** al filtrar por varias etiquetas: por defecto UNION.

## Consecuencias

- Fase A: aditiva (tablas nuevas + UI), sin tocar motores. Bajo riesgo. Migracion de `Category` -> tags.
- Fase B: el trabajo grande es el **snapshot RICO de flujo** (export/import de paquete con cargos/agentes
  por nombre). Reusa el import/export de formularios ya existente. El mapeo por-nombre es best-effort con
  reporte -- nunca inventa asignaciones: lo no resuelto queda sin asignar.
- Multi-tenant intacto: `MarketplaceItem` es de plataforma (sin `TenantId`); el import escribe solo en el
  tenant activo por el filtro global; el snapshot no transporta ids de tenant (todo por nombre/valor).
- Migraciones nuevas en AMBOS proveedores (PG + SqlServer).

## Plan de olas

- **A1**: modelo de etiquetas (`CardTag` + `FlowTag`/`FormTag`) + migracion de `Category` + UI de
  agrupado/filtros/gestion en Flujos y Formularios. (Entregable rapido, util ya.)
- **B1**: snapshot RICO de flujo (`ExportFlowPackageAsync` / `ImportFlowPackageAsync`) + tests unitarios
  del round-trip (grafo + formularios + policies-por-nombre + agente-por-nombre).
- **B2**: `MarketplaceItem` (plataforma) + "Publicar al marketplace" (SuperAdmin) + imagen/descripcion.
- **B3**: galeria marketplace en cada tenant + "Traer" (import) + reporte de mapeo + metricas.

## Action Items

1. [ ] A1: entidades + migracion dual + UI (Flujos/Formularios) + migrar Category.
2. [ ] B1: paquete portable de flujo + tests.
3. [ ] B2: catalogo de plataforma + publicar + imagen.
4. [ ] B3: explorar + traer + reporte.
5. [ ] Reflejar este ADR en el vault (Capa 3 / Capa de formularios) y en PROGRESO.md.
