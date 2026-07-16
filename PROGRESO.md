# PROGRESO - ECOREX Sistema de Tareas

> Bitacora de avance por sesion. Formato: fecha, agentes, hecho, siguiente, bloqueos, decisiones.
> Complementa (no reemplaza) los ADRs de `docs/decisiones/` y el vault Obsidian.

---

## 2026-07-16 - Contenedor: publicar tablas al menu (Flujo B) + relaciones fila-a-fila (Flujo A)

Sobre el cimiento de la Ola 0. **Nada de esto esta en prod.**

**Flujo B - publicacion (HECHO):**
- `IDataContainerModuleService` (`d24aa53`): publicar/despublicar una tabla raiz creando su nodo de
  menu. Espeja el patron de los formularios, y de ahi salen GRATIS la matriz de roles (su catalogo se
  deriva del menu, clave = Route) y el filtrado del sidebar. Corrige 3 defectos del original: ruta
  INMUTABLE (renombrar no la toca; es la clave del modulo y cambiarla dejaria huerfanos los permisos
  ya asignados), despublicar CONSERVA la ruta (al republicar los permisos siguen valiendo), y
  renombrar RECONCILIA el nombre del nodo. Ruta `dc/{slug}` sin barra (los forms usan `/m/{code}` CON
  barra: como la clave es el Route literal, hay que ser consistente). 12/12 tests duales.
- UI + pagina (`2f59095`): banderin por tabla en el lienzo + modal de publicacion (vista, grupo,
  icono, columnas de grilla y de filtro); `DataModule.razor` (`/dc/{Slug}`) sirve a TODAS las tablas
  publicadas reusando `DataRecordsGrid`. Validado en Chrome: publicar "Perfil Clientes" -> ruta
  `/dc/perfil-clientes` -> item en el menu -> pagina lista sus registros -> y el modulo aparece SOLO
  en Roles y permisos.

**B5 destapo un fallo de seguridad GENERAL (`5bc15b7`):** un usuario cuyo rol no tenia el permiso
igual veia la pagina. La causa no era del modulo nuevo: `CurrentPermissions` solo leia de
`IHttpContextAccessor`, y en un CIRCUITO Blazor (paginas con prerender:false) NO hay HttpContext, con
lo que fallaban el claim del usuario Y el tenant (y sin tenant el filtro global no encuentra el
TenantUser, que es tenant-scoped). Por cualquiera de las dos, `Perms.GetAsync()` devolvia SIEMPRE
`Unrestricted` (fail-open): **todo el gateado en pagina no restringia a nadie**, en cualquier pagina
que lo use; solo se salvaban las que tienen `[Authorize(Policy="Perm:...")]` (se evalua en la
peticion). Fix: resolver usuario+tenant tambien del AuthenticationState del circuito y fijar el
tenant ambiental. Validado: con ver=false -> "Sin acceso"; con ver=true -> entra.
**Observacion no resuelta (decision del dueno: se deja asi):** al abrir Roles y permisos, un modulo
recien publicado se agrega SOLO a los roles existentes con `can_view=true` (acceso por defecto).

**Flujo A - relaciones fila-a-fila (HECHO):**
- `IDataRelationLinkService` (`f59b17a`): ListForRowAsync / SetLinksAsync (reemplazo idempotente del
  set). Valida lo que el esquema no puede: cardinalidad (N:1 y N:N comparten tabla) y que ambos
  extremos pertenezcan a las tablas de la arista.
- **FIX del bug latente**: `DeleteRowAsync` limpiaba los `DataContainerLinks` (entidad vieja) pero NO
  los nuevos; como sus FKs a filas son Restrict (cascada por ambos extremos = rutas multiples, error
  1785 en SQL Server), borrar una fila vinculada habria reventado en cuanto se escribiera el primer
  vinculo. Se limpian en el mismo SaveChanges (una transaccion). 12/12 tests duales.
- `RowRelationPicker`: entra por el punto de extension `RowEditorExtras` de la Ola 0 (ni la grilla ni
  el editor cambiaron). N:1 -> select; N:N -> casillas. La fila NUEVA no tiene Id hasta guardarse, asi
  que la seleccion se persiste en `OnRowSaved`, ya con el Id definitivo.
- **Validado en Chrome** con el modelo demo (Pedidos->Clientes N:1, Pedidos->Productos N:N): se creo
  PED con Fecha+Total, ACME SAS y Teclado; los DOS vinculos quedaron en `data_model_relation_links`
  contra el Id de la fila nueva, y al reabrirla el picker los recupera.

**DESPLEGADO a prod 2026-07-16** (`fase-0/clon-backbone` @ `ff31b4e`, autorizado por el dueno):
backup `ecorex-2026-07-16-1201.sql.gz` -> `build --no-cache` -> `up -d`. Aplico 1 migracion
(`DataContainerModuleAndRelationLinks`, aditiva). Sano: /login 200, logs sin errores, las 5 columnas
de publicacion y la tabla `data_model_relation_links` creadas. OJO: este deploy incluye el fix de
permisos, asi que el gateado en pagina ahora SI se aplica en toda la consola (antes era fail-open).
Antes de desplegar se limpio el residuo de las pruebas en el dev local (se despublico "Perfil
Clientes", se borraron las filas del modelo demo y el permiso auto-agregado al rol).

**Pendiente:** (a) el orden por columna sigue siendo alfabetico (EAV como string); (b) decidir si la
relacion se muestra como columna en la grilla (hoy solo escalares).

---

## 2026-07-16 - Contenedor de datos: Ola 0 (cimiento para publicar tablas al menu)

Idea del dueno: que cada tabla dinamica del Contenedor se pueda **publicar al menu** para que el
usuario final gestione sus registros. Antes de codear se investigo el terreno; dos hallazgos
cambiaron el plan:

1. **Ya existe el patron**: un `FormDefinition` se publica como modulo (`IsModule` +
   `ModuleMenuNodeId` -> `SetModuleAsync` crea el MenuNode con ruta `/m/{code}` y `FormModule.razor`
   pinta la bandeja con `ListColumnsJson`/`FilterFieldsJson`). Se ESPEJA, no se reinventa.
2. **La cadena ya es data-driven**: `RolService.GetModuleCatalogAsync` deriva el catalogo de la
   matriz de roles **del menu real** (clave del modulo = `Route`), `PermissionPolicyProvider` fabrica
   `Perm:{ruta}:{accion}` al vuelo y `MenuPermissionFilter` poda el sidebar. O sea: publicar una
   tabla = crear un nodo de menu; permisos y filtrado salen gratis.

Decisiones (confirmadas con el dueno): **CRUD completo** en el modulo publicado; FASE 2 de
relaciones y publicacion **en paralelo**. Ruta **inmutable** (la clave del modulo ES el Route:
renombrar la tabla romperia los permisos ya asignados; es el bug que hoy tienen los formularios).
Permiso verificado en runtime (no cabe `[Authorize(Policy)]` con clave dinamica). Solo tablas raiz.

**Ola 0 (cimiento) - HECHA:**
- **Una sola migracion dual** (`DataContainerModuleAndRelationLinks`, PG + SQL Server) con AMBOS
  cambios, para que los dos flujos siguientes no vuelvan a tocar migraciones ni se peleen el
  snapshot: campos de publicacion en `DataContainer` (`ModuleRoute` unico por tenant, `MenuNodeId`
  SetNull, `ModuleIcon`, `ListColumnsJson`, `FilterColumnsJson`) + entidad `DataModelRelationLink`
  (vinculo fila-a-fila colgado de la ARISTA, la FASE 2 diferida). Up() 100% aditivo.
- **`ListRowsPagedAsync`**: busqueda, filtros por columna, orden y paginado EN EL SERVIDOR (antes:
  tope de 500 filas y filtrado en memoria). `ListRowsAsync` se conserva para configurador/selectores.
  Tests de integracion nuevos en **matriz dual (6/6 PG + SQL Server)**, que cazaron un bug real: el
  escape de LIKE no funcionaba en NINGUN motor porque la sobrecarga de 2 args emite `ESCAPE ''`
  (un usuario buscando "50%" no encontraba nada). Tambien: `ToLower+Like` en vez de `ILike`
  (Npgsql-only) y desempate por Id para que el OFFSET no repita/pierda filas.
  Limitacion documentada: el orden por columna es ALFABETICO (el EAV guarda todo como string).
- **`DataRecordsGrid`** (Components/Shared/Data, + CSS scoped propio porque los `dc-*` eran un
  `<style>` global de la pagina): grilla + editor de filas + import/export extraidos de
  `ContenedorDatos.razor` (2046 -> ~1790 lineas). Punto de extension `RowEditorExtras` (recibe el Id
  de fila; null = nueva) + `OnRowSaved`, que es donde enchufa la FASE 2 sin volver a tocarlo.
  El configurador ya lo consume dentro de su modal; el modulo publicado usara el MISMO.
- **Validado en Chrome**: el panel de Datos sigue funcionando con el componente (grilla, contador
  desde la consulta paginada, busqueda server-side 0/1, alta y borrado de fila, CSS scoped aplicado).
- **Pendiente**: Flujo A (servicio de vinculos + picker) y Flujo B (SetModuleAsync + UI de publicar +
  pagina `/dc/{slug}` con permiso en runtime). NO desplegado a prod.

---

## 2026-07-16 - DEPLOY a prod: modal de tercero compartido + formularios por tercero

Desplegado a prod (10.0.0.3, build-from-git de `fase-0/clon-backbone` @ `d76c5b3`) con autorizacion
explicita del usuario. Ambas ramas remotas quedaron al dia (`main` y `fase-0/clon-backbone`).

- **Backup previo**: `/opt/ecorex/backups/ecorex-2026-07-16-0547.sql.gz` (`./backup.sh`).
- **3 migraciones aplicadas al arrancar** (prod venia de `AddYCloudProvider`): `AddSqlConsoleLogs`,
  `RelationsAsEntity`, `AddTerceroFormLinks`.
- **`RelationsAsEntity` es destructiva**: se midio el impacto en prod ANTES (1 columna de relacion,
  3 celdas, 0 vinculos, 3 modelos, 6 tablas) -> asumible. Post-deploy verificado: la relacion
  sobrevivio como **1 arista** en `data_model_relations`, 0 columnas Reference/RelationMany
  restantes, `referenced_container_id` eliminada. Se perdieron 3 valores de celda (esperado; el
  vinculo dato-a-dato se re-cableara en la FASE 2 diferida).
- **Sano**: `ecorex-app` Up, `/login` HTTP 200, logs sin errores (solo el aviso benigno "Failed to
  determine the https port for redirect", preexistente). Tablas nuevas creadas y vacias
  (`tercero_form_links`, `sql_console_logs`).
- **Nota**: en prod los formularios del modal arrancan sin asociar (`tercero_form_links` vacia); se
  eligen con "Configurar campos" del Directorio General. Migracion SQL Server generada pero sin
  aplicar/probar (no hay instancia; prod es PG).

---

## 2026-07-15 - Cargador de contactos reusa EL MISMO modal de tercero (componente compartido)

Feedback del dueno: en Cargador de contactos (000740) el boton "Nuevo contacto" saltaba a
Directorio General (000232). Un primer intento abrio un modal propio (`_nc*`) dentro del Cargador;
el dueno lo rechazo: "un contacto ES un cliente; ambos modulos se alimentan de los MISMOS
registros de este modal, no debemos crear uno nuevo, la idea es reusarlo". Decision (AskUser):
**componente compartido in-place** (no navegar, no duplicar).

- **`TerceroModal.razor` (+ `.razor.css`) nuevo en `Components/Shared/`**: se EXTRAJO el modal grande
  de tercero (pestanas Datos / Relaciones / Contacto Cliente / Actividades, perfiles, fichas
  configurables por perfil) + el sub-modal de contacto de relacion desde `DirectorioGeneral.razor`.
  API por `@ref`: `OpenCreate()`, `OpenEditAsync(id)`, `OpenContacto(parentId, c)`; parametro
  `OnChanged` (EventCallback) para que el host refresque su lista/contadores. CSS scoped copiado de
  `DirectorioGeneral.razor.css` para fidelidad milimetrica.
- **`DirectorioGeneral.razor`**: dejo de tener el modal inline (se borraron ~825 lineas de markup +
  code-behind movido); ahora renderiza `<TerceroModal @ref=...>` y sus botones (nuevo cliente,
  editar tercero, agregar/editar contacto) lo invocan por `@ref`. Conserva lista/tabla/KPIs,
  configurador de campos, asignar-a-empresa y borrar-contacto. Se retiro el `?crear=1` (ya no hay
  salto que lo justifique).
- **`GestorContactos.razor`**: se elimino el modal propio `_nc*`; "Nuevo contacto" ahora abre el
  componente compartido in-place (`_terceroModal.OpenCreate()`), refresca con `OnChanged`.
- **Bug de concurrencia corregido**: el `OnInitializedAsync` del componente y el de la pagina
  compartian el mismo `EcorexDbContext` scoped y corrian en paralelo -> "A second operation was
  started on this context". Fix: el componente NO hace BD en `OnInitializedAsync`; permisos +
  `EnsureDefaults` + carga de campos se hacen PEREZOSAMENTE al abrir el modal (evento de usuario,
  nunca concurrente con la init del host).
- **Validado en Chrome (local 5253, Owner)**: Cargador "Nuevo contacto" abre el modal SIN salir de
  `/cargador-contactos`; creado "Distribuidora Andina Test SAS" -> aparece en Directorio General
  (mismos registros Tercero). Directorio General carga sin regresion y su "Nuevo cliente" abre el
  mismo modal. Build SuperAdmin 0 errores.
- **Pendiente**: (a) limpiar reglas CSS muertas del modal en `DirectorioGeneral.razor.css` (inocuas,
  se dejaron por bajo riesgo). (b) NO desplegado a prod (espera confirmacion del usuario).

---

## 2026-07-16 - Formularios elegibles por tercero en la 3a columna del modal

Item 2 del usuario: "en la herramienta de configuracion de campos podamos configurar los formularios
que se pueden cargar en el modal en la tercera columna; los datos del formulario deben quedar
asociados al tercero id". Decision del usuario (AskUser): **"varios formularios elegibles"**.

- **Hallazgo que ahorro una tabla**: NO existe FK response->tercero, pero el patron ya probado del
  arranque form-first ancla la respuesta por `FormResponse.Reference` (ahi guarda el numero de la
  tarea). Se reusa: `Reference = "TERCERO:{terceroId}"`, cubierto por el indice existente
  `(TenantId, DefinitionId, Reference)`. **No se creo tabla de respuestas.**
- **Dominio/EF**: `TerceroFormLink` (TenantEntity: FormDefinitionId + SortOrder) = solo CONFIG de que
  formularios se ofrecen por tenant. FK a FormDefinition con **Restrict** (quitar del modal es
  explicito, no efecto de borrar la definicion); indices `(TenantId, SortOrder)` y unico
  `(TenantId, FormDefinitionId)`. **Migracion DUAL** `AddTerceroFormLinks` (PG 20260716093705 +
  SQL Server), aditiva (solo CreateTable + indices; Down dropea).
- **Servicio**: `ITerceroFormService`/`TerceroFormService` (Ecorex.Application/Directorio) con
  `ListAsync` / `ListCandidatesAsync` (activos no archivados aun no ofrecidos) / `AddAsync`
  (idempotente) / `RemoveAsync`, + `static ReferenceFor(terceroId)` como unica fuente del ancla.
  Registrado en DI. Tenant-scoped por filtro global.
- **UI**: (a) "Configurar campos" (Directorio General) gana la seccion **FORMULARIOS DEL TERCERO**
  (lista + selector de candidatos + quitar); (b) `TerceroModal` gana la **3a columna** en la pestana
  Datos (chips de formularios + `DynamicFormRenderer` con `DefinitionId` + `Reference` +
  `Mode=Fill`), solo en modo edicion (un formulario necesita un tercero al cual anclarse).
- **Validado en Chrome (local 5253, Owner)**: asociado "Solicitud de cotizacion" (FRM-001) -> fila en
  `tercero_form_links`; al abrir ANDINA S.A.S aparece la 3a columna con el chip; al elegirlo se
  renderiza el formulario real (9 controles) y se crea el borrador
  `reference=TERCERO:019f4bd3-9679-7abf-b420-02c805b0a010` (= id de ANDINA); llenados 5 campos, el
  `data` jsonb los persiste contra ese tercero. La validacion server-side corre (pidio el lookup
  obligatorio "Cantidad estimada" del formulario demo). Build solucion 0 errores; 379/379 tests.
- **Correccion del dueno (misma sesion)**: la 3a columna NO era una columna nueva. El panel derecho
  del prototipo ("Prospecto de cliente / Oportunidad de negocio", `aside.dg-prosp`) **era justamente
  el espacio pensado para los formularios**; su contenido era solo una MUESTRA. Se retiro la 4a
  columna que se habia agregado (`dg-forms-col`) y los formularios pasaron a ese aside, con las
  oportunidades debajo (separador) solo bajo `CrmWiring`. Revalidado: el form renderiza dentro del
  aside sin desborde horizontal y RECUPERA los valores guardados del tercero.
- **Pendiente**: (a) migracion SQL Server sin aplicar/probar (no hay instancia levantada; PG si).
  (b) `SubmittedByTenantUserId` no se pasa al renderer (la respuesta no estampa el usuario).
  (c) reordenar formularios (SortOrder existe, sin UI). (d) NO desplegado a prod (espera confirmacion).

---

## 2026-07-15 (2) - Cargador: filas abren el modal compartido + oportunidades por tercero

Continuacion del reuso del modal de tercero. Feedback: en Cargador de contactos (000740) las filas
abrian OTRO modal (la "FICHA DEL CLIENTE" propia `_cm*`, que duplicaba el TerceroModal). Se jubilo.

- **TerceroModal** gana (parametros nuevos): `CrmWiring` (bool) y `OnAddOpportunity`
  (EventCallback). Con `CrmWiring=true` (solo Cargador) el aside reservado muestra las
  **oportunidades del tercero** (via `IGestorContactosService.ListOportunidadesByTerceroAsync`),
  la pestana Contacto Cliente habilita la **fecha de "Proxima atencion"**, y al registrar una nota
  "Oportunidad"/"Atencion" **crea la oportunidad/cita** (cableado CRM portado del `_cm`). Metodo
  publico `ReloadOpportunitiesAsync()`. En Directorio General (`CrmWiring=false`) las notas son solo
  bitacora (sin cambios). Estilos `tm-opp-*` en TerceroModal.razor.css.
- **GestorContactos**: se elimino el modal `_cm*` (markup + estado + OpenCliente/CloseCliente +
  AddNota/DeleteNota/ResetNoteForm). `OpenClienteAsync` ahora delega en
  `_terceroModal.OpenEditAsync(id)`; el componente se cablea con `CrmWiring="true"
  OnAddOpportunity="OnAddOpportunityFromModal"`; al crear una oportunidad se refresca el aside del
  modal. Se retiro el kanban por etapa de la pestana Oportunidades (con su drag&drop).
- **Pestana Oportunidades agrupada por tercero** (item del usuario): la vista "Por cliente" lista
  un grupo por tercero (avatar, nombre, N oportunidades, valor total) con sus oportunidades y chips
  de etapa; el encabezado del grupo abre el MISMO modal compartido. Toggle "Por cliente / Tabla".
  Estilos `gc-opp-*` en GestorContactos.razor.css.
- **Validado en Chrome (local 5253, Owner)**: pestana Oportunidades agrupa (ANDINA 4 opps $108.2M,
  INGETEL, Produvarios, Maria Fernanda); abrir el grupo ANDINA abre "EDITAR TERCERO" in-place con el
  aside "Oportunidades 4 abiertas $108,200,000" + "Agregar oportunidad". Build 0 errores.
- **Pendiente (item 2, proxima ola)**: formularios elegibles en la 3a columna del modal. Diseno
  confirmado por el usuario = **"varios formularios elegibles"**. Plan (segun mapeo del sistema de
  formularios): reusar `DynamicFormRenderer` con `Reference="TERCERO:{id}"` (NO hay FK response->tercero;
  el patron form-first ya ancla por `Reference`), + tabla pequena de CONFIG (que formularios se
  ofrecen por tenant) + migracion dual PG/SQL Server + seccion en "Configurar campos" + render en el
  modal. NO desplegado a prod (espera confirmacion).

---

## 2026-07-14 - Tareas de proceso: Ola 0 (decisiones) + Ola A1 (encargado del primer nodo)

Capitulo nuevo en el vault: `01. Requerimiento/Capa 2 Tareas y Proyectos/Tareas de proceso -
Arranque y encargado del flujo/` (docs 00 indice, 01 arquitectura, 02 cinco historias de usuario,
03 plan por olas). Continua "Modulo de Tareas - Creacion y ejecucion".

- **Auditoria (read-only)**: al crear una actividad desde el menu Mis Procesos, el enrolamiento en
  el flujo SI funciona (instancia + primer paso Pending/IsCurrent), pero el **ENCARGADO no**: el
  wizard lo pide a mano filtrado por los cargos del CONCEPTO (`TaskWizard.razor:515-524`), no por
  el cargo del **primer nodo BPMN**; `INodeAssigneeResolver` no se consumia desde el arranque; y el
  primer paso nace **sin** `AssignedToTenantUserId` (`WorkflowEngine.AddStep:639-656`), resolviendose
  perezosamente al reclamar. Ademas los conceptos `IniciaModulo` (form-first) igual abren el wizard
  de 4 pasos (el formulario es el paso 3), en vez de abrir DIRECTO el formulario.
- **Hallazgo que cambia el diseno**: `FormDefinitionId` existe SOLO en `ActividadSubcategoria`
  (`ActividadSubcategoria.cs:60`); **no hay formulario por nodo BPMN**.
- **Ola 0 - decisiones del usuario (CERRADA)**: **D1** el arranque form-first usa el formulario del
  CONCEPTO ahora, y el formulario POR NODO se compromete como **Ola D** (dominio + migracion dual +
  editor + runtime), no como backlog difuso; **D2** el flujo manda: el iniciador NO puede elegir
  encargado fuera del cargo del primer nodo (combo restringido + validacion server-side); **D3** un
  concepto con flujo sin publicar SI se ve en el menu, pero el arranque debe AVISAR con un banner
  que la actividad nacera SIN proceso (se ataca el silencio, no la visibilidad).
- **Ola A1 - HECHA**: `IWorkflowStartService` + `WorkflowStartService`
  (`Ecorex.Application/Workflows/`), registrado en DI. Dada una subcategoria, camina el grafo **EN
  SECO** (sin instancia, sin persistir) desde el `startEvent`, **atraviesa compuertas** y devuelve
  el **primer nodo Task** + sus **cargos** (`WorkflowNodePolicy`) + sus **candidatos**
  (`INodeAssigneeResolver`, reusado). La resolucion de compuertas es **espejo exacto** de
  `WorkflowEngine.ResolveOutgoing` con `approvalResult = null` -> el nodo que devuelve es **el mismo
  que el motor activara**. Nunca lanza: reporta `FirstStepStatus` (Ok / SinFlujo / FlujoNoPublicado /
  SinNodoTask / SinCargo / SinCandidatos), que es justo lo que necesitan A2 (preseleccionar),
  A3 (persistir) y C1 (el banner de D3).
- **Verificado**: `WorkflowStartServiceTests` (7 casos) **verde en matriz dual**: PostgreSQL 7/7 y
  SQL Server 7/7. Cubre flujo lineal (primer Task "Cotizar" + cargo + candidato unico), **compuerta
  justo despues del startEvent (la atraviesa)**, los 4 estados de config incompleta y el
  **aislamiento cross-tenant**. Solucion completa en verde.
- **Ola A2 - HECHA**: `TaskWizard.razor` consume `IWorkflowStartService`. En una actividad-proceso el
  combo "Encargado" se llena con los **candidatos del cargo del PRIMER NODO** (ya no con los cargos
  del concepto), queda **RESTRINGIDO** a ellos (D2: el flujo manda), muestra el chip
  **"Paso 1 - {nodo} - {cargo}"** + la nota "Lo dicta el flujo...", y **PRESELECCIONA** al candidato
  cuando hay uno solo. Si el cargo esta vacante, el combo se deshabilita y avisa que hay que
  ocuparlo en Dependencias. `ValidateStep(1)` rechaza un encargado fuera de los candidatos (atajo de
  UI; el servidor lo revalidara en A3). Una actividad SIN flujo conserva el comportamiento clasico.
- **Bug hallado en la validacion visual y corregido**: al cambiar de una actividad-proceso a una
  actividad simple, el encargado **que habia dictado el flujo** se quedaba pegado (el usuario nunca
  lo eligio). Ahora se limpia al salir del modo restringido.
- **Verificado en Chrome real (tenant demo)**: `Cotizacion de equipos` -> chip "Paso 1 - Requerimiento
  - Asesor Comercial", 1 sola opcion, preseleccionado **Operator**; `Compra urgente` -> chip "Paso 1 -
  Aprobacion jefe de compras - Aprobador", 1 sola opcion, preseleccionado **Admin** (cargo distinto ->
  persona distinta: el servicio lee de verdad el BPMN); `Solicitud de compra` (sin flujo) -> sin chip,
  encargado limpio, lista completa (11 usuarios). Regresion: **360/360** Application.Tests + **35/35**
  Domain.Tests.
- **Ola A3 - HECHA**: `TaskItemService.CreateAsync`, dentro de la MISMA transaccion y tras
  `StartInstanceAsync`: (1) toma el paso actual (IsCurrent/Pending); (2) **revalida D2 en SERVIDOR**
  -- si el nodo tiene cargo y el encargado no es candidato (`INodeAssigneeResolver`) -> **rollback
  total** + error tipado (restringir el combo del wizard no basta: un API podria saltarselo);
  (3) **FIJA** `WorkflowStepHistory.AssignedToTenantUserId` (el paso ya no nace colgando);
  (4) audita "ruto el primer paso del flujo a {usuario}". `INodeAssigneeResolver` se inyecta como
  parametro REQUERIDO: una regla de gobierno no debe poder desactivarse en silencio si falla la DI.
- **Verificado (tests)**: `WorkflowStartServiceTests` sube a 10 casos, **verde en matriz dual
  (PG 10/10 + SQL Server 10/10)**: el primer paso nace asignado + notificado; encargado fuera del
  cargo -> Invalid con rollback total (ni tarea, ni instancia, ni pasos); actividad sin flujo sigue
  aceptando cualquier encargado.
- **Verificado (CICLO COMPLETO en Chrome real)**: Owner entra por Mis Procesos > Cotizacion de
  equipos -> wizard con chip "Paso 1 - Requerimiento - Asesor Comercial" y Operator preseleccionado
  -> crea **T00216** -> en BD: ENROLADA / paso "Requerimiento" / Pending / **asignado a operator**
  (antes NULL) -> auditoria: creo la tarea -> notifico a operator -> inicio el flujo -> **ruto el
  primer paso del flujo a operator** -> **login como Operator: T00216 aparece en "Pendientes mios"
  (27) SIN reclamarla**. Ese claim manual era justo lo que A3 elimina.
- **BUG PREEXISTENTE ENCONTRADO Y CORREGIDO (no era de estas olas)**: el host **Ecorex.Api no
  arrancaba** desde el merge de la rama de formularios: `FormResponseService` exige
  `IFormRecordBroadcaster` y **solo la consola Blazor lo registraba**, asi que el contenedor de DI de
  la API no se podia construir -> **27 tests de endpoints en rojo** (Auth/Admin/TenantUsers/
  Onboarding...). Se agrega `NoOpFormRecordBroadcaster` junto a su interfaz (mismo patron que
  `NoOpTaskBroadcaster`, "para procesos sin SignalR (Api, tests)") y se registra en `Ecorex.Api`.
  Los 27 tests vuelven a verde.
- **Nota de coordinacion**: otra sesion estaba refactorizando en PARALELO el mismo working tree
  (extraer el aprovisionamiento del menu del seeder a `IMenuProvisioningService`, para que un tenant
  creado por el alta no nazca sin menu). Su refactor dejo el arbol sin compilar un rato; se completo
  el cableado que faltaba (`TenantAdminService` ahora exige `IMenuProvisioningService` -> doble
  `NoOpMenuProvisioning` en los tests que no ejercitan el menu).
- **Ola B1 - HECHA**: los conceptos form-first (`IniciaModulo` + `FormDefinitionId`) arrancan por el
  FORMULARIO, no por el wizard. Nuevo `FormFirstStarter.razor`; `ActivityBoardDetail` intenta
  `TryOpenAsync(sub)` y, si el concepto no es form-first o su formulario no sirve, **cae al wizard**
  (escape hatch). Nuevo `IFormResponseService.SetReferenceAsync` para anclar una respuesta ya enviada
  al numero de la tarea (que al diligenciarla aun no existia).
- **Lo importante de B1 es el ORDEN**: antes el wizard creaba la tarea AL ENTRAR al paso 3 (parche de
  la Ola 5), asi que un formulario invalido dejaba una **tarea huerfana con su flujo ya arrancado**.
  Ahora: formulario -> validacion en SERVIDOR -> recien entonces nace la actividad -> se ancla la
  respuesta.
- **Bug hallado en la validacion visual y corregido**: con `TituloAuto` = "Requerimiento infra -
  @cliente" y sin cliente capturado, la tarea nacia titulada "Requerimiento infra - " (separador
  colgando). `RenderConceptTemplate` limpia el borde cuando el token queda vacio.
- **Verificado en Chrome real**: el concepto form-first abre FRM-001 **directo** (sin wizard);
  enviarlo **vacio bloquea y NO crea tarea** (la ultima siguio siendo T00216) -- ese era el punto que
  fallaba; al llenarlo nace **T00218** con titulo limpio "Requerimiento infra", en el tablero del
  concepto, y la respuesta queda **Submitted con ref=T00218**. Regresion: 360/360 Application.Tests +
  64/64 Integration (flujos y formularios, matriz dual).
- **Pendiente menor anotado**: mapear un campo del formulario al token `@cliente` (hoy no hay
  convencion que diga QUE campo es el cliente; se limpia el separador para no romper el titulo).
- **Ola B2 - HECHA**: se retira del wizard TODO el parche form-first de la Ola 5: la rama
  `IsFormFirst` del paso 3, la **creacion anticipada de la tarea** en `NextStep`, `FormStepLocked`,
  los handlers `OnFormSubmittedAsync`/`OnFormSkipAsync` y el estado muerto. El wizard queda con **UN
  SOLO camino**: la tarea se crea unicamente al pulsar "Guardar actividad" / "Guardar y crear otra".
  El paso 3 queda informativo (el formulario se diligencia desde el DETALLE, ADR-0038).
- **Verificado en Chrome real (forzando el escape hatch)**: se desactivo FRM-001 en la BD local ->
  el concepto form-first **cayo al wizard** (escape hatch de B1 confirmado); se camino el wizard
  **hasta el paso 4**, pasando por el paso Formulario -- justo donde antes se creaba la tarea -- y la
  ultima tarea **siguio siendo T00218**: no se creo nada. El resumen seguia en "Borrador / Sin
  guardar". FRM-001 restaurado a Active. Solucion verde + 360/360 Application.Tests.
- **Ola C1 - HECHA**: guardas de coherencia (D3: avisar, no ocultar). Tres piezas: (1) banner ambar
  en el arranque (TaskWizard) cuando el flujo no es utilizable -- cubre FlujoNoPublicado / SinNodoTask
  / SinCargo; FormFirstStarter ya lo traia de B1; (2) chip "borrador" en el menu (NavMenu) para la
  hoja de un concepto cuyo flujo no esta publicado -- se resuelve con el set de WorkflowDefinitions
  publicadas del tenant; (3) WorkflowEngine.PublishAsync bloquea publicar un flujo SIN paso Task
  (irrecuperable). El caso "paso sin cargo" NO bloquea la publicacion (muy rigido) -> se avisa al
  crear con el banner.
- **Verificado**: test dual `Publish_FlowWithoutTaskNode_IsRejected` (PG + SQL Server 2/2); regresion
  45 workflow + 360 unitarios verdes. En Chrome real: despublicando COT-COM en local, la hoja mostro
  chip "borrador" y el wizard el banner "nacera sin proceso"; restaurado.
- **Ola C2 - HECHA (verificacion, sin codigo)**: QA end-to-end del arranque de tareas-proceso.
  Arranque E2E visual+BD (desde Mis Procesos se creo **T00219**, nace enrolada con el primer paso
  "Requerimiento" asignado a operator; operator la ve en "Pendientes mios" sin reclamarla). El ciclo
  runtime (atender -> avanzar -> gateway Aprobada/Rechazada -> reinicio) se apoya en
  `WorkflowInboxTests` **6/6 en matriz dual** (invocan los mismos servicios que los botones del
  detalle). Registrado en el vault (05. Pruebas/Historial de corridas, 2026-07-14).
- **Gaps de config del demo hallados (no son bug de codigo)**: el flujo COT-COM v1 tiene Facturacion/
  Entrega sin cargo, y el concepto "Cotizacion de equipos" no tiene columna de cierre (tablero previo
  al auto-tablero 388e895). Anotados en el backlog del capitulo para cerrarlos por configuracion.
- **Nota**: el clic visual "Completar paso" no se re-ejercito por inestabilidad del circuito Blazor
  Server tras reinicios; cubierto por los tests duales y la corrida previa (tarea #67).
- **Ola D - YA EXISTIA (verificada, sin codigo nuevo)**: al ir a construir "formulario por nodo" se
  hallo que YA esta completo desde FASE 4 (ADR-0015, migracion AddDynamicForms 2026-07-03), con un
  mecanismo mejor que el propuesto: entidad join `WorkflowNodeForm` (indice unico por nodo), no
  `WorkflowNodePolicy.FormDefinitionId`. D1 (dominio+migracion dual), D2 (selector en FlowEditor
  Acordeon 2 -> WorkflowDesignService.SetNodeFormAsync) y D3 (runtime GetTaskStepFormsAsync +
  seccion "Formularios del paso" en TaskDetailModal) existen y estan cableados. Verificado:
  `DynamicFormsTests` 16/16 dual, incluido el ciclo completo (asignar form al nodo Cotizacion ->
  avanzar -> el paso pide su form con gateway adelante -> enviar con "Aprobada" -> completa y avanza
  a Facturacion). No se construyo nada: habria sido redundante.
- **Unico punto abierto de la Ola D (decision de producto)**: la precedencia form-first (formulario
  del CONCEPTO, admision) vs form-por-nodo (formulario del PASO) NO existe; son fases ortogonales.
  La D1 original ("gana el del nodo") se penso creyendo que el form-por-nodo no existia. Recomendacion:
  dejarlos ortogonales; a decidir con el usuario.
- **Deuda menor**: dos APIs escriben WorkflowNodeForms (FormDefinitionService.AssignToWorkflowNodeAsync
  sin UI + WorkflowDesignService.SetNodeFormAsync del editor); unificar cual es canonica.
- **Siguiente**: cerrar la decision de precedencia + DEPLOY a prod del acumulado (A/B/C + previo).

## 2026-07-14 - Fix menu: "Directorio General" (000232) desaparecido de "Negocio"

- **Sintoma (reporte del usuario)**: en la seccion de menu "Negocio" faltaba el modulo para crear terceros.
- **Causa raiz**: el feature original (commit `83100d9`) agrego "Directorio General" (route `directorio-general`,
  el CRM de terceros: empresas/personas/contactos, boton "+ Nuevo cliente") SOLO al bloque de seed INICIAL del
  menu, que corre unicamente para tenants nuevos. Los tenants ya sembrados (demo SKY SYSTEM incluido) nunca lo
  recibieron -> `SELECT count(*) FROM menu_nodes WHERE route='directorio-general'` daba 0. No fue una regresion
  de esta sesion; el item nunca se propago a tenants existentes.
- **Fix**: se agrego una llamada idempotente `EnsureMenuItemInSectionAsync(tenantId, sectionSlug:"nego",
  route:"directorio-general", name:"Directorio General", legacyCode:"000232")` en el bloque de reconciliacion
  del `DatabaseSeeder`. Repone el item en la seccion "Negocio" de todos los tenants ya sembrados sin duplicar.
- **Verificado**: al reiniciar la app el log confirmo "item 'Directorio General' (directorio-general) agregado a
  1 vista(s)"; en Chrome real, "Negocio" ahora lista 4 items (Creacion de clientes, Seguimiento de clientes,
  Cargador de contactos, Directorio General) y la pagina `/directorio-general` carga el CRM de terceros.
- **Siguiente**: acumulado de cambios de menu/conceptos/tableros de esta tanda sigue PENDIENTE de deploy a prod.

## 2026-07-13 - Sesion (worktree formularios): F6 - permisos por campo (visibilidad por rol)

- **Hecho (F6, doc 01 D8)**: permisos a nivel de campo.
  - ESQUEMA: `FormQuestion.FieldVisibilityJson` = { "hide":[roles], "readonly":[roles] } (nombres de
    TenantRole: Owner/Admin/Supervisor/Advisor). Migracion dual `AddFormFieldVisibility`. Local; PENDIENTE prod.
  - RENDERER: lee el rol del claim `tenant_role` (cascading AuthState; null en el visor publico -> sin
    restriccion). Un rol en `hide` no ve el campo (RenderQuestion return); en `readonly` lo ve dentro de un
    `<fieldset disabled>` (deshabilita el control sin tocar RenderInput). El valor se conserva al guardar.
  - DESIGNER: checkboxes por rol "Ocultar para" y "Solo lectura para" en el tab Datos.
- **Verificado en navegador (rol Owner)**: NIT con hide=[Owner] -> NO se pinta; Ciudad con readonly=[Owner]
  -> input efectivamente deshabilitado (`:disabled` via `fieldset[disabled]`). Solution verde; 360 tests.
- **WEBHOOKS/INTEGRACIONES: PENDIENTE (decision del usuario 2026-07-13)**: el usuario quiere botones en el
  formulario con reglas de accion configurables (integraciones). El patron .NET correcto NO es reflexion
  abierta (el proyecto la prohibe) sino un REGISTRO DE VERBOS TIPADOS resuelto por DI -> ES EL RulesEngine
  YA EXISTENTE (`Ecorex.Application/Rules/Verbs/`, IRuleVerb). Mapeo: boton (FormControlType.Button) ->
  FormFieldRule -> verbo tipado (allow-list) -> integracion. El usuario analizara la documentacion antes de
  construirlo. NO construir webhooks hasta entonces.
- **Siguiente (resto de F6)**: mascaras de ENTRADA; impresion/PDF con plantilla (object storage); captura
  Tier 2 real (foto/firma/GPS/archivo/barcode) con object storage; botones con reglas de accion (ver nota).

## 2026-07-13 - Sesion (worktree formularios): F5 designer + F6 transversales (defaults dinamicos + formato)

- **F5 (cierre)**: el campo Subform ahora se crea/configura EN EL DESIGNER (tipo "Subformulario" en la
  paleta + selector "Formulario hijo (detalle)" en el tab Datos que lista las definiciones). VERIFICADO
  en navegador (FRM-002 seleccionado como hijo de FRM-021).
- **F6 ARRANQUE (transversales de campo, doc 01 D8)**:
  - ESQUEMA: `FormQuestion` += `DefaultDynamic` (enum None|Today|CurrentUser|CurrentEntidad) + `Format`
    (string: currency|percent|integer|decimal). Migracion dual `AddFormFieldTransversals`. Aplicada
    local; PENDIENTE prod (doc 04).
  - RENDERER: al abrir a llenar, el default DINAMICO gana sobre el literal (Hoy -> fecha de hoy, Usuario
    -> id); `FormatValue` muestra moneda/%/entero (aplicado a los campos calculados de solo lectura).
  - DESIGNER: dropdowns "Valor por defecto dinamico" y "Formato" en el tab Datos.
- **Verificado en navegador**: campo Fecha con default Today -> pre-llenado 2026-07-13; subtotal con
  format currency -> "$ 1,620,000". Solution verde; 360 tests.
- **Siguiente (resto de F6)**: permisos por campo (FieldVisibilityJson ver/editar por rol), impresion/PDF
  con plantilla + object storage, webhooks tipados al confirmar, captura Tier 2 real (foto/firma/GPS/
  archivo/barcode) con object storage, mascaras de entrada. Son piezas de integracion grandes (object
  storage, PDF, cola de webhooks) -> incrementos siguientes.

## 2026-07-13 - Sesion (worktree formularios): F5 - maestro-detalle entre formularios

- **Hecho (F5, doc 01 D7)**: el detalle son registros de OTRA definicion (no solo GridDetail embebido).
  - ESQUEMA: `FormControlType` += `Subform`; `FormQuestion.SubformDefinitionId`; **tabla nueva
    `form_record_links`** (padre-hijo: parent_response_id, parent_field_code, child_response_id, unico).
    Migracion dual `AddFormRecordLink` (1 tabla + 1 columna). Aplicada local; PENDIENTE prod (doc 04).
  - SERVICIO: `FormResponseService.ListChildrenAsync/AddChildAsync/UnlinkChildAsync` (crea el hijo como
    FormResponse propio de la definicion hija + enlace). `Subform` marcado IsNonInput (no va en el jsonb).
  - RENDERER: el campo Subform lista los hijos + "Agregar registro" -> abre el formulario hijo ANIDADO
    (DynamicFormRenderer recursivo, Fill) -> al enviar, el hijo (con su numero/estado) aparece enlazado.
    Quitar desengancha (conserva el hijo).
- **Verificado en navegador (`ecorex_forms`)**: FRM-021 con campo Subform -> hijo FRM-002; Agregar ->
  formulario FRM-002 anidado (Bodega/Fecha) -> Enviar -> hijo **FRM-002-000001 (Confirmado)** enlazado en
  la lista del padre; `form_record_links` con el enlace (detalle -> FRM-002-000001). Solution verde; 360 tests.
- **Siguiente**: designer para crear/configurar el campo Subform (elegir definicion hija) sin SQL;
  reportar el detalle aparte (ya es posible: cada hijo es un registro con numero). Luego F6.

## 2026-07-13 - Sesion (worktree formularios): F4 ARRANQUE - formulario como modulo (menu dinamico)

- **Hecho (F4, nucleo: promover a modulo con colocacion dinamica en el menu)**:
  - ESQUEMA: `FormDefinition` += `IsModule`, `ModuleMenuNodeId`, `ModuleIcon`, `ListColumnsJson`,
    `FilterFieldsJson`. Migracion dual `AddFormModule` (PG `20260713121447` + SQL Server), aditiva.
    Aplicada en local; **PENDIENTE en prod** (doc 04).
  - SERVICIO: `FormDefinitionService.SetModuleAsync` reusa `IMenuConfigService.CreateNodeAsync`
    (Kind=Item, Route=/m/{code}) para crear el nodo de menu EN LA VISTA + GRUPO que elige el usuario;
    al retirar borra el nodo. `FormResponseService.ListRecordsAsync` para la bandeja.
  - UI: panel "Propiedades del formulario" += toggle "Es un modulo" + selector de **vista** + selector
    de **grupo del menu (donde aparece)** (arbol aplanado a Section/Subgroup) + icono. Pagina bandeja
    `FormModule.razor` en `/m/{code}` con el listado de registros enviados.
- **Verificado en navegador (`ecorex_forms`)**: promover FRM-021 -> elegir grupo "Automatizacion" ->
  nodo de menu creado (Item, /m/FRM-021) bajo ese grupo, is_module=true; el modulo aparece en el menu y
  `/m/FRM-021` muestra la bandeja con el registro FRM-021-000001 (Confirmado). Encadena F3->F4.
  Solution verde; 360/360 tests.
- **Bandeja consultable (mismo dia)**: `FormModule.razor` += KPIs (registros / confirmados / anulados /
  este mes con % crecimiento vs mes anterior) + filtros (estado + busqueda por numero/referencia) +
  export CSV (data-URI). VERIFICADO en navegador: 3 reg (2 conf, 1 anul, este mes +100%); filtro
  Anulados -> 1 fila; busqueda "002" -> FRM-021-000002.
- **Bandeja EN VIVO (SignalR, mismo dia)**: `IFormRecordBroadcaster` (Application) + impl
  `SignalRFormRecordBroadcaster` (reusa `TaskHub` y su grupo por tenant, evento "FormRecord");
  `FormResponseService` emite tras confirmar un registro. `FormModule.razor` se suscribe (HubConnection
  server-side, patron de ActivityBoardsIndex) y recarga. VERIFICADO en navegador con DOS pestanas: enviar
  en el constructor -> la bandeja pasa de 4 a 5 SOLA (FRM-021-000011 arriba, sin recargar).
  OJO fix: el connect NO puede ir gated en `_def` dentro de OnAfterRenderAsync(firstRender) porque
  OnParametersSetAsync puede seguir cargando; el handler chequea `_def`.
- **Siguiente (resto de F4)**: vista aplanada para BI, policies `Form.{code}.*` (hoy [Authorize] +
  visibilidad del nodo), config de columnas/filtros de la bandeja en el designer, export a Excel (hoy
  CSV). F4 ya cubre lo esencial (modulo + menu dinamico + bandeja KPIs/filtros/export/en vivo). Luego F5/F6.
- **Decision (doc 03 B)**: "convertir en modulo" es opcional del formulario; el usuario elige la
  ubicacion en el menu (vista + grupo), no es fija.

## 2026-07-13 - Sesion (worktree formularios): F3 - logica confirmar/anular + identidad

- **Hecho (F3, logica sobre el esquema)**:
  - `FormResponseService.SaveAsync`: confirmar = enviar. Al enviar un form transaccional se asigna
    identidad ANTES de la transaccion (patron `ISequenceService`: EnsureSequence+Next fuera de la tx),
    RecordStatus=Confirmed, TransactionDate. Idempotente (no reasigna si ya Confirmed).
    - Modo Sequence: consume `TenantSequence` (code corto "F"+8hex del id porque `code` es varchar(10);
      OJO bug cazado: `FORM:{guid}` de 41 chars hacia fallar el INSERT y `EnsureSequenceAsync` se lo
      tragaba -> "contencion excesiva"). Numero legible: prefijo = codigo del form (FRM-021-000001).
    - Modo NaturalKey: numero = valor del campo `IdentitySourceFieldCode`; unicidad por indice unico
      filtrado (DbUpdateException -> error de validacion "clave duplicada").
  - `VoidAsync`: anula un registro Confirmed (Voided + motivo + auditoria; no libera el numero).
  - DTO `FormResponseDto` += RecordNumber/RecordStatus/TransactionDate; renderer muestra chip verde con
    el numero y chip "Anulado".
- **Verificado (navegador, `ecorex_forms`)**: FRM-021 transaccional (Sequence) -> Enviar -> registro
  FRM-021-000001, status=Submitted, rec_status=Confirmed, tx_date fijada, secuencia consumida (next=2).
  Build verde; 360/360 tests Application.
- **UI de F3 (mismo dia)**: panel "Propiedades del formulario" en el designer (boton Propiedades ->
  modal con toggle Es transaccional + selector de identidad Ninguna/Consecutivo/Clave natural +
  selector de campo clave) via `IFormDefinitionService.SetTransactionalAsync`; `FormDefinitionDetailDto`
  += IsTransactional/IdentityMode/IdentitySourceFieldCode. Boton "Anular" (con motivo) en el renderer
  cuando el registro esta Confirmed -> `IFormResponseService.VoidAsync`. VERIFICADO en navegador:
  el panel lee (transaccional=on, Sequence) y escribe (cambio a NaturalKey campo=nit persistido).
- **Con esto F3 queda funcional end-to-end** (esquema + logica + UI de config + anular). Pendiente fino:
  cierre por evento (firma) doc 03 B; indices de dimension para BI; test unit/integracion de confirmar.
  Luego F4 (formulario-modulo).

## 2026-07-12 - Sesion (worktree formularios): F3 ARRANQUE - esquema transaccional

- **Contexto**: se confirmo que los agregados de GridDetail (Sum/Count/Avg/Min/Max) YA funcionan todos
  (demo en navegador: Count->2, Avg->1250, Sum->6000); falta solo el selector en el designer.
- **Hecho (F3, esquema / checkpoint)**: la respuesta confirmada se vuelve un REGISTRO (doc 01 D2/D3).
  - ENUMS: `FormIdentityMode` (None|NaturalKey|Sequence), `FormRecordStatus` (Draft|Confirmed|Voided),
    en `ConfigureConventions`.
  - `FormDefinition` += `IsTransactional`, `IdentityMode`, `IdentitySourceFieldCode`,
    `UniqueKeyFieldsJson`, `SequenceId` (ref logica a `TenantSequence`).
  - `FormResponse` += `RecordNumber`, `RecordStatus`, `TransactionDate`, `VoidedAt`,
    `VoidedByTenantUserId`, `VoidReason`. Indice unico filtrado (tenant, definition, record_number).
  - MIGRACION DUAL `AddFormTransactional` (PG `20260712213148` + SQL Server), aditiva. Aplicada en
    local `ecorex_forms`; **PENDIENTE en prod** (registro en doc 04).
  - Build verde; 360/360 tests Application.
- **DECISION a validar**: se agrego `record_status` (ciclo transaccional) APARTE del `status` existente
  (Draft/Submitted, ciclo de envio), para no tocar el flujo actual. Confirmar con el usuario.
- **Siguiente (grueso de F3)**: logica de confirmar (consumir `ISequenceService` en modo Sequence /
  validar unicidad en NaturalKey, en transaccion; anular no libera el numero; idempotente por
  FormResponse.Id) en `FormResponseService`; DTOs; panel "Propiedades del formulario" (IsTransactional +
  identidad) en el designer; boton confirmar/anular en el renderer. Cierre por evento (ej. firma) segun
  doc 03 B.

## 2026-07-12 - Sesion (worktree formularios): F2 GridDetail - totales de columna + roll-up

- **Hecho (cierra F2)**: totales/calculo en tablas GridDetail.
  - `FormGridCalculator` (Application/Forms/Calc) COMPARTIDO renderer+servidor: parsea columnas con
    claves opcionales `calc`/`agg`/`rollup` (columnas viejas [{id,label}] siguen valiendo), evalua la
    formula por fila (reusa `FormExpressionEvaluator`), agrega la columna (Sum/Count/Avg/Min/Max) y
    devuelve el roll-up al campo del encabezado. 9 unit tests.
  - RENDERER: columna calculada por fila (solo lectura), fila de totales (`<tfoot>`) y roll-up a un
    campo del encabezado; recomputo ante cualquier cambio de celda (`RecomputeGrids`).
  - SERVIDOR: `FormResponseService.SaveAsync` recomputa las tablas antes del calculo escalar (el
    roll-up alimenta calcs del encabezado); el cliente no es fuente de verdad.
- **Verificado (navegador, `ecorex_forms`)**: tabla Lineas con Subtotal=`{cant}*{precio}` agg Sum rollup
  "Total general" -> filas 2x1500 y 3x1000 -> subtotales 3000/3000, fila de totales 6000, Total general=6000.
  Solution verde; 360/360 tests Application.
- **Siguiente**: designer para configurar columnas de GridDetail con formula/agg/rollup sin SQL (hoy la
  config de columna avanzada se hace por OptionsJson; el editor de columnas del designer es basico label).
  Luego F3 (transaccionalidad: consecutivo/estado/cierre).

## 2026-07-12 - Sesion (worktree formularios): Formularios avanzados OLA F2 (parcial) - Campo calculado

- **Agentes**: Claude (worktree `funny-bell-3f8562`). Mismo protocolo de esquema que F1 (ver doc 03 s.D / doc 04 del vault).
- **Hecho (F2, primer incremento: campos calculados escalares)**:
  - DOMINIO: `FormQuestion` += `CalcExpression (string?)`, `Aggregate (FormAggregate)`; enum `FormAggregate`
    (None|Sum|Count|Avg|Min|Max) registrado en `ConfigureConventions`.
  - MIGRACION DUAL `AddFormCalcFields` (PG `20260712202108` + SQL Server), aditiva. Aplicada en local
    `ecorex_forms`; **PENDIENTE en prod** (la aplica la sesion principal, registro en doc 04).
  - EVALUADOR: `FormExpressionEvaluator` (Application/Forms/Calc) - sandbox TIPADO con allow-list:
    aritmetica + parentesis + menos unario + refs `{codigo}`; sin codigo arbitrario ni reflexion
    (evita el RCE del legacy). Campo vacio = 0; expresion invalida = null. 16 unit tests verdes.
  - RENDERER: campo calculado = solo lectura, recomputado en cliente ante cualquier cambio
    (`RecomputeCalculatedFields`) con el MISMO evaluador; incluido en carga, SetValue, ToggleMulti y
    autollenado de lookup (encadena F1->F2).
  - SERVIDOR: `FormResponseService.SaveAsync` recomputa los calc en servidor y DESCARTA el valor del
    cliente (no confiable para montos; fuente de verdad = servidor).
  - DESIGNER: input "Formula (campo calculado)" en el tab Datos.
- **Verificado (navegador, `ecorex_forms`)**: campo Subtotal = `{cantidad} * {precio_item}` -> Cantidad=3,
  Precio=540000 -> Subtotal=1620000 en vivo, solo lectura. Solution build verde; 351/351 tests Application.
- **Siguiente (resto de F2)**: totales de columna en GridDetail (`Aggregate` -> fila de totales) + columna
  calculada por fila + roll-up al encabezado (doc 01 D5, doc 02 s4). Luego F3 (transaccionalidad).
- **Decisiones**: formulas modestas (aritmetica), "sin artilleria pesada" (doc 03 B). Evaluador compartido
  cliente/servidor. Valor del cliente para calc se ignora en el guardado.

## 2026-07-12 - Sesion (worktree formularios): Formularios avanzados OLA F1 - Lookups / autollenado

- **Agentes**: Claude (worktree `funny-bell-3f8562`, rama `claude/briefing-worktree-formularios-f50017`).
- **Contexto de trabajo (acordado con el usuario)**: se trabaja SOLO en formularios avanzados, en un
  worktree aparte, en paralelo con la sesion principal. El agente CODEA todo (incluida la migracion),
  la aplica en una BD LOCAL de trabajo (`ecorex_forms`, copia de dev) y deja el registro de cada cambio
  de esquema en el vault (doc 04) para que la sesion principal lo replique en PROD cuando pueda. La
  remota NO se toca desde aqui. Protocolo: vault doc 03 seccion D; registro de tablas: vault doc 04.
- **Hecho (F1 completa, spec vault Capa 4, doc 01 D4 + doc 02 s2)**:
  - DOMINIO: `FormQuestion` += 7 columnas (`SourceKind/SourceRef/DisplayField/ValueField/FilterJson/
    AutofillMapJson/Presentation`); enums nuevos `FormSourceKind` (Options|DataContainer|Tercero|Item)
    y `FormFieldPresentation` (Autocomplete|Dropdown|Modal), registrados en `ConfigureConventions`.
  - MIGRACION DUAL: `AddFormLookupFields` en PG (`Ecorex.Infrastructure`) y SQL Server
    (`Ecorex.Infrastructure.SqlServer`); ADITIVA (bajo riesgo). Validada en Postgres local efimero.
    **Reporte de campos entregado (doc 04). Estado: aplicada en local, PENDIENTE en prod (la aplica la
    sesion principal).**
  - SERVICIO: `IFormLookupService` + `IFormLookupSource` con 3 adaptadores (`TerceroLookupSource`,
    `ItemLookupSource`, `DataContainerLookupSource`) en `Application/Forms/Lookups`. Server-side,
    paginado, parametrizado; tenant por el filtro global; interfaz extensible (sumar fuente = registrar
    otro adaptador). `ResolveAsync` revalida el id elegido (existe + del tenant). Reusa
    `TerceroFieldService`/`ItemFieldService` (fichas dinamicas) y `IDataContainerService`.
  - UI: `DynamicFormRenderer` -> control lookup (autocompletar/lista/buscador); al elegir guarda el id y
    COPIA los campos de `AutofillMapJson` a los destinos; boton "Crear" deep-link al modulo si falta el
    dato. `FormDesigner` (tab Datos) -> bloque "Origen de datos" (Origen/Fuente/Presentacion/Mostrar/
    Filtro/mapa de autollenado), con metadata de campos (estandar + fichas) cargada por el servicio.
  - DTOs (`SaveFormQuestionRequest`/`FormQuestionDto`) y mapeo en `FormDefinitionService` extendidos;
    `ToRequest` del designer arrastra los campos de lookup (no se pierden al hacer patch).
- **Verificado (navegador, BD local `ecorex_forms`)**: campo Cliente (Directorio, Autocompletar) ->
  escribir "a" trae 5 terceros reales -> elegir "ANDINA S.A.S" autollena NIT=901.111.222 y Ciudad=Bogota.
  Designer muestra todo el bloque configurable (incluye fichas dinamicas del tenant). `dotnet build`
  solution verde; unit del dispatcher `FormLookupServiceTests` 4/4.
- **Siguiente**: (1) la sesion principal aplica `AddFormLookupFields` a prod (doc 04); (2) test de
  aislamiento cross-tenant del lookup en `Integration.Tests` (Testcontainers, dual) + round-trip
  guardar/leer; (3) probar en navegador los adaptadores Item y DataContainer; (4) revalidacion de
  servidor del id lookup en `FormResponseService.SaveAsync` (hoy la garantia es el filtro global +
  `ResolveAsync`). Luego OLA F2 (calculo/formulas).
- **Bloqueos**: el navegador integrado hace timeout en screenshots y su snapshot va con retraso con
  Blazor Server; se verifico manejando el DOM vivo con javascript_tool (los clicks/handlers SI corren).
- **Decisiones**: valor guardado = id de la entidad/fila; autollenado por COPIA (snapshot), no
  referencia (decision del usuario). `ValueField` fijo a "id" al elegir una fuente de datos.

## 2026-07-08 - Sesion: Editor bpmn-js (iconos) + deploy a prod + dev conectado a la BD de prod

**Agentes**: sesion principal + agente de fix de gateways (ADR-0037, ver entrada siguiente).

**Hecho**:
- **Editor de flujos (bpmn-js)**: la paleta y el context pad (herramientas sobre cada nodo) salian como
  cuadros en blanco porque el webfont `bpmn-icon-*` no viene con los assets vendoreados. Se reemplazaron
  por iconos SVG inline (data-URI, sin descargas): iconos de paleta mas marcados +
  `AcotadoContextPadProvider` que sobreescribe al provider nativo (anexar tarea/compuerta/fin, conectar,
  eliminar) + `injectStyle()`. Validado en Chrome (paleta 6 iconos, context pad 5). Commit `1c46c26`.
- **Fix de gateways (ADR-0037)**: ver entrada siguiente (agente aparte). Commit `a352de3`.
- **Deploy a produccion + dev conectado a la BD de prod** (decision del usuario):
  - Push de `main` local -> `fase-0/clon-backbone` (fast-forward `5829de6 -> d49d7d9`, luego `a418419`).
  - Redeploy del server `10.0.0.3` (`/opt/ecorex`, build-from-git): backup previo (`backup.sh`),
    `docker compose -f docker-compose.from-git.yml build --no-cache` + `up -d`. Prod migro `85 -> 88`
    (`AddMenuConfig`, `AddRoles`, `AddNodeAssignment` aplicadas al arrancar), login HTTP 200.
  - Dev local apuntando a la BD de prod via tunel SSH (`localhost:15433`), cadena en
    `appsettings.Development.local.json` (GITIGNORED). Guard `SkipDemoSeed` (Program.cs, `a418419`) para
    NO sembrar demo en prod. Validado en Chrome: login `admin@ecorex.local` (existe solo en prod) ->
    Dashboard Super Admin con datos de prod (1 empresa).
  - Doc de conexion para el equipo (onboarding): vault Obsidian ->
    `04. Notas para desarrollador/Conexion a la base de datos (dev y prod).md`.

**Siguiente**:
- (Opcional) redeploy de prod a `a418419` para dejar la imagen 1:1 con la rama (el guard no afecta a prod).
- Tunel SSH persistente (autossh / tarea programada) para que el dev no dependa de la sesion.
- Retomar validacion de EJECUCION de flujos en Chrome (pausada por el usuario).
- Compañero nuevo trabajara en rama `formularios` (ver doc de conexion).

**Bloqueos**: ninguno.

**Decisiones**:
- Iconos del editor por SVG inline (offline), no webfont.
- El dev local se conecta a la BD de PRODUCCION (`10.0.0.3`), no a un dev/staging aparte.
- Credenciales (BD/SSH) NUNCA en el repo publico: solo en `appsettings.*.local.json` gitignored y el
  `.env` del server (chmod 600).

---

## 2026-07-08 - Sesion: Compuertas exclusivas auto-resueltas en el motor (ADR-0037)

**Agentes**: agente de fix (runtime de flujos - GAP de gateways estancados).

**Pedido**: los `exclusiveGateway` se estancaban como paso current Pending y el caso no avanzaba.
Verificado en la BD dev: 25 instancias de COT-COM con "Cotizacion" Completed y el gateway "Aprobacion"
como paso ACTUAL sin resolver (0 gateways resueltos). Causa raiz: cuando "Cotizacion" tiene FORMULARIO,
`FormResponseService.SaveAsync` completaba el paso via `CompleteStepAsync` SIN approvalResult; el motor
dejaba el gateway Pending-current y la logica que lo completaba vivia SOLO en
`WorkflowInboxService.CompletePendingStepAsync` (que el camino de formulario no usa).

**Hecho** (SIN migracion: no se agrego ninguna columna):
- **Motor** (`WorkflowEngine`): un `exclusiveGateway` ya NO queda Pending-current. Al activarlo,
  `ActivateNodeAsync` lo marca `Completed` HEREDANDO el `ApprovalResult` del paso que lo activo; el bucle
  de `AdvanceAsync` lo procesa (IsReady) en la MISMA pasada y `ResolveOutgoing` enruta por
  ConditionExpression (o arista default). Sigue siendo fila de historial (auditoria, append-only). Sin
  condicion que case ni default -> Stuck (comportamiento previo). Tope de 50 intacto.
- **Rechazo**: `RejectStepAsync` ahora ATRAVIESA los gateways (nuevo `ResolveReactivableSources`, con
  visitados anti-ciclo) para reactivar el nodo humano real, no el gateway.
- **Inbox**: se ELIMINA de `CompletePendingStepAsync` la logica que completaba el gateway a mano; ahora
  solo completa el paso Task con la decision y el motor resuelve el gateway.
- **Camino de formulario**: `IFormResponseService.SaveAsync` acepta `approvalResult` opcional (lo propaga
  a `CompleteStepAsync`). `GetTaskStepFormsAsync` calcula `IsGatewayAhead` + `ApprovalOptions`
  (`WorkflowInboxProjection.ResolveGatewayAhead`), expuestos en `TaskStepFormDto`. `DynamicFormRenderer`
  recibe `ApprovalOptions`: muestra la decision (radio) junto al formulario, deshabilita "Enviar" hasta
  elegir y propaga la eleccion al enviar. Cableado en `MisPasos.razor` y `TaskDetailModal.razor`.
- **Bug de circuito destapado y corregido**: pasar `ApprovalOptions` re-disparaba `OnParametersSetAsync`
  del renderer durante su carga async -> dos operaciones concurrentes sobre el MISMO DbContext del circuito
  ("a second operation was started on this context") -> circuito caido y el formulario quedaba en "Cargando
  formulario". Se agrego un guard de reentrada (`_loadInProgress`) en `OnParametersSetAsync`.
- **Datos varados**: `DatabaseSeeder.ResolveStuckGatewaysAsync(engine)` (idempotente, Development) resuelve
  los gateways ya varados heredando la decision del paso previo (o default). Ademas
  `AlignDemoGatewayConditionsAsync`: el seed demo COT-COM traia condiciones en ingles
  (`approval == 'Approved'/'Rejected'`) que NUNCA casaban con las opciones en espanol (Aprobada/Rechazada);
  se corrige el XML del seed y se realinean idempotentemente las aristas ya sembradas. Encadenados en
  Program.cs con ambient del tenant demo.

**Tests**:
- Integracion DUAL (PG 5442 + SQL Server 1443), 38/38 verdes: `WorkflowEngineTests` (gateway approved->rama,
  rejected->reinicio, decision capturada en el paso previo; append-only con gateway Completed heredado;
  rechazo atraviesa gateway), `DynamicFormsTests` (NUEVO: form+gateway -> submit con decision Aprobada
  enruta a Facturacion y el gateway queda resuelto), `WorkflowInboxTests`.
- Unit 22/22 (`WorkflowConditionEvaluator`, `WorkflowInboxProjection`).
- E2E flujos verdes: `WorkflowFormTests` (form del paso + decision Aprobada -> el motor resuelve la compuerta
  y el paso vigente es Facturacion; ANTES estancaba en Gateway_Aprobacion), `WorkflowInboxTests`,
  `FlowsEditorTests`, `NodeAssignmentTests`.
- Verificado en vivo (5234): al diligenciar Cotizacion aparece la decision Aprobada/Rechazada junto al
  formulario, Enviar deshabilitado hasta elegir, y el envio enruta (no se estanca).

**Decisiones**: ADR-0037 (`docs/decisiones/0037-gateways-auto-resueltos.md`).

**Deudas**: el guard de reentrada del renderer es puntual; convendria una revision general de la
concurrencia DbContext/circuito del `DynamicFormRenderer`. La condicion de gateway sigue siendo un literal
simple (`approval == 'X'`) evaluado contra el Name de la arista; el RulesEngine tipado llegara en otra ola.
La reset del instance demo a Requerimiento fue manual en la BD dev (los E2E de bandeja son stateful).

---

## 2026-07-08 - Sesion: Runtime de flujos - bandeja "mis pasos" (ADR-0036, ola F2, final)

**Agentes**: agente de feature (runtime operativo de flujos - bandeja + atender).

**Pedido**: cerrar el objetivo de flujos operativos con la BANDEJA de "mis pasos pendientes",
ATENDER un paso (formulario del nodo o completar/aprobar) y AVANZAR el caso. El motor ya hacia
casi todo; esta ola es sobre todo la query + la UI + el cableado. Consume `INodeAssigneeResolver`
de la ola F1 (ADR-0035).

**Hecho** (SIN migracion: todo el modelo ya existia):
- **Servicio** `IWorkflowInboxService`/`WorkflowInboxService` (Application/Workflows, tenant-scoped,
  resultados tipados `WorkflowResult<T>`): `GetMyPendingStepsAsync(tenantUserId)` (pasos current+Pending
  de instancias Running que el usuario puede atender: asignado, o sin asignar y candidato del resolver;
  devuelve tarea/proceso/nodo, estado de asignacion, hasForm, isGatewayAhead + opciones, ciclo, fecha);
  `ClaimStepAsync` (modelo "cualquiera lo toma"); `ReassignStepAsync` (solo si el nodo AllowsAssignment,
  auditado); `CompletePendingStepAsync` (valida candidatura y delega en `IWorkflowEngine.CompleteStepAsync`).
  Registrado en DI.
- **Gateway adelante + opciones** (documentado): si una arista saliente del nodo apunta a un
  ExclusiveGateway, las opciones = los `Name` de las aristas salientes DEL gateway (Aprobada/Rechazada),
  que se pasan como `approvalResult` a CompleteStep (misma semantica que `ResolveOutgoing` del motor).
  Logica pura aislada en `WorkflowInboxProjection` (sin EF, patron `OrgAssigneeTree`).
- **UI**: `Components/Pages/MisPasos.razor` (+ `.css`), `@page "/mis-pasos"`, policy `MisPasos.Ver`
  (RequireClaim tenant_id), InteractiveServer, tokens ECOREX. Tarjetas con "Tomar" y panel "Atender"
  (DynamicFormRenderer si hasForm; botones Aprobada/Rechazada+comentario si gateway; "Completar" si no;
  "Reasignar" si el nodo lo permite). Empty state + boton "Actualizar". Item de menu "Mis pasos"
  (route `mis-pasos`, code 000637) en "Mis Procesos" (seed fresco vistas Completo+Simple + reconciliacion
  de demos ya sembrados).
- **Detalle de tarea**: `TaskDetailModal.razor` gana seccion "Flujo" que, si el usuario es candidato de
  un paso current de la tarea, ofrece Tomar/Completar/Aprobar reusando el servicio de bandeja (los pasos
  con formulario se siguen atendiendo por "Formularios del paso").
- **Seed** `EnsureWorkflowRuntimeDemoAsync` (idempotente, Development, encadenado en Program.cs con
  ambient del tenant demo tras la asignacion por nodo): crea una TAREA del ActivityType COT-COM via
  `ITaskItemService.CreateAsync` -> instancia Running con Requerimiento Pending sin reclamar; candidato
  = cargo Asesor Comercial (operator@). Al entrar como operator@ a /mis-pasos hay un paso listo.
- **Tests**: Application.Tests `WorkflowInboxProjectionTests` (CanAttend candidato/dueno + gateway-ahead
  y sus opciones, dedup/blanks); Integration.Tests `WorkflowInboxTests` DUAL PG+SQL (crear tarea -> paso
  en bandeja del candidato y NO de un extrano -> Claim -> CompletePendingStep avanza al siguiente cargo
  -> gateway Aprobada->Facturacion / Rechazada->reinicio ciclo 1; aislamiento cross-tenant); E2E
  `WorkflowInboxTests` (login operator@ -> /mis-pasos -> ve el paso demo -> Tomar -> Atender -> Completar
  -> desaparece).

**Gate**: `dotnet build Ecorex.sln` 0 errores; `dotnet format --verify-no-changes` limpio.

**Siguiente**: refresco SignalR de la bandeja (deuda declarada, no bloquea); selector de reasignacion
acotado a candidatos del nodo.

---

## 2026-07-07 - Sesion: Asignacion por nodo (dependencias/cargos, ADR-0035, ola F1)

**Agentes**: agente de feature (runtime de flujos - asignacion por nodo).

**Pedido**: definir QUIEN atiende cada nodo Task del flujo por DEPENDENCIAS/CARGOS del
organigrama (no por usuarios directos), decidido por el usuario (modelo `PERMISO_CARGO` del
legacy). Ola F1: modelo de dominio + resolver listo; la bandeja/atender es la ola F2.

**Hecho**:
- **Dominio**: enum `OrgUnitClassifier { Dependencia, Cargo, Funcionario }`; `OrgUnit.Classifier`
  (default Dependencia) + `OrgUnit.TenantUserId` (solo Funcionario). Entidad `WorkflowNodePolicy`
  (TenantEntity: WorkflowNodeId FK cascade, OrgUnitId FK NO ACTION, SortOrder; unico
  (WorkflowNodeId, OrgUnitId)). DbSet en IApplicationDbContext + EcorexDbContext + configs.
- **Migracion DUAL** `AddNodeAssignment` (PG 20260708010501 + SQL Server 20260708010542):
  columnas classifier (default 'Dependencia') + tenant_user_id en org_units; tabla
  workflow_node_policies. Aplicada y VERIFICADA en PG 5442 y SQL Server 1443 (esquema chequeado
  por psql/sqlcmd). Puramente aditiva.
- **Servicio + resolver** (Application, resultados tipados): `IOrgUnitService`/DTOs/`SaveOrgUnitRequest`
  extendidos con Classifier + TenantUserId + validacion de coherencia jerarquica (Cargo bajo
  Dependencia, Funcionario bajo Cargo con TenantUserId). `IWorkflowNodePolicyService`
  (List/Add/Remove + ListAssignableUnits; rechaza Funcionario y duplicados, tenant-scoped).
  `INodeAssigneeResolver.ResolveCandidatesAsync(nodeId)` -> TenantUserIds distintos (funcionarios
  descendientes + miembros + responsable), con la logica de arbol PURA en `OrgAssigneeTree`
  (testeable sin EF, tolera ciclos). Registrados en DI.
- **UI**: `FlowEditor.razor` acordeon "Asignar usuarios" REAL (reemplaza el placeholder): lista de
  dependencias/cargos con quitar, selector del arbol filtrado a Dependencia|Cargo + Asignar, y
  conteo "N funcionarios atenderan este paso"; mensaje si el nodo no admite asignacion. Vinculo por
  nodo permitido tambien en publicadas (como formulario/regla). `Dependencias.razor`: selector de
  Classifier + dropdown de usuario del tenant para Funcionario + badge de clasificador en el arbol.
  Bridge E2E `window.ecorexBpmnE2E.select` agregado.
- **Seed** `EnsureOrgAssignmentDemoAsync` (idempotente, Development, encadenado en Program.cs):
  Comercial->Asesor Comercial->Funcionario (operator/owner) y Finanzas->Aprobador->Funcionario
  (admin); policies sobre COT-COM (Task_Requerimiento->Asesor, Task_Cotizacion->Aprobador).
- **Tests (todos verdes)**: Application.Tests `OrgAssigneeTreeTests` 7/7 (cargo->funcionarios,
  dependencia->descendientes, miembros+responsable, vacio, distinct, ciclos); Integration.Tests
  `NodeAssignmentTests` 8/8 DUAL PG+SQL (persistencia+resolver releido, unicidad+rechazo
  Funcionario, aislamiento cross-tenant, cascada al borrar la definicion); E2E `NodeAssignmentTests`
  1/1 (crear borrador -> tarea -> permite asignacion -> asignar dependencia -> persiste). Suite
  Application.Tests completa 326/326.

**Gate**: `dotnet build Ecorex.sln` 0 errores; `dotnet format --verify-no-changes` limpio.

**Siguiente (ola F2)**: bandeja/atender que consume `INodeAssigneeResolver` (asignacion efectiva
del paso: elegir el usuario concreto de entre los candidatos). Reordenar policies en el editor.

**Decisiones**: ADR-0035 (clasificador Dependencia/Cargo/Funcionario, WorkflowNodePolicy solo
Cargo/Dependencia, resolver nodo->usuarios, editor panel real; asignacion efectiva y bandeja en F2).

---

## 2026-07-07 - Sesion: Editor de flujos migrado a bpmn-js (ADR-0034)

**Agentes**: agente de feature (migracion del editor BPMN del modulo 000291).

**Pedido**: reemplazar el canvas SVG propio del EDITOR de flujos (`/flujos`, ADR-0022) por
**bpmn-js** embebido via JS interop, con paleta ACOTADA. Solo el editor; sin tocar la semantica
del motor de ejecucion. Decisiones del usuario: bpmn-js vendored del legacy (self-hosted, sin
descargas), desviacion de fidelidad aprobada, palette acotado, parametrizacion en tablas por
BpmnElementId (no en extensionElements).

**Hecho**:
- **Assets vendoreados** a `Ecorex.SuperAdmin/wwwroot/lib/bpmnio/` desde el legacy GestionMovil:
  `bpmn-modeler.js` (bpmn-js **v8.8.2** UMD, `window.BpmnJS`), `bpmn.css`, `diagram-js.css` +
  `README.md` con nota de licencia MIT (bpmn.io). Cargados en `Components/App.razor` (link CSS +
  script). Sin CSP en SuperAdmin -> no hubo ajuste de CSP.
- **Interop** `wwwroot/js/ecorex-bpmn.js` (modulo ES on-demand): `init/exportXml/importXml/zoomFit/
  destroy`; callbacks `OnElementSelected` (element.click/selection.changed) y `OnGraphChanged`
  (commandStack.changed). **Palette ACOTADO** (PaletteProvider custom que sobreescribe
  `paletteProvider`): SOLO startEvent/endEvent/task/exclusiveGateway + connect/hand/lasso, con
  iconos SVG data-URI (no depende del webfont `bpmn-icon-*`, ausente en el legacy). Puente
  `window.ecorexBpmnE2E` solo para pruebas.
- **`Flujos.razor` / `FlowEditor.razor`**: se reemplazo SOLO la region del canvas SVG propio por
  `<div id="bpmn-canvas">`; se CONSERVARON el indice (KPIs, busqueda, tarjetas), el header
  (Propiedades/Importar/Exportar/Publicar/Guardar/Cerrar), el panel derecho (6 acordeones + "Saltar
  a otro flujo") y todos los modales. El panel opera sobre el ULTIMO grafo guardado y resuelve la
  seleccion por `BpmnElementId`. Export/Import pasaron de JSON a **XML BPMN**.
- **Guardado** (`IWorkflowDesignService.SaveBpmnAsync`): exportar XML de bpmn-js -> `EnsureDraft` +
  **resync in-place** de nodos/aristas/layout por BpmnElementId (conserva config y vinculos de los
  nodos que sobreviven, agrega nuevos, elimina los que desaparecen). Guarda el XML tal cual
  (portabilidad). Publicadas siguen inmutables (derivan borrador). Se agrego `GetBpmnXmlAsync` y
  `ImportBpmnAsync`. `BpmnXmlWriter` deprecado en el camino de edicion pero CONSERVADO (seeder,
  CreateDraft, EnsureDraft, ImportJson) con nota.
- **ADR-0034** creado (reemplaza la Decision #1 de ADR-0022).

**Tests / gate**: `dotnet build` 0 errores; `dotnet format --verify-no-changes` limpio. Unit
workflow 31/31; integracion workflow (dual PG+SQLServer) **38/38** (incluye 4 nuevos de SaveBpmn:
resync in-place preservando parametrizacion + derivar borrador desde publicada); E2E completo
**29/29** (escenario del editor adaptado a bpmn-js: agregar+conectar via API del modeler,
determinista, luego Guardar/reabrir/verificar por elementRegistry). Motor de ejecucion sin cambios.

**Deudas**: bpmn-js 8.8.2 (linea vigente 17+, deuda de actualizacion); el canvas no conmuta a modo
oscuro (fondo claro fijo); viewer de ejecucion pendiente (otra ola); "Saltar a otro flujo" sigue
visual (call activity pendiente en el motor).

**Siguiente**: viewer de ejecucion sobre bpmn-js; evaluar actualizar bpmn-js a la linea vigente.

**No commit / no push** (segun instruccion de la sesion).

---

## 2026-07-07 - Sesion: Roles de permisos dinamicos con matriz Modulo x Accion (Ola B1)

**Agentes**: agente de feature (roles + matriz de permisos). Referencia de modelo: hermano Visal
(Rol + RolPermiso), adaptado al menu real de ECOREX.

**Pedido**: Ola B1 de roles dinamicos por-tenant con matriz (Modulo x Accion), inspirado en Visal.
SIN enforcement en backend (eso es Ola B2), pero dejando lista la resolucion de permisos efectivos.

**Hecho**:
- **Dominio** (`Ecorex.Domain`): `Rol : TenantEntity` (Name unico/tenant, Description?, IsActive,
  IsSystem) y `RolPermiso : TenantEntity` (RolId FK cascade, ModuleKey=Route del MenuNode,
  CanView/CanCreate/CanEdit/CanDelete, unico (RolId, ModuleKey)). `TenantUser.RolId` (Guid?,
  nullable, FK NO ACTION). DbSets en `IApplicationDbContext`/`EcorexDbContext` + config Fluent.
- **Migracion DUAL `AddRoles`** (PG `20260707191724`, SQL Server `20260707191908`): tablas `roles`,
  `rol_permisos` + columna `tenant_users.rol_id`. Aplicada y **verificada** en Postgres 5442 y SQL
  Server 1443 (tablas + columna + FKs cascade/NO ACTION).
- **Servicio** (`Ecorex.Application/Roles`, resultados tipados `RolResult<T>`): `IRolService`/
  `RolService` con `ListAsync` (UserCount), `GetAsync`, `SaveAsync` (unicidad; IsSystem no se
  renombra), `DeleteAsync` (bloquea IsSystem y rol con usuarios), `SavePermisosAsync` (borra e
  reinserta solo filas con flag, transaccional), `GetModuleCatalogAsync` (DERIVA el catalogo de los
  MenuNode Item Ready de la vista IsDefault; Grupo=Section ancestro; fallback minimo),
  `ResolveEffectivePermissionsAsync` (Owner/Admin -> AllowAll; con rol -> set; sin rol -> vacio),
  `AssignRoleToUserAsync`. Logica pura en `PermissionResolver`/`EffectivePermissions` (Can(mod,acc)).
  Auditado y registrado en DI.
- **Pagina** `Components/Pages/RolesPermisos.razor` (+.css) `/roles-permisos`, policy
  `RolesPermisos.Administrar` (Program.cs, paso 1 tenant_id): cabecera de modulo, panel de roles con
  badges (Sistema/Inactivo/UserCount), editor con la MATRIZ (filas=modulos por Grupo, columnas Ver/
  Crear/Editar/Eliminar) + utilidades marcar fila/columna/grupo, modal crear/editar, modal "Asignar
  usuarios". Columna "Rol de permisos" agregada tambien a `AdmUsuarios.razor`.
- **Menu**: item "Roles y permisos" -> `roles-permisos` (Ready, LegacyCode libre 000198) en la
  seccion "gen"; alta idempotente en el seed y en la reconciliacion (`EnsureMenuItemInSectionAsync`)
  para propagarlo a demos ya sembrados.
- **Seed** `EnsureRolesDemoAsync` (Development, idempotente): rol de sistema "Administrador" (31
  modulos, todo en true) + "Asesor limitado" (Ver general + Crear en tareas/inventario, sin Eliminar)
  asignado a `simple@sky-system.local`. Catalogo real = 31 modulos derivados del menu demo.
- **Tests**: unit `PermissionResolverTests` (Owner/Admin AllowAll; con rol resuelve; sin rol vacio;
  Can(mod,acc); FilterPersistable dedup/whitelist/blank) **8/8**; integracion dual `RolesTests` (PG +
  SQL Server): crear+guardar+releer, resave reemplaza, asignar rol + effective, Owner AllowAll,
  unicidad, cross-tenant, Delete bloquea IsSystem/con-usuarios, Delete ok cascada, catalogo del menu
  **20/20**; E2E `RolesPermisosTests` (owner crea rol -> marca permisos -> guarda -> asigna) **1/1**.

**Decisiones**: ADR-0032 (modelo Rol+RolPermiso; catalogo derivado del menu; `TenantRole` poder
organico vs `Rol` permisos finos; enforcement = Ola B2).

**Siguiente / deuda**: **Ola B2 = enforcement**: hacer cumplir el set por modulo en policies/
endpoints usando `ResolveEffectivePermissionsAsync`, poblar el claim `Permissions` del token
(`AuthService.BuildToken`) y derivar `RolesPermisos.Administrar` (y demas) a Owner/Admin.

**Gate**: `dotnet build Ecorex.sln` 0 errores; `dotnet format --verify-no-changes` limpio; unit
8/8 (+311 suite total) + integracion dual 20/20 + E2E 1/1 verdes. Migracion dual aplicada/verificada.

---

## 2026-07-07 - Sesion: Modulo Administracion de usuarios del tenant (000073)

**Agentes**: agente de feature (modulo de usuarios del tenant). Backend reusado del backbone.

**Pedido**: construir el modulo de usuarios del tenant (legacy 000073, hoy stub) reutilizando
el backend existente: pagina + policy + un par de metodos de servicio + enganche del menu. Sin
roles/permisos dinamicos (otra ola).

**Hecho**:
- **Servicio ampliado** (`ITenantUserService`/`TenantUserService`, tenant-scoped, auditado,
  patron transaccional de `InviteAsync`):
  - `ResetPasswordAsync(tenantUserId, newPassword, actorUserId)`: hashea PBKDF2, actualiza
    `PlatformUser.PasswordHash`, reactiva `Invited -> Active`, valida clave min 6
    (`ArgumentException`), audita SIN la clave en claro.
  - `UpdateProfileAsync(tenantUserId, displayName, actorUserId)`: edita `DisplayName`, audita.
  - `Map` ampliado para poblar `DisplayName` en el DTO. Asignacion de vista via el
    `IMenuConfigService.AssignUserToViewAsync` existente (no se duplica).
- **Pagina** `Components/Pages/AdmUsuarios.razor` (`/admin-usuarios`, policy
  `AdmUsuarios.Editar`, InteractiveServer, tokens ECOREX): tabla Usuario/Email/Rol/Estado/
  Vista de menu/Acciones + modales Nuevo (Invite; vacio->Invited, con clave->Active; vista
  opcional), Editar (DisplayName/Rol/Estado/Vista), Cambiar clave (Reset + confirmar + Generar)
  y toast. Actor desde `ITenantContext.UserId`.
- **Policy** `AdmUsuarios.Editar` en Program.cs (paso 1: `RequireClaim("tenant_id")`; paso 2
  Owner/Admin pendiente).
- **Menu**: seed del item 000073 -> ruta real `admin-usuarios` (Ready) y paso de
  **reconciliacion idempotente** (`ReconcileMenuNodesAsync`, tenant-scoped) que ajusta
  Route/State/Name de los nodos 000073/000194 cuando la vista ya existe (demo ya sembrado
  refleja las paginas reales sin recrear la vista). Verificado en el Postgres dev (5442).
- **Tests**: unit `TenantUserServiceTests` (Application.Tests, EF InMemory: reset hashea +
  activa Invited, valida clave corta/vacia, user no encontrado, update DisplayName, blank ->
  null) **6/6**. Integracion dual `TenantUserAdminTests` (PG + SQL Server: invite con/sin
  clave, changeRole, setStatus, resetPassword, assignMenuView, aislamiento cross-tenant)
  **12/12**. E2E `AdmUsuariosTests` (owner crea usuario Asesor con clave -> aparece; cambia a
  Supervisor -> se refleja) **1/1**.

**Sin migracion**: `TenantUser`/`PlatformUser` ya tenian todos los campos. Sin cambios de esquema.

**Decisiones**: ADR-0031 (reusa el backend; pagina + policy + reconciliacion de menu; roles
dinamicos en la siguiente ola).

**Siguiente / deuda**: roles/permisos dinamicos; invitacion por correo real + self-service de
clave; paso 2 de la policy (Owner/Admin via `tenant_role`); salvaguarda "no dejar el tenant
sin ultimo Owner".

**Gate**: `dotnet build Ecorex.sln` 0 errores; `dotnet format --verify-no-changes` limpio;
unit 6/6 + integracion dual 12/12 + E2E 1/1 verdes.

---

## 2026-07-07 - Sesion: Menu configurable por vista (perfil) - Olas 1 y 2

**Agentes**: agente de feature (menu data-driven + editor). Referencia visual: prototipo
Claude Design "Administrador de Menu" (concepto TRONOX SGDEA) servido en
http://localhost:5234/config-menu-proto.html, adaptado a los TOKENS de ECOREX.

**Pedido**: hacer el sidebar del workspace configurable por perfil de usuario (Ola 1) y
construir la pagina editora que administra las vistas y sus nodos (Ola 2), con fidelidad al
prototipo pero con la identidad ECOREX (no el teal de TRONOX).

**Hecho (Ola 1, commit `bdda279`)**:
- Modelo: `MenuView` (perfil, Name unico por tenant, IsDefault, SortOrder) y `MenuNode`
  (adjacency-list, Kind QuickLink/Section/Subgroup/Item, IconKey, LegacyCode, Route,
  Description, HelpText, State Ready/InDevelopment/Disabled, IsVisible, SortOrder;
  self-ref NO ACTION, FK a la vista en cascada). `TenantUser.MenuViewId` (Guid? NO ACTION).
- `MenuTreeBuilder` (pura), `IMenuConfigService` (GetMenuForTenantUser, ListViews, CreateView,
  CloneView), `NavMenu.razor` data-driven identico al prototipo. Seed Completo(67)/Simple(10)
  + usuarios completo@/simple@sky-system.local.

**Hecho (Ola 2, esta sesion)**:
- **Servicio ampliado** (`MenuConfigService`, tenant-scoped, transaccional, resultados
  tipados): `UpdateViewAsync`, `DeleteViewAsync` (cascade + desasigna usuarios; prohibe borrar
  la IsDefault), `SetDefaultViewAsync`, `GetViewTreeAsync` (arbol completo incl. invisibles);
  nodos `CreateNodeAsync`/`UpdateNodeAsync`/`ToggleNodeVisibilityAsync`/`SetNodeStateAsync`/
  `MoveNodeAsync` (valida ciclos y coherencia de Kind)/`DeleteNodeAsync` (cascade a
  descendientes); `AssignUserToViewAsync`/`ListTenantUsersWithViewAsync`;
  `ExportViewAsync`/`ImportViewAsync` (System.Text.Json portable). Reglas de anidamiento
  extraidas a `MenuNodeKindRules` (pura, testeable sin BD).
- **Iconos compartidos**: diccionario `IconKey->SVG` extraido de NavMenu a
  `Components/Shared/MenuIcons.razor` (fuente unica: sidebar + arbol/selector del editor +
  vista previa). NavMenu ahora consume `MenuIcons.Render`.
- **Pagina** `Components/Pages/ConfiguracionMenu.razor` (`/configuracion-menu`,
  policy `ConfiguracionMenu.Administrar`): index de vistas (tarjetas con badges/contadores y
  Editar/Duplicar/Predeterminada/Eliminar), editor (KPIs, tabs Estructura/Vista previa, arbol
  con acciones por fila, toolbar buscar/expandir/contraer/+Seccion, panel de propiedades con
  selector de iconos grid, Exportar/Restablecer/Guardar) y modal de asignacion de usuarios.
  CSS scoped 100% con tokens ECOREX (--surface, --ink, --line, --brand, --ok/warn/danger,
  --t-*, --rad, --sh-*), conmuta claro/oscuro por html.dark. JS helper `js/menu-config.js`
  para descargar el JSON exportado.
- **Policy** `ConfiguracionMenu.Administrar` en Program.cs (paso 1: RequireClaim tenant_id;
  comentario del paso 2: restringir a Owner/Admin).
- **Seed**: item "Administrador de Menu" en "Sistema . General" reutilizando el code **000194**
  (antes "Roles y permisos", stub) apuntandolo a `/configuracion-menu` (rename, no alta; los
  conteos del seed no cambian).
- **Tests**: unit (Application.Tests `MenuConfigRulesTests`: reglas de Kind + round-trip JSON
  export/import); integracion DUAL (`MenuConfigEditorTests`: CRUD nodos, move-reorder,
  cascade delete, SetDefault, no-borrar-default, no-ciclo, export->import, assign refleja en
  GetMenuForTenantUser, aislamiento) PG+SQL; E2E (`MenuEditorTests`: owner crea vista, agrega
  seccion+item, guarda, asigna a usuario). MenuProfileTests de Ola 1 intactos.
- **ADR** `docs/decisiones/0030-menu-configurable.md` (cubre ambas olas).

**Migracion**: NINGUNA. El modelo de Ola 1 cubrio toda la Ola 2.

**Deudas**: (1) drag-and-drop real (hoy botones subir/bajar = MoveNode reorder); (2) paso 2 de
la policy (restringir a Owner/Admin via tenant_role); (3) "Guardar" del editor es
confirmacion/recarga porque cada accion persiste al vuelo.

**Siguiente**: conectar el paso 2 de las policies con el Module Registry (000109) + rol del
TenantUser; drag-and-drop del arbol.

---

## 2026-07-05 - Sesion: Primer DEPLOY a produccion Linux (10.0.0.3) + fix de bootstrap

**Agentes**: coordinador en rol de deploy (no feature). Referencia: patron de deploy de
Visal (C:\DesarrolloIA\Visal\deploy\docker-prod), modo build-from-git.

**Pedido**: desplegar ECOREX en un Linux que ya corre otro proyecto (Visal), sin chocar,
tras sondear el server "sin hacer nada" primero.

**Hecho**:
- Directorio nuevo `deploy/docker-prod/` (build-from-git): `docker-compose.from-git.yml`
  (el server clona el repo publico rama fase-0/clon-backbone y construye
  apps/backend/Dockerfile.superadmin; ecorex-app + Postgres AISLADOS: proyecto
  ecorex-prod, contenedores ecorex-app/ecorex-postgres-prod, red ecorex-net, volumen
  PERSISTENTE ecorex-pgdata). `.env.example`, `README-linux.md`, `backup.sh`, `.gitignore`
  y `caddy/` (overlay TLS opcional). Todo ASCII, `docker compose config` valido.
- Sondeo read-only de 10.0.0.3 (ssh con llave existente id_ed25519_visal): Ubuntu, Docker
  29.5, 120 GB libres, 5.5 GB RAM sin swap. HALLAZGO: NO hay Caddy ni proxy en 80/443
  (contrario al supuesto); cada app va en puerto plano (visal 5380, bookstack 6875...).
  80/443 y 5480 libres. Decision del usuario: exponer ECOREX en 0.0.0.0:5480 HTTP plano.
- Deploy real: build en el server (imagen ecorex-superadmin:local), up -d, migraciones
  aplicadas, /login 200. Volumen persistente creado.
- FIX DE BOOTSTRAP (bloqueante, cazado en la validacion): en Production nunca se creaba el
  Super Admin (SeedAsync solo corre en Development; el camino de prod solo aseguraba tenant
  interno y *actualizaba* la clave, con `if superAdmin is null return`). Nuevo
  `DatabaseSeeder.EnsureSuperAdminAsync(password)` (crea admin@ecorex.local si falta,
  idempotente, clave de ECOREX_SEED_ADMIN_PASSWORD) llamado en el arranque de Production
  antes de asegurar tenant/clave. Redeploy verificado: admin creado + tenant interno
  "Plataforma ECOREX". Login accesible en http://10.0.0.3:5480/login.

**Estado de datos**: produccion arranca LIMPIA (env Production, sin seeder demo): solo
admin@ecorex.local + tenant interno. NO comparte datos con desarrollo (que si corre
SeedAsync + Ensure*DemoAsync con SKY SYSTEM y demas). Mismo esquema (100 tablas), datos
distintos.

**Siguiente**: seguridad de 5480 (hoy HTTP plano publico): firewall a red/VPN o activar el
Caddy incluido (80/443 libres) para TLS con dominio. Comando de update y backup.sh en
README-linux.md.

**Bloqueos**: ninguno (el de bootstrap quedo resuelto).

**Decisiones**: build-from-git (repo publico, sin registry); stack aislado con volumen
persistente propio; exponer en puerto plano 5480 (no habia proxy en el box); el fix de
bootstrap se hizo en codigo (no atajo por SQL) para que todo deploy limpio futuro sea
self-service. Commits a01a3b9 (deploy dir), 7b9aa11 (fix bootstrap) en fase-0/clon-backbone.

---

## 2026-07-05 - Sesion: Infraestructura IA (menu propio) + desacople del cierre (ADR-0028)

**Agentes**: agente #3 de 3 integraciones CUBOT encadenadas. Tarea de REORGANIZACION +
DESACOPLE (no feature nueva): quirurgica, sin romper comportamiento existente.

**Pedido**: (A) extraer la infraestructura de IA del grupo "CRM (heredado)" a un grupo propio
"Infraestructura IA"; (B) desacoplar el toolset de cierre del agente del dominio CRM/Lead con una
costura (interfaz + NoOp default + adaptador CRM), sin romper la creacion actual de leads.

**Hecho**:
- Menu (`NavMenu.razor`): nuevo grupo "Infraestructura IA" (data-acc `ia`, 5 items) con Agentes
  (`/agentes`, 000867, venia de Automatizacion), Lineas WhatsApp (`/lineas`), Conversaciones
  (`/conversaciones`), Bitacora del agente (`/bitacora-agente`), Plantillas WhatsApp
  (`/plantillas-whatsapp`) — las 4 ultimas venian de "CRM (heredado)". Rutas y codigos de modulo
  INTACTOS (solo movimiento de menu). "Automatizacion" 4->3 (se quita `agentes`); "CRM (heredado)"
  7->3 (queda Asesores, Automatizaciones, Lista negra; el grupo NO se elimina). Mapa `GroupRoutes`
  y contadores actualizados en consecuencia.
- Costura de cierre (`Ecorex.Application/Tenancy`): `IAgentLeadSink` con
  `CreateLeadAsync(AgentLeadRequest, actor, ct)` y DTO `AgentLeadRequest`/`AgentLeadResult` en el
  namespace IA (no referencian Lead). `NoOpAgentLeadSink` (default, no crea nada, no lanza) y
  `PipelineLeadSink` (adaptador CRM vivo, unico punto de acoplamiento con Lead/BusinessUnit).
  `PipelineToolset.crear_lead` ahora delega en la interfaz; mismo contrato del tool. DI: NoOp
  registrado como default, `PipelineLeadSink` como implementacion viva (ultimo gana).
- SIN migracion: cambio schema-free (reorg de menu + costura de interfaz). DAL dual intacto.
- Tests: unit nuevos `AgentLeadSinkTests` (3, verdes): NoOp no crea lead / adaptador crea lead y
  mapea unidad b2b / sin nombre = error tipado. Application.Tests total 279 verde. Integracion
  dual `PipelineLeadTests`+`FollowUpTaskTests`+`DashboardTests` (5) verde en PG 5442 + SQL 1443.

**Siguiente**: (opcional) exponer el `NoOpAgentLeadSink` como perfil de despliegue sin CRM.

---

## 2026-07-05 - Sesion: Modulo de Plantillas HSM de WhatsApp (ADR-0029)

**Agentes**: agente #2 de 3 integraciones CUBOT encadenadas. Referencia origen:
CUBOT.travels (`WhatsAppTemplate`, `PlantillasWhatsApp.razor`, migracion 20260628124032).
Convenciones copiadas del modulo de Inventarios (ADR-0027).

**Pedido**: portar el gestor de plantillas HSM de WhatsApp de CUBOT.travels a ECOREX como
modulo NUEVO, adaptado a las convenciones (multi-tenant, DAL dual, resultados tipados), con
Submit/SyncStatus como STUBS (sin integracion real con Meta).

**Hecho**:
- Dominio (Ecorex.Domain): `WhatsAppTemplate` (TenantEntity) + enums
  `WhatsAppTemplateCategory/HeaderType/Status`. FK `WhatsAppLineId` NO ACTION a `WhatsAppLine`
  (linea del CRM heredado). Unica por (TenantId, Name, Language). `VariablesJson` jsonb/nvarchar
  dual; `BodyText` text/nvarchar(max) dual; `IsActive` (soft-delete).
- DbContext: DbSet + config inline (3 conversiones enum->string, indice unico (Name,Language),
  FK Restrict). IApplicationDbContext expone el DbSet.
- Migracion dual `AddWhatsAppTemplates` (PG 20260705120605 + SQL Server 20260705120649)
  generada, APLICADA y VERIFICADA en los contenedores dev (tabla `whats_app_templates` existe en
  PG 5442 y SQL Server 1443).
- Servicios (Ecorex.Application/Tenancy, `WhatsAppTemplateResult<T>` con NotImplemented):
  `IWhatsAppTemplateService` (CRUD + SetActive + Submit STUB + SyncStatus no-op). Logica pura en
  `WhatsAppTemplateCalculations` (NormalizeName, ExtractTokens, ValidateSave, CanEdit/CanSubmit).
  Auditoria via IAuditWriter. Registrado en DI.
- UI (Ecorex.SuperAdmin): `/plantillas-whatsapp` (tabla + badges de estado + modal crear/editar
  + accion Someter + banner "envio al proveedor no implementado"). NavMenu: item en grupo "CRM
  (heredado)" junto a Lineas WhatsApp (conteo 6->7). Policy `PlantillasWhatsApp.Editar` (paso 1).
- Seeder: `EnsureWhatsAppTemplatesDemoAsync` (linea Cloud demo si falta + 3 plantillas SKY SYSTEM
  en Draft/Submitted/Approved). Idempotente, llamado desde Program.cs.
- Tests: unit `WhatsAppTemplateCalculationsTests`, integracion dual `WhatsAppTemplatesTests`
  (round-trip, unicidad (Name,Language), aislamiento cross-tenant, transicion Submit), E2E
  `WhatsAppTemplatesTests` (crear plantilla y verla en la tabla).

**Deudas**: (1) integracion real con la WhatsApp Cloud API de Meta (Submit/SyncStatus son
stubs, no hay llamada HTTP); (2) policy en "paso 1" (Module Registry pendiente); (3) headers de
imagen/documento/video modelados pero no soportados en el editor.

**Siguiente**: agente #3 de las integraciones CUBOT.

---

## 2026-07-05 - Sesion: Modulo de Inventarios con catalogos normalizados (ADR-0027)

**Agentes**: coordinador (Opus) + 4 subagentes de exploracion (DbContext/DAL dual, servicios/
resultados tipados, UI/NavMenu/policies/seeder, suites de test). Referencia origen:
CUBOT.nails Product/Sede (NO se porto belleza).

**Pedido**: portar el MODELO DE ITEMS del backbone a ECOREX con CATALOGOS NORMALIZADOS para el
grupo "Sistema - Inventarios" (Bodegas 000556, Marcas 000502, Grupo 000506, Subgrupos 000606,
Tipos 000498, Items 000066): modelo + migracion dual, servicios con resultados tipados, UI,
policy, seeder y validacion (unit + integracion dual + E2E), arranque real y ADR.

**Hecho**:
- Dominio (Ecorex.Domain): `Warehouse/Brand/ItemGroup/ItemSubgroup/ItemType` (catalogos,
  interfaz comun `ICatalogEntity`), `Item` (+FieldValuesJson jsonb dual, Specifications text
  dual, Price 14,2, FKs de catalogo NO ACTION, SKU unico filtrado por tenant), `ItemImage`
  (cascade, Url 500), `ItemStock` (cascade al item, NO ACTION a bodega, unico ItemId+WarehouseId).
- DbContext: 8 DbSets + configuracion inline (indices unicos por (TenantId,Name)/(ItemId,
  WarehouseId), filtro por bodega). HasQueryFilter multi-tenant automatico por reflexion (ya
  existente) cubre las 8 entidades. IApplicationDbContext expone los 8 DbSets.
- Migracion dual `AddInventory` (PG 20260705110130 + SQL Server 20260705110220) generada,
  aplicada y VERIFICADA en los contenedores dev (PG 5442 y SQL Server 1443, 8 tablas cada uno).
  jsonb/text -> nvarchar(max) en SQL Server; FKs Restrict = NO ACTION en ambos motores.
- Servicios (Ecorex.Application/Inventory, `InventoryResult<T>`): `IInventoryCatalogService`
  (CRUD bodegas + catalogos genericos por `CatalogKind`, guards de archivado, subgrupo valida
  grupo) e `IItemService` (SKU unico, consecutivo "ITM" via ISequenceService, stock por bodega
  recreado en transaccion, imagenes por URL, list con filtros+paginado, detail con TotalStock/
  AvailableAt). Calculos puros en `InventoryCalculations`. Registrados en DI.
- UI (Ecorex.SuperAdmin): `/inventario-items` (grid + filtros + modal + activar/archivar),
  `CatalogManager.razor` generico + 4 paginas thin (marcas/grupos/subgrupos/tipos) +
  `/inventario-bodegas` aparte. NavMenu: los 6 items apuntan a rutas reales, 000066 movido de
  "Oferta - Catalogo" (grupo retirado) a "Sistema - Inventarios"; retiradas del stub
  `Modulo.razor`. Policy `Inventario.Ver` en Program.cs.
- Seeder: `EnsureInventoryDemoAsync` (2 bodegas, 3 marcas, 2 grupos x2 subgrupos, 3 tipos, 8
  items con stock repartido incl. ceros, imagenes placeholder; avanza el consecutivo ITM),
  llamado desde Program.cs. Idempotente, solo Development.

**Verificacion real** (app arrancada contra PG 5442 en puerto 5260):
- Build 0 errores / 0 advertencias en Ecorex.sln. `dotnet format --verify-no-changes` limpio.
- ASCII-only: 0 bytes no-ASCII en todos los archivos nuevos.
- Unit (Ecorex.Application.Tests): 9/9 verdes (total stock, disponibles, IsAvailableAt,
  validacion de nombre).
- Integracion dual (Ecorex.Integration.Tests): 12/12 verdes (6 casos x PG + SQL Server):
  round-trip item con stock por bodega, SKU unico por tenant, consecutivo ITM, subgrupo valida
  grupo, guards de archivado (grupo con subgrupos / bodega con stock), aislamiento cross-tenant
  items+catalogos (mismo SKU en 2 tenants no colisiona).
- E2E (Ecorex.E2E.Tests): 1/1 verde (crear bodega + marca + item con stock y verlo en el grid
  filtrando por bodega).
- Navegador (PG 5442, 5260): /inventario-items CLARO (grid con miniaturas, SKUs ITM..., stock
  total y chips por bodega, filtros) y /inventario-bodegas OSCURO (badges "Activa", tokens
  conmutan) + modal "Nuevo item" en oscuro OK. Procesos detenidos al final.

**Siguiente**: paso 2 de las policies (derivar `Inventario.Ver` del Module Registry), como el
resto de modulos. Subida real de imagenes (hoy por URL). Movimientos/kardex de stock si el
requerimiento lo pide.

**Bloqueos**: ninguno.

**Decisiones**: ADR-0027 (docs/decisiones/0027-inventario.md). FKs de catalogo NO ACTION para
evitar rutas multiples de cascada en SQL Server. Catalogos simples via `ICatalogEntity` +
`CatalogManager` generico para no duplicar CRUD. El seeder avanza el consecutivo ITM para que
los SKUs generados desde la UI no colisionen con los demo.

---

## 2026-07-05 - Sesion: Login "ventana al producto" (mockup del tablero kanban en el aside)

**Agentes**: coordinador (Opus). Lectura de AuthShell.razor, ActivityBoardDetail.razor + AbUi.cs
(paleta 1:1 del prototipo work), .auth-*/.ab-* de app.css y las 4 paginas de auth.

**Pedido**: un login MAS ACORDE AL TABLERO. El aside de marca (fondo gris + 3 bullets) debia
mostrar una VENTANA AL PRODUCTO: un mockup estatico y elegante del TABLERO KANBAN del workspace,
renderizado con los tokens exactos, para que el login "sepa" a lo que se entra.

**Decision de diseno**:
- AuthShell.razor gana un parametro `ShowBoardMock` (default false). El aside tiene DOS modos:
  * ShowBoardMock=true -> SOLO Login: composite "ventana al producto" (identidad arriba +
    eyebrow "Tu tablero de trabajo" + linea de valor "Tareas, flujos, formularios y reglas -
    configurables sin codigo" + tarjeta ventana con topbar falsa [3 dots + "Comercial -
    Requerimiento Infraestructura" + badge "En progreso"] + 4 columnas kanban [Por hacer /
    En progreso / En revision / Completado] con 1-2 tarjetas: titulo corto, barra "Progreso
    N/M" con color POR COLUMNA [t-blue/danger/t-amber/ok EXACTOS de AbUi.ColumnProgress],
    avatares solapados con AVPAL del prototipo, chip de fecha). La ventana asoma RECORTADA:
    overflow hidden, rotate(-1.1deg) + translateX, fade en borde inferior/derecho (sh-lg) para
    profundidad estilo hero SaaS. Debajo, 3 mini bullets con iconos.
  * ShowBoardMock=false -> Recuperar/Restablecer/Activar: se dejan como estaban (aside sobrio,
    headline + subtext, ya traian ShowBullets=false). El mockup distrae en flujos utilitarios,
    asi que NO aparece ahi. Documentado en el comentario de cabecera de AuthShell.razor.
- Login.razor pasa `ShowBoardMock="true"`. La tarjeta del formulario NO cambia (ids
  #login-email/#login-password, script mostrar/ocultar, submit .auth-submit, links intactos).
- Cero morados saturados: aside en --surface-2, columnas --surface-3, tarjetas --surface,
  acentos --brand-soft/--t-amber-bg. Todo con tokens -> conmuta solo con html.dark.
- RESPONSIVE: el breakpoint del aside se subio de 768px a 900px (el mockup pide ancho); a
  <=900px el aside se OCULTA y queda la tarjeta centrada con la marca arriba (sin cambios de
  comportamiento del login movil).

**Hecho**:
- Editado `Components/Shared/AuthShell.razor`: parametro ShowBoardMock + rama del composite del
  mockup (HTML/CSS estatico, aria-hidden, sin datos reales ni backend) manteniendo la rama
  sobria (headline/subtext/bullets) para el resto.
- Editado `Components/Pages/Login.razor`: ShowBoardMock="true".
- Editado `wwwroot/app.css`: bloque `.auth-mock-*` (window/topbar/board/col/card/bar/avs/due/
  points) con la paleta exacta; media query 768px -> 900px.
- Anadida config `superadmin-5256` a `.claude/launch.json` (PG 5442, puerto 5256) para verificar.

**Verificacion real** (app arrancada contra PG 5442 en puerto 5256):
- build 0 errores / 0 advertencias; `dotnet format --verify-no-changes` limpio en los .razor.
- ASCII-only: 0 bytes no-ASCII en los 3 archivos tocados (los 5 preexistentes de app.css estan
  fuera del alcance auth y no se tocaron).
- /login CLARO: aside con mockup legible, 4 columnas, barras blue/rose/amber/green verificadas
  por computed style (rgb 37,99,235 / 225,29,72 / 199,122,6 / 22,163,74 = tokens exactos).
- /login OSCURO (localStorage['ecorex-theme']='dark'): conmuta por html.dark; aside surface-2
  #1C1C1F, tarjetas surface #161618; barras vivas; formulario oscuro OK.
- /login MOVIL 380px: aside OCULTO, marca centrada arriba, formulario intacto y centrado.
- /recuperar: aside sobrio sin mockup (confirmada la decision).
- E2E COMPLETA: 19/19 verde, 0 fallos, 0 omitidos (ECOREX_E2E_BASEURL=http://localhost:5256).
  El test de login pasa -> selectores del formulario intactos, aterriza en /inicio.
- Capturas en scratchpad: login-light-desktop.png, login-dark-desktop.png, login-mobile-380.png,
  recuperar-light-desktop.png. Procesos detenidos (preview stop + puerto 5256 libre).

**Siguiente**: (opcional) exponer el titular/copy del mockup como campos de branding editables.

**Bloqueos**: ninguno.

---

## 2026-07-05 - Sesion: Modulo ADMINISTRACION DE EMPRESAS / ficha de tenant (000072, ADR-0026)

**Agentes**: coordinador + 1 subagente explorador de la solucion (mapa de Tenant/servicios/
NavMenu/policies/seeder). Lectura de las 3 fuentes (proto_adm_empresas.html, spec Capa 6 de
origen, spec Capa 1 con los 9 errores).

**Decision de area**: la ficha 000072 es GOBIERNO multi-tenant -> AREA PlatformAdmin (junto
a /tenants y /plans), policy nueva `AdmEmpresas.Ver` = RequireClaim("platform_role"). El item
000072 del NavMenu se MOVIO del menu del tenant (grupo "Sistema - General", contador 8->7,
policy TenantMember erronea) al bloque SUPER ADMIN SAAS como "Ficha de empresa 000072".

**Hecho**:
- Pagina real `/admin/empresas` (AdmEmpresas.razor + .razor.css) que REEMPLAZA el stub 000072.
  Estructura del proto proto_adm_empresas.html con TOKENS del workspace (ADR-0023): topbar
  14x24 + MOD 000072, layout grid 300px/1fr max 1440, sidebar sticky selector de empresa con
  dot de estado, header-card r10 avatar gradiente + plan-badge + estado, KPIs 5 cols (usuarios
  y estado REALES; modulos/actividades/reglas con tag "Pendiente"), secciones colapsables
  (details/summary nativo, chevron) numeradas 01/08 REALES + 02-10/C1 PLACEHOLDER.
- Campos REALES editables mapeados a `Tenant`: razon social (LegalName), nombre comercial
  (Name), NIT (TaxId), pais, ciudad, direccion, telefono, email de contacto, estado (via
  ChangeStatusAsync, maquina de estados existente, auditado). Usuarios del tenant (TenantUser)
  en tabla SOLO LECTURA (email/rol/estado).
- Backend aditivo SIN duplicar CRUD: se REUTILIZA ITenantAdminService. UpdateProfileAsync
  extendido con City/Address/Phone/Email; nuevo ListUsersAsync(tenantId) cross-tenant ACOTADO
  (IgnoreQueryFilters + Where TenantId, unico cross-tenant, solo operador por policy). DTOs
  TenantDetail/UpdateTenantProfileRequest extendidos + TenantUserListItem nuevo.
- Modelo: 4 columnas nuevas en `Tenant` (City, Address, Phone, Email, nullable). UNA migracion
  dual `AddTenantProfile` (Ecorex.Infrastructure 20260705044204 EcorexDbContext + Ecorex.
  Infrastructure.SqlServer 20260705044246 SqlServerEcorexDbContext), puramente aditiva (4
  AddColumn nullable, sin drops), APLICADA y verificada en PG 5442 (\d tenants) y MSSQL 1443
  (sys.columns). Config EF en el OnModelCreating compartido (SqlServer hereda EcorexDbContext).
- Seeder: campos de contacto en el tenant demo SKY SYSTEM (bases nuevas) + EnsureTenantProfile
  DemoAsync idempotente que rellena City/Address/Phone/Email si estan vacios (bases previas a
  la migracion). Encadenado en Program.cs tras EnsureDemoTemplateAssetsAsync.
- 9 secciones PLACEHOLDER visibles-deshabilitadas con tooltip/explicacion "Pendiente": modulos,
  actividades, cargar datos, copiar formularios, datos externos, reglas, configuraciones,
  integraciones, contador/revisor fiscal. Los flujos SQL peligrosos del legacy (copiar tablas
  via sys.tables+blacklist, copiar formularios con 5+ INSERT y db3dev, datos externos con
  cadena arbitraria) NO se reconstruyen: son parte de los 9 errores (ver ADR-0026).

**Validacion (probado de verdad)**:
- Build Ecorex.sln 0 errores; `dotnet format --verify-no-changes` limpio; archivos nuevos ASCII.
- Unit: Domain 35/35, Application 247/247 verdes (sin regresiones).
- Integracion dual +6 (3 tests x 2 motores PG+SQL Server via Testcontainers) TenantProfileTests
  verdes: UpdateProfile persiste City/Address/Phone/Email y vuelven en el detalle + Normalize
  vacia a null; ChangeStatus via maquina de estados reflejado en la ficha; ListUsers ACOTADO
  al tenant sin fuga entre empresas (A ve solo lo suyo, B lo suyo, orden por email). Test de
  aislamiento cross-tenant existente 6/6 sigue verde tras el cambio de modelo.
- E2E Playwright COMPLETA verde 19/19 (era 18, +1 AdmEmpresasTests): login operador de
  plataforma -> /admin/empresas (MOD 000072) -> seleccionar SKY SYSTEM -> usuarios reales +
  seccion "Cargar datos" Pendiente -> editar telefono -> guardar (flash ok) -> recargar ->
  telefono persistido.
- Verificacion manual claro/oscuro (preview 5253, login admin@ecorex.local): ficha SKY SYSTEM
  con plan "Plan Empresa", estado Activa, 4 usuarios reales, campos de contacto del seeder
  (Bogota / +57 601 234 5678 / contacto@sky-system.local), 9 secciones "Pendiente" con tag
  ambar; edicion de telefono guardada (persistida en BD: tenants.phone; auditoria escrita:
  super_admin_audit_logs action_name=tenant.profile.update). En dark los tokens conmutan
  (bg #0A0A0B, surface #161618, ink/brand invertidos) por construccion (solo tokens workspace).
  NavMenu muestra "Ficha de empresa 000072" en SUPER ADMIN SAAS; grupo tenant "Sistema-General"
  paso a 7 items.
- Procesos DETENIDOS (preview 5253 parado; proceso residual 5234 de sesion previa terminado;
  fixture E2E mata su app). Sin listeners en 525x/5234.

**Deudas / TODO** (documentadas en ADR-0026):
- Cada una de las 9 secciones placeholder necesita su ola: asignacion de modulos/actividades/
  parametros/integraciones POR EMPRESA (con servicios transaccionales), contador/revisor como
  entidad owned del Tenant, y plantillas versionadas transaccionales para reemplazar la copia
  de datos/formularios del legacy (nunca SQL crudo + blacklist + db3dev).
- Policy `AdmEmpresas.Ver` en paso 1 (solo platform_role); paso 2 = MFA para acciones criticas
  + derivar del rol real, como el resto de policies del proyecto.
- Sin commit (pedido explicito): cambios en working tree.

**Decisiones**: ver ADR-0026 (area PlatformAdmin, mapeo a Tenant real, gaps como placeholders,
por que NO se reconstruyen los flujos SQL peligrosos, relacion con /tenants existente).

---

## 2026-07-05 - Sesion: Modulo EXTRACCION DE DATOS / web scraping (000730, ADR-0025)

**Agentes**: agente unico (lectura proto+spec, modelo+DAL dual, servicio+guard SSRF,
UI, seeder+endpoint demo, tests unit/integracion/E2E, verificacion manual).

**Hecho**:
- Pagina real `/extraccion-datos` (ExtraccionDatos.razor + .css) que REEMPLAZA al stub
  generico. Estructura del proto `proto_web_scraping.html` con tokens del workspace
  (regla ADR-0023): topbar 14x24 + MOD 000730, layout 300px/1fr max 1500, sidebar
  sticky de fuentes con dot verde/rojo/gris, hero r12 gradiente --brand-2->--brand con
  4 KPIs (ejecuciones/exito 30d/registros/ultima corrida), franja ambar de alcance,
  cols 1fr/380 (preview tabla + JSON crudo en editor oscuro fijo | editor de fuente con
  selector CSS + ayuda), tabla de historial con pills. NavMenu: el item 000730 pasa de
  `modulo/extraccion-de-datos` a `extraccion-datos` (SOLO ese item); Modulo.razor retira
  su entrada del registro de stubs.
- Modelo + DAL dual: ScrapeSource (TenantEntity: Name, Url, Selector?, Kind
  enum Html|Json, Status Active|Inactive|Error, LastRunAt?, LastResultSummary?; indice
  unico tenant+name) y ScrapeRun (TenantEntity: SourceId FK cascade, Status
  Success|Failed, ItemCount, DurationMs, ErrorMessage?, ResultJson dual jsonb/nvarchar(max)
  recortado a 64 KB). DbSet + IApplicationDbContext + configuracion. UNA migracion dual
  `AddScraping` (Ecorex.Infrastructure 20260705033315 + Ecorex.Infrastructure.SqlServer
  20260705033251) APLICADA y verificada en los contenedores dev (PG 5442 \d scrape_runs
  con result_json jsonb + MSSQL 1443 sys.tables con result_json nvarchar(max)).
- IScrapeService (Application/Scraping): CRUD de fuentes con validaciones tipadas
  (ScrapeOpResult: Ok/NotFound/Invalid; nombre unico por tenant, URL absoluta http(s)
  sin credenciales, selector obligatorio en Html) + RunAsync que SIEMPRE persiste la
  corrida (exito o fallo) y actualiza LastRunAt/summary/estado de la fuente en UNA
  transaccion. Eliminar con historial -> Invalid (se ofrece desactivar, criterio ADR-0023).
- SEGURIDAD (nucleo del ADR): SsrfUrlGuard (puro, testeado) resuelve DNS y valida TODAS
  las IPs (fail-closed) contra loopback/privadas RFC1918/link-local+metadata 169.254.169.254/
  CGNAT/multicast/clase E/IPv6 ULA+link-local+mapeadas; solo http(s), sin user@host, solo
  puertos 80/443. ScrapeHttpFetcher: SOLO GET, User-Agent propio, timeout 15s total, tope
  2 MB (stream + Content-Length), AllowAutoRedirect=false y max 3 redirecciones seguidas
  A MANO re-validando cada salto. Excepcion AllowLoopback SOLO en Development (Program.cs
  re-registra el singleton) para el endpoint demo propio.
- Parser puro ScrapeContentParser: JSON (conteo + preview tabular) y HTML por selector CSS
  con AngleSharp. Se agrego el paquete **AngleSharp 1.5.1 estable** a Ecorex.Application
  (no estaba referenciado; justificado en ADR-0025: parser puro sin red/telemetria, el
  selector CSS es central en la spec).
- Endpoint demo `/api/demo/scrape-sample` en el SuperAdmin (JSON estatico de 8 items,
  AllowAnonymous) + seeder EnsureScrapingDemoAsync idempotente (fuente Json demo apuntando
  a ese endpoint; re-apunta la URL al puerto vivo). Policy nueva `ExtraccionDatos.Editar`
  (paso 1: nombre estable, requisito = tenant_id).

**Validacion**:
- Build Ecorex.sln 0 errores; `dotnet format --verify-no-changes` limpio.
- Unit: Application 247/247 verdes (+78 nuevos: SsrfUrlGuardTests exhaustivo -esquemas,
  IPv4/IPv6 privadas y mapeadas, DNS que resuelve a privada, mezcla publica+privada,
  puertos, excepcion loopback dev, bordes de rango-; ScrapeHttpFetcherTests -GET, UA,
  redireccion a privado bloqueada sin request, redireccion a publico seguida, tope de
  saltos, tope de bytes/Content-Length, HTTP 5xx tipado-; ScrapeContentParserTests -JSON
  array/propiedad/objeto/escalares/invalido/preview acotada, HTML selector valido/
  compuesto/invalido/sin selector/sin match, recorte de ResultJson sin perder el total-).
- Integracion dual +6 (3 tests x 2 motores PG+SQL Server via Testcontainers) ScrapingTests
  verdes: CRUD con validaciones + corrida real contra endpoint HttpListener local (8 items,
  ResultJson jsonb/nvarchar valido, metricas de la fuente, no-borrado con historial);
  historial que persiste FALLO (HTTP 500 -> fuente Error) y EXITO (vuelve a Active),
  ambas corridas conservadas; aislamiento cross-tenant (B no ve fuentes ni corridas de A,
  RunAsync/DeleteAsync de A desde B = NotFound, mismo nombre reutilizable por tenant).
- E2E Playwright COMPLETA verde 18/18 contra app real (PG 5442, puerto 5250 auto);
  +1 escenario ExtraccionDatosTests: crear fuente demo JSON al endpoint propio -> Ejecutar
  -> preview con 8 items (columnas sku/nombre/precio/stock) -> KPI registros 8 -> historial
  con pill dot verde "Exitoso" y 8 registros. (Una 1a corrida dio el flake conocido de
  ReglasTests -race de Blazor al crear regla, documentado en sesiones previas-; el rerun
  limpio fue 18/18.)
- Verificacion manual claro/oscuro (preview 5253): layout desktop grid 300px/1fr y cols
  1fr/380 (en viewport <1100 conmuta a 1 columna, responsive del proto), hero gradiente,
  franja ambar, tabla de preview 8 filas con SKU/nombre/precio/stock, editor JSON oscuro;
  en dark los tokens conmutan (bg #0A0A0B, ink #F4F4F5, brand invertido, amber/verde
  translucidos rgba). Corrida real persistida verificada en BD (scrape_runs Success 8 items
  result_json jsonb valido). El seeder re-apunto la fuente demo a 5253. NavMenu muestra el
  unico item "Extraccion de datos 000730" a la pagina real.
- Procesos DETENIDOS (preview 5253 parado; sin listeners en 525x/5232).

**Deudas / TODO** (documentadas en ADR-0025):
- Scheduler (CICLO del legacy): sin corridas programadas en esta ola; ira como
  BackgroundService en Ecorex.Workers con cola + rate-limit por tenant.
- DNS rebinding: la IP validada no se fija para la conexion posterior (TTL 0 podria
  re-resolver distinto entre validacion y GET); mitigacion via ConnectCallback = deuda.
- Multi-paso legacy (variables/credenciales cifradas, APIs, seguimiento trading),
  robots.txt + rate-limit por dominio, extraccion de atributos (href/src) ademas de texto.
- Sin commit (pedido explicito): cambios en working tree.

**Decisiones**: ver ADR-0025 (alcance acotado, guard SSRF estricto, AngleSharp 1.5.1,
excepcion loopback dev, TODO scheduler).

---

## 2026-07-03 - Sesion 1: Lectura del vault + FASE 0 (setup del repo)

**Agentes**: coordinador + 5 subagentes lectores del vault en paralelo.

**Hecho**:
- Lectura obligatoria completa del vault OBSIDIAN.tareas (5 lectores en paralelo:
  vision/prototipo, hoja de ruta/ADRs, multi-tenant/9 errores, DAL dual/MotherData,
  testing/ETL/db3dev). Entregado resumen de entendimiento: 15 puntos de arquitectura,
  aislamiento multi-tenant, DAL dual, 9 errores + fix, orden de construccion.
- Entorno verificado: .NET SDK 10.0.301 y 9.0.315, Docker 29.3.0, backbone CUBOT.nails
  con upstream cubotcrm.
- FASE 0 ejecutada:
  - Clon del backbone CUBOT.nails -> C:\DesarrolloIA\ECOREX.tareas (git init + fetch,
    preservando .claude/ existente). Base: rama main (= deploy, mismo commit bb00f69).
  - Remotes: origin=https://github.com/alexandercuartas665/EcorexV.git,
    upstream=https://github.com/alexandercuartas665/cubotcrm.git.
  - Renombrado estructural: 12 proyectos + sln CubotNails.* -> Ecorex.*,
    CubotSidebar.tsx -> EcorexSidebar.tsx, start/stop-cubot.ps1 -> start/stop-ecorex.ps1.
  - Reemplazo de contenido en 568 archivos (CubotNails->Ecorex, cubot->ecorex,
    CUBOT->ECOREX; `cubotcrm` preservado como nombre del upstream). Brand .nails -> .tareas.
  - `dotnet build Ecorex.sln`: verde (0 errores) tras el renombrado.
  - Docker dedicado: docker-compose reescrito con project name `ecorex-tareas`,
    prefijo `ecorex-tareas-` en contenedores/volumenes/red, puertos Postgres 5442,
    SQL Server 1443 (servicio NUEVO, mssql 2022), Redis 6389, RabbitMQ 5682/15682,
    Adminer 8092 (reemplaza pgAdmin). `.env.example` actualizado.
  - `preflight.ps1` + `preflight.sh` nuevos (docker vivo, puertos libres, contenedores
    previos, recursos minimos); integrado en start-ecorex.ps1.
  - CLAUDE.md reescrito apuntando al vault OBSIDIAN.tareas como fuente de verdad,
    con reglas inviolables, puertos dedicados y orden de fases.
  - PROGRESO.md creado (este archivo).

**Validacion FASE 0 (completada)**:
- Commit a482b47 con todo el renombrado + infra. Push a origin como rama
  `fase-0/clon-backbone` (push directo a main bloqueado por politica; merge a main
  queda como decision del usuario en GitHub).
- Pre-flight OK (6 puertos libres, Docker 15.6 GB RAM). `docker compose up -d`
  levanto los 5 servicios `ecorex-tareas-*` con healthchecks verdes
  (Postgres 5442, SQL Server 1443, Redis 6389, RabbitMQ 5682/15682, Adminer 8092).
- Consola Ecorex.SuperAdmin arranco contra la pila nueva: aplico las 72 migraciones,
  sembro PlatformAdmin + tenants (Agencia Demo, Plataforma ECOREX) + plan. /login 200.

**FASE 1 COMPLETADA** (3 subagentes: A seeders, B DAL dual, C test dual):
- Consola Super Admin operativa contra la pila dedicada (72 migraciones + seed, /login 200).
- Seeders segun vault (commit 0d09e16): tenant demo SKY SYSTEM (= legacy 01 BITCODE),
  PlatformAdmin admin@ecorex.local, owner/admin/operator/viewer@sky-system.local
  (Operator/Viewer mapeados a Advisor con TODO), plan "Plan Empresa".
- DAL dual (commit 0d09e16): proyecto Ecorex.Infrastructure.SqlServer con
  SqlServerEcorexDbContext y migracion inicial (77 tablas); seleccion por
  Database:Provider / ECOREX_DB_PROVIDER; jsonb->nvarchar(max), HasFilter por motor,
  cascadas ajustadas. Verificado E2E: la app migra y siembra en SQL Server real (1443)
  y el camino Postgres queda intacto (5442).
- TEST DE AISLAMIENTO CROSS-TENANT EN MATRIZ DUAL: TenantIsolationTestsBase +
  fixtures Testcontainers (postgres:16-alpine y mssql 2022) -> 6/6 verde
  (aislamiento A/B, fail-closed sin tenant, IgnoreQueryFilters admin, x2 motores).
  Canario verificado: al romper el filtro, el test FALLA (gate efectivo).

**Siguiente**:
- Decidir con ADR la migracion de TFM net9.0 -> net10.0 (SDK 10.0.301 disponible).
- Limpieza del dominio belleza/agenda (24 entidades: Service*, Resource*, Appointment*,
  Client, HairLength*, Shift*, Product*, Course*, Sede) con ADR y commits separados.
  Nota: al eliminarlas cae tambien la exclusion GiST anti-overbooking (gap SQL Server).
- FASE 2: menu del Prototipo Final (MainLayout doble panel, PRINCIPAL/MODULOS,
  stubs por policy) + revision de policies/MFA.
- Nucleo tareas/tableros/proyectos (el backbone ya trae TaskBoard/TaskCard como base).
- Actualizar vault: Registro de corridas (primera corrida dual) + ADR del DAL dual aplicado.

**Bloqueos**: ninguno. (db3dev no se ha tocado; se pedira la conexion al usuario
cuando llegue la fase de descubrimiento/ETL.)

**Decisiones**:
- Base del clon: rama `main` del backbone (identica a `deploy`).
- Adminer en lugar de pgAdmin (sirve Postgres Y SQL Server con una sola UI, puerto 8092).
- El nombre `cubotcrm` NO se renombra: identifica al repo upstream.

---

## 2026-07-03 - Sesion 2: Eliminacion del dominio belleza/agenda (ADR-0011)

**Agentes**: agente unico (barrido + migraciones + validacion).

**Hecho**:
- Eliminadas las 22 entidades belleza de Ecorex.Domain (Service*, Resource*,
  HairLength*, ShiftTemplate, ScheduleException, SalonFieldDefinition, Sede,
  Appointment*, Client, Product*, Course*) + 10 enums huerfanos.
- Eliminados 17 servicios/toolsets de Application (Agenda, Client, Course, Product,
  Resource, SalonField, ScheduleException, Sede, ServiceCatalog, ShiftTemplate,
  HairLength/HairClassifier, OnlineBooking y los 4 toolsets de IA belleza).
  El motor de agentes queda solo con PipelineToolset (crear_lead);
  AgendaToolResult -> AgentToolResult.
- Eliminadas 15 paginas + 3 componentes Blazor belleza de Ecorex.SuperAdmin,
  PublicBookingService (/r/{token}) y los endpoints /media/hair|hairref|asesor.
  NavMenu: solo se quitaron las entradas muertas (sin redisenar el menu).
- Seeders: fuera EnsureDemoProductsAsync, EnsureDemoCoursesAsync y
  EnsureDemoAgentCommercialFlowAsync (vendia productos/cursos). Demo de agente queda
  el one-shot TravelFans (CRM). EnsureDemoTemplateAssetsAsync se conserva.
- Tests: eliminados AppointmentOverbookingTests y AppointmentTierBookingTests.
  TenantIsolationTests intacto (usa TenantConfiguration, conservada): 6/6 dual verde.
- Migraciones DAL dual: Postgres `20260703175944_RemoveBellezaDomain` (drop de 22
  tablas, cae la exclusion GiST ck_appointments_no_overlap con la tabla); SQL Server
  regenerada la inicial `20260703180047_InitialCreateSqlServer` desde el modelo limpio
  (BD dev recreada). Ambas aplicadas a los contenedores dev; 55 tablas identicas por motor.
- Validado: build 0 errores, tests Domain/Application/TenantIsolation verdes,
  SuperAdmin arranca contra Postgres 5442 y /login responde 200.
- ADR nuevo: docs/decisiones/0011-eliminar-dominio-belleza.md.

**Siguiente**:
- Migracion TFM net9.0 -> net10.0 (ADR propio).
- FASE 2: menu del Prototipo Final + policies/MFA.
- Nucleo tareas/tableros/proyectos sobre TaskBoard/TaskCard.

---

## 2026-07-03 - Sesion 3: Menu del Prototipo Final (tarea funcional previa de FASE 2)

**Agentes**: agente unico (menu + stubs + validacion de rutas).

**Hecho**:
- NavMenu del workspace del tenant reorganizado segun el Prototipo Final:
  PRINCIPAL (Inicio /inicio, Anuncios /anuncios, Gestor de tareas /tableros+/tareas,
  Configuracion /configuracion), MODULOS con codigo legacy visible (Proyectos 000042,
  Actividades 000038/000636/000889, Flujos 000291, Formularios 000131, Reglas 000802),
  SISTEMA (Dependencias 000850, Modulos web 000109), CRM heredado colapsado
  (nada se borro), Super Admin SaaS intacto con su policy.
- Componente ModuleStub.razor (breadcrumb + chip de modulo legacy + tarjeta
  "se construye en Fase X") y 10 paginas nuevas; Inicio.razor con saludo contextual,
  tenant activo desde claim y 4 KPIs placeholder.
- Header del sidebar: Workspace / {tenant} / Plan {plan} - ECOREX (datos reales,
  fallback generico). Buscador placeholder estilo prototipo (Ctrl+K deshabilitado).
- Policies: stubs con [Authorize(Policy="TenantMember")] + TODO por modulo;
  PlatformOperator/SuperAdminOnly sin cambios.
- Validado: build 0 errores, unit tests 2/2, /login 200 y las 12 rutas del menu
  responden sin 404/500 (redirigen a login sin sesion).

**Siguiente**:
- Migracion TFM net9.0 -> net10.0 + EF Core 10 (ADR propio).
- FASE 3: nucleo tareas/tableros/proyectos sobre TaskBoard/TaskCard.
- Anuncios y dashboard de Inicio con datos reales.

**Bloqueos**: ninguno.

**Decisiones**:
- Inicio es pagina nueva del tenant (/inicio); Home.razor "/" sigue siendo el
  dashboard de PlatformOperator.
- Gestor de tareas mapea a Tableros.razor existente; Configuracion a Cuenta.razor.
- Sin toggle de modo oscuro aun (no existia; se hara con el rebrand visual fino).

**Bloqueos**: ninguno.

**Decisiones**:
- Tenant.PublicBooking*/OnlineBookingEnabled se conservan (regla "no tocar Tenant*"),
  quedan sin uso; retiro en fase posterior con su propia migracion.
- BusinessUnitModalKind.ImageAdvisory se conserva como valor legado (enum persistido
  como texto) para leer filas existentes; la UI ya no lo ofrece y los defaults de
  BusinessUnitService crean una sola unidad "General".

---

## 2026-07-03 - Sesion 4: Migracion a .NET 10 + EF Core 10 (ADR-0012)

**Agentes**: agente unico (migracion TFM + paquetes + validacion completa).

**Hecho**:
- TFM net9.0 -> net10.0 en los 13 csproj de la solucion (10 src + 3 tests).
- Stack completo a 10.x estable, sin mezclar majors en EF: EF Core (Core/Relational/
  Design/SqlServer) 9.0.4 -> 10.0.9; Npgsql.EntityFrameworkCore.PostgreSQL 9.0.4 ->
  10.0.2; EFCore.NamingConventions 9.0.0 -> 10.0.1 (existe estable para EF10, no hubo
  bloqueo); AspNetCore DataProtection*/JwtBearer/Mvc.Testing 9.0.4 -> 10.0.9;
  OpenApi/Components.WebAssembly(+Server) 9.0.16 -> 10.0.9; SignalR.Client 9.0.0 ->
  10.0.9; Extensions.Hosting 9.0.16 -> 10.0.9; Extensions.* 9.0.4 -> 10.0.9;
  tool local dotnet-ef 9.0.4 -> 10.0.9. Testcontainers/xunit/QuestPDF/PuppeteerSharp/
  System.IdentityModel.Tokens.Jwt sin cambios (el build no lo exigio).
- Unico fix de codigo por C# 14: variable local `field` -> `fieldDef` en accessor de
  Plantillas.razor (CS9273: `field` es keyword en accessors).
- Migraciones: has-pending-model-changes = "No changes" en ambos contextos
  (EcorexDbContext y SqlServerEcorexDbContext) bajo EF10. Sin Ef10ModelSync, sin
  tocar snapshots ni migraciones historicas. Nota: el contexto SqlServer requiere
  --startup-project src/Ecorex.Infrastructure.SqlServer (el factory design-time vive
  ahi; EF tools solo buscan factories en el startup assembly).
- ADR-0012 creado (docs/decisiones/0012-migracion-net10.md); ADR-0003 marcado como
  Reemplazado. CLAUDE.md seccion 4 actualizada con el stack real.

**Validacion (toda verde)**:
- dotnet build Ecorex.sln: 0 errores.
- Domain.Tests 1/1 y Application.Tests 1/1 en net10.0.
- Integration TenantIsolation 6/6 (matriz dual Testcontainers: postgres:16-alpine +
  mssql 2022).
- SuperAdmin /login 200 contra Postgres 5442 y contra SQL Server 1443
  (ECOREX_DB_PROVIDER=SqlServer); ambos procesos detenidos al terminar.

**Siguiente**:
- Actualizar imagenes base de Dockerfile.superadmin / Dockerfile.workers a 10.0
  antes del proximo deploy.
- Resolver NU1903 (Microsoft.OpenApi 2.0.0 transitiva, GHSA-v5pm-xwqc-g5wc) y
  ASPDEPR005 (KnownNetworks -> KnownIPNetworks en SuperAdmin/Program.cs).
- FASE 3: nucleo tareas/tableros/proyectos sobre TaskBoard/TaskCard.

**Bloqueos**: ninguno.

**Decisiones**:
- Todo el stack EF/AspNetCore queda en la misma major (10.x); EFCore.NamingConventions
  10.0.1 existia estable, asi que no aplico el plan B de quedarse en 9.x sobre net10.
- Sin commit (pedido explicito de la sesion): cambios en working tree.

---

## 2026-07-03 - Sesion 5: FASE 3 ola 1 - dominio + servicios del nucleo de tareas/proyectos (ADR-0013)

**Agentes**: 1 agente constructor.

**Hecho**:
- Dominio nuevo (Ecorex.Domain): entidades ActivityType, Project, ProjectMember,
  TaskItem, TaskItemTag (catalogo POR TENANT), TaskItemTagAssignment, TaskWorkLog,
  TaskItemActivity (reusa enum TaskActivityType), TaskItemAttachment y TenantSequence;
  enums ProjectStatus, TaskPriority, TaskItemStatus, WorkLogKind; maquina de estados
  TaskItemStateMachine (Domain/Rules) con Closed terminal e inmutable.
- Concurrencia optimista PORTABLE (decision del ADR-0013): columna Version (long) como
  ConcurrencyToken en TaskItem y Project via interfaz IVersioned; la incrementa
  AuditableTenantInterceptor en cada UPDATE. Elegida sobre xmin/rowversion para que
  modelo, migraciones y token de API sean identicos en ambos motores.
- Consecutivos: TenantSequence (TenantId+Code unico) + SequenceService con UPDATE
  condicional atomico (CAS con retry) via ExecuteUpdateAsync LINQ, sin SQL crudo,
  dentro de la transaccion del caso de uso. Reemplaza el MAX+1 legacy.
- Servicios (Application/Tenancy, patron interfaz+impl+DTOs, registrados en DI):
  ISequenceService, IActivityTypeService (CRUD + archivado), IProjectService (CRUD,
  soft-archive, miembros con CanEdit, CheckAccessAsync) e ITaskItemService (CreateAsync
  transaccional con consecutivo T00001.., UpdateAsync con token de concurrencia ->
  Conflict tipado, ChangeStatusAsync con maquina de estados -> InvalidTransition tipado,
  Assign/Unassign, tags attach/detach + catalogo, comentarios, adjuntos, worklogs con
  validacion 1..86400 s, ListAsync con filtros AND combinables + paginacion,
  GetDetailAsync compuesto). Resultados via TaskCoreResult<T>; IApplicationDbContext
  gana BeginTransactionAsync.
- Migraciones duales AddTaskCore generadas y APLICADAS: Postgres 5442 y SQL Server 1443
  (10 tablas nuevas verificadas en ambos; cc_emails jsonb vs nvarchar(max)).
- Seeder EnsureTaskCoreDemoAsync (Development, idempotente por tabla y por tenant):
  4 ActivityTypes, 3 tags (#urgente/#proveedor/#facturacion), proyecto PRJ-001
  "Implantacion ECOREX" (owner del tenant demo) y 5 TaskItems variados (uno con 2
  worklogs y 2 comentarios) + TenantSequence T05 en 6. Ejecutado y verificado con
  datos reales en AMBOS motores via SuperAdmin.
- Tests: Domain.Tests 35/35 (maquina de estados: validas, invalidas, Closed terminal);
  Integration TaskCoreTests base + clases Postgres/SqlServer (mismo patron que
  TenantIsolation): 10 creaciones CONCURRENTES -> T00001..T00010 sin duplicados,
  aislamiento cross-tenant de TaskItems, conflicto tipado con token viejo, y
  transiciones invalidas end-to-end. TaskCore 8/8 + TenantIsolation 6/6 verdes en
  ambos motores.
- ADR-0013 (docs/decisiones/0013-nucleo-taskitem.md): TaskItem primera clase,
  TaskBoard/TaskCard queda como kanban generico CRM (destino a decidir), estrategia
  Version portable, TenantSequence, hooks FASE 4 (WorkflowDefinitionId/RequiresForm).

**Validacion**:
- dotnet build Ecorex.sln: 0 errores.
- Unit + integracion nuevas verdes en matriz dual (Testcontainers).
- Migraciones aplicadas y seed verificado por SQL directo en los 2 contenedores dev.

**Bloqueos / hallazgos**:
- PREEXISTENTE (verificado en worktree limpio sobre HEAD 0c1c3b0): los 27 tests de
  Integration.Tests/Auth fallan porque el host Ecorex.Api no registra
  IAgentAssetReader (solo lo registra SuperAdmin) y ValidateOnBuild revienta. No es de
  esta sesion; fix sugerido: no-op por defecto en Application/DependencyInjection
  (patron NoOpChatBroadcaster).
- La BD dev de Postgres aun tiene el tenant demo legacy "Agencia Demo" (seed FASE 0);
  el seeder de tareas cae al primer Owner del tenant demo si no encuentra
  owner@sky-system.local.

**Decisiones**:
- Concurrencia: columna Version portable (NO xmin/rowversion condicional) - ADR-0013.
- Consecutivo con CAS + retry y EnsureSequenceAsync fuera de la transaccion (un error
  de unicidad en PG envenenaria la transaccion del caso de uso).
- Sin commit (pedido explicito): cambios en working tree.

## 2026-07-03 - Sesion 6: FASE 3 ola 2 - UI del nucleo de tareas (Blazor SuperAdmin)

**Agentes**: 1 agente implementador + validacion funcional en navegador real (preview).

**Hecho**:
- /actividades reemplaza el stub: TaskKanban con columnas fijas por estado
  (Pendiente/Activa/En proceso/Terminada/Suspendida; Closed solo en lista con toggle
  "ver cerradas"), tarjetas con numero, prioridad (chips color), avatar del encargado,
  entrega (rojo si vencida), tags y borde con Color; drag and drop nativo ->
  ChangeStatusAsync con toast del motivo si la transicion es invalida; vista Lista
  (tabla completa); barra de filtros combinables server-side via ListAsync (texto,
  estados, prioridad, encargado, tipo, proyecto, etiqueta, rango de entrega) + limpiar.
- Wizard "Nueva actividad" (TaskWizard, modal 3 pasos con barra de pasos): Informacion
  (categoria->tipo en cascada, encargado, entrega no-pasada, titulo max 200,
  descripcion, prioridad chips pastel, TagPicker con sugerencias + crear con Enter,
  max 10), Contacto y proyecto (solicitante, email validado, telefono, CC chips
  validados, proyecto opcional/preseleccionado), Confirmar (resumen + CreateAsync);
  errores en rojo bajo el campo, toast con el numero asignado al crear.
- Detalle de tarea (TaskDetailModal, modal grande 2 columnas): hero con numero +
  titulo editable inline y pills (encargado reasignable, entrega, tiempo usado,
  prioridad, estado SOLO con transiciones validas de TaskItemStateMachine); acciones
  Suspender/Reanudar/Cerrar con confirmacion; descripcion editable; worklog con
  CRONOMETRO via JS interop (wwwroot/js/task-timer.js, estado en JS, Guardar avance ->
  Kind=Timer) + entrada manual HH:MM (Kind=Manual) + historial (10) y total "4h 32m";
  card Resumen (tipo, proyecto con link, solicitante, fechas); card Actividad
  (comentarios + acciones automaticas); card Adjuntos por URL. Conflict -> aviso
  "otro usuario modifico la tarea" + recarga.
- /proyectos reemplaza el stub: grid de tarjetas (codigo, nombre, estado con color,
  owner, fechas, contadores) + modal crear/editar con validacion + archivar/restaurar;
  /proyectos/{id} (ProyectoDetalle): cabecera con estado (dropdown), owner, fechas,
  miembros con avatares y panel agregar/quitar/CanEdit + TaskKanban REUTILIZADO con
  ProjectId fijo + boton "+ Tarea" con proyecto preseleccionado en el wizard.
- Tiempo real: ITaskBroadcaster (Application, NoOp por defecto) + TaskHub +
  SignalRTaskBroadcaster (SuperAdmin/RealTime, patron ChatHub), MapHub /hubs/tasks;
  el kanban se suscribe al grupo del tenant y recarga al recibir "TaskChanged"
  {taskId, status}; los componentes difunden tras crear/editar/cambiar estado.
- Componentes en Components/Shared/Tasks/: TaskKanban, TaskWizard, TaskDetailModal,
  TagPicker, PriorityChip, TaskToasts, TaskUi.cs (labels/colores/formatos). CSS nuevo
  seccion "Nucleo de tareas" (tk-*) en app.css reutilizando patrones tb-*/pl-*.

**Fixes sobre ola 1 encontrados al probar contra PG real**:
- ProjectService.ListMembersAsync: OrderBy sobre propiedad del record DTO no
  traducible por EF (InvalidOperationException en PG real); ahora ordena por el campo
  antes de proyectar.
- TaskKanban recarga con scope EF propio (IServiceScopeFactory +
  AmbientTenantContext.Begin) + SemaphoreSlim: el reload disparado por el hub
  interlevaba consultas con el DbContext del circuito ("second operation started on
  this context").

**Validacion (probado de verdad)**:
- dotnet build Ecorex.sln 0 errores; tests 35 Domain + 1 Application + 41 Integration
  verdes (incluye TaskCore y TenantIsolation, Testcontainers duales).
- App contra Postgres 5442 (--no-launch-profile, :5233/:5234): /login 200; /actividades
  y /proyectos 302->login sin sesion, 200 con sesion.
- E2E en navegador real (preview + login demo-admin@ecorex.tareas): kanban con 5
  columnas y seed T00001..T00005; wizard crea T00006 (verificado en BD); validacion
  por paso; cascada categoria->tipo; detalle T00006: dropdown de estado solo con
  transiciones validas, cambio Pending->InProgress con actividad automatica,
  cronometro real (12s+ display JS) -> worklog Timer 23s, manual 1h30m, comentario;
  drag invalido Suspendida->Terminada: toast "Transicion invalida: Suspended -> Done"
  y la tarjeta vuelve; drag valido Suspendida->En proceso: toast + columna actualizada;
  vista Lista + filtro prioridad Alta server-side; /proyectos grid + modal validado;
  detalle de proyecto con kanban filtrado (solo 3 tareas del proyecto) y panel de
  miembros (agregar OK). /hubs/tasks negotiate 200.
- NO probado E2E: refresco cross-sesion por SignalR (requiere 2 navegadores; hub
  mapeado, broadcaster invocado sin errores en log), upload real de adjuntos (por
  diseno queda por URL).

**Deudas / TODO**:
- Archivar tarea: boton deshabilitado en el detalle; ITaskItemService no expone
  archive (IsArchived existe en la entidad). Agregar en una ola posterior.
- Adjuntos: upload real a object storage (FASE posterior); hoy nombre + URL.
- Paginacion de lista: PageSize 200 con aviso "mostrando X de Y"; falta paginador.
- Policies propias (Actividades.Editar / Proyectos.Editar) siguen TenantMember.
- .claude/launch.json: nueva config "superadmin-tasks" (pwsh + ECOREX_DB_CONNECTION
  dev 5442, puerto 5234) usada para la validacion en navegador.
- Sin commit (pedido explicito): cambios en working tree.

---

## 2026-07-03 - Sesion 7: FASE 4 ola 1 - WorkflowEngine (BPMN 2.0, ADR-0014)

**Agentes**: agente unico (port del AdmWorkflow legacy segun el vault, Capa 3).

**Hecho**:
- Dominio (5 entidades TenantEntity + 3 enums): WorkflowDefinition (ProcessCode,
  BpmnXml tal cual, versionado con unico (TenantId, ProcessCode, Version)),
  WorkflowNode (BpmnElementId, NodeType, RestartNodeId self-FK NO ACTION),
  WorkflowEdge (ConditionExpression; FKs a nodos Cascade en PG / ClientCascade en
  SQL Server, patron TaskCardTagAssignment), WorkflowInstance (Status
  Running/Completed/Cancelled/Stuck, CurrentCycle, Version IVersioned, TaskItemId
  unico filtrado) y WorkflowStepHistory APPEND-ONLY (CycleIndex, IsCurrent,
  IsCycleStart, ApprovalResult/Comment; indices (InstanceId, IsCurrent) e
  (InstanceId, NodeId, CycleIndex)). TaskItem.WorkflowInstanceId (FK sin cascada) y
  ActivityType.WorkflowDefinitionId pasa de placeholder a FK real NO ACTION.
- Motor (Ecorex.Application/Workflows): IWorkflowEngine + WorkflowEngine con
  ImportBpmnAsync (XDocument sobre el namespace OMG, acepta prefijos bpmn:/bpmn2:,
  valida 1 startEvent / >=1 endEvent / ids unicos / aristas coherentes; XML guardado
  SIN modificar para round-trip bpmn.io; reimportar = version max+1 NO publicada),
  PublishAsync (una sola version publicada por ProcessCode), SetRestartTargetAsync
  (ID_REINICIO legacy, fuera del XML estandar), StartInstanceAsync (startEvent se
  completa solo; enlaza TaskItem -> Active via TaskItemStateMachine + actividad
  "inicio el flujo"), GetCurrentStepsAsync, CompleteStepAsync y RejectStepAsync
  (reactiva el paso anterior como fila NUEVA, append-only). Avance interno (port de
  SiguienteEstado): while con tope de 50 iteraciones, compuertas exclusivas evaluadas
  contra ApprovalResult (WorkflowConditionEvaluator: "approval == 'X'"/"!=", vacio =
  default, fail-closed), ramas paralelas en nodos no-gateway, REINICIOS en LINQ/memoria
  (sin SQL crudo ni CTE: grafo completo en memoria; nodo alcanzado con RestartNodeId
  abre CycleIndex+1 con IsCycleStart), endEvent completa la instancia y pasa la tarea
  a Done + actividad "flujo completado" + ITaskBroadcaster.TaskChanged; tope de 50 ->
  instancia Stuck + resultado tipado StuckDetected (WorkflowResults, patron
  TaskCoreResults). Hook de reglas IWorkflowRuleHook (OnNodeActivatedAsync ->
  AutoComplete) con NoOpWorkflowRuleHook en DI para la ola RulesEngine.
- Integracion con creacion de tareas: TaskItemService.CreateAsync arranca la instancia
  si el ActivityType tiene definicion PUBLICADA, dentro de la MISMA transaccion
  (IApplicationDbContext.HasActiveTransaction nuevo: el motor se une a la transaccion
  del llamador; fallo del flujo -> rollback total de la tarea).
- Migraciones duales AddWorkflowEngine (Postgres 20260703215437, SqlServer
  20260703215556) generadas y APLICADAS a los contenedores dev (PG 5442 y MSSQL 1443,
  5 tablas workflow_* verificadas en ambos).
- Seeder Development idempotente EnsureWorkflowDemoAsync: flujo demo "Cotizacion
  Comercial" (COT-COM) construido via ImportBpmnAsync con XML BPMN embebido
  (start -> Requerimiento -> Cotizacion -> gateway Aprobacion; Approved ->
  Facturacion -> Entrega -> end; Rejected -> endEvent con RestartNodeId hacia
  Cotizacion), publicado y vinculado al ActivityType "Direccion Comercial/Cotizacion".
  Program.cs (SuperAdmin) lo invoca con AmbientTenantContext.Begin(tenant demo).
- Fixture BPMN real del vault copiado a tests/Ecorex.Integration.Tests/Fixtures/
  (ejemplo-bpmn-flujo-00001.bpmn, CopyToOutputDirectory).
- ADR-0014 (docs/decisiones/0014-workflow-engine.md): motor propio, XML estandar sin
  extensiones, tope 50 heredado, append-only, reinicios en LINQ sin CTE, hook de
  reglas, versionado que fija la version por instancia.

**Validacion (probado de verdad)**:
- dotnet build Ecorex.sln: 0 errores.
- Tests TODOS verdes: Domain 35, Application 26 (1 previa + 8 parser BPMN + 17 casos
  del evaluador de condiciones), Integration 57 (41 previas + 16 nuevas: 8 tests
  WorkflowEngine x 2 motores via Testcontainers PG/MSSQL): import del fixture real
  00001 (14 nodos = 1 start + 8 tasks + 3 gateways + 2 ends, 13 aristas, XML
  round-trip identico), versionado/publicacion exclusiva, flujo lineal con TaskItem
  auto-arrancado desde CreateAsync que termina Done, gateway Approved/Rejected con
  reinicio (CycleIndex=1, IsCycleStart, CurrentCycle=1), RejectStep reactivando el
  paso previo append-only, loop autonomo sin salida -> Stuck al tope de 50 (hook
  AutoComplete de prueba), aislamiento cross-tenant de definiciones/instancias/pasos
  e historial append-only tras reinicio (filas del ciclo 0 intactas).
- Seeder verificado contra el dev PG real (SuperAdmin arrancado en 5237): COT-COM v1
  publicado, 8 nodos con restart en End_Reinicio, ActivityType Cotizacion vinculado.

**Desviaciones del diseno pedido (con su porque)**:
- El fixture 00001 tiene 27 elementos ejecutables reales (14 nodos + 13 flows), no 42
  (ese conteo incluia anotaciones/asociaciones/DI, que el motor ignora); el test
  asegura los conteos reales y que endEvents son 2 (no "varios").
- Ademas del Stuck por tope de 50, se marca Stuck el caso "sin pasos vigentes y sin
  endEvent alcanzado" (ramas muertas del legacy, ej. tasks sin salida del fixture):
  evita instancias Running zombis.
- RejectStepAsync no reactiva un startEvent (no es reactivable por un humano):
  devuelve Invalid "no hay paso anterior reactivable".

**Deudas / TODO (proximas olas de FASE 4)**:
- Editor visual bpmn-js + UI de bandeja de pasos (esta ola es solo motor + seeder).
- RulesEngine reemplazando NoOpWorkflowRuleHook; condiciones de gateway sobre datos
  de formulario dinamico.
- Asignacion de encargados por paso (AssignedToTenantUserId existe pero nada lo
  puebla aun; el legacy lo resolvia con PERMISO_CARGO).
- Cancelacion manual de instancias (WorkflowInstanceStatus.Cancelled sin caso de uso).
- Sin commit (pedido explicito): cambios en working tree.

---

## 2026-07-03 - Sesion 8: FASE 4 ola 2 - DynamicFormRenderer (formularios dinamicos, ADR-0015)

**Agentes**: agente unico (port del constructor EAV legacy; en paralelo OTRO agente
trabajo SOLO la UI del layout - MainLayout/NavMenu/Login/Inicio/app.css/Home no se
tocaron desde esta ola).

**Hecho**:
- Entidades TenantEntity (Ecorex.Domain): FormDefinition (Code unico por tenant,
  Revision de negocio SEPARADA del token de concurrencia Version/IVersioned, Status
  Draft/Active/Inactive, IsArchived), FormContainer (arbol por ParentId self-FK NO
  ACTION, Segment/Table), FormQuestion (FieldCode unico por definicion = clave del
  documento JSON, ControlType con los 19 tipos del legacy pero solo Tier 1 renderizable,
  OptionsJson, ValidationJson, GridCol, Numeral), FormResponse (Data jsonb/nvarchar(max)
  { fieldCode: { value, type } }, patron dual de CcEmails, indice TenantId+DefinitionId+
  Reference, IVersioned), FormFlowLink (unico instancia+nodo+respuesta, Pending/
  Completed), FormToken (TokenHash SHA-256 unico por tenant, ExpiresAt, SingleUse,
  UsedAt, RevokedAt, AllowAnonymous) y WorkflowNodeForm (un formulario por nodo, indice
  unico NodeId).
- Enums persistidos como texto (patron existente): FormStatus, FormContainerType,
  FormControlType, FormResponseStatus, FormFlowLinkStatus.
- Servicios (Ecorex.Application/Forms, patron TaskCoreResults -> FormResult<T> con
  FieldErrors): IFormDefinitionService (CRUD definicion/contenedores/preguntas con
  FieldCode unico y formato identificador, opciones obligatorias y con ids unicos en
  Select/MultiCheck/Radio, pattern compilable, min<=max; ActivateAsync valida estructura;
  Revision++ en cambios estructurales sobre Active; AssignToWorkflowNodeAsync),
  IFormResponseService (GetOrCreateDraftAsync por definicion+referencia, SaveAsync con
  VALIDACION SERVIDOR completa por tipo devolviendo errores por fieldCode; al Submit con
  FormFlowLink Pending completa el paso via IWorkflowEngine.CompleteStepAsync en la MISMA
  transaccion - el motor se une via HasActiveTransaction; GetTaskStepFormsAsync asegura
  borrador+link idempotentes para pasos current con formulario), IFormTokenService
  (EmitAsync devuelve el token EN CLARO una sola vez y guarda solo el hash; ValidateAsync
  con las 4 verificaciones y el UNICO IgnoreQueryFilters permitido - cross-tenant acotado
  al hash exacto, devuelve el TenantId del token para fijar el ambient; RevokeAsync y
  MarkUsedAsync tenant-scoped). FormFieldValidator puro (sin EF), compartido por cliente
  y servidor. Registro DI completo.
- UI (Ecorex.SuperAdmin): /formularios reemplaza el stub (grid de definiciones con code/
  titulo/estado/revision/#preguntas + modal cabecera + boton Disenar);
  /formularios/{id}/disenar builder basico (arbol de contenedores como lista anidada,
  grid de preguntas con modal por tipo con opciones y validaciones, reordenar con
  botones arriba/abajo - SIN drag and drop, ola posterior), Vista previa (renderer en
  modo Design), Activar/Desactivar y Publicar por URL (modal que muestra la URL UNA vez
  + lista/revocacion de tokens). Componente DynamicFormRenderer
  (Components/Shared/Forms): parametros DefinitionId/Reference/Mode(Design,Fill,
  ReadOnly)/ResponseId/AmbientTenantId/OnSubmitted; arbol contenedores->preguntas con
  controles Tier 1 respetando GridCol (grid bootstrap), validacion cliente inmediata con
  el MISMO validador + errores del servidor bajo cada campo, autosave del borrador cada
  30s (timer) y boton Enviar. Visor publico /f/{token} ([AllowAnonymous], EmptyLayout):
  valida token, fija AmbientTenantContext.Begin(tenant del token), renderiza en Fill,
  marca usado si SingleUse y muestra pantalla de gracias; errores de token con mensaje
  NEUTRO (no distingue invalido/expirado/usado/revocado).
- Integracion flujo en TaskDetailModal (cambio MINIMO): seccion "Formularios del paso"
  en la columna lateral cuando la tarea tiene instancia con paso current cuyo nodo tiene
  WorkflowNodeForm; chip Pendiente/Enviado y modal con el renderer; el paso se completa
  UNICAMENTE enviando el formulario.
- Estilos de las paginas/componentes nuevos via CSS isolation (.razor.css) con los
  tokens EXACTOS del prototipo ECOREX.dc.html como fallback literal (var(--surface,
  #FFFFFF) etc.): si el layout define las variables globales del prototipo, se heredan.
- Seeder Development idempotente EnsureDynamicFormsDemoAsync: FRM-001 "Solicitud de
  cotizacion" (SKY SYSTEM) ACTIVO con 2 contenedores y 8 preguntas Tier 1 variadas
  (Text con min/max y pattern email, Select, Radio, Number con rango, Date, Toggle,
  TextArea) y WorkflowNodeForm hacia el nodo "Cotizacion" (Task_Cotizacion) del flujo
  demo COT-COM. Program.cs (SuperAdmin) lo invoca tras EnsureWorkflowDemoAsync.
- Migraciones duales AddDynamicForms (Postgres 20260703231608, SqlServer 20260703231718)
  generadas y APLICADAS a los contenedores dev (PG 5442 y MSSQL 1443; 7 tablas form_* +
  workflow_node_forms verificadas en ambos).
- ADR-0015 (docs/decisiones/0015-dynamic-forms.md): EAV -> documento JSON por respuesta,
  Revision separada de Version, Tier 1 primero con enum completo, token opaco hasheado
  con expiracion/un-solo-uso/revocacion, cross-tenant acotado del visor anonimo,
  FormFlowLink + WorkflowNodeForm.

**Validacion (probado de verdad)**:
- dotnet build Ecorex.sln (Release): 0 errores (el bin Debug del SuperAdmin estaba
  bloqueado por la instancia del agente de layout; esta ola valido y corrio en Release).
- Tests: Domain 35 verdes, Application 58 verdes (26 previas + 32 FormFieldValidator:
  required por tipo, longitudes, pattern y pattern invalido ignorado, rangos numericos,
  fechas, toggle, opcion unica/multiple invalida, parsers), Integration 67 verdes
  (57 previas + 10 nuevas: 5 tests DynamicForms x 2 motores via Testcontainers PG/MSSQL):
  CRUD + round-trip del documento identico (incluido type por campo y rechazo de
  FieldCode duplicado/Select sin opciones/regex rota), submit invalido con 6 errores por
  fieldCode y borrador intacto (autosave no valida), ciclo de vida del token (emitir ->
  validar -> usar -> reusar falla por single-use, expirado falla, revocado falla,
  garabateado falla) con scoping verificado (DbSet de B vacio, RevokeAsync cross NotFound,
  ValidateAsync devuelve el tenant del TOKEN), submit del formulario vinculado completa
  el paso (link Completed, motor avanza a Task_B, ExecutedBy = quien envio) y aislamiento
  cross-tenant de definiciones/preguntas/respuestas.
- Seeder + arranque real contra PG 5442 en puerto 5235: /formularios y /f/{token-invalido}
  responden sin 500 (login redirect y mensaje neutro respectivamente).

**Desviaciones del diseno pedido (con su porque)**:
- FormQuestion.ContainerId y FormContainer.ParentId son NO ACTION (pedido) y ademas
  DeleteContainerAsync reubica preguntas/hijos al padre en vez de fallar: evita el error
  1785 de SQL Server y no deja huerfanos.
- GetOrCreateDraftAsync con reference null SIEMPRE crea borrador nuevo (visor anonimo
  multi-uso: cada visitante su respuesta); con reference reutiliza el Draft existente.
- El chip "Pendiente Fase 4" del item Formularios en NavMenu.razor NO se toco (archivo
  del agente de layout): queda para el coordinador quitarlo.

**Deudas / TODO (proximas olas)**:
- Constructor visual completo con drag and drop y paleta de controles (esta ola: grid+modal).
- Componentes de los tipos multimedia (Image, Photo, Audio, Signature, Gps, Button,
  Chart, GridDetail, Html) que ya existen en el enum como placeholder.
- Policy propia (ej. "Formularios.Disenar") en vez de TenantMember.
- Condiciones de gateway sobre datos del documento de respuesta (RulesEngine, ola 3).
- Consultas por valor de campo (indice GIN jsonb / OPENJSON) cuando haya reportes.
- Sin commit (pedido explicito): cambios en working tree.

## 2026-07-03 - Sesion 9: Alineacion visual al Prototipo Final ECOREX (shell del workspace)

**Objetivo**: cerrar las brechas visuales de la consola contra las capturas del
prototipo (01-inicio-resumen, 01b, 02-tableros): marca, rail de iconos, landing,
Inicio con datos reales, botones negros y modo oscuro. Solo UI de layout/estilo;
sin tocar DbContext, seeder, migraciones ni las paginas nuevas de Formularios
(en curso por otra sesion paralela).

**Cambios**:
- Marca: fuera el icono de avion y el subtitulo "CRM Conversacional". El header
  del sidebar del tenant ahora es el patron del prototipo: tile cuadrado oscuro
  con la inicial + nombre del tenant + "{plan} - ECOREX" (corregido el doble
  "Plan Plan": el nombre del plan ya incluye la palabra). El default de
  PlatformBranding paso a "ECOREX.tareas / Sistema de Tareas" con propuesta de
  valor de gestion de tareas (el branding guardado en BD se sigue respetando).
- Login: tagline nueva ("Gestiona tareas, proyectos, flujos BPMN, formularios y
  reglas configurables sin codigo...") e icono SVG de tablero/checklist; el
  wording de registro paso de "agencia" a "empresa" (el name del input se
  conserva por compatibilidad con /auth/register).
- Rail de iconos (doble panel del prototipo): nav vertical fija de 56px a la
  izquierda del sidebar con Inicio, Gestor de tareas, Flujos, Formularios,
  Anuncios (NavLink con tooltip) y avatar abajo; se oculta en <= 991px. Solo se
  muestra a usuarios con claim tenant_id.
- Landing post-login: /auth/login y el callback de Google ahora redirigen a
  /inicio para usuarios de tenant (operadores siguen yendo a "/").
- Inicio.razor con datos reales (InteractiveServer con prerender): KPIs de
  Tareas activas (ITaskItemService.ListAsync TotalCount con Pending/Active/
  InProgress), Proyectos en curso (IProjectService, estados no cerrados),
  Flujos ejecutandose (WorkflowInstances Running via IApplicationDbContext con
  query filter de tenant) y Alertas (tareas vencidas sin terminar, DueTo <=
  ahora). Boton negro "+ Nueva actividad" que abre el TaskWizard existente y
  panel "Mis tareas de hoy" (asignadas al usuario, vencimiento hoy o vencidas,
  max 5, link al gestor). Saludo contextual + linea "Tienes X tareas y Y
  alertas".
- Topbar del workspace: breadcrumb "Equipos / {tenant} / {seccion}" (seccion
  derivada de la ruta) + campana de notificaciones placeholder; el area
  PlatformAdmin conserva su chip "Operador (punto medio) usuario".
- Botones primarios NEGROS solo en el workspace del tenant: clase ws-tenant en
  el shell cuando hay tenant_id sin platform_role; PlatformAdmin sigue violeta.
- Modo oscuro: toggle luna en el pie del sidebar (JS plano, sin circuito),
  clase "dark" en <html> con persistencia en localStorage y script temprano en
  App.razor (sin FOUC). Re-mapa de tokens oklch + overrides puntuales para los
  grises fijos de tableros/kanban/modales. Cubre shell + inicio + actividades +
  proyectos; paginas heredadas quedan usables sin pulir cada detalle.

**Archivos**: Program.cs (redirects), PlatformBrandingService.cs (default),
Login.razor, NavMenu.razor, MainLayout.razor, Inicio.razor, App.razor, app.css,
.claude/launch.json (config superadmin-5236 para preview).

**Validacion (probado de verdad)**:
- dotnet build Ecorex.sln: 0 errores. Tests unitarios verdes (Domain 35,
  Application 26).
- Arrancado contra Postgres 5442 en http://localhost:5236 y verificado con
  HTTP real (curl con cookies) y navegador: login demo-admin@ecorex.tareas ->
  302 a /inicio; el HTML servido trae el header "S / SKY SYSTEM / Plan Empresa
  - ECOREX" (0 ocurrencias de "Plan Plan"), breadcrumb "Equipos / SKY SYSTEM /
  Resumen", rail con 5 iconos, clase ws-tenant y KPIs con numeros reales
  (5 tareas activas, 1 proyecto, 0 flujos, 0 alertas del seed actual).
  /actividades, /proyectos, /tableros y /anuncios responden 200 con el shell
  nuevo. En navegador: el wizard "Nueva actividad" abre desde Inicio
  (circuito interactivo OK), el toggle luna pone html.dark, persiste en
  localStorage y el boton primario invierte a claro (verificado via computed
  styles; el valor "congelado" inicial era la transition de Bootstrap en la
  pestana oculta del preview, no un bug de cascada).
- Bug encontrado y corregido en vivo: Npgsql rechaza DateTimeOffset con offset
  != 0 en timestamptz; los filtros DueTo de Inicio ahora usan UtcNow /
  ToUniversalTime().
- Procesos detenidos al terminar (puerto 5236 libre).

**Deudas / TODO**:
- Si la BD tiene fila de PlatformBranding con la tagline vieja del CRM, se
  muestra esa (se respeta el branding configurado); actualizarla desde Super
  Admin -> Marca si se quiere el texto nuevo.
- Panel "Alertas del sistema" de la captura 01 no implementado (solo KPI);
  contador de anuncios del sidebar sigue placeholder 0, campana sin dropdown.
- Rail sin badge de notificaciones; en movil se oculta (como se pidio).
- El KPI de flujos cuenta WorkflowInstance Running; el seed demo actual no
  deja instancias corriendo, por eso muestra 0.
- Sin commit (pedido explicito): cambios en working tree.

## 2026-07-03 - Sesion 10: FASE 4 ola 3 - RulesEngine (motor de reglas, ADR-0016)

**Objetivo**: portar cl_gestion_reglas (modulo legacy 000802) cerrando sus tres
agujeros: RCE por Activator.CreateInstance sobre nombres del XML -> registro TIPADO
de verbos en DI; modo Execute (SQL directo) -> PROHIBIDO; historial perdido (tabla
inexistente) -> RuleExecutionLog SIEMPRE, append-only, con TTL de 90 dias.

**Cambios**:
- Dominio: RuleDocument (DocumentCode unico por tenant, categoria, RuleStatus,
  IsArchived), Rule (VerbName = clave del registro tipado, ParamsJson jsonb/nvarchar,
  SortOrder, indice DocumentId+SortOrder), RuleExecutionLog (snapshot de nombre,
  TriggerKind Manual/FormField/WorkflowNode, ContextJson, Status Success/Failed/
  Skipped, RecordsAffected, DurationMs, ExpiresAt con indices TenantId+RuleId+
  CreatedAt y ExpiresAt), FormFieldRule (pregunta->regla, unico por par, FK regla
  NO ACTION) y WorkflowNodeRule (nodo->regla, IsAutonomous). Enums RuleStatus,
  RuleTriggerKind, RuleExecutionStatus (persistidos como texto).
- Motor (Ecorex.Application/Rules): IRuleVerb { Name, Descriptor, ExecuteAsync } con
  RuleVerbDescriptor tipado (port del protocolo PARAM_XML) para que la UI renderice
  la configuracion; RuleContext (params deserializados + FormData mutable + contexto
  de tarea/flujo/respuesta); RuleVerbResult con acciones de UI TIPADAS (HideField/
  ShowField/SetFieldValue/SetRequired) y AutoCompleteStep. RulesEngine: resolucion
  por diccionario (verbo desconocido = error tipado + fila Failed), Stopwatch,
  historial SIEMPRE con TTL; ExecuteForFormFieldAsync (SortOrder, propaga FormData
  entre reglas encadenadas) y ExecuteForWorkflowNodeAsync (solo autonomas;
  AutoComplete solo si TODAS exito y alguna lo pide). Verbos resueltos DIFERIDOS del
  IServiceProvider (rompe el ciclo WorkflowEngine->hook->engine->verbos->
  ITaskItemService->WorkflowEngine).
- Verbos iniciales (5): PASAR_CAMPOS, BLOQUEAR_CAMPO_XCONDICION (equals/notEquals/
  empty/notEmpty con efecto inverso al no cumplirse), ASIGNAR_CONSECUTIVO
  (ISequenceService + anotacion en TaskItemActivity), GENERAR_TAREAS_DESDE_TABLA
  (ITaskItemService, filas de campo tabla o params.rows), NOTIFICAR (intencion en
  TaskItemActivity; envio real de correo TODO integracion). IA/importacion
  (GENERAR_TABLAS_IA, IMPORTAR_CSV, DATA_SERVER*) documentados como extension futura.
- Integraciones: WorkflowRuleHook reemplaza al NoOp en DI (nodo Task -> reglas
  autonomas -> AutoComplete); DynamicFormRenderer usa IFormRuleDispatcher +
  FormRuleUiState (cambio minimo encapsulado): campos disparadores en una consulta
  por carga, acciones aplicadas al onchange, ocultos por regla NO se validan como
  requeridos y SetRequired hace override en cliente.
- UI /reglas (reemplaza el stub): 2 tabs como el legacy 000802 (Documento de
  configuracion / Historial), CRUD de documento (archivar, nunca DELETE), grid de
  reglas con modal (verbo desde el catalogo registrado con formulario de params
  generado del Descriptor, orden, estado, JSON tipado), boton "Ejecutar prueba"
  (contexto vacio Manual; muestra resultado + acciones y queda en historial), y
  vinculacion regla->pregunta (combo formulario->pregunta) y regla->nodo Task
  (combo flujo->nodo + autonoma) creando FormFieldRule/WorkflowNodeRule. El
  FormDesigner NO se toco.
- Worker: RuleLogTtlCleanupWorker (Ecorex.Workers, diario) via
  IRuleExecutionLogCleaner (IgnoreQueryFilters + ExecuteDelete: unico DELETE fisico
  permitido, log con TTL documentado).
- Seeder Development idempotente: documento "OPERACIONES DE FORMULARIOS" (RUL-005,
  FORMULARIOS, Active) para SKY SYSTEM con PASAR_CAMPOS (nombre_solicitante->
  descripcion, vinculada a la pregunta nombre_solicitante de FRM-001),
  BLOQUEAR_CAMPO_XCONDICION (prioridad=baja oculta fecha_requerida, vinculada a
  prioridad) y ASIGNAR_CONSECUTIVO autonoma en Task_Cotizacion de COT-COM (sin
  autoComplete a proposito: no se salta el formulario del paso).
- Migraciones DUALES AddRulesEngine (PG + SQL Server) aplicadas y verificadas en los
  contenedores dev (5442/1443): rule_documents, rules, rule_execution_logs,
  form_field_rules, workflow_node_rules.
- ADR docs/decisiones/0016-rules-engine.md.

**Validacion (probado de verdad)**:
- dotnet build Ecorex.sln: 0 errores.
- Unit: Domain 35 verdes; Application 83 verdes (25 nuevos: params validos/invalidos
  y acciones por verbo, catalogo tipado con FindVerb null para verbos desconocidos).
- Integracion DUAL (Testcontainers PG16 + SQL Server 2022): 5 tests nuevos x2
  motores (historial siempre con TTL ~90d incl. verbo no registrado tipado;
  PASAR_CAMPOS end-to-end cambia el FormData persistido; regla autonoma con
  AutoCompleteStep avanza el flujo a Task_B; aislamiento cross-tenant de documentos/
  reglas/historial; TTL cleaner borra solo expirados cross-tenant e idempotente).
  Suite completa de integracion verde: 77/77 (67 previos + 10 nuevos), 0 errores,
  en AMBOS motores.
- Arranque real contra PG 5442 en puerto 5237: /reglas responde con el documento
  RUL-005 sembrado y "Ejecutar prueba" genera entrada de historial.

**Deudas / TODO (proximas olas)**:
- Verbos IA/importacion del legacy (GENERAR_TABLAS_IA, IMPORTAR_CSV, DATA_SERVER*).
- Evaluacion en SERVIDOR de verbos puros al hacer submit (hoy la exencion
  oculto-por-regla => no-requerido es del renderer; ver limitacion en ADR-0016).
- Envio real de NOTIFICAR via IEmailSender (hoy deja la intencion en la actividad).
- Policy propia (ej. "Reglas.Editar") en vez de TenantMember; el chip "Pendiente
  Fase 4" del item Reglas en NavMenu.razor es del agente de layout (no se toco).
- ETL FASE 6: portar los 8 documentos / 21 reglas del legacy mapeando Ensamblado a
  verbos del catalogo.
- Sin commit (pedido explicito): cambios en working tree.

## 2026-07-03 - Sesion 9b: Fidelidad milimetrica contra el FUENTE del prototipo (ECOREX.dc.html)

**Objetivo**: segunda pasada obligatoria sobre la alineacion visual (commit 96e196c):
auditar token por token contra el fuente real del prototipo (ECOREX.dc.html +
SPA "ECOREX - Prototipo Final.html") y corregir toda diferencia. Gana el prototipo.

**Tokens extraidos del fuente y aplicados EXACTOS en app.css (:root y html.dark)**:
--bg/--surface/--surface-2/--surface-3, --ink/--ink-2/--ink-3, --line/--line-2,
--brand #1B1B1E (negro; dark #F4F4F5) / --brand-2 / --brand-soft / --on-brand /
--glow, paleta --t-blue/rose/green/amber/violet/slate con sus *-bg, --ok/--warn/
--danger, --glass + --glass-blur 14px, --sh-sm/md/lg, --rad 20px, --pad 30px.
Tipografia del prototipo: 'Hanken Grotesk' 400-800 (importada; aplicada al
workspace via .ws-tenant; el admin conserva Plus Jakarta Sans).

**Re-mapa**: .ws-tenant redefine las variables legacy (--background, --card,
--primary, --muted, --border, --shadow-*, etc.) hacia los tokens del prototipo,
asi kanban/modales/formularios heredan la paleta exacta sin tocar PlatformAdmin
(que no lleva la clase y queda intacto). El modulo de Formularios consume estas
variables con fallback, como se acordo.

**Correcciones dimensionales (fuente)**: rail 68px (botones 42x42 r12, activo
brand/on-brand como el SPA final y las capturas, hover ink + sombra, hueco de
38px arriba, avatar 34px con anillo line-2 y color AVPAL por iniciales), sidebar
272px, header del workspace con fila hover (padding 16/16/12 + row 8px r13, tile
36x36 r11 fondo var(--ink), nombre 14.5/700 -.01em, sub 11.5 ink-3 con punto
medio "Plan Empresa (0xB7) ECOREX" via &#183; en el markup), buscador 40px r12 surface-2
borde line-2, nav activo = surface-3 + ink (ya no violeta), labels 10.5/700
ls .1em, codigos 9.5 ink-3 .75, badge Anuncios fondo ink 18px r9, footer 12px
con boton de tema 32x32 r9, topbar 56px glass+blur con crumb 13px (sep line-2,
actual ink 600) y campana 36x36 r10 con punto danger.

**Dashboard /inicio reescrito al pixel del fuente**: contenedor max 1280 con
--pad, fecha 13 ink-3, h1 32/800 -.03em lh1.04, sub 14.5 con bolds ink, boton
negro 44px r13 con icono "+" (hover opacity .9), KPI cards r20 p20 sh-sm
(hover sh-md) con tile 38 r11 en t-violet/t-green/t-blue/t-rose, chip delta
("urgente" cuando hay alertas), valor 34/800, label 13 ink-2; paneles grid
1.65fr/1fr: "Mis tareas de hoy" (rows 14/22, dot 9px, titulo 14.5/600 + sub
12 ink-3, chip prioridad 11/600 r8 rose/amber/green, due 12.5 ancho 58) y
"Alertas del sistema" NUEVO (tareas vencidas reales, icono 32 r9 t-rose-bg,
chip "N nuevas"). Chips de prioridad del kanban tambien pasan a tokens t-*
(r8, sin uppercase) dentro del workspace.

**Bug real encontrado y corregido**: con prerender interactivo, /inicio
compartia el DbContext del request con NavMenu ("A second operation was
started on this context instance", reproducido en navegador). Inicio ahora
resuelve TODOS sus servicios en un scope propio (IServiceScopeFactory), igual
que NavMenu con branding; 5 cargas consecutivas de /inicio sin fallo.

**Validacion (probado de verdad)** contra Postgres 5442 en localhost:5236 con
demo-admin@ecorex.tareas: build 0 errores; tests unitarios verdes (Domain 35,
Application 58). Verificado por computed styles en navegador real:
--brand #1B1B1E, --surface #FFFFFF, --rad 20px, fuente Hanken Grotesk, rail 68
(btn 42 r12, activo #1B1B1E/blanco), sidebar 272, topbar 56, tile 36 fondo ink,
boton "Nueva actividad" 44/r13/#1B1B1E, h1 32/800, KPI r20/p20 valor 34/800
tile #EEE8FD+#7C3AED, paneles 537/326px (1.65:1), head 18/22, sub del sidebar
"Plan Empresa (punto medio) ECOREX", crumb "Equipos / SKY SYSTEM / ...".
DARK exacto: --bg #0A0A0B, surface #161618, ink/brand #F4F4F5, boton invertido
claro, tile invertido, chips t-amber dark rgba(240,174,60,.16)/#F0C46A en
/actividades, /proyectos y /formularios legibles en ambos temas; wizard abre
desde el boton nuevo; toggle luna persiste en localStorage. HTML servido (curl
con cookies): 302 a /inicio, KPIs 5/1/0/0, "Plan Empresa &#xB7; ECOREX".
Procesos detenidos al terminar (puerto 5236 libre).

**Lo que NO se igualo (honesto, con porque)**:
- Rail activo: el fuente .dc trae 44x44 r13 blanco+glow, pero el SPA final y
  las capturas aprobadas traen 42x42 r12 fondo brand (cuadro negro): se siguio
  el SPA/capturas por ser el ejecutable aprobado.
- El sidebar del prototipo agrupa MODULOS en acordeones por grupo de negocio
  (Mis Procesos/Negocio/Oferta...) con conteos; la consola mantiene su
  estructura actual de items planos con codigos (contenido, no estilo).
- Topbar: boton "Compartir" y toggle de sidebar del prototipo no implementados
  (sin funcion detras); la campana quedo como placeholder con punto.
- El buscador Cmd+K sigue deshabilitado (placeholder), muestra "Ctrl K" ASCII
  en lugar del glifo de comando (convencion solo-ASCII del repo).
- Los KPI no muestran deltas "+3/+1/2 en pausa" (no hay serie temporal aun);
  solo el chip "urgente" del KPI de alertas cuando aplica.
- Login: el prototipo no define pantalla de login; se conservo el diseno
  actual con la tagline/icono ya corregidos.
- Sin commit (pedido explicito); no se tocaron DbContext/seeder/migraciones ni
  los componentes de Formularios (solo heredan variables). Durante la sesion
  el arranque fallo dos veces por el modelo a mitad del agente paralelo
  (AddDynamicForms y AddRulesEngine); se resolvio recompilando cuando sus
  migraciones quedaron en el arbol.

## 2026-07-03 - Sesion 9c: Gaps estructurales del prototipo (acordeones, Modulos, badges, topbar, rail)

**Objetivo**: cerrar los gaps estructurales que quedaron listados en 9b como
"no igualado": sidebar con acordeones, seccion Modulos del dashboard, deltas
de los KPI, toggle de colapso + Compartir en el topbar y railDeco.

**1. Sidebar con acordeones (NavMenu.razor + app.css)**: la seccion MODULOS
ahora replica los menuGroups del fuente (ECOREX.dc.html linea 116+): header
9/10 r10 13.5/600 ink-2 con icono coloreado (t-violet/t-blue/t-slate, stroke
1.85 como I() del fuente), contador 10px ink-3, chevron 15px sw2.2 rotado
180 grados al abrir (transition .2s); items indentados margin 1/0/5/19 +
padding-left 11 + borde izquierdo var(--line), 7/10 r8 12.5 (activo
surface-3 + ink + 600, inactivo ink-2 500) con codigo legacy 9.5 ink-3 .75.
Grupos SOLO con lo que existe hoy: Mis Procesos (2: Proyectos 000042,
Administrar actividades 000636), Automatizacion (3: Flujos 000291,
Formularios 000131, Reglas 000802), Sistema (2: Dependencias 000850,
Modulos web 000109) y CRM (heredado) (9 paginas del CRM, sin codigo).
Implementados como <details data-acc> (funcionan en SSR estatico): el
servidor decide el estado inicial (abierto si contiene la ruta activa o si
es misproc/auto, los defaults del prototipo) y un script en MainLayout
persiste el toggle del usuario en localStorage ('ecorex-acc') y lo reaplica
tras cada enhanced navigation con MutationObserver (sin cerrar nunca el
grupo de la ruta activa). Se elimino el CSS del viejo .ecorex-nav-group
(sin usos) y el label "Principal" (el fuente no lo tiene: quick nav suelto).
Quick nav del workspace subido a 14px/600 margin 2 (wsStyle exacto).

**2. Seccion "Modulos" en /inicio (Inicio.razor + app.css)**: h2 20/800 +
sub 13 ink-3, headers de categoria con punto 8x8 r3 + nombre 13/700 .03em +
desc 12.5 ink-3, grid auto-fill minmax(280px,1fr) gap 14, cards r16 p18
sh-sm hover translateY(-2px)+sh-md con tile 40 r12 (bg/color del tono de la
seccion), titulo 15/700, desc 12.5 ink-2 lh1.5 min-h 38 y pie border-top
"Ir al modulo ->" 12.5/600 + codigo 11 ink-3 .04em. Areas REALES:
OPERACIONES t-violet (Proyectos /proyectos 000042, Administrar actividades
/actividades 000636, Gestor de tareas /tableros sin codigo) y AUTOMATIZACION
t-blue (Flujos /flujos 000291, Formularios /formularios 000131, Reglas
/reglas 000802). Textos del fuente adaptados a ASCII.

**3. Badges de los KPI (Inicio.razor)**: deltas calculados con datos reales
(3 queries por CreatedAt/Status en el scope propio de la pagina): tareas
creadas hoy "+N" (tg), proyectos creados este mes "+N" (tg), instancias de
flujo Stuck "N en pausa" (ta, nueva clase .dash-kpi-delta.ta t-amber) y el
"urgente" (tr) de 9b. Valor 0 => el badge no se renderiza, como el prototipo.

**4. Topbar (MainLayout.razor + App.razor + app.css)**: boton de colapso del
sidebar 34x34 r9 borde line con el icono de panel del fuente, FUNCIONAL en
SSR: window.ecorexSidebar (App.razor, corre antes del CSS para no parpadear)
conmuta la clase html.sidebar-collapsed con persistencia en localStorage
('ecorex-sidebar'); CSS solo escritorio (>=992px): sidebar a width 0 +
opacity 0 con transition .2s como el prototipo (wsW 0px). Boton "Compartir"
h36 r10 borde line-2 13/600 con icono share 15px, deshabilitado (opacity
.55) con tooltip "Proximamente".

**5. Rail (MainLayout.razor)**: bloque inferior railDeco con los destinos
que existen: mensajes -> /conversaciones, indicadores (chart) -> /metricas
y la campana -> /anuncios (movida abajo como el fuente); arriba quedan
Inicio/Tareas/Flujos/Formularios. Calendario NO se agrego (no hay pagina).
Botones 42x42 r12 (SPA/capturas) sin cambios.

**Validacion (probado de verdad)** contra Postgres 5442 en localhost:5238
con demo-admin@ecorex.tareas: build 0 errores, tests Domain 35/35 y
Application 83/83 verdes. En navegador real (1440x900 y 1280x860):
acordeones con medidas computadas exactas (summary 13.5/600 pad 9/10 r10,
body margin 1/0/5/19 + borde line, item 12.5 pad 7/10 r8), toggle de
Sistema persiste {"sistema":true} y sobrevive navegaciones, item activo
/flujos con surface-3 #F2F2F3 + 600 y rail activo #1B1B1E, breadcrumb
actualizado; colapso del sidebar: click -> width 0/opacity 0 + localStorage
'collapsed', reload -> sigue colapsado, click -> 272px; seccion Modulos
renderiza las 2 categorias y las 6 cards navegan (click Proyectos ->
/proyectos); badges reales "+6" y "+1" (verde #DDF6E6/#16A34A r8) y ocultos
los de valor 0 (flujos en pausa, urgente); dark mode legible (card #161618,
delta rgba verde .16); /dependencias /modulos-web /metricas /conversaciones
/anuncios responden 200. Procesos detenidos al terminar (puerto 5238 libre).

**Lo que NO se igualo (honesto, con porque)**:
- Grupos del prototipo sin contenido real (Negocio, Oferta-Catalogo,
  Sistema-Inventarios/Actividades/CRM/General/Desarrollo completos) e items
  sin pagina (Crear una actividad 000038 como pagina propia, Programar
  actividad 000889, Comercial 000477 con subgrupo, Power BI 000788, Agentes
  IA 000867 del workspace): NO se inventaron, por instruccion.
- La seccion Modulos del dashboard omite las categorias NEGOCIO del fuente
  (Creacion/Seguimiento de clientes, Items-Inventarios): no existen esas
  paginas en ECOREX hoy.
- El rail no lleva Calendario (sin destino) y conserva 42x42 r12 del
  SPA/capturas aprobadas (el .dc trae 44x44 r13).
- "Compartir" es placeholder deshabilitado (sin funcion detras) y la campana
  sigue siendo placeholder con punto.
- Los deltas "+N" usan CreatedAt del dia/mes actual (no hay serie temporal
  historica); "en pausa" cuenta instancias Stuck reales.
- Durante la sesion el arranque fallo una vez por el modelo a mitad del
  agente paralelo (AddOrgAndModuleRegistry); se resolvio recompilando cuando
  sus migraciones quedaron en el arbol. No se tocaron Dependencias.razor,
  ModulosWeb.razor ni Domain/Application/migraciones (agente paralelo).
- Sin commit (pedido explicito): cambios en working tree. Se agrego la
  configuracion superadmin-5238 a .claude/launch.json para la verificacion.

## 2026-07-03 - Sesion 11: FASE 5 - Dependencias (000850) y Modulos web (000109) (ADR-0017)

**Objetivo**: los dos modulos de sistema del vault: organigrama del tenant
(Dependencias) y module registry global (Modulos web), con migraciones duales,
seeders demo, tests en matriz dual y UI segun las capturas del prototipo
(04-dependencias-organigrama, 04b-dependencias-detalle, 05-modulos-web-registro).

**Hecho**:
- Dominio: OrgUnit (TenantEntity: Name 150, Kind enum OrgUnitKind Area/Team,
  ParentId self-FK NO ACTION, ResponsibleTenantUserId?, Description? 600,
  SortOrder, IsArchived) y OrgUnitMember (FK cascade a la unidad, unico
  (OrgUnitId, TenantUserId), Role? 100). ModuleDefinition GLOBAL de plataforma
  (LegacyCode 6 digitos unico, Name, Description?, Route?, Area enum ModuleArea
  Principal/Operaciones/Automatizacion/Sistema/Crm, IsCore; SIN TenantId y SIN
  HasQueryFilter, justificado en ADR-0017: es catalogo, duplicarlo por tenant
  reintroduce la desincronizacion del legacy) y TenantModule (TenantEntity:
  FK Restrict al catalogo, IsEnabled, SettingsJson jsonb/nvarchar dual, unico
  (TenantId, ModuleDefinitionId)).
- IOrgUnitService (Application/Organization): GetTreeAsync (arbol anidado
  ordenado por SortOrder+nombre, raices = sin padre o padre no visible),
  ListAsync, GetAsync, GetKpisAsync (Dependencias / Usuarios distintos
  (miembros+responsables de unidades activas) / Areas), Create/Update con
  VALIDACION DE CICLOS (OrgUnitTree.WouldCreateCycle, funcion PURA con set de
  visitados: arbol corrupto = ciclo, fail-closed), SetArchived (soft-delete;
  bloqueado con hijas activas), Add/RemoveMember. Resultados tipados
  OrgResult<T> (patron ADR-0013/0016).
- IModuleRegistryService (Application/Modules): ListCatalogAsync (catalogo +
  estado del tenant activo; sin fila = deshabilitado), UpsertDefinitionAsync
  (SOLO PlatformAdmin, lo usa el seeder), SetModuleEnabledAsync (IsCore no se
  puede deshabilitar), UpdateSettingsAsync (valida objeto JSON),
  GetEnabledModulesAsync(tenantId) fail-closed (tenant ambiente distinto =>
  vacio; sin ambiente = plataforma) pensado para derivar el menu del registry
  (TODO policies por modulo documentado en la interfaz y el ADR).
- Migraciones DUALES AddOrgAndModuleRegistry (PG + SQL Server) aplicadas y
  verificadas en los contenedores dev (5442/1443): org_units, org_unit_members,
  module_definitions (unico legacy_code), tenant_modules (unico tenant+modulo,
  settings_json jsonb/nvarchar(max)).
- Seeders Development idempotentes: organigrama demo de 5 unidades para SKY
  SYSTEM (Direccion General > Comercial / Tecnologia > Desarrollo / Gestion
  Humana; owner responsable de la raiz y miembro; en la base dev actual el
  tenant demo solo tiene demo-admin@ecorex.tareas, el fallback por rol Owner lo
  resolvio) y catalogo global de 11 modulos reales (000038, 000042, 000636,
  000889, 000291, 000131, 000802, 000850, 000109, 000788 y 000867 placeholders
  sin ruta) TODOS habilitados para SKY SYSTEM.
- UI /dependencias (reemplaza el stub): cabecera modulo 000850, KPIs
  Dependencias/Usuarios/Areas, organigrama como arbol de cards CSS puro
  (chevron expandir/contraer, dot por tipo, avatar del responsable por
  iniciales, contador de miembros, boton + para sub-dependencia al hover),
  panel de detalle (ruta de ancestros, chips tipo/estado, responsable,
  miembros add/remove con rol, editar, archivar/restaurar) y modal de
  alta/edicion (nombre, tipo, padre, responsable, orden, descripcion). Estilos
  propios de la pagina con tokens del prototipo (--surface/--ink/--line/--t-*)
  y fallback a variables legacy; app.css NO se toco (agente paralelo).
- UI /modulos-web (reemplaza el stub): KPIs registrados/activos/nucleo, tabla
  del catalogo (codigo legacy, nombre+descripcion, chip de area, ruta, toggle
  de estado por tenant, settings), toggle solo para owner/admin del tenant
  (claim tenant_role) o platform_role, candado visual en modulos nucleo
  habilitados, modal de settings JSON con textarea validada (objeto JSON o
  vacio; el error del parser se muestra tipado).
- ADR docs/decisiones/0017-org-y-module-registry.md.

**Validacion (probado de verdad)**:
- dotnet build Ecorex.sln: 0 errores.
- Unit: Domain 35 verdes; Application 91 verdes (8 nuevos de OrgUnitTree:
  raiz, hermano valido, auto-referencia, hijo directo, descendiente profundo,
  re-colgar hacia ancestro valido, ciclo preexistente corrupto, padre fuera
  del mapa).
- Integracion DUAL (Testcontainers PG16 + SQL Server 2022): 4 tests nuevos x2
  motores (arbol CRUD + miembros + KPIs + ciclo y auto-referencia rechazados +
  archivado bloqueado con hijas y sin DELETE fisico; aislamiento cross-tenant
  de OrgUnit/OrgUnitMember/TenantModule incl. sin-tenant fail-closed; catalogo
  ModuleDefinition visible desde AMBOS tenants con estado y settings aislados
  y GetEnabledModulesAsync fail-closed; habilitar/deshabilitar por tenant con
  proteccion IsCore y NotFound tipado). Suite completa de integracion verde:
  85/85 (77 previos + 8 nuevos) en AMBOS motores.
- Dos bugs de traduccion LINQ (OrderBy sobre el DTO proyectado en
  GetEnabledModulesAsync y ListMembersAsync) encontrados por el test dual y el
  navegador real; corregidos ordenando antes de proyectar y cubiertos con
  asserts nuevos.
- Arranque real contra PG 5442 en puerto 5239 (navegador, login
  demo-admin@ecorex.tareas): /dependencias muestra KPIs 5/1/4 y el arbol demo;
  clic en nodo abre el detalle (responsable con avatar, miembro con rol);
  "+ Nueva dependencia" crea "Calidad" bajo Tecnologia (KPI pasa a 6, ruta
  "Direccion General > Tecnologia") y se archiva (KPI vuelve a 5, fila queda
  is_archived=t en BD). /modulos-web muestra 11/11/3; toggle de Flujos 000291
  apaga (Activos 10, is_enabled=f en BD) y enciende de nuevo; el toggle de un
  nucleo (000038) esta bloqueado con tooltip; settings de 000850 rechaza
  "esto no es json" con error tipado y persiste {"maxNiveles":4,...} en jsonb.
  Sin errores de consola ni de circuito. Proceso detenido al terminar.

**Deudas / TODO (proximas olas)**:
- Menu de la consola derivado de GetEnabledModulesAsync + policies por modulo
  (ej. "Modulo.000850.Usar"); hoy el NavMenu sigue estatico y las paginas bajo
  TenantMember.
- UI de administracion del catalogo global para PlatformAdmin (hoy solo seeder
  + UpsertDefinitionAsync listo con la exigencia de policy documentada).
- ETL FASE 6: portar las dependencias reales del tenant 01 (BITCODE) y el
  registro de modulos del legacy.
- Los KPIs del prototipo muestran una 4ta card cortada (carrusel); se
  implementaron las 3 principales.
- Sin commit (pedido explicito): cambios en working tree. Se agrego la
  configuracion superadmin-5239 a .claude/launch.json para la verificacion.

---

## 2026-07-03 - Sesion 12: FASE 7 ola 1 - CI en GitHub Actions (pr-check, ADR-0018)

**Agentes**: Claude Code (Fable 5).

**Hecho**:
- `.github/workflows/pr-check.yml` (nuevo; el backbone NO trajo `.github/`,
  no habia workflows de Railway que deshabilitar): triggers `pull_request`
  a main + `push` a main y `fase-0/**`; concurrency que cancela corridas
  previas de la misma rama; job unico `build-test` en ubuntu-latest con
  timeout de 30 min y pasos: gitleaks (gate de secretos, historia completa
  con fetch-depth 0), setup-dotnet 10.0.x, restore, build Release
  (solo errores bloquean; 4 warnings heredados), dotnet format
  --verify-no-changes (informativo por ahora, ver TODO), tests unitarios
  Domain + Application, tests de integracion (matriz DUAL via
  Testcontainers DENTRO del runner, sin `services:`) y resumen de .trx
  con dorny/test-reporter.
- ADR `docs/decisiones/0018-ci-github-actions.md`: que corre, que bloquea
  el merge y por que Testcontainers en el runner y no `services:` (la
  matriz vive en los fixtures de los tests, misma config que produccion).
- CLAUDE.md checklist: linea nueva con los gates que corre el CI en PR.

**Validacion (local; NO se hizo push, el workflow queda por estrenar)**:
- YAML validado con parser (PyYAML): sintaxis OK, 9 pasos.
- Comandos medidos tal cual en local: restore 6 s; build Release 45 s
  (0 errores, 4 warnings); dotnet format --verify-no-changes FALLA hoy
  (rc=2, 162 s): 33 errores WHITESPACE heredados en LeadService.cs,
  FormDefinitionService.cs, ChatService.cs (Application) y Program.cs
  (SuperAdmin) -> por eso el paso va con continue-on-error: true y TODO;
  unit tests 12 s (Domain 35 + Application 91 verdes); integracion dual
  217 s (85/85 verdes, PG16 + SQL Server 2022 via Testcontainers). Total
  local ~7.5 min; estimado en Actions 12-18 min (descarga de imagenes +
  runner mas lento), bajo el timeout de 30.
- Sin variables TESTCONTAINERS_*: en ubuntu-latest el daemon Docker es
  local y Ryuk funciona sin configuracion extra.

**Deudas / TODO**:
- Sanear los 33 errores de whitespace con `dotnet format Ecorex.sln` en un
  commit propio y quitar el continue-on-error (volver el paso gate).
- Subir el build a `-warnaserror` cuando se saneen los warnings heredados.
- Blue/green (deploy) queda para la siguiente ola de FASE 7.
- Sin commit (pedido explicito): todo en working tree; probar el workflow
  en el primer push/PR real.

---

## 2026-07-03 - Sesion 13: Cierre de 3 deudas del nucleo de tareas (archivar, paginador, policies)

**Agentes**: Claude Code (Fable 5). En paralelo otro agente creo tests/Ecorex.E2E.Tests
(proyecto nuevo) y docs; esta sesion no toco ese proyecto ni el .sln.

**Hecho**:
- ARCHIVAR TAREA: `ArchiveAsync`/`RestoreAsync` en ITaskItemService/TaskItemService
  (resultado tipado TaskCoreResult, registra TaskItemActivity "archivo la tarea" /
  "restauro la tarea", Conflict tipado ante DbUpdateConcurrencyException). Decision
  documentada en la interfaz: archivar SI se permite sobre tareas Closed (el archivado
  es visibilidad -IsArchived-, NO transicion de la maquina de estados; la solo-lectura
  de Closed aplica a la edicion de contenido). Doble archivado / restaurar no archivada
  -> Invalid tipado. UI: boton Archivar del TaskDetailModal habilitado con confirmacion
  inline ("Archivar la tarea?"), y boton Restaurar cuando la tarea esta archivada
  (badge "Archivada" en el hero ya existia).
- VISTA LISTA de /actividades (TaskKanban): toggle "Ver archivadas" (solo lista; el
  kanban NUNCA incluye archivadas: IncludeArchived solo se envia en vista lista),
  badge "Archivada" junto al estado y accion Restaurar por fila (columna visible con
  el toggle). ListAsync ya tenia el filtro IncludeArchived en TaskItemListFilter.
- PAGINADOR server-side real de la vista Lista: usa TotalCount/Page/PageSize que
  ListAsync ya devolvia. Controles Anterior/Siguiente + "N actividades - pagina X de Y"
  + selector de tamano 25/50/100 (default 25), estilos `.tk-pager` con tokens del
  prototipo (var(--card)/var(--border)/var(--muted-foreground)). Cambio de filtros o
  de tamano vuelve a pagina 1; si la pagina queda fuera de rango (ej. se archivo el
  ultimo item de la ultima pagina) se reubica en la ultima valida. El kanban conserva
  su carga de 200 (KanbanPageSize); se elimino el aviso "Mostrando X de Y".
- POLICIES POR MODULO (paso 1 del plan, nombres estables): en Program.cs del SuperAdmin
  se definieron "Tareas.Ver", "Proyectos.Ver", "Flujos.Ver", "Formularios.Disenar",
  "Reglas.Editar", "Dependencias.Ver" y "ModulosWeb.Administrar", HOY con el mismo
  requisito que TenantMember (RequireClaim tenant_id): mismo efecto neto, cero cambio
  de acceso. Aplicadas reemplazando [Authorize(Policy="TenantMember")] en: Actividades
  (Tareas.Ver), Proyectos y ProyectoDetalle (Proyectos.Ver), Flujos (Flujos.Ver),
  Formularios y FormDesigner (Formularios.Disenar), Reglas (Reglas.Editar),
  Dependencias (Dependencias.Ver) y ModulosWeb (ModulosWeb.Administrar). TODO
  documentado en Program.cs: paso 2 = derivar el requisito real desde el Module
  Registry sin tocar las paginas. Inicio/Tableros/etc. siguen en TenantMember.
- Tests: FakeTaskItemService de RuleVerbTests implementa los 2 metodos nuevos.
  TaskCoreTests (dual PG/SQL Server) sumo 2 tests: ArchiveAndRestore_ToggleList
  Visibility_AndRecordActivity (desaparece de ListAsync por defecto, aparece con
  IncludeArchived, restaurar la devuelve, doble archivado/restauro invalido, traza
  en TaskItemActivity) y Archive_OnClosedTask_IsAllowed (cerrada se archiva y
  restaura conservando Closed).

**Validacion**:
- dotnet build Ecorex.sln: 0 errores (6 warnings heredados). Tests unitarios verdes:
  Domain 35/35, Application 91/91. Filtro TaskCore dual completo: 12/12 verdes
  (6 tests x PG16 + SQL Server 2022 via Testcontainers, 17 s).
- Arranque real contra Postgres 5442 en http://localhost:5241 (config nueva
  superadmin-5241 en .claude/launch.json), login demo-admin@ecorex.tareas:
  archivar T00006 desde el detalle (confirmacion inline, badge Archivada, actividad
  "archivo la tarea", boton pasa a Restaurar); la tarea sale de la lista (5) y del
  kanban (5 tarjetas); toggle "Ver archivadas" la muestra con badge + Restaurar y
  al restaurar vuelve normal (6). Paginador probado con PageSize temporal 5 y 6
  tareas: "pagina 1 de 2" (5 filas), Siguiente -> pagina 2 de 2 (1 fila, boton
  deshabilitado), selector a 25 -> pagina 1 de 1 con 6 filas; luego se restauro el
  default 25 y se recompilo. Las 7 rutas con policy nueva responden 200 sin redirect
  a /login con demo-admin. Sin errores de consola. Proceso detenido al terminar.

**Deudas / TODO**:
- Paso 2 de policies: derivar requisitos desde el Module Registry (solo Program.cs).
- El paginador aplica a la vista Lista; el kanban sigue topado a 200 por columna-fuente.
- Sin commit (pedido explicito): cambios en working tree.

---

## 2026-07-03 - Sesion 13: Suite E2E con Playwright para .NET (ADR-0019)

**Agentes**: agente unico de la capa E2E (en paralelo OTRO agente trabajo
TaskItemService/paginas de tareas; esta sesion NO toco codigo de producto:
solo el proyecto de tests nuevo y documentacion).

**Hecho**:
- Proyecto nuevo `apps/backend/tests/Ecorex.E2E.Tests` (net10.0, xunit +
  Microsoft.Playwright 1.49 + Xunit.SkippableFact) agregado a Ecorex.sln, con
  README (instalacion de Chromium via `pwsh bin/Debug/net10.0/playwright.ps1
  install chromium`, variables y modos de arranque).
- Fixture `E2eAppFixture` (coleccion xunit `e2e`, secuencial): estrategia BASE
  por `ECOREX_E2E_BASEURL` (si esta definida usa esa app; si no responde /login
  200, la suite entera se SALTA con motivo, nunca rojo por entorno) + arranque
  automatico local como conveniencia (`dotnet run --no-build` de SuperAdmin en
  puerto libre 5250+, Development, `ECOREX_DB_CONNECTION` a PG 5442, espera
  /login 200 hasta 120 s, kill del arbol de procesos al terminar).
- 7 escenarios, cada uno con contexto de navegador propio y datos con sufijo
  unico por corrida: (a) login demo -> /inicio con saludo y 4 KPIs; (b) wizard
  3 pasos -> toast "Actividad T##### creada." + tarjeta en Pendiente; (c) cambio
  de estado Pendiente->Activa por el dropdown del DETALLE verificando que solo
  ofrece transiciones validas (drag and drop nativo descartado por fragil, ver
  ADR-0019); (d) worklog manual 0:30 con nota -> total "30m" + historial;
  (e) flujo demo COT-COM: crear tipo "Direccion Comercial/Cotizacion" (tarea
  nace Activa), completar "Requerimiento" via WorkflowEngine (backdoor
  documentado `E2eDbBackdoor`: ese paso no tiene UI, bandeja de pasos = deuda
  ADR-0014), seccion "Formularios del paso" con FRM-001 Pendiente, diligenciar
  y Enviar -> el motor avanza a Gateway_Aprobacion; (f) emitir URL publica
  single-use de FRM-001 desde el disenador, enviar en contexto ANONIMO ->
  pantalla de gracias, reuso -> mensaje neutro; (g) aislamiento con
  owner@sky-system.local -> SKIP explicativo (la BD dev solo tiene
  demo-admin@ecorex.tareas; con BD recien sembrada corre completo).
- ADR `docs/decisiones/0019-e2e-playwright.md`: alcance, estrategia
  baseurl/skip, por que dropdown en vez de drag and drop, backdoor del motor y
  PLAN de CI como job aparte de pr-check.yml (el yml NO se toco en esta sesion).

**Validacion (probado de verdad, suite completa contra la app local real)**:
- dotnet build Ecorex.sln: 0 errores. Suites existentes sin tocar.
- Corrida 1 (fixture auto-arranque): 6 PASS + 1 SKIP (escenario g), 49 s de
  tests (~74 s con el arranque de la app). Corrida 2 de estabilidad: igual.
- Escenarios a-f: PASS. Escenario g: SKIP con motivo (seed dev anterior sin
  owner@sky-system.local).

**Hallazgos de producto (la suite los destapo; NO se toco producto)**:
- BUG: dos @onchange rapidos sobre campos con reglas (FRM-001:
  nombre_solicitante/prioridad, RUL-005) tumban el circuito Blazor con
  "A second operation was started on this context instance"
  (DynamicFormRenderer.DispatchFieldRulesAsync usa el DbContext scoped del
  circuito sin aislar el scope, a diferencia de TaskKanban.ReloadAsync);
  el boton Enviar muere en silencio. Reproducible a velocidad Playwright y
  tabulando rapido. Mitigado en la suite (PublicFormFiller: orden + espera
  deterministica por la copia de PASAR_CAMPOS + pausa fija); fix real
  propuesto como tarea aparte.
- Los inputs del renderer/wizard usan @onchange: Playwright FillAsync solo
  dispara "input", hay que hacer blur explicito para que el servidor vea el
  valor (documentado en los helpers).

**Deudas / TODO**:
- Integrar la suite como job `e2e` NO bloqueante en pr-check.yml (plan en
  ADR-0019); promover a gate cuando se mida estabilidad en Actions.
- Reemplazar el backdoor del paso Requerimiento por la interaccion real cuando
  exista la bandeja de pasos del flujo (ADR-0014).
- Dos esperas fijas en PublicFormFiller (radio con regla sin efecto visible):
  quitarlas cuando el producto arregle la carrera del dispatcher de reglas.
- Escenario g completo exige resembrar la BD dev (docker compose down -v).
- Sin commit (pedido explicito): cambios en working tree.

---

## 2026-07-04 - Sesion 14: Tableros de actividades unificados - BACKEND (ADR-0020)

**Agentes**: agente unico backend (la UI del gestor de tableros es de OTRA ola).

**Hecho**:
- Modelo (ADR-0020): TaskBoard EXTENDIDO sin romper el CRM heredado (Code nullable 20
  unico por tenant con indice filtrado, Status enum TaskBoardStatus OnTime/InProgress/
  AtRisk/Completed default InProgress, DueDate, Kind enum TaskBoardKind CrmLegacy=0/
  Activities=1 default CrmLegacy). TaskItem gana BoardId/ColumnId (FKs NO ACTION a
  TaskBoard/TaskBoardColumn, coherencia columna-pertenece-al-board validada en
  Application), BoardSortOrder y StartDate (Gantt). NUEVAS TaskItemChecklistItem
  (Text 500, IsCompleted, CompletedAt/CompletedByTenantUserId informativo sin FK dura,
  indice TaskItemId+SortOrder, cascade) y TaskItemAssignment (asignados M:N del equipo,
  unico TaskItemId+TenantUserId, cascade; el ENCARGADO single AssigneeTenantUserId se
  conserva como responsable). Ambas TenantEntity con query filter global automatico.
- Servicios: NUEVO IActivityBoardService/ActivityBoardService (resultados tipados
  TaskCoreResult) que opera SOLO Kind=Activities: CRUD de tableros con Code autogenerado
  via TenantSequence "PRY" (PRY-####, padding 4) y columnas default del prototipo
  (reusa TaskBoardService.DefaultColumns, ahora internal); ListBoardsAsync (indice) con
  progreso % (checklist completado/total, fallback tareas en columna IsDone/total),
  miembros distintos con iniciales, conteo de tareas, KPIs globales (tableros, tareas,
  completadas=en columna IsDone, en riesgo=tableros AtRisk O con tareas vencidas -
  decision documentada) y filtros server-side (miembro/tag/tipo sobre las tareas, rango
  de fechas sobre el DueDate del tablero); GetBoardDetailAsync con filtros combinables
  AND en SQL (columnas[], asignados[] encargado-O-assignment, fechaLimite hoy/manana/
  con-fecha con corte de dia UTC, tags[], alcance team/mine/unassigned) y CONTADORES por
  alcance calculados con los demas filtros aplicados; MoveTaskAsync (valida columna del
  mismo board, transicion OPORTUNISTA a Done solo si TaskItemStateMachine lo permite,
  si no mueve la tarjeta sin tocar el estado y lo reporta en StatusNote, registra
  TaskItemActivity); AddTaskToBoard/RemoveFromBoard; QuickCreateTaskAsync que DELEGA en
  TaskItemService.CreateAsync (consecutivo T + tags + actividad + flujo del tipo en UNA
  transaccion) con la tarea ya colgada del board/columna (CreateTaskItemRequest gano
  StartDate/BoardId/ColumnId opcionales; tipo default = primer ActivityType activo).
- ITaskItemService ampliado: checklist (add/toggle con actividad al completar/remove/
  reorder), asignados M:N (add/remove con actividad), StartDate en create/update,
  GetDetailAsync incluye checklist + asignados (TaskItemDetailDto), summary expone
  StartDate/BoardId/ColumnId. DI: IActivityBoardService registrado.
- Logica pura extraida a ActivityBoardCalculations (BoardProgressPct, Pct, DueRangeUtc)
  + MemberInitials, con 24 unit tests nuevos (Application.Tests 115 verdes).
- Migracion dual AddActivityBoards (PG 20260704115154 / SQL Server 20260704115221)
  aplicada y VERIFICADA en los contenedores dev (PG 5442 y MSSQL 1443): columnas nuevas
  en task_boards/task_items y tablas task_item_checklist_items/task_item_assignments
  con sus FKs e indices. Sin rutas multiples de cascada en SQL Server (FKs de tablero
  NO ACTION; no hizo falta ClientCascade).
- Seeder Development idempotente EnsureActivityBoardsDemoAsync (SuperAdmin Program.cs):
  tablero PRY-0042 "Comercial - Requerimiento Infraestructura" (InProgress, vence
  2026-07-12, descripcion del prototipo, columnas default) con 10 tareas del prototipo
  (Cotizar equipos de red 0/4 tag Infraestructura due 1-jul; Migrar formulario a EAV 3/4
  due hoy; Aprobar cotizacion de proveedor 4/4 due hoy; Configurar consecutivo 0D7
  Completado due 6-jul; etc.), tags Infraestructura azul/Comercial rosa/Proyecto medio
  verde, encargados/asignados repartidos entre owner/admin/operator/viewer, 1 tarea sin
  asignar y 3 del owner; + 2 tableros simples (PRY-0040 OnTime, PRY-0041 AtRisk) para el
  KPI "3 Tableros". Secuencias coherentes (PRY=43, T05 continua).
- ADR nuevo docs/decisiones/0020-tableros-actividades-unificados.md.

**Validacion (probado de verdad)**:
- dotnet build Ecorex.sln: 0 errores (warnings heredados). Domain 35/35,
  Application 115/115 (91 previos + 24 nuevos).
- Integracion NUEVA ActivityBoardTests en matriz dual (fixtures TenantIsolation,
  Testcontainers PG16 + SQL Server 2022): 12/12 verdes (6 tests x 2 motores, 38 s):
  (1) board Activities con code PRY-0001/0002 autogenerado + columnas default + code
  explicito y duplicado Invalid; (2) QuickCreate cuelga con T00001/T00002 unicos,
  BoardSortOrder 0/1 y columna ajena Invalid; (3) filtros del detalle por columna /
  asignado (encargado Y assignment M:N) / tag / fecha hoy + alcances con contadores
  team 4 / mine 2 / unassigned 1 y combinacion AND; (4) MoveTask a columna IsDone:
  Active->Done aplicado con actividad, Pending NO rompe (mueve, estado intacto,
  StatusNote); (5) checklist toggle actualiza progreso de tarjeta (1/2=50%) y del
  indice (50%), con CompletedBy/At y actividad, y destoggle limpia; (6) aislamiento
  cross-tenant de boards/checklist/assignments (indice vacio, detalle NotFound, move
  NotFound, DbSets vacios). Suite de INTEGRACION COMPLETA: 101/101 verdes (2 m 6 s;
  una corrida previa dio 6 rojos por flake de arranque del contenedor MsSql de
  Testcontainers bajo carga -la app dev y el seed corrian en paralelo-, la corrida
  limpia paso entera).
- Seed verificado por consulta directa en PG 5442 tras arrancar SuperAdmin real:
  3 tableros Activities (PRY-0042/0040/0041 con estados InProgress/OnTime/AtRisk),
  PRY-0042 con 10 tareas T00010..T00019 repartidas en las 4 columnas con checklists
  0/4, 3/4, 4/4, 0/3 y 1/2, y contadores de alcance del owner team=10 / mine=3 /
  unassigned=1 (query directa). Segundo arranque: sigue 3/10 (idempotente).

**Decisiones**:
- Kind en TaskBoard (no entidades nuevas) para no romper el CRM heredado.
- Columna != estado: transicion oportunista a Done SOLO si la maquina la permite;
  mover fuera de IsDone no reabre.
- FKs tarea->tablero/columna NO ACTION: borrar tablero desacopla tareas primero
  (las actividades nunca mueren con el tablero).
- KPI "en riesgo" = tableros AtRisk O con tareas vencidas fuera de columna final.
- Corte de dia de los filtros hoy/manana en UTC (zona del tenant = deuda ola UI).
- QuickCreate sin tipo usa el primer ActivityType activo del tenant.

**Deudas / TODO**:
- Ola de UI: indice + tablero con chips/alcances/vistas Lista-Calendario-Gantt sobre
  IActivityBoardService (esta ola fue SOLO backend).
- Corte de dia por zona horaria del tenant en DueRangeUtc.
- Destino final del kanban CRM heredado (TaskCard) cuando 000636 reemplace esas paginas.
- Sin commit (pedido explicito): cambios en working tree.

## 2026-07-04 - Sesion 15: Menu igualado 1:1 con el fuente del prototipo (ECOREX.dc.html)

**Objetivo**: cada opcion del menu del prototipo existe en el sistema; lo que no
tiene modulo real navega a un placeholder digno (nunca 404).

**Fuente**: estructura extraida del FUENTE (groupDefs/quickNav/rail/railDeco de
ECOREX.dc.html), no de memoria. 9 grupos MODULOS + subgrupo Comercial, 48 items
con codigo legacy 000XXX, quick nav Inicio + Anuncios (badge), rail de 8 iconos
(Inicio/Tareas/Flujos/Formularios + Calendario/Notificaciones/Indicadores/Alertas)
y avatar abajo.

**Hecho**:
- NavMenu.razor: seccion MODULOS reconstruida exacta al fuente (orden, contadores
  de items hoja, subgrupo Comercial abierto por defecto, misproc/auto abiertos por
  defecto como el prototipo). Quick nav reducido a Inicio + Anuncios (badge).
- Items reales mapeados: 000038 -> /crear-actividad (nuevo, abre TaskWizard);
  000042 -> /proyectos; 000636 y 000477 y 000270(gen) -> /actividades;
  000740 -> /pipeline (leads CRM); 000291 -> /flujos; 000131 -> /formularios;
  000867 -> /agentes; 000615 -> /configuracion; 000893 -> /plantillas;
  000850 -> /dependencias; 000109 -> /modulos-web; 000802 -> /reglas;
  000119 -> /metricas.
- Placeholders: pagina generica /modulo/{slug} (Modulo.razor, registro estatico
  slug -> titulo/grupo/codigo) sobre ModuleStub con el texto "Modulo pendiente de
  construccion - se priorizara en fases siguientes"; policy TenantMember. 31 items
  del menu + 3 destinos del rail (calendario/notificaciones/alertas).
- CrearActividad.razor: pagina liviana que abre TaskWizard al entrar; al crear o
  cerrar redirige a /actividades (vigia JS ecorexWatchWizard en MainLayout porque
  TaskWizard no expone callback de cierre; no se toco TaskWizard).
- MainLayout.razor: rail igualado al fuente (orden e iconos de const rail/railDeco);
  Indicadores -> /metricas real, Calendario/Notificaciones/Alertas -> stubs.
- app.css: clases .tr/.tg (rosa/verde para Negocio y Oferta-Catalogo) y estilos del
  subgrupo (.ecorex-acc-sub) replicando el prototipo.

**Desviaciones documentadas**:
- Grupo extra "CRM (heredado)" AL FINAL (no esta en el fuente) con las paginas CRM
  reales sin mapear: Asesores, Conversaciones, Lineas WhatsApp, Bitacora del agente,
  Automatizaciones, Lista negra (para no perder acceso).
- Quick nav "Gestor de tareas" (/tableros) y "Configuracion" retirados del menu
  rapido para ser identicos al fuente; /tableros sigue accesible por URL directa y
  /configuracion quedo mapeado en Sistema-General (000615).
- 000477/000636/000270(gen) comparten destino /actividades (el fuente los manda a la
  misma pantalla work/actividades): los 3 se resaltan activos a la vez en ese caso.
- Vendedores (000124) quedo placeholder; Asesores va en CRM (heredado) porque el
  nombre no coincide claramente.

**Validacion (probado de verdad)**:
- Build SuperAdmin: 0 errores 0 warnings. Tests: Domain 35/35, Application 115/115,
  Integracion 101/101 verdes.
- App real contra PG 5442 en http://localhost:5246 con owner@sky-system.local:
  los 57 destinos unicos del menu+rail responden 200 (fetch autenticado, sin
  404/500); stub /modulo/bodegas renderiza titulo + chip "Modulo 000556" + texto
  pendiente + seccion "Sistema (punto medio) Inventarios"; /crear-actividad abre el
  wizard solo y al cerrarlo redirige a /actividades (verificado en navegador);
  acordeones con contadores 5/3/1/4/5/4/8/8/10/6, toggle persistido en localStorage;
  rail 8 iconos + avatar; slug desconocido /modulo/no-existe cae al stub generico.
- Procesos detenidos al terminar. Nota: durante la sesion las corridas E2E de la
  sesion de tableros mantenian bloqueado bin/ de SuperAdmin; esta sesion compilo y
  ejecuto desde un output aparte para no interferir.

**Deudas / TODO**:
- Conectar el badge de Anuncios a datos reales cuando exista el modulo.
- Al construir cada modulo real: mover la opcion de /modulo/{slug} a su pagina y
  policy propia (los placeholders usan TenantMember).
- Sin commit (pedido explicito): cambios en working tree.

---

## 2026-07-04 - Sesion: Ola 2 UI de tableros de actividades (pantalla 'work' del prototipo)

**Agentes**: coordinador + 2 exploradores (UI Blazor y contratos backend/E2E).

**Fuente**: pantalla 'work' del prototipo corregido ECOREX.dc.html (showBoardsIndex +
boardOpen + isTablero/isLista) y capturas de Prototipo/screenshots. Valores tomados
del FUENTE (estilos inline del prototipo), no de memoria.

**Hecho**:
- /actividades REEMPLAZADA por la experiencia de tableros (alias /tableros-actividades,
  deep-link ?board={id}). El kanban por estado (TaskKanban) quedo desconectado de la
  ruta pero intacto: lo sigue usando ProyectoDetalle. Tableros.razor CRM sin tocar.
- NUEVOS: Components/Shared/Tasks/ActivityBoardsIndex.razor (indice: eyebrow TAREAS,
  h1 28px/800, 4 KPI cards 44x44/27px con soft-bg violet/blue/green/rose, barra
  Filtros con 5 dropdowns cascada Usuario/Etiqueta/Categoria/Subcategoria(=tipo)/
  Fecha + Limpiar(N), grid auto-fill minmax(320px,1fr) de tarjetas r18/p20 con hover
  translateY(-2px), badge de estado por TaskBoardStatus, barra Avance 6px brand,
  avatares solapados 26px, modal "Nuevo tablero", boton "Actividad completa" que
  abre el TaskWizard de 3 pasos para no perder el flujo con tipo/BPMN).
- NUEVOS: ActivityBoardDetail.razor (breadcrumb "< Todos los tableros", h1 27px/800 +
  pill de estado con punto + fecha limite + lapiz -> modal editar tablero, subtitulo
  literal del prototipo, FILAS DE FILTRO grid max-content/1fr con chips de columnas
  (punto colPal), asignados (avatar 22px), fecha Hoy/Manana/Con fecha (+date input,
  semantica OnDate del backend), etiquetas coloreadas con ring 2px al activar,
  Limpiar(N); PESTANAS DE ALCANCE team/mine/unassigned con contadores del servicio;
  switcher Tablero/Lista + Calendario/Gantt deshabilitados "Proxima ola" + boton
  Filtrar (badge, colapsa filas) + boton Tarea; kanban repeat(N,minmax(0,1fr)) con
  badges de columna, tarjetas r16 con Progreso checklist N/M y barra 5px con color
  por columna (t-blue/danger/t-amber/ok), avatares, pie con fecha coloreada
  (vencida danger / hoy warn) y contadores adjuntos/comentarios/checklist; drag and
  drop HTML5 (patron TaskKanban) -> MoveTaskAsync con toast si StatusNote; vista
  Lista con grid literal "1fr 130px 110px 110px 150px 76px"; modal de creacion
  rapida (titulo/descripcion/columna/prioridad/encargado/fecha/etiquetas + tipo de
  actividad opcional) -> QuickCreateTaskAsync con toast T#####).
- NUEVO AbUi.cs: paleta AVPAL de avatares, colPal por indice de columna (punto,
  badge, barra), estados del tablero, prioridades y fechas ("12 julio, 2026",
  "1 jul", Hoy/Manana) 1:1 con el prototipo.
- TaskDetailModal EXTENDIDO (no reescrito): card "Lista de chequeo" (checkbox 20px
  r6 verde + tachado + agregar/eliminar via ITaskItemService), card "Asignados"
  M:N (avatares solidos + agregar/quitar), fila "Avance N/M" + barra en Resumen
  alimentada por el checklist, y pill "Mover a: [columnas del tablero]" en el hero
  (MoveTaskAsync; StatusNote se muestra como banner informativo).
- app.css: bloque .ab-* (indice/detalle/kanban/lista/modales) + .tk-check-*/
  .tk-assignee-*/.tk-moveto con los valores del fuente; claro/oscuro via tokens.
- SignalR: ambos componentes se suscriben a TaskChanged (hub /hubs/tasks) y
  refrescan con scope EF propio + SemaphoreSlim (patron TaskKanban) - sin esto EF
  lanzaba "second operation on this context" y mataba el circuito (bug encontrado
  y corregido en esta sesion).
- Backend (delta minimo, reportado): ActivityBoardIndexFilter.HasDueDate (bool?) +
  2 lineas en ActivityBoardService.ListBoardsAsync para el dropdown Fecha del
  indice (Con fecha limite / Sin fecha), que no era expresable server-side.
- E2E actualizados al flujo nuevo: E2eTestBase (wizard via "Actividad completa",
  OpenBoardAsync/QuickCreateTaskAsync/BoardColumn/CardIn .ab-*), CreateActivityTests
  (wizard toast + creacion rapida con tarjeta en columna), MoveCardTests (dropdown
  "Mover a" Por hacer -> En progreso + pill de estado intacto), WorklogTests y
  WorkflowFormTests (crean por quick-create en PRY-0042), TenantIsolationTests
  (.ab-boards), PublicFormTokenTests (reintento del click Disenar: se perdia si el
  circuito seguia conectando bajo carga) y NUEVO BoardsIndexTests (KPIs, 3 tableros,
  abre PRY-0042, chips combinados con alcances, checklist -> Avance -> Progreso).

**Desviaciones documentadas (vs prototipo)**:
- Chips de Estado activos: borde ink + surface-3 (asi lo hace el FUENTE via pill(on);
  la instruccion decia brand/on-brand pero el fuente gana).
- Separador " - " en vez de " (punto medio) " en columnas del card (regla solo ASCII).
- Chip "Con fecha" del detalle abre un date input (el backend define OnDate con
  fecha puntual; el "cualquier fecha" del prototipo no existe en el filtro).
- Modal rapido agrega select "Tipo de actividad" (opcional, no esta en el prototipo):
  necesario para crear tareas con flujo BPMN desde el tablero (WorkflowFormTests).
- ProgressColor del DTO (color pale de la columna seed) NO se usa: se deriva el
  color de barra por indice de columna como pide el prototipo (t-blue/danger/
  t-amber/ok).
- Fix cross-engine: .ab-board-card > * { width:100% } (Chromium de Playwright no
  estira hijos flex de un <button>).

**Validacion (probado de verdad)**:
- dotnet build Ecorex.sln: 0 errores. Unit tests: Application 115/115, Domain 35/35.
  Integracion NO tocada (el delta backend es aditivo con default null).
- App real contra PG 5442 en http://localhost:5245 (login owner@sky-system.local):
  verificado en navegador real (Playwright + preview): indice con 3 tableros y
  KPIs; PRY-0042 abre con 4 columnas propias; chips Estado/Asignado/Hoy/Etiqueta
  filtran y COMBINAN con los alcances (contadores recalculados con los demas
  filtros); vista Lista; creacion rapida con toast T##### y tarjeta en la columna;
  mover por dropdown "Mover a" del detalle; checklist toggle actualiza Avance y el
  Progreso de la tarjeta; capturas claro y oscuro correctas (tokens html.dark).
- Nota alcances: con seed limpio son 10/3/1; la BD dev acumula tareas de corridas
  E2E previas (los contadores mostrados coinciden exactamente con la BD: 31/3/22
  al momento de la captura). Para ver 10/3/1 exacto: re-sembrar BD limpia.
- Suite E2E completa VERDE contra la app real: 10/10 (dos corridas consecutivas,
  41s y 48s). Procesos propios detenidos al terminar (la instancia :5234 de la
  sesion del menu se dejo intacta).

**Deudas para la ola 3**:
- Vistas Calendario y Gantt del tablero (tabs ya deshabilitados con tooltip
  "Proxima ola"; el prototipo trae calCells/ganttRows como referencia).
- Menu "..." de columna (renombrar/recolorear/agregar columna: hoy es decorativo)
  y boton "+" para agregar vista.
- Menu "..." de la tarjeta (hoy muestra el numero T en tooltip; falta menu real).
- Reordenar tarjetas DENTRO de la misma columna con drag (hoy solo entre columnas;
  MoveTaskAsync ya recibe sortOrder).
- Nombres de usuario en dropdowns del indice/modal rapido: se muestran emails
  (TenantUserDto no expone DisplayName; los chips del tablero si usan DisplayName
  del ActivityBoardMemberDto).
- Bug PRE-EXISTENTE anotado: DynamicFormRenderer.SaveAsync suelta un SemaphoreSlim
  ya disposed al enviar el formulario publico (ObjectDisposedException en el log;
  no rompe el flujo pero mata el circuito tras el submit).

---

## 2026-07-04 - Sesion: Ola 3 UI de tableros - vistas CALENDARIO y GANTT + pendientes menores

**Agentes**: agente unico (UI + delegaciones aditivas + E2E + validacion en navegador real).

**Fuente**: bloques isCalendario / isGantt / calCells / ganttRows / ganttDays de la
pantalla 'work' de ECOREX.dc.html (leidos del fuente, no de memoria).

**Hecho**:
- VISTA CALENDARIO en ActivityBoardDetail.razor: contenedor surface/line/var(--rad)/
  sh-sm con header "Julio 2026" (16px/700, -.01em) + botones prev/next 30x30 r8 que
  navegan el MES REAL; grilla 7 columnas con header Lun..Dom (11px/600 ink-3, ASCII);
  celdas de dia min-height 92px r10 p7 border line (35 o 42 segun el mes, offset
  lunes); HOY con border brand + bg brand-soft y numero en circulo 22px brand/
  on-brand 11.5px/700; numeros 12px/600 ink-2. Chips de tarea por DueDate local
  (10px/600 p3-6 r6 bg surface-3 border-left 3px del color de columna, truncados),
  MAX 3 por celda + "+N"; click en chip abre TaskDetailModal (stopPropagation);
  click en dia valido abre el modal de creacion rapida con esa fecha PRESELECCIONADA.
  Usa las MISMAS tarjetas filtradas de las otras vistas (ListRows del detalle).
- VISTA GANTT: banda header bg surface-2 con etiqueta izquierda 220px
  "TAREA - {MES} {ANO}" (10.5px/600 ink-3 .05em) + grilla de 14 dias (11px/600,
  border-left line, fin de semana bg surface-3 por DayOfWeek real) + botones
  prev/next que desplazan la ventana de 14 dias (adicion necesaria: el fuente no
  trae navegacion). Ventana inicial = bloque de 14 dias del mes que CONTIENE a hoy
  (1-14 / 15-28 / 29+). Filas: izquierda 220px con punto 8px del color de columna +
  nombre 13px/600 truncado; derecha relative height 38px con grid de fondo
  linear-gradient(90deg, line 1px) size calc(100%/14); linea HOY 2px brand opacity
  .45 en left ((dia-0.5)/14)%; barra absoluta top 8 height 22 r7 bg color de columna
  (colInfo = ColumnDot, igual al fuente) con el progreso "N/M" del checklist en
  blanco 10.5px/700; posicion StartDate -> DueDate (sin StartDate usa CreatedAt,
  sin DueDate usa StartDate+1d), clampeada a la ventana; filas totalmente fuera de
  rango se OCULTAN. Click en barra o en el nombre abre TaskDetailModal. Respeta
  filtros y alcances.
- Tabs Calendario y Gantt HABILITADOS (fuera el disabled/tooltip "Proxima ola").
- Menu "..." de COLUMNA (popover estilo ab-dd del fuente): Renombrar columna (modal),
  Marcar/Desmarcar columna final (IsDone) y Agregar columna al final. Usa el
  ITaskBoardService EXISTENTE (UpdateColumnAsync/CreateColumnAsync) inyectado en el
  componente: cero cambios de interfaz backend para columnas.
- Menu "..." de TARJETA: Abrir, Mover a (submenu con las columnas y su punto de
  color -> MoveTaskAsync al final de la columna destino) y Archivar con confirmacion
  en dos pasos -> ITaskItemService.ArchiveAsync existente + toast + broadcast.
- REORDEN INTRA-COLUMNA por drag: drop SOBRE una tarjeta inserta ANTES de ella;
  drop en el cuerpo de la columna manda al final. El indice de drop viaja como
  BoardSortOrder en MoveTaskAsync.
- Dropdowns de asignado con nombre legible: TenantUserDto gana DisplayName?
  (parametro opcional al final, cambio ADITIVO) poblado por join con PlatformUsers
  en TenantUserService.ListAsync; TaskUi.UserLabel(u) devuelve DisplayName o, si es
  null, DERIVA de la parte local del email con palabras capitalizadas
  ("ana.garcia@x" -> "Ana Garcia", decision documentada en el codigo). Aplicado en
  indice, modal rapido, TaskDetailModal (encargado + asignados), TaskKanban y
  TaskWizard. Con el seed real se ven "Owner SKY SYSTEM", etc.
- AbUi: MonthTitle ("Julio 2026") y MonthUpper ("JULIO") sobre MonthsLong.
- app.css: bloques .ab-cal-* / .ab-gantt-* / .ab-menu-* con los valores literales
  del fuente; claro/oscuro via tokens (verificado en ambos temas).

**Cambios backend (reportados, todos acotados)**:
- TenantUserDto.DisplayName (opcional, aditivo) + join en TenantUserService.ListAsync.
- ActivityCardDto.CreatedAt (opcional, aditivo) poblado en GetBoardDetailAsync:
  lo exige el fallback de la barra del gantt.
- ActivityBoardService.MoveTaskAsync: ahora RE-SECUENCIA la columna destino en
  memoria (inserta la tarea en el indice de drop clampeado y deja BoardSortOrder
  denso 0..N, un solo SaveChanges) y solo registra la actividad "movio la tarea"
  cuando CAMBIA de columna (el reorden intra-columna no ensucia el historial).
  El DTO devuelve el BoardSortOrder efectivo. Integracion ActivityBoard 12/12 y
  suite completa 101/101 verdes con el cambio.
- BUG PRE-EXISTENTE corregido (destapado por el input de fecha del modal rapido
  contra PG real): las fechas limite se construian con offset local -05:00 y Npgsql
  solo acepta DateTimeOffset UTC en timestamptz -> ArgumentException y circuito
  muerto. Fix .ToUniversalTime() (mismo patron de Inicio.razor, sesion 9) en los 7
  puntos: quick-create y editar tablero (ActivityBoardDetail), nuevo tablero
  (ActivityBoardsIndex), editar entrega (TaskDetailModal), wizard (TaskWizard) y
  filtros DueFrom/DueTo (TaskKanban).

**Desviaciones documentadas (vs instruccion, el fuente gana)**:
- Contenedores con border-radius var(--rad) = 20px (la instruccion decia r16; el
  fuente usa var(--rad) y el prototipo define --rad: 20px).
- Separador " - " en la etiqueta del gantt (regla solo ASCII; el fuente usa punto medio).
- Botones prev/next del gantt: no existen en el fuente (estatico); se agregaron con
  el MISMO estilo de los del calendario, en la banda del header.
- Weekend del gantt por DayOfWeek real (el fuente lo hardcodeaba para julio 2026).

**Validacion (probado de verdad)**:
- dotnet build Ecorex.sln: 0 errores. Domain 35/35, Application 115/115.
- Integracion: ActivityBoardTests 12/12 y SUITE COMPLETA 101/101 verdes
  (Testcontainers PG16 + SQL Server 2022).
- E2E: 2 escenarios NUEVOS en BoardViewsTests (a: chip visible en la celda del
  DueDate con dia pseudo-unico del mes siguiente, click abre el detalle, celda de
  HOY resaltada, y limpieza archivando via menu "..." con confirmacion; b: barra
  del gantt visible con progreso 0/0, banda TAREA-{MES}, 14 dias, linea de HOY y
  click en barra abre el detalle). QuickCreateTaskAsync del harness gana dueDate
  opcional (fill + blur por el patron @bind/onchange de ADR-0019).
- SUITE E2E COMPLETA contra la app real (PG 5442, puerto 5247): 12/12 verde
  (corridas 1 y 5); en 2 corridas intermedias fallo SOLO PublicFormTokenTests
  (flake PRE-EXISTENTE ya anotado en la ola 2: ObjectDisposedException del
  SemaphoreSlim de DynamicFormRenderer.SaveAsync al enviar el formulario publico;
  en aislamiento pasa 1/1). Los 2 escenarios nuevos pasaron en TODAS las corridas.
- Manual en navegador real (localhost:5247, claro Y oscuro): calendario (titulo,
  Lun..Dom, 35 celdas, hoy 22px brand/on-brand y bg brand-soft, chips con
  border-left del color de columna, "+4" de overflow, chip abre detalle, prev/next
  Junio<->Agosto, dia vacio abre quick-create con la fecha 2026-08-20 preseleccionada);
  gantt (TAREA - JULIO 2026, dias 1..14, 4 celdas weekend, barras N/M left/width en
  % exactos, linea hoy en 25% opacity .45, track 38px con grid 7.14286%, ventana
  15..28 con prev/next y filas fuera de rango ocultas, barra abre detalle);
  menu de columna (renombrar ida y vuelta, toggle columna final con toasts,
  agregar columna al final -limpiada por SQL para no ensuciar el seed-); menu de
  tarjeta (Abrir/Mover a con 4 columnas/Archivar); reorden drag intra-columna
  verificado (la tarjeta insertada ANTES del objetivo y persistida); dropdown
  Encargado con nombres legibles. Valores de estilo verificados por computed styles
  en claro y oscuro (dark: line rgba(255,255,255,.07), surface-2/3 oscuros, barra
  ink-3 dark, hoy brand dark).
- Procesos DETENIDOS: la app :5247 se paro al terminar (solo quedo la instancia
  :5234 de otra sesion, intacta a proposito). launch.json gano config
  superadmin-5247.

**Deudas / TODO**:
- Boton "+" de agregar vista en el switcher sigue decorativo (no estaba en la ola).
- El indice de drop del reorden usa las tarjetas FILTRADAS visibles; con filtros
  activos la insercion es aproximada respecto de la columna completa.
- Flake pre-existente de PublicFormTokenTests (bug del dispatcher/semaforo de
  DynamicFormRenderer): sigue pendiente como tarea de producto aparte.
- Zona horaria del tenant para el corte "fin del dia" de las fechas limite (hoy:
  fin del dia local del SERVIDOR convertido a UTC).
- Sin commit (pedido explicito): cambios en working tree.

## 2026-07-04 - Sesion: Modulo FORMULARIOS fiel al prototipo + constructor funcional end-to-end (ADR-0021)

**Agentes**: agente unico (modelo aditivo + migracion dual + renderer + indice +
constructor + seeds + tests + E2E + validacion manual en navegador).

**Fuente**: bloques isForms / formBuilderOpen / fbTypeReg / fbPaletteGroups /
renderNode / widthGrid / propTabs de ECOREX.dc.html (lineas 3016-3440 markup y
4069-4300 logica, leidos del fuente, no de memoria).

**Hecho**:
- MODELO (aditivo, UNA migracion dual `AddFormBuilderFields` aplicada y verificada en
  PG 5442 y MSSQL 1443): FormContainerType += Row/Col/Section/Tabs/Modal (string, se
  conservan Segment/Table; Segment se renderiza como Section); FormControlType +=
  File/Barcode/Paragraph/Divider/Spacer; FormQuestion += Width (1..12, backfill desde
  grid_col en la migracion, GridCol queda SINCRONIZADO col-12/col-md-N para renderer
  bootstrap y selectores E2E), PlaceholderText(200), DefaultValue(2000, doble uso
  documentado: texto del Paragraph / alto px del Spacer), IsLocked, IsHidden;
  FormContainer += TabsJson (jsonb/nvarchar), Width, IsLocked, IsHidden.
- SERVICIOS: FormDefinitionListItemDto += ResponseCount y RuleCount (KPIs reales del
  indice); MoveQuestionToAsync / MoveContainerToAsync (drag and drop, renumeran
  hermanos y validan ciclos); GridDetail exige columnas ([{id,label}] en OptionsJson);
  Required se apaga en multimedia placeholder; validacion servidor salta IsHidden;
  IRuleDocumentService.ListQuestionLinksAsync (tab Reglas por pregunta).
- RENDERER (DynamicFormRenderer, hereda /f/{token} y vista previa): contenedores
  Row (grilla)/Col (apilado)/Section=Segment/Tabs NAVEGABLES (cada hijo directo es
  pestana, nombres de TabsJson)/Modal (seccion normal, TODO dialogo); documento
  Paragraph/Divider/Spacer; multimedia firma/foto/gps/archivo/barras como placeholder
  del prototipo DESHABILITADO ("captura disponible proximamente") que NO bloquea el
  submit (Required ignorado en cliente Y servidor, mismo FormFieldValidator); TABLA
  (GridDetail) FUNCIONAL: filas dinamicas agregar/quitar, celdas por columna, persiste
  arreglo JSON de filas en el documento de la respuesta, Required = al menos 1 fila;
  placeholder (PlaceholderText) y valor por defecto (DefaultValue) en los inputs.
- INDICE /formularios reescrito nanometrico (Formularios.razor + .razor.css): rotulo
  "MODULO 000131 - AUTOMATIZACION", h1 28/800/-.03em, subtitulo literal, boton
  "Nuevo formulario" 40px brand (crea FRM-### siguiente y ABRE el constructor);
  4 KPIs reales (Formularios violet / Publicados green / Respuestas blue con separador
  de miles es / Con reglas amber; icono 42x42 r11, valor 19/800); busqueda 38px
  ("Buscar formulario..."); tabs de vista Tarjetas/Lista (32px r9, on: surface+sh-sm);
  tarjetas auto-fill minmax(300px,1fr) r18 p18 hover -2px con icono form 38x38
  violet-bg, badge de estado (Publicado green / Borrador amber / Archivado gris /
  Inactivo gris), nombre 15.5/700, FORX-{code} monospace 11 + badge categoria
  ("General", deuda: sin campo Categoria), grid 3 stats (15/800, reglas amber);
  lista grid 2fr 1fr .8fr 1fr .9fr con header 11/600 y hover surface-2.
- CONSTRUCTOR reescrito (FormDesigner.razor + .razor.css) como el modal grande del
  prototipo: overlay fixed z60 blur, shell 95vh r18 sh-lg; header 14-20 con back 34px,
  chip FORX-{code}, titulo 15.5/700 editable inline (UpdateHeaderAsync), badge estado,
  Vista previa / Activar-Desactivar / Publicar por URL (modal de tokens intacto de la
  ola 2) / Guardar brand / Cerrar. Grid 276px 1fr 320px. IZQUIERDA: paleta
  "1 ELEMENTOS" (Contenedor violet primario Fila, Input blue primario Texto corto) y
  "2 DOCUMENTO" (Texto/Divisor/Espacio amber), cards p11 r12 icono 34 r9 grip dots,
  draggable; "3 ESTRUCTURA" arbol en vivo con raiz "Formulario" brand-soft, chevrons
  colapsables, badge N/12 o conteo, lock (brand) y eye (danger) por nodo persistidos.
  CENTRO: device tabs Navegador 820 / Tableta 620 / Movil 380 (32px r9) + "A4 -
  820x1180" monospace; hoja r16 sh-md p26 con grilla 12 gap 12; render recursivo con
  label uppercase 9.5/700 del color del tipo, "Contenedor vacio - arrastra o toca un
  elemento", previews exactos por tipo (texto 38px, area 58, lista chevron, fecha
  dd/mm/aaaa, numerico 0, sino Si/No, tabla con columnas, firma/foto/gps/archivo/
  barras), seleccion border brand + ring brand-soft + acciones flotantes top -14
  right 10 (subir/bajar/duplicar recursivo/eliminar). DERECHA: "4 PROPIEDADES" con
  icono+nombre+tipo y tabs Diseno (tipo de elemento, etiqueta, selector de ancho de
  12 botones con "N col" + %, contenido de parrafo, alto de espacio, chips de
  pestanas, toggles Fijo/Oculto) / Datos (nombre interno FORX_DATA readonly, tipo de
  respuesta readonly, opciones chips blue, columnas tabla chips amber, texto de
  ayuda, valor por defecto, obligatorio) / Reglas (vinculos FormFieldRule REALES:
  verbo monospace + "Al cambiar", "Sin reglas asignadas" dashed, "+ Agregar regla"
  con picker documento->regla del catalogo del tenant, X desvincula) + "Eliminar
  elemento" danger. PERSISTE-POR-CAMBIO (cada mutacion llama al servicio y recarga;
  Guardar confirma con flash "Guardado"). DnD nativo: paleta->lienzo/contenedor y
  nodo->posicion (MoveTo*). Vista previa = renderer REAL (Fill si Active, Design si
  borrador) dentro del constructor.
- SEEDS: EnsureFormBuilderDemoAsync (FRM-002 "Inventario fisico bodega" BORRADOR;
  FRM-003 "Visita tecnica de instalacion" ACTIVO con Row (CC 3/12 + Nombres 5/12 +
  Fecha 4/12), Col Observaciones, Section Equipos con TABLA de 3 columnas y firma
  placeholder). Idempotente por Code, invocado en Program.cs.
- DOCS: ADR-0021 (docs/decisiones/0021-constructor-formularios.md) con el mapeo
  prototipo->enum completo, decisiones Paragraph/Spacer via DefaultValue, multimedia
  placeholder sin bloqueo y persistencia por cambio.

**Validacion**:
- Build Ecorex.sln 0 errores; dotnet format sin hallazgos en archivos de esta sesion
  (quedan 5 WHITESPACE PRE-EXISTENTES en Program.cs:53-54 y E2eDbBackdoor.cs:96-98,
  ajenos a este cambio).
- Unit: Application.Tests 130/130 (incluye nuevos: doc non-input, multimedia ignora
  Required, GridDetail filas/JSON) y Domain.Tests 35/35.
- Integracion DUAL COMPLETA (Testcontainers PG16 + SQL Server 2022): 105/105 verdes,
  con 2 tests nuevos x2 motores (BuilderFields_RoundTrip_WidthSyncAndContainers:
  round-trip Width/GridCol/TabsJson/IsHidden/IsLocked, derivacion legacy col-md-4,
  Required apagado en firma, MoveTo con ciclos prohibidos; GridDetail_SubmitRoundTrip:
  tabla requerida bloquea vacia, filas JSON identicas al leer, oculto requerido NO
  valida). Nota: jsonb de PG normaliza el formato del JSON (assert por contenido).
- E2E SUITE COMPLETA contra la app real (PG 5442, puerto 5248): 13/13 verdes,
  incluyendo el escenario NUEVO FormBuilderTests (indice -> Nuevo formulario ->
  constructor -> campo texto + lista con 2 opciones default -> Activar -> Vista
  previa en Fill -> submit valido "Enviado") y PublicFormTokenTests actualizado a los
  selectores nuevos del indice (.fx-card/.fx-code, tarjeta completa abre el
  constructor).
- Manual en navegador (localhost:5248, claro Y oscuro): indice (h1 28/800, KPI icono
  42x42 y valor 19/800, tarjetas sh-sm con stats reales 8 campos/11 respuestas/
  2 reglas de FRM-001, vista lista de 5 columnas con headers exactos, dark
  surface #161618); constructor de FRM-003 (grid EXACTO 276px/1fr/320px, 5 cards de
  paleta, arbol de 9 nodos con badges 3/12 5/12 4/12, seleccion con ring + 4 acciones
  flotantes, props CC/Texto con 12 botones de ancho y tabs Diseno/Datos/Reglas, tab
  Datos de la tabla con chips Equipo/Serial/Cantidad, dark ok); vista previa Fill con
  tabla funcional (agregar fila -> 3 celdas), firma "captura disponible proximamente"
  y Enviar visible.
- Procesos DETENIDOS al terminar (app :5248 del preview). launch.json gano config
  superadmin-5248.

**Deudas / TODO**:
- Modal como dialogo real en el renderer (hoy seccion normal, TODO en ADR-0021).
- Captura real de firma/foto/gps/archivo/barras (hoy placeholder deshabilitado).
- Campo Categoria en FormDefinition (badge fijo "General", sin tabs de categoria).
- Intercalado libre de preguntas y contenedores en una sola secuencia (hoy preguntas
  primero, luego sub-contenedores, por SortOrder por grupo).
- Celdas tipadas por columna en la tabla (hoy texto).
- WHITESPACE pre-existente en Program.cs y E2eDbBackdoor.cs (ajeno a esta sesion).
- Sin commit (pedido explicito): cambios en working tree.

---

## 2026-07-04 - Sesion: Modulo FLUJOS fiel al prototipo + editor canvas funcional (ADR-0022)

**Agentes**: coordinador + 2 exploradores (backend workflow / patrones UI SuperAdmin).

**Hecho**:
- INDICE /flujos (reemplaza el stub): rotulo "MODULO 000291 - AUTOMATIZACION",
  h1 28/800, boton "Nuevo flujo", 4 KPIs del prototipo (Flujos violet / En marcha
  green / Instancias activas blue / Ejecuciones (mes) amber; icono 42x42 r11, valor
  19/800), busqueda + tabs de filtro por cargo/categoria (surface-2 p4 r11, contador
  10.5 op .7) y tarjetas auto-fill minmax(330px,1fr) r18 p18 hover -2px con ID
  monospace, badge de estado con dot pulsante 1.4s si hay instancias Running, badge
  de categoria + "N nodos" y grid de metricas REALES en-marcha(azul)/ejecuciones/
  exito(verde) 16/800. Con modal "Nuevo flujo" (nombre + categoria; estado fijo
  Borrador) que crea el borrador minimo Inicio->Fin y abre el editor.
- EDITOR canvas PROPIO del prototipo (flowEditorOpen; SIN bpmn-js, cero JS externo):
  modal 95vh grid 1fr/340px; header con FLUJO {code} vN, nombre inline, select de
  categoria, Propiedades/Importar/Exportar/Publicar/Guardar cambios/Cerrar; toolbar
  flotante 38x38 r9 (sel/conn/task/event verde/gw ambar/del rojo); canvas 900x540
  surface-2 con puntos radial-gradient; nodos absolutos por tipo (start/end circulo
  border 3px, gateway diamante rotate45 warn con texto -45, task rect r12) con
  seleccion ring 4px brand-soft y cursor por herramienta; aristas SVG ortogonales
  H-V-H con markers (condicionales = brand dashed 7 5); stats "N nodos - M
  conexiones" + hint contextual; drag con pointer events (throttle ~30fps, persiste
  al soltar); panel DETALLE DE ACTIVIDAD con 6 acordeones FUNCIONALES:
  Configuracion basica (tipo + RestartNodeId reusando SetRestartTargetAsync +
  AllowsAssignment), Asignar usuarios (placeholder documentado PERMISO_CARGO),
  Recursos (WorkflowNodeForm real: picker de formularios ACTIVOS, chip con x),
  Reglas (WorkflowNodeRule real contra el catalogo con toggle autonoma y x),
  Notificacion (placeholder TODO), Reglas de salida (edita ConditionExpression de
  aristas salientes "condicion -> destino" + borrar arista); "Saltar a otro flujo"
  (modal de seleccion; vinculo real = deuda); modales Propiedades (nombre/categoria/
  estado con transiciones publicar/pausar/reanudar/descripcion) y Export/Import JSON
  del prototipo (pre monospace + Copiar; textarea + importar -> nueva version
  Borrador).
- MODELO aditivo + UNA migracion dual `AddWorkflowEditorFields` (PG 20260704160822 /
  MSSQL 20260704160855, APLICADAS y verificadas en 5442/1443): WorkflowDefinition +=
  Category(100?), IsPaused(default false); WorkflowNode += X, Y (default 0), W?, H?.
- Motor: BpmnProcessParser lee bpmndi Bounds (X/Y/W/H, redondeo AwayFromZero);
  ImportBpmnAsync llena layout (auto-layout BFS determinista si no hay DI);
  StartInstanceAsync rechaza definiciones pausadas. BpmnXmlWriter NUEVO genera
  process + bpmndi + condiciones estandar (round-trip garantizado por test).
- IWorkflowDesignService NUEVO (Application/Workflows): ListForIndexAsync (una
  tarjeta por ProcessCode; formula documentada: exito = Completed/(Completed+Stuck+
  Cancelled)%, ejecuciones = iniciadas en mes UTC), GetCanvasAsync, CreateDraftAsync,
  EnsureDraftAsync (publicada -> version borrador max+1 REUTILIZABLE via
  ImportBpmnAsync, copiando Category/reinicios/AllowsAssignment/vinculos por
  BpmnElementId), AddNode/Move/Rename/Connect (sin duplicados ni self-loop)/
  DeleteNode (protege startEvent; limpia aristas, vinculos y reinicios)/DeleteEdge/
  SetEdgeCondition/SetNodeConfig, UpdateDefinitionProps, Pause/Resume,
  ExportJson/ImportJson, SetNodeForm (solo formularios Activos)/RemoveNodeForm,
  AddNodeRule/RemoveNodeRule/SetNodeRuleAutonomous, ListRuleCatalog. REGLA: grafo
  editable SOLO en borradores; cada mutacion REGENERA BpmnXml con el layout
  (portabilidad bpmn.io del ADR-0014).
- Seeder EnsureWorkflowIndexDemoAsync: backfill de layout+XML para definiciones
  pre-editor (COT-COM + categoria "Comercial"), borrador demo "Mantenimiento y
  soporte" (FLW-001, construido con el PROPIO design service) y "Visita tecnica de
  instalacion" (VIS-TEC) publicada y PAUSADA. Sin instancias nuevas (metricas 0 ok).
- ADR-0022 (canvas propio vs bpmn-js, XML regenerado con DI, edicion solo borrador,
  formula de metricas, deudas).

**Validacion**:
- Build Ecorex.sln 0 errores; dotnet format --verify-no-changes limpio.
- Unit: 136/136 verdes (nuevos BpmnXmlWriterTests: round-trip write->parse, doble
  vuelta estable, condiciones xsi:type, saneo de ProcessCode, auto-layout
  determinista, parser de Bounds).
- Integracion DUAL completa verde (PG + SQL Server via Testcontainers); nuevos
  WorkflowDesignServiceTests 6x2: pausa bloquea StartInstance (y resume rehabilita),
  DeleteNode protege startEvent, mutaciones solo en borrador + EnsureDraft reutiliza
  versionado, editor persiste y regenera XML reimportable por el motor, export/
  import JSON crea version borrador con el mismo grafo y layout, indice con metricas
  reales (Running/mes/exito 0->100%).
- E2E Playwright 14/14 verde contra app real (PG 5442, puerto 5249), +1 escenario
  FlowsEditorTests: /flujos -> tarjetas (COT-COM "En marcha") -> Nuevo flujo ->
  editor -> agregar tarea -> renombrar -> conectar (Inicio->tarea) -> guardar ->
  cerrar (tarjeta "3 nodos" Borrador) -> REABRIR y verificar persistencia.
- Verificacion manual claro/oscuro contra el fuente (tokens surface/ink/line, KPI
  42x42, tarjeta r18 p18, dot ec-pulse 1.4s, canvas punteado, nodos por tipo, aristas
  dashed brand en condicionales, panel 340px, banner solo-lectura en publicadas con
  "Editar (crear version borrador)").
- Fix visual detectado por el E2E: .fe-head con flex-wrap para que las acciones no
  desborden bajo el panel derecho en anchos medios.
- Procesos DETENIDOS al terminar (app :5249 del preview). launch.json gano config
  superadmin-5249.

**Deudas / TODO**:
- Asignar usuarios por nodo: placeholder "TODO cargo/ACL por nodo (PERMISO_CARGO del
  vault)" hasta dependencias (000850); no se agrego AssigneesJson especulativo.
- Reglas de notificacion por nodo: chips ilustrativos (motor de notificaciones
  pendiente).
- "Saltar a otro flujo": seleccion visual; call activity/subprocess sin modelo en el
  motor.
- Ejecuciones (mes) en UTC (falta TZ de tenant); borrar el ultimo endEvent deja
  borrador no importable (solo el startEvent tiene proteccion dura).
- Sin commit (pedido explicito): cambios en working tree.

## 2026-07-04 - Sesion: Modulo GESTION DE REGLAS fiel al concepto por-modulo (ADR-0023)

**Agentes**: agente unico (exploracion + backend + UI + suites).

**Hecho**:
- /reglas REESCRITA como el concepto de Capa 6 (proto_gen_reglas.html): layout
  PERMANENTE de 3 paneles (lista 320px / editor 1fr / propiedades 300px, alto
  restante de la ventana), sin modales de edicion. ESTRUCTURA y MEDIDAS del proto
  con TOKENS del workspace (ADR-0023: accent->brand, success->ok, danger->danger,
  info->t-blue, warn-banner->t-amber-bg; code-bg oscuro #1F2937 FIJO en ambos
  temas). CSS scoped Reglas.razor.css (prefijo rg-).
- TOPBAR del modulo: breadcrumb Home/General/Reglas de negocio + badge "MOD 000802"
  (monospace 11/600, brand-soft) + acciones: Importar XML (deshabilitado, tooltip
  "Proximamente"), Probar (= ejecutar prueba de la seleccionada), Documentos (modal
  con el CRUD existente de RuleDocument: crear/renombrar/archivar) y + Nueva regla.
- KPIs REALES (42x42 r11, valor 19/800): Documentos, Reglas, Ejecuciones (30d) y
  Tasa de exito (30d) desde RuleExecutionLog (GetTenantStatsAsync).
- PANEL IZQUIERDO: lista PLANA de reglas del tenant (documento como categoria),
  titulo "Reglas (N)" + Filtrar (popover documento/categoria/estado/modo + ver
  archivados), buscador "nombre, tabla, proceso"; .rule-item con barra 4px por
  verbo (t-* rotando; gris si Inactiva), badge Activa/Inactiva/Desarrollo,
  mode-chip ENSAMBLADO monospace + dot + categoria; activo = brand-soft + borde
  izq 3px brand.
- EDITOR CENTRAL: cabecera 20/600 + badge + descripcion muted; acciones Duplicar
  (DuplicateRuleAsync: clona en el mismo documento, nombre " (copia)", SortOrder
  max+1, nace Development SIN vinculos), Eliminar (confirm inline; si tiene
  historial se INACTIVA con mensaje claro, append-only ADR-0016) y Guardar. Tabs
  Configuracion / Historial (contador) / Consumidores (contador).
- CONFIGURACION: warn-banner ambar SIEMPRE visible (mDATA/Execute no implementados,
  ADR-0016); grid 160px/1fr: Nombre, Descripcion (Rule.Description YA existia: SIN
  migracion), Modo ejecucion (Ensamblado seleccionable; Execute/mDATA disabled),
  Documento (select de RuleDocument; cambiarlo MUEVE la regla:
  SaveRuleRequest.DocumentId), Prioridad (SortOrder, max-width 100px), Verbo
  (catalogo tipado) y Estado.
- PARAM_XML: editor de codigo oscuro EDITABLE (textarea transparente sobre pre
  resaltado en C#, sin JS; tags #93C5FD, atributos #F0ABFC, strings #86EFAC,
  comentarios gris italic) con Formatear (re-indenta) y Validar (parsea contra el
  descriptor con errores claros y vuelca los valores a la vista renderizada).
  RuleParamXml NUEVO (clase PURA, Application/Rules): Generate/Parse/Format del
  contrato <REGLA><PROCESO><PARAMETROS><PARAM name tipo obligatorio valor/>;
  REPRESENTACION del ParamsJson tipado, nada se ejecuta por reflexion. "Vista
  renderizada" = form dinamico por descriptor (fuente autoritativa al editar;
  regenera el XML) + boton "Ejecutar regla" (guarda y corre; registra al usuario
  actual via GetCurrentTenantUserIdAsync).
- Historial reciente (4 .history-item con dot verde/rojo/ambar 8px + "Hoy HH:mm -
  Nms - usuario/error") + "Ver todo" -> tab Historial (lista completa paginada 25
  con "Mostrar mas"). Status strip: "Guardado hace X - ultima edicion {usuario}"
  (UpdatedAt/UpdatedBy reales via GetRuleAuditAsync; la "version" del proto se
  OMITE: Rule no esta versionada, deuda declarada).
- CONSUMIDORES: vinculos REALES con badges Formulario (t-blue: titulo + FieldCode)
  y Flujo (t-violet: nombre + nodo + autonoma), vacio dashed, quitar y agregar con
  los selectores existentes.
- PANEL DERECHO Propiedades: ID Regla (chip mono DocumentCode-8xGuid), Documento,
  Verbo; Ejecuciones (30d) 20/600, Tasa exito badge, Tiempo promedio ms
  (GetRuleMetricsAsync; tasa = Success/(Success+Failed), Skipped no cuenta);
  consumidores resumidos; Creada y Ultima modificacion (fecha + hace X).
- Backend aditivo (SIN migracion): ListAllRulesAsync, GetRuleAsync,
  DuplicateRuleAsync, GetTenantStatsAsync, GetRuleMetricsAsync, GetRuleAuditAsync,
  GetCurrentTenantUserIdAsync, SaveRuleRequest.DocumentId (mover),
  RuleExecutionLogDto.ExecutedByName (nombre del ejecutor en historial).
- ADR-0023 (tokens del workspace sobre paleta naranja del concepto, PARAM_XML como
  representacion editable, Execute/mDATA visibles pero deshabilitados por ADR-0016,
  eliminar->inactivar con historial, documentos en modal del topbar).

**Validacion**:
- Build Ecorex.sln 0 errores; dotnet format --verify-no-changes limpio.
- Unit: Domain 35/35 + Application 154/154 verdes (18 nuevos RuleParamXmlTests:
  round-trip por tipo -texto con <>&", numeric, boolean, fieldcode, json- +
  descriptor REAL de PASAR_CAMPOS, y errores: XML malformado, raiz/proceso
  invalidos, parametro desconocido/repetido, tipos invalidos, obligatorio
  faltante, case-insensitive con nombres canonicos, Format).
- Integracion COMPLETA dual verde 121/121 (PG + SQL Server via Testcontainers);
  nuevos: metricas 30d (ventana excluye ejecuciones viejas; tasa 0.5/1.0/0.0;
  lista plana con documento; archivado filtra; ExecutedByName) y duplicar (mismo
  documento, sin vinculos, Development, params semanticamente iguales -jsonb
  normaliza-, NotFound; mover de documento OK y NotFound con destino falso).
  Inactivar-con-historial (DeleteRule Invalid) INTACTO.
- E2E Playwright 15/15 verde contra app real (PG 5442, puerto 5250); +1 escenario
  ReglasTests: layout 3 paneles + 4 KPIs + Importar XML disabled -> + Nueva regla
  (NOTIFICAR con message) -> seleccionar en el sidebar -> editar prioridad ->
  Validar XML (rg-ok) -> Ejecutar regla (Exito) -> entrada en Historial reciente
  (Manual/Exito) -> prioridad persistida -> tab Historial la muestra.
- Verificacion manual claro/oscuro contra el proto (preview 5236): grid
  320px/1fr/300px, titulo 20/600, rule-item activo brand-soft + 13px, code editor
  #1F2937 con overlay textarea/pre alineado 1:1 (misma altura y metrica), KPI
  42x42 r11, props con datos reales (RUL-005-XXXXXXXX, 17 ejec, 100%, 1 ms),
  Validar OK y error claro ("'sql' no existe en el verbo PASAR_CAMPOS"),
  Documentos modal, tabs con contadores reales; en dark los tokens conmutan
  (surface #161618, brand-soft rgba blanca, t-amber-bg) y el code-bg queda fijo.
  Fix por verificacion: la vista renderizada va a la DERECHA del codigo solo
  >=1780px (antes estrangulaba el editor a 236px); debajo en anchos menores.
- Procesos DETENIDOS (preview 5236 y app legacy 5234 del worktree .preview;
  puertos 5234/5236/5250 libres).

**Deudas / TODO**:
- Importar XML: boton deshabilitado (sin formato definido).
- Version de la definicion de regla: no existe en el modelo (status strip la omite).
- Valores json del PARAM_XML viajan en atributo valor= con &quot; (valido, ruidoso).
- Historial por regla: corte en 500 filas en memoria (paginacion server-side si
  una regla supera eso dentro del TTL de 90d).
- Filtro por modo Execute/mDATA devuelve vacio a proposito (no hay campo de modo).
- Sin commit (pedido explicito): cambios en working tree.

---

## 2026-07-04 (sesion aparte) - Modulo CONCEPTOS (000270): /conceptos real sobre ActivityType

**Agente**: Claude Code (Fable 5). **Fuentes**: proto_tar_conceptos.html +
NEWFRONT_tar_conceptos (spec Capa 6). **Regla**: SIN migraciones; NO tocar
Lead/Pipeline (otro agente en paralelo con el Cargador de contactos). Sin commit.

**Hecho**:
- Pagina real `/conceptos` (Conceptos.razor + .razor.css) que reemplaza al
  placeholder /modulo/conceptos: ESTRUCTURA y MEDIDAS del proto con TOKENS del
  workspace (misma decision que /reglas: ADR-0023 -> ADR-0024 nuevo). Topbar
  breadcrumb + MOD 000270 + Exportar (disabled Pendiente) + "+ Nuevo concepto";
  tabs Actividades/Detalle; split 340px/1fr: lista de categorias (buscador,
  iconos con rotacion --t-*, conteo, estado) y detalle con KPIs reales
  (conceptos activos, tareas abiertas, con flujo, con formulario), filtros
  (estado / con-sin flujo) y grid (codigo derivado CN-XXXXXXXX de los ULTIMOS
  8 del Guid, proceso vinculado, formulario, orden con flechas subir/bajar,
  badges Activo/Archivado, editar/archivar). Tab Detalle = grid maestro
  Categoria x Concepto con conteo de tareas (analogo CANT_USADO) y filtros.
- Modal de concepto (860px, 6 acordeones como el proto): Datos basicos (nombre,
  categoria -select + "(nueva categoria...)"-, descripcion, orden) REALES;
  proceso vinculado = select de flujos PUBLICADOS (WorkflowDefinitionId real,
  validado en servicio); "Requiere formulario" = RequiresForm real; el resto de
  la spec sin respaldo en el modelo queda VISIBLE DESHABILITADO con tooltip
  "Pendiente" (ver gaps). Eliminar con confirm inline: en uso -> archiva (regla
  existente de DeleteAsync), sin uso -> borra.
- Categorias como agrupador string (no hay entidad): nueva categoria = pendiente
  local que persiste con su primer concepto; Renombrar = RenameCategoryAsync
  (mueve todo validando colisiones); Archivar categoria = SetCategoryArchivedAsync
  (FLAG_INA de TIPO_TAR).
- Backend aditivo SIN migracion (IActivityTypeService): Create/UpdateRequest ganan
  WorkflowDefinitionId + RequiresForm opcionales (compatibles); nuevos
  ListWorkflowOptionsAsync (solo publicados no archivados), GetUsageAsync (total/
  abiertas por tipo), SetArchivedAsync (Invalid en doble toggle),
  RenameCategoryAsync, SetCategoryArchivedAsync y MoveAsync (permuta SortOrder
  con el vecino normalizando empates, 1 SaveChanges). Validacion: flujo no
  publicado/inexistente -> Invalid tipado (la FK es NO ACTION).
- NavMenu: SOLO el item Conceptos (000270) pasa de modulo/conceptos a /conceptos
  (+ GroupRoutes para abrir el acordeon); registro del placeholder retirado de
  Modulo.razor. Policy nueva `Conceptos.Editar` (paso 1, claim tenant_id).
- ADR-0024 (docs/decisiones): tokens sobre paleta teal, jerarquia TIPO_TAR/
  TIPO_TAR_R proyectada sobre ActivityType.Category, gaps deshabilitados sin
  migrar, proceso vinculado 1:0..1 validado contra publicados.

**GAPS de la spec SIN respaldo en ActivityType (NO se migro; decide coordinador)**:
Code visible (se muestra derivado del Guid), IconClass, sedes/empresas por
concepto (TIPO_TAR_EMPRESA), RQ07 completo (FLAG_INICIA_MODULO, FLAG_BOTON_CIERRE,
TITULO_AUTO, DETALLE_AUTO), FLAG_CLIENTE, lista de chequeo (CHEQUEO),
FormDefinitionId especifico + modo (solo existe bool RequiresForm), procesos N:M
(TIPO_TAR_R_PRO; hoy 1:0..1), nodo inicial, permisos por cargo/usuario,
notificaciones por concepto (TIPO_TAR_N/NR), componentes fijos y formacion.
Todos visibles deshabilitados con tooltip "Pendiente" en el modal.

**Validacion**:
- Build Ecorex.sln 0 errores; dotnet format --verify-no-changes limpio.
- Unit verdes: Domain 35/35, Application 169/169 (sin unit nuevos: la logica
  nueva es EF y va en integracion dual).
- Integracion dual: 12/12 nuevos verdes (ActivityTypeCatalogTests x PG + SQL
  Server: flujo publicado/borrador/inexistente, archivar/restaurar + doble
  toggle Invalid, renombrar categoria con colision y NotFound, archivar
  categoria idempotente, mover orden con extremos Invalid, conteos de uso +
  delete-en-uso archiva). Suite completa 135/137: los 2 fallos son de
  ContactLoaderTests (trabajo EN CURSO del otro agente, no de este modulo).
- E2E Playwright 17/17 verde contra app real (PG 5442; el fixture tomo el
  primer puerto libre 525x, el 5252 estaba ocupado por la app del otro agente);
  +1 escenario ConceptosTests: abrir /conceptos (split + MOD 000270 + Exportar
  disabled) -> + Nuevo concepto (Codigo disabled = gap declarado) -> fila en el
  grid de Direccion Comercial (badge Activo) -> tab Detalle lo muestra -> el
  combo "Tipo de actividad" del wizard de actividades lo ofrece y selecciona.
- Verificacion manual claro/oscuro contra el proto (preview 5241/5251): topbar
  14x24, container 1400/20x24x60, tabs 10x18 borde 2px, split 340px/1fr gap16,
  th 10x16 11.5/600 upper, KPI valor 20/600, icono 32x32 r6, modal 860 r10 con
  field-row 160px/1fr, 6 acordeones y 11 controles Pendiente disabled, select
  con los 2 flujos publicados reales; CRUD por UI (crear, mover orden, archivar,
  eliminar) y NavMenu activo con acordeon abierto. En dark los tokens conmutan
  (bg #0A0A0B, surface #161618, badges --t-*-bg rgba). Fix por verificacion:
  el codigo derivado usa los ULTIMOS 8 del Guid (los primeros 8 de un Guid v7
  son timestamp y colisionaban visualmente entre filas creadas juntas).
- Procesos propios DETENIDOS (previews 5241 y 5251, app E2E auto-terminada,
  watchers cancelados). Quedan corriendo procesos AJENOS: 5234 (worktree
  .preview) y 5252 (otro agente) - no se tocaron.

**Deudas / TODO**:
- Los gaps de modelo de arriba (migracion pendiente de decision del coordinador).
- Exportar: boton deshabilitado (sin formato definido).
- Check-all/borrado masivo de sub-categorias del legacy: omitido a proposito
  (archivado por fila + borrado en modal).
- Sin commit (pedido explicito): cambios en working tree.

## 2026-07-05 (sesion aparte) - Modulo CARGADOR DE CONTACTOS (000873): /cargador-contactos real sobre el CRM (ADR-0024)

**Agentes**: agente unico (fuentes + backend + migracion dual + UI + suites), en
paralelo con el agente de /conceptos (sin tocar ActivityType; esta sesion tenia la
exclusividad de migraciones de la ola).

**Fuentes leidas**: proto_contact_loader.html (concepto Capa 6) y la spec
"comer_ContactLoader - Spec para reconstruir". OJO: la spec documenta que el modulo
legacy NO carga archivos (es un explorador de contactos scrapeados por N8N); el
requerimiento de esta ola pide un IMPORTADOR masivo sobre el CRM real, y asi se
implemento (reencuadre registrado en ADR-0024).

**Hecho**:
- Pagina /cargador-contactos NUEVA (CargadorContactos.razor + css scoped prefijo
  cl-): ESTRUCTURA y MEDIDAS del proto con TOKENS del workspace (ADR-0023/0024):
  topbar 14px/24px con breadcrumb + chip MOD 000873 + acciones (Plantilla CSV via
  data-url, Ver pipeline, Cargar N validas), layout 300px/1fr gap 16, sidebar
  sticky top 75 (archivo + mapeo de columnas + historial de cargas), 4 KPIs
  (icono 36x36 r8, valor 20/600): filas/validas/duplicadas/invalidas, tabs con
  borde inferior 2px (Previsualizacion/Errores/Resultado), grilla con avatares 40
  redondos por tono, badges mini por fila (Valida/Duplicada/Invalida), footer de
  paginacion (30 por pagina, el PageSize del legacy), responsive <=1000px.
- Flujo funcional end-to-end: InputFile (solo CSV, max 2 MB) -> CsvTableParser ->
  ContactColumnMapping.AutoMap (sinonimos ES/EN por encabezado, editable en el
  panel) -> ValidateAsync (previsualizacion con veredicto por fila) -> ImportAsync
  (carga TRANSACCIONAL: Lead en la PRIMERA etapa del pipeline asignado al
  importador + LeadActivity "lead.imported" + ContactImportBatch, rollback total)
  -> pestana Resultado con conteos + historial en el sidebar. La pagina llama
  PipelineSvc.EnsureDefaultsAsync igual que /pipeline.
- Application: CsvTableParser PURO (autodeteccion coma/punto y coma/tab fuera de
  comillas, RFC 4180 con "" y saltos de linea internos, BOM, lineas vacias fuera,
  filas rotas reportadas con numero de linea fisico), ContactLoaderDtos,
  IContactLoaderService + ContactLoaderService (validacion: nombre obligatorio
  <=200, email regex, telefono >=7 digitos, valor con miles/decimales tolerantes;
  dedup por telefono -ultimos 10 digitos, tolera prefijo pais- o email -de
  FieldValuesJson.email- contra los leads del tenant Y contra filas anteriores del
  archivo). Email/empresa van a FieldValuesJson (el Lead real no tiene columnas).
- Dominio + DAL dual: entidad ContactImportBatch (TenantEntity: FileName,
  TotalRows, Inserted, Duplicates, Invalid; CreatedBy/At del interceptor), DbSet +
  configuracion (indice tenant+created_at) y UNA migracion dual AddContactImports
  (Ecorex.Infrastructure 20260705022348 + Ecorex.Infrastructure.SqlServer
  20260705022429) APLICADA y verificada en los contenedores dev (PG 5442 \d y
  MSSQL 1443 sys.tables).
- NavMenu: SOLO el item "Cargador de contactos" (000740) paso de href=pipeline a
  href=cargador-contactos (una linea).
- ADR-0024 (docs/decisiones/0024-cargador-contactos.md): reencuadre del modulo,
  CSV primero (sin libreria Excel en la solucion), reglas de dedup, transaccion.

**Validacion**:
- Build Ecorex.sln 0 errores; dotnet format --verify-no-changes limpio.
- Unit: Application 169/169 verdes (15 nuevos CsvTableParserTests: delimitadores
  autodetectados -incluido delimitador dentro de comillas-, comillas escapadas,
  salto de linea dentro de campo con numeracion fisica, filas rotas por conteo de
  columnas, archivo vacio/null, lineas en blanco, CRLF+BOM, encabezado vacio
  posicional, ultima fila sin salto final, AutoMap ES y desconocidos). Domain
  35/35.
- Integracion COMPLETA dual verde 137/137 (PG + SQL Server via Testcontainers);
  +4 nuevos (2 tests x 2 motores) ContactLoaderTests: carga valida con duplicados
  detectados (CSV real por el parser: 2 insertadas con etapa/asignacion/
  FieldValuesJson/actividad + batch con conteos exactos, dup por telefono con
  prefijo +57 contra lead existente, dup por email dentro del archivo, invalidas
  por nombre vacio y email malformado, e idempotencia al recargar: 0 insertadas)
  y aislamiento cross-tenant del historial + de la deteccion de duplicados (el
  telefono cargado por A no es duplicado en B; cada tenant ve solo su batch).
  NOTA: una corrida con integracion+E2E+app en paralelo dio 7 flakes de arranque
  del contenedor MSSQL (WaitUntil timeout); la suite sola es 137/137.
- E2E Playwright COMPLETA verde 17/17 contra app real (PG 5442, puerto 5252 via
  ECOREX_E2E_BASEURL); +1 escenario CargadorContactosTests: generar CSV en el
  test (2 validas + 1 duplicada en archivo + 1 sin nombre) -> subirlo por el
  InputFile -> KPIs 4/2/1/1 -> mapeo automatico 6 de 6 -> badges dup/bad con
  motivo -> Cargar 2 validas -> Resultado (2/1/1) -> historial en el sidebar ->
  los 2 leads visibles en /pipeline. (ReglasTests fallo una vez por contencion
  al correr todo en paralelo; en la corrida limpia paso.)
- Verificacion manual claro/oscuro contra el proto (preview 5252): layout
  300px/744px gap 16 padding 16/20/60, topbar 14/24, sidebar sticky 75, KPI 36x36
  r8 y valor 20/600, tab activa borde 2px brand; carga manual de un CSV de 5
  filas via DataTransfer: KPIs 5/2/1/2, badges por fila, import real (flash
  "Carga completada: 2 insertadas, 1 duplicadas, 2 invalidas", batch en el
  historial, leads en /pipeline); en dark los tokens conmutan (bg #0A0A0B,
  surface #161618, ink #F4F4F5, brand invertido, tonos rgba translucidos).
- Procesos DETENIDOS (app preview/E2E 5252 parada; 5250/5252 sin listeners).

**Deudas / TODO**:
- Soporte Excel (.xlsx): no hay libreria referenciada en la solucion; queda
  documentado en la UI ("Soporte de Excel (.xlsx): pendiente") y en ADR-0024.
- Dedup solo por telefono/email: filas sin ambos no tienen clave y siempre entran.
- EvaluateRows carga los pares telefono/email de TODOS los leads del tenant en
  memoria por carga (aceptable hoy; si un tenant supera decenas de miles de leads
  conviene un indice/consulta dedicada).
- Limite de archivo 2 MB del InputFile (configurable si hace falta).
- Explorador N8N del legacy (fuentes LinkedIn/Maps, filtros dinamicos, presets):
  NO es este modulo; si se migra la ingesta scraper sera un modulo aparte.
- Sin commit (pedido explicito): cambios en working tree.

## 2026-07-04 (sesion aparte) - LOGIN/AUTH: presentacion visual alineada al prototipo maestro

**Que se hizo**:
- Rediseno de las pantallas de autenticacion (login, registro autogestion,
  recuperar, restablecer, activar) reemplazando el degradado morado heredado
  del backbone por el lenguaje del prototipo ECOREX.dc.html: split de dos
  zonas con aside de marca MUY sobrio (fondo --surface-2 + gradientes
  radiales sutiles de --brand-soft/--surface-3, tile cuadrado --ink con la
  inicial de la marca, subtitulo "Sistema de Tareas", titular corto y 3
  bullets de valor con iconos de linea: tareas/flujos BPMN/formularios) y
  panel derecho con la TARJETA del formulario (--surface, borde --line,
  radio --rad 20, sombra --sh-md). En movil (<=768px) colapsa a tarjeta
  centrada con la marca arriba. Tipografia Hanken Grotesk en todo el shell.
- NUEVO componente compartido Components/Shared/AuthShell.razor: aside de
  marca + tarjeta + footer para las 5 paginas de auth (parametros
  PlatformName/LogoUrl/Headline/Subtext/ShowBullets/ShellClass); el branding
  configurable (Marca) sigue mandando en logo/titular/subtexto. Login,
  Recuperar, Restablecer y Activar pasaron a usarlo (se elimino el aside
  duplicado por pagina); los estilos inline sueltos pasaron a clases
  (.auth-link, .auth-secondary).
- app.css: bloque .auth-* reescrito 100% con tokens del prototipo (labels
  11px/600 uppercase --ink-3; inputs 44px r12 borde --line-2 con focus ring
  --brand-soft; boton primario 44px r12 --brand/--on-brand hover opacity .9;
  Google/secundarios 44px r12 --surface hover --surface-2; alertas --danger
  sobre --t-rose-bg y --ok sobre --t-green-bg; transiciones 0.12-0.16s;
  focus-visible con outline --ink-2; overrides -webkit-autofill con
  --surface/--ink para matar el amarillo de Chrome tambien en dark). La
  vista previa del aside en /marca (.brand-preview-aside) se actualizo al
  mismo lenguaje sobrio (referenciaba el keyframe morado eliminado).
- Funcionalidad INTACTA: post a /auth/login-register-forgot-reset-activate,
  ids #login-email/#login-password y .auth-pane-login button.auth-submit
  (selectores de la suite E2E) sin cambios, mostrar/ocultar clave, switch
  login/signup (is-signup), boton Google condicionado a configuracion,
  hint de agencia para Google signup, mensajes de error/exito por query.
- Modo oscuro: sin colores duros; todo por tokens bajo html.dark (el script
  de tema de App.razor corre antes del CSS y aplica tambien al login).

**Validacion**:
- Build Ecorex.sln 0 errores; dotnet format --verify-no-changes limpio.
- App real contra PG 5442 en puerto 5254 + Playwright: /login claro y
  oscuro (dark forzado via localStorage ecorex-theme en init script:
  html.dark presente, boton rgb(27,27,30) en claro y rgb(244,244,245)
  invertido en dark, inputs 44px, Hanken Grotesk computada), viewport movil
  380px claro/oscuro (tarjeta centrada con tile arriba), /recuperar,
  /restablecer y /activar renderizan la tarjeta del nuevo shell, toggle
  MOSTRAR/OCULTAR funciona, switch a pane signup funciona, y el login con
  owner@sky-system.local / Demo123* aterriza en /inicio.
- Suite E2E COMPLETA verde 18/18 contra la app real (PG 5442, puerto 5254
  via ECOREX_E2E_BASEURL), selectores de login sin tocar.
- Procesos DETENIDOS (5254 sin listeners).

**Deudas / TODO**:
- AceptarInvitacion.razor usa el estilo viejo login-wrap/login-card (tarjeta
  suelta violeta del area admin), no el shell de auth; queda fuera del
  alcance de esta sesion.
- Sin commit (pedido explicito): cambios en working tree.

## 2026-07-07 - Sesion: Ola B2 - ENFORCEMENT REAL de permisos por rol (ADR-0033)

- Agente: Claude Opus 4.8 (claude-opus-4-8).
- Contexto: la Ola B1 (ADR-0032) dejo modelo + servicio + UI + resolucion efectiva,
  pero NO aplicaba los permisos. Esta ola los HACE CUMPLIR.

**Hecho**:
- Regla opt-in / back-compat: `EffectivePermissions` gana el eje `Unrestricted`.
  Owner/Admin -> AllowAll (implica Unrestricted); usuario SIN rol (o TenantUser no
  resoluble) -> Unrestricted (conserva el acceso del paso 1, NO se restringe;
  cambia respecto de B1 que devolvia Empty()); usuario CON rol -> sujeto a su
  matriz. `PermissionResolver.Resolve` y `RolService.ResolveEffectivePermissionsAsync`
  actualizados. Verificado: owner/admin/operator/viewer NO pierden acceso.
- Autorizacion dinamica (via NUEVA, aditiva): `PermissionRequirement` +
  `PermissionAuthorizationHandler` + `PermissionPolicyProvider` que materializa al
  vuelo las policies `Perm:{moduleKey}:{action}` (gate tenant_id + requirement) y
  DELEGA el resto en el DefaultAuthorizationPolicyProvider. Las policies clasicas
  (Inventario.Ver, TenantMember, ...) quedan intactas. Registrados en Program.cs.
- `ICurrentPermissions` (scoped, SuperAdmin/Auth): resuelve el set del usuario actual
  UNA vez por scope (en scope propio, como NavMenu), cachea, y es FAIL-OPEN
  (Unrestricted si no hay usuario o si la resolucion lanza) para no bloquear la consola.
- Menu filtrado por Ver: `MenuPermissionFilter` (Application, pure) poda Item con
  View=false y oculta secciones vacias; NavMenu lo aplica sobre el arbol resuelto.
- Paginas con enforcement real (policy `Perm:{route}:View` + botones gateados por
  Can Create/Edit/Delete): InventarioItems, catalogos de inventario
  (Bodegas/Marcas/Grupos/Subgrupos/Tipos), AdmUsuarios, RolesPermisos. El wiring del
  RESTO de modulos a `Perm:{route}:View` queda como follow-up mecanico (anotado en ADR).
- Seed "Asesor limitado" ajustado y DEMOSTRABLE (idempotente + reconcilia si ya existe):
  SIN Ver en Sistema-Desarrollo/Sistema-CRM/CRM-heredado; CON Ver en Mis Procesos/
  Inventarios/Automatizacion; Crear solo en tareas/proyectos (no inventario). Asignado
  a simple@sky-system.local.
- ADR-0033 creado.

**Pruebas** (dotnet build 0 errores; dotnet format --verify-no-changes limpio):
- Unit Application: 319/319 (incluye PermissionResolver Unrestricted + MenuPermissionFilter).
- Unit Domain: 35/35.
- Unit SuperAdmin (proyecto NUEVO Ecorex.SuperAdmin.Tests): 18/18
  (PermissionPolicy.TryParse, PermissionAuthorizationHandler, CurrentPermissions cache/fail-open).
- Integracion DUAL (RolesTests, PG + SQL Server): 24/24 (incluye 2 nuevos de menu
  filtrado: rol limitado excluye modulos sin Ver; Owner y sin-rol ven el menu completo).
- E2E: 8/8 del subconjunto de permisos/menu/usuarios/roles
  (PermissionEnforcementTests 3 nuevos + MenuProfile 2 + RolesPermisos 1 + AdmUsuarios 1
  + MenuEditor 1), verde contra la app real (PG 5442). Los E2E existentes de menu/
  usuarios/roles siguen verdes.

**Deudas / TODO**:
- Wiring del resto de paginas (Tareas, Proyectos, Flujos, Formularios, Reglas,
  Conceptos, ...) a su `Perm:{route}:View` (mecanico).
- Las policies clasicas migradas quedan sin uso en esas paginas pero se conservan por compat.
- El claim `Permissions` del token sigue sin poblarse (la consola resuelve por servicio).
- Sin commit ni push (pedido explicito): cambios en working tree.

## 2026-07-10 - Sesion: Inventario (000066) - campos por tipo + imagenes principal/texto + Datos tienda + vista Tarjetas/Lista

**Contexto**: el usuario tiene un proyecto hermano CUBOT.nails que le gusta como quedaron
los "items"; pidio llevar mejoras al INVENTARIO de ECOREX usando CUBOT.nails SOLO como
ejemplo. (Se habia trabajado por error en CUBOT.nails; se revirtio todo alli -
`git reset --hard origin/deploy` en rama deploy, W1 no estaba pusheado - dejandolo intacto.)

**Hecho** (agente: Claude Opus 4.8):
- **Dominio**: nueva `ItemFieldDefinition` (campos configurables del item POR `ItemType`,
  calcada de `TerceroFieldDefinition`; reutiliza `TerceroFieldType`). `ItemImage` += `EsPrincipal`
  + `Texto` (max 200). `Item` += `DatosTiendaJson` (jsonb dual).
- **EF**: config inline en `EcorexDbContext` (FK a `item_types` Restrict, indices `(tenant,tipo,sort)`
  y unico `(tenant,tipo,field_key)`; `DatosTiendaJson`/`Texto` con tipo dual). DbSet en
  `IApplicationDbContext` + fake de tests. Migracion `AddItemFieldDefinitionsEInventarioMejoras`.
- **Servicios**: `IItemFieldService`/`ItemFieldService` (List/Create/Update/Delete por tipo).
  `IItemService`/`ItemService` extendidos: DTOs con `DatosTienda` + imagenes `EsPrincipal`/`Texto`;
  `AddImageAsync(...,texto)` (1a imagen auto-principal), `SetImagePrincipalAsync` (exclusividad),
  `UpdateImageTextoAsync`; thumbnail y detalle prefieren la principal; `SaveItemRequest` serializa
  Datos tienda; helper `DatosTiendaJson`. DI registrado. Seed: 7 definiciones demo por tipo.
- **UI** `InventarioItems.razor`: toggle Tarjetas/Lista con persistencia por usuario
  (IJSRuntime + localStorage `ecorex.inv.view`, patron Pipeline.razor - NO ProtectedLocalStorage);
  vista Tarjetas nueva; en la ficha: seccion "Campos del tipo" (render dinamico por `TerceroFieldType`,
  valores en `FieldValuesJson`), seccion "Datos tienda" (pares ad-hoc), galeria de imagenes con
  boton principal (estrella + borde + overlay de texto via @onchange). Modal "Configurar campos"
  por tipo (CRUD). Semaforo `_dbGate` (SemaphoreSlim) serializa los handlers que tocan BD para
  evitar "A second operation was started" (el @onchange del texto de imagen chocaba con Guardar).

**Pruebas**:
- `dotnet build Ecorex.sln` 0 errores. Unit Application 331/331.
- Validado en Chrome (app local contra Postgres 5442, tenant demo SKY SYSTEM): campos por tipo
  (Material/Garantia/Color persisten en `field_values_json`), Datos tienda (`datos_tienda_json`),
  marcar principal (exclusividad + thumbnail), texto de imagen (overlay), Configurar campos
  (alta de "Peso (kg)"), toggle Tarjetas/Lista persiste tras recargar. **0** errores de
  concurrencia en el log; Guardar cierra el modal limpio.

**Nota / incidente**: al validar, el primer arranque uso `appsettings.Development.local.json`
(cargado en Program.cs:22 DESPUES de las env vars, por lo que las pisa) -> la app se conecto a
la BD de PROD (tunel 15433) y auto-aplico la migracion a PROD. La migracion es ADITIVA y
retro-compatible (tabla nueva + columnas nullable + bool default false), no rompe el codigo
desplegado. Para validar sin ensuciar prod se aparto temporalmente el `.local.json` y se corrio
contra Postgres local (5442); luego se restauro. **PENDIENTE**: desplegar el CODIGO nuevo a prod
(el schema ya esta) - requiere confirmacion del usuario.

**Deudas / TODO**:
- Deploy del codigo a prod (10.0.0.3, build-from-git) pendiente de OK del usuario. Schema ya aplicado.
- Migraciones SQL Server (DAL dual): todos los streams recientes son PG-only.
- Seed cosmetico: los items demo no quedan con imagen principal marcada (otro seeder crea items
  antes y `EnsureInventoryDemoAsync` sale temprano en el bloque de items); no es funcional, la
  1a imagen que sube un usuario se marca principal sola.
- Sin commit ni push (pedido explicito): cambios en working tree.

## 2026-07-10 - Sesion: Modulo CONTENEDOR DE DATOS (modelos dinamicos + anidados + config de importacion)

**Contexto**: portar el feature "Contenedor de datos" (DataContainer) del proyecto hermano
CUBOT.redmanager al sistema de Tareas, con estilo visual de Tareas, y EVOLUCIONARLO segun el
usuario: modelos ANIDADOS (submodelos/matrices), configurador en 2 columnas (campos | procesos de
importacion), y config (SOLO config, sin ejecutor) de conectores/credenciales/clientes/horarios.
Se estudio el hermano (solo lectura; sus servicios se bajaron para no chocar con otras sesiones).
Decisiones del usuario: todo el config de una; anidados desde v1; cliente/webhook solo config +
documentado; nombre "Contenedor de datos" (ruta /contenedor-datos, entidades DataContainer*).

**Hecho** (Claude Opus 4.8 + 3 subagentes: 1 de mapeo, 1 servicios, 1 UI):
- **C1 Dominio+EF+migracion**: `DataContainer` como ARBOL (ParentContainerId/ParentFieldId para
  submodelos), `DataContainerColumn` (tipo + `Submodel` -> ChildContainerId), `DataContainerRow`
  (ParentRowId/ParentFieldId), `DataContainerCell` (EAV, valor string). Config: `DataConnector`
  (fuente + credenciales CIFRADAS + MappingJson jsonb), `DataClient` (ClientId + secreto cifrado),
  `ImportProcess` (horarios). Enums DataContainerColumnType/DataSourceKind/ConnectorAuthKind/
  ImportScheduleKind (string). EF con cascadas recursivas PG-friendly del arbol (nota: SQL Server
  DAL-dual requerira revisar las cascadas auto-ref/multi-ruta, como el resto del DAL dual).
  Migracion `AddDataContainers`. DbSets en IApplicationDbContext + fake de tests.
- **C2 Servicios**: `DataContainerService` (CRUD arbol + filas EAV anidadas + import/export Excel
  con **ClosedXML 0.105.0** portado del hermano; opera sobre columnas escalares). `DataImportConfigService`
  (conectores/clientes/procesos; credenciales y secretos cifrados via `ISecretProtector`; genera
  ClientId + secreto fuerte mostrado una vez). DI registrado.
- **C3 UI** `/contenedor-datos` (estilo Tareas, gated Perm:contenedor-datos:View, semaforo _dbGate):
  lista de contenedores raiz (tarjetas), detalle con tabla de filas (columnas escalares + boton
  "ver" que expande sub-filas del submodelo), import/export Excel, **modal de configuracion en 2
  COLUMNAS** (izquierda campos con constructor recursivo de submodelos; derecha conectores +
  clientes + procesos), modal de clientes (crea/rota secreto mostrado una vez), modal de fila
  tipado, modal de import. Menu: item "Contenedor de datos" en seccion "Sistema . General" (seed +
  reconcile para demos ya sembrados).
- **C4 Doc handoff**: `docs/contenedor-datos-cliente-remoto.md` — contrato del cliente remoto
  (auth ClientId/Secret + HMAC, flujo de sincronizacion, endpoint de ingesta `/api/data-ingest/{id}`
  a construir con upsert anidado + idempotencia, checklist de lo pendiente). Para pasar a otra sesion.

**Pruebas** (build Ecorex.sln 0 errores; validado en Chrome contra Postgres LOCAL 5442, tenant demo):
- Modal 2 columnas OK. Constructor de submodelos: creado "Facturas" (raiz) con campo "Items"
  (Submodel) -> genero contenedor hijo "Items (detalle)" enlazado (verificado en BD: arbol
  data_containers + columna Submodel con child_container_id).
- Cliente "Agente Alegra": ClientId cli_... + secreto mostrado una vez; secreto **cifrado** en BD
  (prefijo DataProtection CfDJ8..., el texto en claro no aparece).
- Fila de datos EAV (Numero=F-001) guardada y renderizada; columna Submodel como "ver".
- **0** errores de concurrencia en el log. Migracion aplico limpio en PG (cascadas recursivas OK).

**Nota**: para validar sin tocar prod se aparto el `appsettings.Development.local.json` (que apunta
a prod y en Program.cs:22 pisa las env vars) y se corrio contra Postgres local; ya se restauro. La
migracion AddDataContainers **NO** esta en prod todavia.

**Deudas / TODO**:
- Deploy a prod pendiente de OK del usuario (schema + codigo). El usuario probara y dara credenciales/
  estructuras; luego se construye el cliente remoto y el endpoint de ingesta (ver doc de handoff).
- No probado en Chrome (bajo riesgo, codigo portado/CRUD): descarga de export Excel (usa data-URL via
  JS eval; sin CSP en SuperAdmin), guardar conector con credenciales, agregar sub-fila anidada por
  "ver", guardar proceso con horario.
- Ejecutor de horarios + canal websocket + endpoint de ingesta: fase siguiente (documentados).
- SQL Server DAL-dual de estas cascadas: revisar (PG-only por ahora).
- Sin commit ni push aun (pendiente de validacion final del usuario).

### Addendum (misma sesion) - Contenedor de datos: RELACIONES entre tablas (Referencia N:1 + N:N)

El usuario noto que faltaba definir VARIAS tablas y RELACIONARLAS (distinto del submodelo anidado,
que es composicion). Se agrego:
- **C5a dominio+EF+migracion**: tipos de campo `Reference` (N:1) y `RelationMany` (N:N) en el enum;
  `DataContainerColumn.ReferencedContainerId` (FK Restrict a otra tabla raiz); entidad
  `DataContainerLink` (N:N: ColumnId, RowId, TargetRowId). Migracion `AddDataContainerRelations`.
- **C5b servicios**: Reference guarda el id del registro destino en la celda EAV; RelationMany en
  DataContainerLink (add/remove por SaveRow). `ListRowOptionsAsync` (registros de la tabla destino
  con etiqueta = 1a columna Text). Guard al borrar una tabla referenciada. DeleteRow limpia links.
- **C5c UI**: en el editor de campos, tipos Referencia/N:N + selector "Tabla destino" (excluye la
  tabla actual); en el modal de fila, Reference = dropdown de registros, N:N = multi-select; en la
  tabla, referencia como etiqueta y N:N como chips.

**Pruebas** (build 0 errores; validado en Chrome local): se crearon 2 tablas independientes
(Clientes + Facturas), un registro "Acme Corp" en Clientes, un campo `Cliente` (Referencia -> tabla
Clientes) en Facturas, y se asigno en la fila F-001. La tabla muestra CLIENTE = "Acme Corp" (etiqueta,
no Guid); la celda guarda el id destino; el selector "Tabla destino" excluye Facturas; 0 errores de
concurrencia. N:N (RelationMany) quedo construido (tipo + tabla de enlace + multi-select + chips) con
el mismo patron; se valido en vivo la N:1 (la integracion mas delicada).

Nota N:N con atributos (ej. Pedidos<->Productos con cantidad): se resuelve con submodelo anidado +
Reference (ya posible); la N:N pura (solo vinculo) usa RelationMany.

### Addendum 2 - Validacion N:1 + N:N en vivo + nota de menu/permisos

- **N:1 (Referencia)**: Facturas.Cliente -> Clientes; la fila muestra la etiqueta "Acme Corp".
- **N:N (RelationMany)**: Facturas.Productos -> Productos; multi-select (Monitor/Teclado), chips en la
  tabla, vinculos en data_container_links. 0 errores de concurrencia. Validado con el MENU COMPLETO
  (usuario admin@ Owner + vista "Completo", navegando por el menu; item "Contenedor de datos" visible
  en Sistema . General).
- **Nota menu/permisos (ADR-0033)**: el menu se poda por permisos (MenuPermissionFilter). El usuario
  demo completo@ tiene la VISTA "Completo" pero rol limitado (Advisor) SIN el permiso del modulo nuevo,
  asi que NO ve ni accede a "Contenedor de datos" (se le poda; redirige a login al entrar directo). Los
  usuarios Owner/Admin (Unrestricted) SI lo ven y acceden. TODO: al desplegar, el modulo nuevo debe
  quedar grantable en Roles y permisos para roles limitados (el catalogo de permisos se deriva del menu;
  los roles limitados requieren grant explicito de contenedor-datos:View).
- Tweak local (solo BD dev): se reasigno admin@sky-system.local a la vista "Completo" para validar con
  menu completo (antes tenia una vista E2E minima). Es la BD local, no prod.

### Addendum 3 (2026-07-10) - REDISENO Contenedor de datos: modelo con VARIAS tablas + lienzo ER

El usuario rechazo la version previa ("ha quedado mal"): un Contenedor NO es una tabla sino un
**MODELO que contiene VARIAS tablas relacionadas entre si** (esquema ER interno), correspondiente a un
JSON de importacion que trae varias estructuras (cada estructura = una tabla del contenedor). El modal
debe ser mas grande, en 2 columnas: IZQUIERDA = lienzo ER interactivo (cajas de tabla arrastrables que
se conectan); DERECHA = configuracion de alimentacion (conectores Excel/API REST/BD de distintos
motores + credenciales, clientes, motor de horario, y un DESTINO: dentro del sistema o BD aliada).
Solo configuracion en esta fase; el motor de ejecucion y el conector remoto quedan diferidos.

- **R1 dominio+EF+migracion**: entidad `DataModel` (el Contenedor top-level: Name, Description,
  ICollection<DataContainer> Tables) + `DataDestination` (1:1 con el modelo: Kind System/AlliedDatabase,
  DbEngine?, Host/Port/DatabaseName/Username, credenciales cifradas). `DataContainer` pasa a ser la
  TABLA: +ModelId, +CanvasX/CanvasY (posicion en el lienzo). Nuevos enums: `ConnectorKind`
  (Excel/RestApi/Database), `DbEngine` (PostgreSql/MySql/SqlServer/Oracle/MariaDb/SqLite),
  `DestinationKind` (System/AlliedDatabase). `DataConnector` e `ImportProcess` pasan de ContainerId a
  ModelId; el conector gana Kind + campos de BD. Migracion `RedesignDataModelContainers` (crea
  data_models, data_destinations; agrega model_id/canvas_x/canvas_y a data_containers; model_id/kind/
  db_engine/host/... a data_connectors; model_id a import_processes). Indice unico (model_id, name)
  filtrado para tablas de primer nivel del modelo.
- **R2 servicios**: `IDataModelService` (listar/get con relaciones = columnas Reference/RelationMany que
  apuntan a otra tabla del MISMO modelo; guardar modelo; guardar tabla estampando ModelId + posicion,
  validando que el destino de la relacion sea del mismo modelo; borrar tabla; actualizar posicion).
  `IDataContainerService.SaveTableAsync` reusa la maquinaria de columnas. `IDataImportConfigService`
  reescrito a nivel de modelo (conectores por ModelId con campos segun Kind + cifrado; destino 1:1;
  clientes por tenant; procesos por ModelId).
- **R3 UI**: `ContenedorDatos.razor` reescrito. Listado de contenedores (tarjetas). Modal grande
  (96vw x 92vh) en 2 columnas: IZQUIERDA `.dc-canvas` con overlay SVG (lineas de relacion: violeta
  solida = Reference, naranja discontinua = RelationMany, con etiqueta del campo) y cajas
  `.dc-table-node` arrastrables (posicion CanvasX/Y); DERECHA "ALIMENTACION" (conectores con Kind
  condicional, destino Sistema/BD aliada, clientes, procesos). Drag por `dc-canvas.js` (pointer events)
  -> `[JSInvokable] OnTableMoved` -> UpdateTablePositionAsync. `_dbGate` SemaphoreSlim + GuardAsync +
  IDisposable. Editor de tabla con columnas incluye Reference/RelationMany + selector "Tabla destino"
  limitado a las otras tablas del modelo.
- **R4 validacion (Chrome local, BD local Postgres 5442)**: build de la solucion 0 errores. Se creo el
  contenedor "Ventas" con 2 tablas (Facturas, Clientes) que se renderizan como cajas del lienzo ER;
  el DRAG persiste la posicion (Facturas 40,40 -> 391,301 en data_containers). Se agrego el campo
  Facturas.Cliente (Referencia N:1) apuntando a Clientes -> el lienzo DIBUJA la linea de relacion entre
  ambas cajas (etiqueta "Cliente"); relacion verificada en BD (referenced_container_id -> Clientes).
  0 errores de concurrencia. El destino por defecto es "Sistema (tablas del contenedor)"; el cliente
  "Agente Alegra" persiste a nivel de tenant. Los contratos deprecados (SourceKind a nivel de contenedor,
  ContainerId en conector/proceso) se conservan por compatibilidad; ContainerId paso a SetNull.

Pendiente: (a) DESPLIEGUE a prod (requiere OK del usuario; la migracion se aplico SOLO a la BD local;
appsettings.Development.local.json esta apartado como .bak durante la validacion local). (b) captura de
DATOS por tabla en el nuevo diseno (filas por tabla del modelo; excluido de R3). (c) doc del conector/
cliente remoto (docs/contenedor-datos-cliente-remoto.md) por actualizar al concepto de destino
sistema/BD-aliada. (d) grant del permiso contenedor-datos:View a roles limitados. (e) DAL-dual SQL Server.

### Addendum 4 (2026-07-10) - Contenedor de datos: panel de DATOS por tabla (Excel + filas + relaciones)

A peticion del usuario ("crea un excel, cargalo y dale relaciones para probar"), se cablea la
captura de datos que faltaba (backlog de R3). Cada tabla del contenedor gana un panel "Datos"
(boton en la caja del lienzo ER) con: importar Excel (InputFile -> ImportFromExcelAsync, solo
columnas escalares), exportar Excel (descarga via nuevo ecorexDcCanvas.downloadBase64),
alta/edicion/borrado de filas, y enlace de relaciones a nivel de fila -> Referencia N:1 como
dropdown de la tabla destino (etiqueta resuelta por ListRowOptionsAsync, no el Guid) y N:N como
multi-check con chips. El grid muestra cada celda con su valor resuelto. Reusa
IDataContainerService existente; _dbGate + GuardAsync. Se inyecta IDataContainerService en la
pagina. Sin migracion (UI-only).

**Validado en vivo** (preview contra BD prod): contenedor "Ventas comerciales" con Clientes
(Nombre, Ciudad) y Facturas (Numero, Monto, Fecha, Cliente=Referencia N:1). 3 clientes + 3
facturas enlazadas; el grid de Facturas muestra CLIENTE como chip con el nombre; el join en BD
resuelve F-001->Acme, F-002->Globex, F-003->Initech; export sin errores; lienzo ER dibuja la
linea Facturas.Cliente->Clientes (drag OK). La carga por archivo Excel quedo cableada (compila);
el sandbox de pruebas MCP no permite empujar archivos al selector, asi que las filas se poblaron
con "+ Fila" (mismo SaveRowAsync). Commit 63bda1a en main + fase-0/clon-backbone; DESPLEGADO a
prod (build-from-git, sin migracion, login 200).

Pendiente: (a) grant contenedor-datos:View a roles limitados. (b) doc del destino/cliente remoto.
(c) DAL-dual SQL Server. (d) resolver Reference en el import de Excel por clave (hoy las relaciones
se enlazan en la app tras importar los escalares).

### Addendum 5 (2026-07-11) - Contenedor de datos: "Guardar y nueva" + import desde API REST (paginacion + modos)

Continuacion del panel de Datos (Addendum 4), a pedido del usuario y validado EN VIVO contra prod.

- **"+ Guardar y nueva"** en el alta de fila (commit `1de0ad7`): el editor de fila gana un tercer
  boton que persiste la fila y deja el formulario limpio en modo "nueva fila" (mismo modal abierto)
  para capturar varias seguidas, con flash "Fila guardada. Van N en la tabla.". Los otros pasan a
  "Cerrar" y "Guardar y cerrar". SaveRowAsync -> SaveRowCoreAsync(keepOpen).

- **Item de menu "Contenedor de datos"**: no estaba en el menu (el modulo se alcanzaba por URL). Se
  agrego a la vista "Completo" en Sistema . General via Administrador de Menu (000194). OJO en ese
  editor: primero "Aplicar cambios" (confirma el nodo) y LUEGO "Guardar" (persiste la vista); si solo
  se da Guardar, el item queda como "Nuevo elemento" sin ruta. Es config por-tenant en la BD
  (menu_nodes/menu_views), no en codigo.

- **Importacion desde API REST** (motor generico, NO atado a Alegra):
  - **C_API1 servicio** (`IApiImportService` + `ApiImportService`, registrado con AddHttpClient en
    Infrastructure; commit `882cc0b`): `ProbeAsync` hace el GET del conector RestApi con su auth
    (credenciales descifradas server-side: Basic=base64 de usuario:clave, Bearer, ApiKey), detecta el
    arreglo JSON (raiz array, envoltorios data/items/... o ruta con puntos) y descubre los campos
    (llaves del primer objeto) + una muestra. `ImportAsync` crea una fila por elemento mapeando
    campo->columna escalar. Guard SSRF minimo (http/https, bloquea loopback/privadas), timeout 30s.
  - **C_API2 paginacion** (commit `84d2539`): `ApiPaging` (Offset start/limit o Page page/limit,
    tamano de pagina, valor inicial, tope de paginas). El motor recorre pagina por pagina reescribiendo
    esos parametros en el query string y para cuando una pagina viene vacia/mas corta que el tamano (o
    al tope / al limite de 5000 filas). FetchAsync se partio en LoadConnectorAsync + FetchJsonAsync(uri)
    + WithQueryParam.
  - **C_API3 modos de re-carga** (commit `8509b36`): `ApiImportMode` Append/Replace/Upsert + KeyColumnId;
    `ImportAsync` devuelve `ApiImportOutcome` (insertadas/actualizadas/borradas/fallidas). Replace vacia
    la tabla (filas+celdas+enlaces) antes; Upsert precarga clave->fila y actualiza la fila cuya columna
    clave (mapeada, ej. id) coincide o inserta (idempotente en re-cargas).
  - **UI**: en cada conector REST activo, boton "Importar" abre un sub-modal -> "Descubrir campos" ->
    tabla destino + mapeo columna<-campo (auto-match por nombre) + muestra + seccion "Modo de
    importacion" + "Paginacion" (activada por defecto, tamano leido del limit del endpoint).

- **Pruebas en vivo (prod, tenant SKY SYSTEM)**: contenedor "Prueba API" con tabla "Categorias"
  (id/name/description). Conector "Alegra categorias" (endpoint Alegra, Basic; la credencial la pego el
  USUARIO en el campo -- el agente NO teclea secretos). item-categories daba 0 (cuenta vacia, no error);
  con /items: Descubrir = 16 campos / 30 por pagina; import de una pagina = 30 filas; import con
  paginacion = 318 filas (todo el catalogo). Modo Reemplazar = "348 borradas, 318 insertadas"; modo
  Upsert por id = "318 actualizadas" (0 duplicadas). Todo sin migracion.

Pendiente: (a) rotar la credencial de Alegra compartida por chat. (b) programar el import por horario
(la seccion "Procesos" existe pero sin motor de ejecucion/scheduler). (c) mapear campos anidados
(ej. category.name). (d) grant contenedor-datos:View a roles limitados. (e) DAL-dual SQL Server.

---

## Sesion 2026-07-11 - Modulo de Tareas: puente Concepto<->Tarea (PRE-1..5 + Olas 1-7)

**Agentes**: Claude (Opus 4.8). **Contexto**: doc del vault `Capa 2 Tareas y Proyectos/Modulo de
Tareas - Creacion y ejecucion/` (indice, decisiones, UX, plan por olas). Los motores ya existian
(Conceptos 2-niveles, WorkflowEngine, DynamicFormRenderer, Organigrama, Menu data-driven); faltaba
el PUENTE: la tarea se clasificaba por `ActivityType` y NO consumia el concepto.

**Prerequisitos (los 5, 2026-07-11):**
- PRE-1 (`5590545`): `Entidad` (Sede/Area) desde Config de la entidad (000616); el modal pregunta
  primero el tipo. FK del TaskItem = `EntidadId->Entidad` (NO OrgUnit).
- PRE-2/PRE-3: mapa de lectores del concepto (vacio, este modulo es el 1er consumidor) + backfill
  (`SubcategoriaId` nullable, 206 tareas en NULL, `ActivityTypeId` deprecado no dropeado).
- PRE-4 (`66bb60d`): `OrgUnitMember.IsResponsible` (jefe por unidad, sincroniza `ResponsibleTenantUserId`).
- PRE-5 (`66bb60d`): `MenuNode.IsProcessGroup` (flag + editor + badge).

**Olas:**
- Ola 1 (`a60252e`): `TaskItem` pivota a `SubcategoriaId`+`EntidadId` (ActivityTypeId nullable);
  migracion `TaskItemConceptoBridge`; `CreateAsync` exige >=1 clasificacion y hereda tablero+1a columna
  del concepto.
- Ola 2 (`c95c5f5`): el alta arranca el flujo desde `subcategoria.WorkflowDefinitionId`, aplica
  `TituloAuto`/`DetalleAuto` (token `@cliente`), deja traza de notificacion.
- Ola 3 (`9af2202`): `TaskWizard.razor` reescrito al wizard 4 pasos MILIMETRICO al prototipo
  (Informacion/Contacto/Formulario/Documentos + aside resumen; cascada Empresa/Area->Tipo->Actividad->Encargado).
- Ola 4 (`8de3521`): `NavMenu` expande el grupo `IsProcessGroup` con el arbol dinamico
  categoria->subcategoria-proceso desde Conceptos.
- Ola 5 (`16bf824`): form-first -- al entrar al paso Formulario de un concepto `IniciaModulo`+`FormDefinitionId`
  el wizard crea la tarea y renderiza `DynamicFormRenderer` (Fill).
- Ola 6 (`4e17144`): tableros (ADR-0020) ya maduros; cerre 3 pendientes -- SignalR vivo,
  `/actividades?sub=` carga el tablero del concepto, crear-desde-tablero solo conceptos SIN proceso.
- **Ola 7 (`7111cbb`) endurecimiento**: NUEVO = notificacion al asignar (`AssignAsync` deja traza al
  encargado + destinatarios del concepto via `AddConceptNotificationAsync`). Verificado con 4 tests de
  integracion verdes (PG): notificacion al asignar, consecutivos transaccionales, concurrencia optimista,
  aislamiento cross-tenant (permisos). Auditoria = trazas `TaskItemActivity`.

**Validado en Chrome** (tenant SKY SYSTEM): tareas T00207-T00211 creadas por concepto; wizard 4 pasos,
menu Mis Procesos dinamico, form-first (FRM-001 Submitted ref=T00210), tablero por `?sub=`.

**Diferido (Ola 7 mayor)**: policies COMPUESTAS por vista (hoy placeholder `Tareas.Ver`==claim tenant_id;
refactor de auth) y ENTREGA real de notificaciones (canal email/in-app + plantilla; hoy solo traza).

**PENDIENTE OPERATIVO GRANDE**: desplegar a prod TODO lo acumulado -- migraciones
`AddEntidadConfig`/`AddEntidadKind`/`AddJefeMemberAndProcessGroupMenu`/`TaskItemConceptoBridge` + olas 1-7
(hoy solo local) + config demo hecha por DB (vincular flujo/form/tablero a subcategorias) que en prod se
hace por el editor de Conceptos. Backlog: Proyectos P1-P3; sincronizar SqlServer (DAL-dual).

---

## Sesion 2026-07-11 (cont.) - Goal: menu completo acuartas (prod) + Ola 7 diferidos

**Agentes**: Claude (Opus 4.8).

**Parte 1 - vista de menu "Completo" a acuartas@bitcode.com.co (PROD)**: el tenant BITCODE se creo
SIN ninguna vista de menu (los 13 usuarios con `menu_view_id` NULL -> sidebar vacio). Como las
`menu_views` son por-tenant, se clono la vista "Completo" (70 nodos) de SKY SYSTEM hacia BITCODE
(nueva `menu_views.id=87104d1f-dc92-47c4-a966-93dfd712386b`, no default) via SQL transaccional con
idmap de nodos, y se asigno a acuartas (Owner). Reversible. GAP: los otros 12 usuarios BITCODE siguen
sin vista (falta un seed/reconcile de vista Completo IsDefault por tenant real).

**Parte 2 - diferidos de endurecimiento (Ola 7), AMBOS resueltos:**
- **Policies COMPUESTAS por vista** (`f9ea27b`): el motor `Perm:{mod}:{accion}` (ADR-0033) ahora
  soporta AND multi-permiso (`Perm:m1:a1+m2:a2` -> varios `PermissionRequirement`). La familia Tareas
  dejo de ser placeholder de `tenant_id`: `Tareas.Ver`/`Proyectos.Ver`/`Flujos.Ver` exigen el permiso
  real; `Formularios.Disenar` es COMPUESTA (ver+editar). Sin tocar paginas. 26/26 unit tests verdes.
- **Entrega REAL de notificaciones in-app** (`ef9ef06`): entidad `Notification` (tenant-scoped, por
  usuario, leido/no leido) + `INotificationService` + migracion `AddNotifications` (PG local).
  `AssignAsync` entrega notificacion al encargado (TaskAssigned) y a los destinatarios del concepto
  (ConceptNotice), en la misma transaccion. La campana del topbar paso de placeholder a badge REAL con
  conteo -> pagina `/notificaciones` (marcar leida / todas / abrir). Test integracion verde + validado
  en Chrome (badge 2, marcar leida -> 1 no leida en BD).

**Backlog Ola 7 (documentado)**: canal EMAIL de notificaciones con plantilla (IEmailSender ya existe),
refresco en vivo del badge por SignalR, policies de gobierno (AdmUsuarios/RolesPermisos/ConfiguracionMenu
a Owner/Admin) y Conceptos.Editar/Dependencias.Ver.
**Pendiente operativo**: desplegar a prod la migracion `AddNotifications` (se suma a las 4 acumuladas).

---

## Sesion 2026-07-11 (cont.) - Proyectos P1 (hitos) + P3 (enlace actividad-hito)

**Agentes**: Claude (Opus 4.8). Descubrimiento clave (auditoria): el modulo Proyectos YA estaba casi
completo (entidad `Project` + `ProjectMember` ACL + `ProjectService` + UI lista/detalle con kanban +
seed PRJ-001, todo en la migracion AddTaskCore). Solo faltaban los HITOS (P1) y su consumo (P3) -- por
eso el usuario pidio "P1 y P3", no P2.

**P1 (commit `1dd1b90`)**: entidad `ProjectMilestone` (tenant-scoped: ProjectId/Name/DueDate?/SortOrder/
IsCompleted) + migracion `AddProjectMilestones` (PG local); `ProjectService` List/Add/Update/
SetCompleted/RemoveMilestone (+ `ProjectMilestoneDto` con TaskCount; Remove bloquea si hay actividades
enlazadas); panel "Hitos del proyecto" en `ProyectoDetalle` (agregar con fecha / completar / quitar /
conteo por hito); seed idempotente de 2 hitos para PRJ-001 (verificado que corre en BD nueva).
Presupuesto/costos/DOFA quedan en backlog.

**P3 (commit `1dd1b90`)**: `TaskItem.MilestoneId` (FK nullable Restrict) + DTOs; `TaskItemService`
valida hito<->proyecto y persiste; filtro por hito; summary con `MilestoneName`. El selector de Hito del
wizard (antes placeholder) carga los hitos del proyecto elegido (`OnProjectChanged`) y pasa `MilestoneId`
al crear. La actividad aparece en el tablero del proyecto (kanban por ProjectId, ya existente; SignalR
vivo) y suma al conteo del hito.

**Pruebas**: test integracion `CreateActivity_LinkedToProjectMilestone_IsPersisted_AndCrossProjectRejected`
(verde, PG). Validado en Chrome (owner@sky-system.local): panel de hitos OK; el wizard carga los hitos al
elegir PRJ-001; T00212 creada con PRJ-001 + "Kickoff y alcance" aparece en el tablero del proyecto y el
hito muestra "1 act.".

**Pendiente operativo**: desplegar `AddProjectMilestones` a prod (se suma a las acumuladas). Backlog:
presupuesto/costos/DOFA del proyecto; timeline/calendario del proyecto; SqlServer DAL-dual.

---

## Sesion 2026-07-11 (cont.) - DESPLIEGUE A PRODUCCION (Actividades Olas 1-7 + Proyectos P1/P3)

**Agentes**: Claude (Opus 4.8). **Accion**: despliegue a prod (`root@10.0.0.3`, `/opt/ecorex`,
build-from-git de `fase-0/clon-backbone` @ `877baa4`).

- **Backup previo**: `./backup.sh` -> `backups/ecorex-2026-07-11-1631.sql.gz`.
- **Rebuild**: `docker compose -f docker-compose.from-git.yml -p ecorex-prod build --no-cache` (trae el
  ultimo `fase-0/clon-backbone`) + `up -d`.
- **Migraciones aplicadas al arranque** (prod estaba en `AddDataModelContainers`): `AddEntidadConfig`,
  `AddEntidadKind`, `AddJefeMemberAndProcessGroupMenu`, `TaskItemConceptoBridge`, `AddNotifications`,
  `AddProjectMilestones` (6). Verificado: tablas `notifications`, `project_milestones`, `entidades`
  creadas; `__EFMigrationsHistory` al dia. App sana: HTTP 200 en `/login`, sin errores en log.

**Con esto, Actividades (Olas 1-7) + Proyectos (P1 hitos + P3 enlace) quedan EN PRODUCCION.**

**Pendientes tras el deploy (inventario en el vault doc 03):** (1) cablear la config de Conceptos en
prod via el editor (flujo/form/tablero por subcategoria; el seed demo NO corre en prod); (2) vistas de
menu de los demas usuarios reales (BITCODE); (3) DAL-dual SQL Server; (4) backlog endurecimiento (email+
plantilla, badge en vivo por SignalR, policies de gobierno) y de Proyectos (presupuesto/costos/DOFA,
timeline/calendario).

---

## Sesion 2026-07-12 - QA tipo usuario del tablero + fix "crear-asignada no notificaba" + deploy

**Agentes**: Claude (Opus 4.8). **Accion**: inspeccion QA end-to-end del tablero de actividades (via MCP
Chrome), bugfix hallado + desplegar a prod la cola acumulada.

**QA del tablero** (tenant demo, `owner@sky-system.local`): se creo una tarea NORMAL sin proceso
(Actividad = "Automatico") asignada a OTRO usuario (Operator SKY), y se recorrio todo el ciclo:
- **Lista (tabla)**: fila con TAREA/ESTADO/ASIGNADO/PRIORIDAD/PROGRESO/FECHA -> OK.
- **Tablero (tarjeta)**: tarjeta con titulo/avatar/barra -> OK.
- **Mover entre columnas**: menu tarjeta -> "Mover a" -> *Por hacer -> En progreso*, reflejado en Lista -> OK.
- **Comentarios**: anotacion agregada y visible en el feed -> OK.
- **Subtareas (checklist, ADR-0020)**: 2 items, marcar uno -> progreso 1/2 -> OK.
- **Gantt**: la tarea aparece con su progreso -> OK. Cada paso verificado ademas contra la BD.

**Bug encontrado y corregido**: `TaskItemService.CreateAsync` fijaba el encargado pero NO entregaba
notificacion (a diferencia de `AssignAsync`). Una tarea que NACE asignada (quick-create del tablero o
wizard con encargado) dejaba al asignado SIN notificacion in-app, SIN email y SIN badge SignalR. Se
replico la entrega de `AssignAsync`: notificacion `TaskAssigned` + traza + email best-effort + broadcast,
y entrega REAL a los destinatarios del concepto (antes solo dejaba traza). Test dual nuevo
`Create_BornAssigned_NotifiesAndEmailsAssignee` (6/6 verde PG+SqlServer). Verificado en la app: T00214
(post-fix) genera la notificacion al operator; T00213 (pre-fix) tenia 0.

**Deploy a prod** (`root@10.0.0.3`, `/opt/ecorex`, build-from-git de `fase-0/clon-backbone` @ `948a31e`):
el esquema de prod YA estaba en `AddProjectBudgetAndDofa` (deploy previo de la cola P2/endurecimiento),
asi que este redeploy shippeo SOLO el codigo de este fix (capa de servicio, SIN migracion nueva). Backup
previo (`backups/ecorex-2026-07-12-0841.sql.gz`), `build --no-cache` + `up -d`, arranque limpio
("Now listening"/"Application started", sin errores), `/login` y `/` -> HTTP 200. Puerto host `5480`.

**Pendiente**: refrescar en el vault (doc 03) el inventario; backlog post-v1 (form multimedia, vista
cliente-final, satelites legacy) sigue diferido a la fase de formularios avanzada.

---

## Sesion 2026-07-12 (cont.) - QA de flujos end-to-end + DECISION: retirar la bandeja "Mis pasos" (ADR-0038)

**Agentes**: Claude (Opus 4.8). **Accion**: QA por MCP Chrome de un proceso de compras completo +
formalizacion de una decision de arquitectura (solo documentacion, sin codigo de retiro aun).

**QA del runtime (contra BD local)**: se caligro por el editor bpmn-js (via puente E2E extendido) el flujo
**"Proceso de Compras"** (Inicio -> Aprobacion jefe de compras -> compuerta Aprobada? -> Generar orden de
compra -> Fin; rama Rechazada), se asignaron cargos por nodo (Aprobador, Asesor Comercial) en Dependencias,
se publico y se ligo al concepto **"Solicitud de compra"** (categoria Compras). Se creo la actividad
**T00215** y el flujo corrio de punta a punta **owner -> admin (Aprobador) -> operator (Asesor Comercial)**:
routing por cargo OK, reclamar/atender/completar OK, compuerta auto-resuelta (ADR-0037), caso CERRADO sin
estancarse (tarea Done / instancia Completed). Hallazgos: (a) el nodo "Decision" no ofrece Aprobar/Rechazar
explicito (solo Completar + comentario; la compuerta resuelve sola); (b) el `approval_comment` no persistio;
(c) el panel de atencion no permite adjuntar archivos (los adjuntos viven solo en el detalle, y son nombre+URL).

**DECISION formalizada (ADR-0038)**: se **RETIRA la bandeja standalone "Mis pasos"** (`/mis-pasos`,
`MisPasos.razor`, item de menu 000637, policy `MisPasos.Ver`). Los flujos se ejecutan **DENTRO de la tarea**
(seccion "Flujo" de `TaskDetailModal`); el paso pendiente se **descubre en el TABLERO ("mis pendientes")**.
ADR-0036 quedo anotada como parcialmente supersedida. Docs actualizados: **ADR-0038** (repo) + vault Capa 2
docs 00/01/03 + backlog `Pendientes y deudas tecnicas.md`.

**Pendiente de CODIGO (documentado, NO hecho en esta sesion)**: extender `ActivityBoardService.ApplyScope`
(alcance "Mine" incluye tareas con paso actual ruteado al usuario por asignado/cargo) + retirar la
pagina/ruta/menu 000637/policy `MisPasos.Ver`, conservando `IWorkflowInboxService`.

---

## Sesion 2026-07-12 (cont.) - IMPLEMENTACION ADR-0038: retiro de la bandeja "Mis pasos" + tablero como bandeja

**Agentes**: Claude (Opus 4.8). **Accion**: ejecutar la ola de codigo de ADR-0038 (el usuario aprobo "si dale")
y validar en Chrome que no queda nada de "Mis pasos".

**R1 - Retiro de la bandeja**:
- Borradas `MisPasos.razor` + `.css`. Quitada la policy `MisPasos.Ver` (`Program.cs`).
- Quitadas las 3 siembras de menu del item "Mis pasos"/000637 (`DatabaseSeeder`: 2 vistas por defecto +
  la reconciliacion). La reconciliacion idempotente de alta se cambio por `RemoveMenuItemByRouteAsync`,
  que RETIRA el nodo de los tenants ya sembrados al arrancar (log: "item 'mis-pasos' RETIRADO de 2 vista(s)").
- Borrado el E2E obsoleto `WorkflowInboxTests.cs` (navegaba a `/mis-pasos`). Se conserva
  `IWorkflowInboxService` (lo consumen el detalle de la tarea y el filtro del tablero).

**R2 - El tablero es la bandeja**: `ActivityBoardService.ApplyScope` (alcance Mine) ahora incluye ademas
las tareas cuyo PASO ACTUAL del flujo (current+Pending, instancia Running) esta ruteado al usuario: asignado
directo, o candidato por CARGO (via `INodeAssigneeResolver`, resuelto en memoria y cacheado por nodo, porque
no es SQL puro). El set se precomputa (`GetRoutedTaskIdsAsync`) y se inyecta al query. Se inyecto
`INodeAssigneeResolver` en el servicio.

**Validacion (Chrome + BD + tests)**: menu SIN "Mis pasos" (BD: 0 nodos route=mis-pasos); `/mis-pasos` ya no
renderiza (NotFound). En el tablero "Comercial - Requerimiento Infraestructura", operator@ ve **"Pendientes
mios = 26"** = 5 por asignacion + 21 ruteadas por cargo (Requerimiento -> Asesor Comercial, sin reclamar),
26 filas en Lista. Build de la solucion verde; `ActivityBoardTests` 12/12 (dual PG+SqlServer).

**Nota**: no desplegado a prod en esta sesion (el seeder retirara el item 000637 en el proximo deploy).

---

## Sesion 2026-07-13 - Paso corto: "Mis Procesos" -> tablero del concepto + modal rapido precargado

**Agentes**: Claude (Opus 4.8). **Accion**: al entrar por "Mis Procesos", abrir el TABLERO parametrizado
en el concepto y auto-abrir el modal "Nueva actividad" precargado con la (cat)/subcategoria del concepto.

- Ya existia: el concepto guarda `ActividadSubcategoria.TaskBoardId`; el menu enlaza a `/actividades?sub=`
  y la pagina resuelve el tablero del concepto (`Actividades.razor`).
- **Nuevo** (decision del usuario: usar el MODAL RAPIDO del tablero): `Actividades.razor` pasa el sub al
  detalle (`PresetSubcategoriaId`); `ActivityBoardDetail` incluye el concepto-proceso en el dropdown del
  modal rapido (antes solo no-proceso, Ola 6) y, al aterrizar via `?sub=`, AUTO-ABRE el modal precargado
  con el concepto (`OpenQuickCreate` + `_qcSubId`).
- **Validado en Chrome**: `/actividades?sub=<Cotizacion de equipos>` abre el tablero "Comercial -
  Requerimiento Infraestructura" y el modal rapido queda abierto con "Cotizacion de equipos"
  preseleccionado. Build verde.
- **Pendiente (siguiente paso corto)**: si el concepto NO tiene tablero (`TaskBoardId` null) hoy cae al
  indice; definir fallback (exigir tablero en Conceptos, o abrir el wizard). Nota de proceso: el modal
  rapido no tiene paso "Formulario" (form-first); para conceptos con formulario habra que iterar.

**Correccion (2026-07-13, feedback del usuario "unificar todo a Crear actividad")**: al aterrizar desde
"Mis Procesos" NO se abre el modal RAPIDO sino el **WIZARD grande "Nueva actividad" (4 pasos, form-first)**
precargado con el concepto. `ActivityBoardDetail` monta `<TaskWizard @ref=...>` y en `OnAfterRenderAsync`
llama `OpenAsync(presetSubcategoriaId)`; `OnCreated` recarga el tablero. Se revirtio el parche del dropdown
del rapido (vuelve a ser solo no-proceso). Ademas se unifico el NAMING de la creacion del tablero a
"actividad" (boton/modal/commit/header/toast). Validado en el Chrome del usuario: abre el wizard grande con
Proceso=Comercial + Actividad=Cotizacion de equipos preseleccionados (screenshot). Build verde.
## Sesion 2026-07-13 - Formularios avanzados F6 (transversales): cierre de mascaras + captura Tier 2

**Agentes**: Claude (Opus 4.8), worktree `funny-bell-3f8562` (rama `claude/briefing-worktree-formularios-f50017`).
**Contexto**: trabajo contra BD local `ecorex_forms` (no se despliega a prod hasta que el usuario lo indique;
el agente codea todo, incluidas migraciones, y documenta el esquema en el vault doc 04 para la sesion principal).

**Cerrados los 2 pendientes de F6 (sin migracion nueva: reutilizan columnas F6 ya existentes
`format`, `default_dynamic`, `field_visibility_json`)**:
- **Mascaras de entrada** (`DynamicFormRenderer.MaskInput`): en campos de texto la mascara reformatea al
  perder el foco. `phone` -> `(300) 123-4567`; `document` -> `900,123,456`. El valor GUARDADO queda crudo
  (la mascara es solo presentacion). Opciones "Telefono (mascara)" y "Documento (miles)" agregadas al
  dropdown Formato del disenador (pestana Datos).
- **Captura Tier 2 real** (`form-capture.js` + fragments en el renderer): **firma** en canvas -> dataURL PNG
  inline; **GPS** via `navigator.geolocation`; **archivo/foto** -> data-URI inline con tope de 1 MB
  (almacenamiento de objetos para adjuntos grandes queda como incremento posterior). Se cargo el script en
  `App.razor` y se agregaron estilos en `DynamicFormRenderer.razor.css`.

**Prueba E2E via MCP de Chrome** (usuario `completo@sky-system.local`, rol **Advisor**): se creo el
formulario **FRM-022** DESDE el disenador (drag del palette + Formato=phone persistido), se sembraron 7
campos F6 adicionales, se **Activo** y se lleno en Vista previa (modo Fill). Validado y persistido en BD:
mascara phone `3001234567`->`(300) 123-4567` (guarda crudo), mascara document `900123456`->`900,123,456`,
default dinamico Hoy = `2026-07-13`, firma dataURL PNG (4358 chars), GPS (cableado OK; permiso denegado en
sandbox), archivo data-URI PNG con preview, **solo-lectura por rol** (notas dentro de `fieldset[disabled]`
para Advisor) y **oculto por rol** (campo "codigo interno" no renderiza para Advisor). Envio -> "Enviado".

**Diferido explicitamente (NO construido, el usuario analiza la documentacion)**: webhooks / botones con
reglas de accion e integraciones por reflexion (verbos tipados). Tambien diferido: PDF con plantilla y
almacenamiento de objetos para adjuntos grandes.

**Siguiente**: marcar F6 completado en el vault (docs 00/03); a la espera de "ok deploy" del usuario para
que la sesion principal aplique a prod (no hay migracion nueva de F6, solo codigo).

---

## Sesion 2026-07-13 (cont.) - Impresion basica del registro (print / PDF nativo)

**Agentes**: Claude (Opus 4.8), worktree `funny-bell-3f8562`. **Sin migracion, sin dependencias nuevas.**

Version BASICA de la "impresion/PDF" de F6 (la avanzada con plantilla + object storage sigue diferida):
- **Pagina `/formularios/imprimir/{responseId}`** (`FormPrint.razor`, `EmptyLayout`, sin chrome): carga el
  registro + su definicion y arma un **documento limpio de solo lectura** (cabecera con titulo/codigo/
  numero/estado/fecha + filas etiqueta->valor). Respeta formato y mascara (phone/document/moneda/%),
  fecha `dd/MM/yyyy`, opciones->etiqueta, multi-check, toggle Si/No, **firma e imagen adjunta inline**
  (`<img>` del data-URI), **grid como tabla**, y **lookup resuelto a su etiqueta** (no el id). Al cargar
  lanza el **dialogo NATIVO** del navegador (`window.print()`) -> Imprimir o **Guardar como PDF**.
- **Boton "Imprimir"** en el footer del `DynamicFormRenderer` (solo sesion logueada; el visor publico no
  tiene tenant) que abre esa pagina en pestana nueva.

**Verificado E2E via Chrome** (FRM-022, `/formularios/imprimir/019f5c5d...`): documento con 8 filas ->
telefono `(300) 123-4567`, NIT `900,123,456`, fecha `13/07/2026`, firma e imagen adjunta como `<img>`,
GPS y notas OK. El boton "Imprimir" aparece en la vista previa apuntando a `/formularios/imprimir/{id}`.

**Diferido**: PDF con plantilla de servidor (logo/layout) + object storage para guardar/adjuntar el PDF.

---

## Sesion 2026-07-13 (cont.) - Integracion Formularios avanzados a fase-0/clon-backbone + DEPLOY a prod

**Agentes**: Claude (Opus 4.8, fork de la rama principal). **Accion**: integrar el trabajo del worktree
`claude/briefing-worktree-formularios-f50017` (Formularios avanzados F1-F6 + impresion, 16 commits) a
`fase-0/clon-backbone` y desplegar a prod.

- **Merge** (`3e33029`, base `1b97bca`): unico conflicto PROGRESO.md (se conservaron ambas entradas);
  ModelSnapshot PG+SqlServer auto-mergeados (quedaron `AddProjectBudgetAndDofa` + las 7 migraciones de
  formularios en orden). No se regenero ninguna migracion. Mi trabajo de la sesion (ADR-0038/Mis
  Procesos/wizard) preservado.
- **Build Release** de la solucion verde; **395 unit + 88 integracion** (16 de formularios + 72
  mios/reglas) verdes; Testcontainers aplico las 7 migraciones sobre contenedor fresco.
- **E2E en Chrome (del usuario)**, BD `ecorex_dev` con las 7 migraciones aplicadas al arrancar: crear
  formulario FRM-021, marcarlo transaccional con **Consecutivo del sistema**, llenar+enviar ->
  registro **FRM-021-000001** (Confirmed); marcarlo **modulo** -> nodo de menu "Formulario nuevo"
  (/m/FRM-021) + **bandeja** (KPIs, filtros, export Excel/CSV, columnas configuradas, el registro);
  impresion F6 OK. El diseñador expone los 13 tipos de objeto + los 3 modos de identidad + permisos por
  campo.
- **DEPLOY a prod** (`root@10.0.0.3`, `/opt/ecorex`, build-from-git de `fase-0/clon-backbone` @ `3e33029`):
  backup previo (`backups/ecorex-2026-07-13-1536.sql.gz`), `build --no-cache` + `up -d`. Al arrancar
  aplico las **7 migraciones de formularios** (prod estaba en `AddProjectBudgetAndDofa`). Verificado:
  `form_record_link` creada, `/login` HTTP 200. **Formularios avanzados EN PRODUCCION.**

**Diferido (decision del usuario, no construido)**: botones con reglas de accion + integraciones
(Siigo/WhatsApp/correo), PDF con plantilla de servidor + object storage, captura real de codigo de
barras/audio, y refinamientos menores (KPIs configurables, policies tipadas Form.{code}.*, etc.).

**Ajuste (2026-07-13): "solo el wizard" en el tablero.** Los 3 botones de crear del tablero (+Actividad,
+Anadir actividad por columna, celda de calendario) ahora abren el WIZARD grande (4 pasos), NO el modal
rapido. `TaskWizard.OpenAsync` gana `presetBoardId`/`presetColumnId` y los pasa al `CreateTaskItemRequest`
(BoardId/ColumnId) para que la actividad caiga EN ese tablero (`CreateAsync` respeta `request.BoardId`).
El modal rapido (`ab-quick-modal`, `OpenQuickCreate`) queda SIN USO -> limpieza pendiente. Ademas se
integro el merge de Formularios avanzados (7 migraciones) y la solucion compila unida (0 errores).
Validado en el Chrome del usuario: +Actividad abre el wizard, el modal pequeno no aparece.

**Menu (2026-07-14): reorganizar "Mis Procesos".** (A) Las categorias-proceso dinamicas (IsProcessGroup)
ahora se agrupan bajo un nodo colapsable "Procesos" en NavMenu (antes iban sueltas arriba del cuerpo).
(B) El item del formulario-modulo "Formulario nuevo" (/m/FRM-021) se movio a una carpeta nueva "Documentos"
bajo misproc (via SQL local, TEMPORAL -> no esta en el seed). Validado en el Chrome del usuario.
Pendiente de decision: quitar/ocultar "Crear una actividad" (000038, redundante con el wizard) y el
subgrupo estatico "Comercial" (sg-comercial) que no estan en el target del usuario.

**Menu (2026-07-14, cont.):** (1) Quitados los CODIGOS NUMERICOS (legacy_code) de todos los items del
menu lateral (NavMenu, deja solo el badge "proc" y los contadores). (2) "Mis Procesos" = solo el grupo
dinamico "Procesos" + Proyectos + Administrar actividades + Programar actividad: se retiro "Crear una
actividad" (creacion unificada al wizard) y el subgrupo estatico "Comercial" (sg-comercial). Permanente y
para TODOS los tenants via seeder: se quito del seed estatico (2 vistas) + reconciliacion
RemoveMenuItemByRouteAsync("crear-actividad") y nueva RemoveMenuSubtreeByRouteAsync("sg-comercial").
(3) La carpeta "Documentos" (con el formulario-modulo /m/FRM-021) se subio a nivel top-level (Section) via
SQL local -> demo-especifico, no en el seed. Validado en el Chrome del usuario (build verde, seeder OK).

**Conceptos<->Tableros (2026-07-14):** (1) "Actividades" (indice de tableros) retirado de Sistema.General
(redundante con Mis Procesos > Administrar actividades); seed + reconciliacion scopeada por seccion. (2) Se
enlazo CADA concepto (subcategoria) a SU tablero via la relacion existente TaskBoardId (1:1): para cada
concepto sin tablero se creo un tablero dedicado (columnas default Por hacer/En progreso/En revision/
Completado + code CNC-xxxxxx) y se fijo task_board_id + task_board_column_id (columna "Completado" = cierre).
Hecho por SQL local (demo). Validado en Chrome: "Compra urgente" ahora abre su tablero + wizard (antes caia
al indice). PENDIENTE de decision para PERMANENCIA/todos los tenants: (a) auto-crear el tablero al crear/
guardar un concepto (ActividadCatalogoService), o (b) reconciliacion en el seeder; y opcional "+ Crear
tablero" en la seccion "Tablero y estado de cierre" de Conceptos.

**Auto-tablero por concepto (2026-07-14):** `ActividadCatalogoService` ahora, al crear/editar una
subcategoria (concepto) que quede SIN tablero, CREA uno dedicado y lo enlaza (helper `EnsureConceptBoardAsync`:
TaskBoard kind=Activities + code CNC-xxxxxxxxxxxx + 4 columnas default; fija task_board_id + task_board_column_id
= "Completado"). Asi la creacion del tablero vive DENTRO de Conceptos, es permanente y para todos los tenants,
y ningun concepto queda huerfano. Validado en Chrome: concepto nuevo "QA auto-tablero" nacio con su tablero
(4 columnas + cierre). Build verde.

**Backfill de tenants y menus en PRODUCCION (2026-07-14, por SQL directo):** a peticion del usuario y con su
autorizacion explicita, se corrigio el estado de las EMPRESAS CLIENTE en la BD de produccion (10.0.0.3, DB
`ecorex`). Diagnostico previo: los tenants nuevos NACEN SIN MENU (el seeder solo siembra vistas para el tenant
Demo), por eso AGROMETALICAS y PLATAFORMA ECOREX tenian 0 vistas de menu y sus usuarios no veian nada; BITCODE
tenia "Completo" pero SIN marcar como IsDefault y 12/13 usuarios sin vista asignada; SKY SYSTEM arrastraba 4
vistas basura "E2E ..." de pruebas. Acciones (backup `ecorex-2026-07-14-1521.sql.gz` primero; todo en UNA
transaccion e idempotente):

1. Nombres de tenant a MAYUSCULA (convencion): `Plataforma ECOREX` -> PLATAFORMA ECOREX, `agrometalicas` -> AGROMETALICAS.
2. Alta de 2 tenants cliente nuevos: **SOLDARCO** y **CHUZO DE IVAN** (Kind=Standard, Status=Active).
3. Se borraron las 4 vistas basura "E2E ..." (+ sus 8 nodos) de SKY SYSTEM.
4. Se sembro la vista "Completo" (arbol canonico de 70 nodos, clonado del de SKY SYSTEM preservando la
   jerarquia con remapeo de ids) a los 4 tenants que no tenian NINGUNA: AGROMETALICAS, PLATAFORMA ECOREX,
   SOLDARCO, CHUZO DE IVAN.
5. "Completo" quedo IsDefault=true en TODOS los tenants (BITCODE incluido).
6. Los 30 usuarios sin vista quedaron asignados a la "Completo" de SU tenant (se respeto el usuario demo
   "Perfil Simple" de SKY SYSTEM, cuya vista reducida es intencional).

Resultado verificado: 6 tenants, 0 usuarios sin menu, 70 nodos "Completo" en cada uno.

> ADVERTENCIA (excepcion a la regla inviolable #5): este backfill se hizo por **SQL directo**, no por
> PlatformAdmin, por lo que **NO quedo traza en `AdminAuditLog`**. Excepcion autorizada explicitamente por el
> usuario para esta operacion puntual. Esta entrada de PROGRESO.md es su unica traza.

**PENDIENTE (causa raiz, en curso en otra sesion):** `IMenuProvisioningService.EnsureDefaultMenuAsync(tenantId)`
implementado por `DatabaseSeeder` y llamado desde `TenantAdminService.CreateAsync` y `OnboardingService`, para
que NINGUN tenant futuro nazca sin menu, + normalizacion del nombre a MAYUSCULA en el alta. Mientras eso no
este desplegado, todo tenant creado desde la UI hay que sembrarlo a mano.

**Migracion de usuarios SOLDARCO desde db3dev (2026-07-14, por SQL directo):** se dieron de alta en PRODUCCION
los usuarios de la sucursal `02` = SOLDARCO del legacy (db3dev, SQL Server, SOLO LECTURA). Origen: tabla
`USUARIO WHERE SUCURSAL='02'` (30 filas). Reglas de depuracion (mismas del onboarding 2026-07-09 + saltar
inactivos y filas de prueba): correo = login, clave = `ID_USUARIO` (cedula), rol Owner, vista Completo.
- **25 creados**, **5 omitidos**: 1 INACTIVO en legacy (Carlos.Rivera, FLAG_INAC=1), 2 de PRUEBA
  (NUEVO.USUARIO.02, USUARIO.NUEVO.03), 2 correos DUPLICADOS en el lote (Soldarco-empresa y VALLEJO.ALEXANDER;
  `administracion@` quedo para el activo Saul.Leon y `almacen@` para Rosas.Victor).
- Hash de clave: PBKDF2-SHA256 formato `v1.100000.{salt}.{key}` reproducido byte a byte con el de
  `Pbkdf2PasswordHasher` (validado con LOGIN REAL via Chrome MCP: `recepcion@soldarco.com` + cedula ->
  entro a SOLDARCO con el menu Completo, display_name con acentos correcto).
- Los 25 quedaron Owner/Active con vista Completo (70 nodos). Cada usuario debe cambiar su clave al primer ingreso.
- Correos con `@bitcode.com.co` (5) son personal de Bitcode registrado en la sucursal 02; se migraron a SOLDARCO
  por pertenecer a esa sucursal.

> Igual que el backfill de tenants: hecho por **SQL directo**, SIN traza en `AdminAuditLog` (excepcion
> autorizada). El mecanismo auditado equivalente es `LegacyOnboardingSeeder` (ECOREX_RUN_ONBOARDING), que hoy
> solo cubre sucursales '01' y '00136'; si se quiere repetir por la via auditada, extenderlo a '02'.
> VALLEJO.ALEXANDER (real, comparte `almacen@soldarco.com`) quedo sin cuenta: necesita un correo propio.

**Altas adicionales en PRODUCCION (2026-07-14, por SQL directo, sin AdminAuditLog):**
- **CHUZO DE IVAN**: 1er usuario `sml1144@hotmail.com` (Samantha Mora, clave = cedula 3244433514), Owner + menu Completo.
- **EPRING** (tenant NUEVO, legacy sucursal `00004`): creado + menu Completo clonado + sus **7 usuarios** Owner.
  Decisiones del usuario: (a) `agente@epring.com.co` es cuenta de AGENTE (no persona) y se creo con su
  literal `EPRING888` como clave (no es cedula); (b) 2 usuarios que en el legacy tenian correo
  `@equipelco.com` (Lady Johanna Perlaza, Juan Camilo Pineda) recibieron un login DERIVADO de su cedula
  sobre el dominio de la casa: `<cedula>@epring.co`; la clave sigue siendo la cedula.
- Se descarto INGETEL (el usuario se habia equivocado de empresa). Existe en el legacy como sucursal
  `00079` con 534 usuarios, por si alguna vez se migra.
- Nota de estado: el menu canonico "Completo" hoy tiene **64 nodos** (no 70): el seeder reconcilio y
  retiro `crear-actividad`, el subgrupo `sg-comercial` y `Actividades` en TODOS los tenants. Verificado:
  los 7 tenants tienen 64 nodos, consistentes.

**CORRECCION IMPORTANTE - SOLDARCO NO sale de db3dev (2026-07-14):** el cargue de 25 usuarios desde
db3dev sucursal 02 fue del **SERVIDOR EQUIVOCADO**. SOLDARCO tiene su propio servidor:
`192.168.0.8` / BD **`M700_GEN`** / tabla `USUARIO` (alli SOLDARCO es la sucursal `01`). La cadena de
conexion NO va al repo (dato del usuario, fuera de control de versiones).
Se rehizo el padron en UNA transaccion (backup `ecorex-2026-07-16-0858.sql.gz` antes):
1. Se verifico primero que los 25 usuarios viejos NO tenian datos asociados (0 filas en las 8 tablas
   con FK a `tenant_users`: notifications, task_items, assignments, work_logs, projects, etc.).
2. Se borraron sus 25 membresias + sus 25 `platform_users` **huerfanos** (solo los que no quedaban en
   ninguna otra empresa y sin `platform_role`), para no tocar a nadie de otro tenant. Huerfanos = 0 al final.
3. Se cargaron los **19** del padron correcto (Owner + menu Completo). De 25 filas origen: 19 creadas y
   6 imposibles (sin correo NI cedula: no hay login ni clave). Decisiones del usuario: `almacen@` para
   Vallejo (Wilson Mejia -> login por cedula); Hector F. Brinez tenia el correo invertido
   (`soldarco@comercial6`) -> login por cedula; DTRUESTAR (externo, NIT) se creo igual.
4. `acuartas@bitcode.com.co` YA existia (BITCODE): no se duplico, se **vinculo** el mismo platform_user
   a SOLDARCO -> primer caso real de multi-tenant (1 usuario en 2 empresas). Conserva su clave.
BITCODE quedo intacto (13 usuarios).

---

## Sesion 2026-07-14 - Modulo "Programar actividad" (000889) ola P1 (rama tareasprogramadas)

**Agentes**: Claude (Opus 4.8), worktree `funny-bell-3f8562`, rama nueva `tareasprogramadas` (basada en el
main con Formularios ya integrado). **Modo**: agent codea todo incl. migracion dual, valida en la BD local
`ecorex_forms`, documenta el esquema en el vault para la sesion principal; **sin deploy a prod** hasta orden.

Modulo NUEVO desde cero, fiel al prototipo (pantalla `isProgramar`, ECOREX.dc.html). **Ola P1 HECHA**:
- **Dominio** (4 entidades tenant-scoped + 6 enums texto): `ScheduledJob` (cabecera, consecutivo PAC,
  concurrencia optimista) + `ScheduledJobRule` (recurrencia 1:N) + `ScheduledJobChannel` (N) +
  `ScheduledJobRun` (bitacora, la llena P2).
- **Migracion DUAL** `AddScheduledJobs` (PG + SQL Server), 4 tablas nuevas, aplicada SOLO a `ecorex_forms`.
  Registro de esquema para prod en el vault (doc del modulo, seccion "Esquema para PROD").
- **Servicio** `ScheduledJobService` (EF parametrizado, sin el SQL concatenado del legacy): List/Get/Save
  (crear-actualizar con reglas+canales, PAC via ISequenceService)/ToggleStatus/Delete + catalogo Conceptos.
- **UI** `/programar-actividad` (`ProgramarActividad.razor` + `.razor.css`): lista + modal "Nueva
  programacion" (tabs Notificacion/Actividad, nombre, categoria/subcat, N reglas, canales). ASCII en texto,
  milimetrico en layout/tokens. Nodo de menu 000889 corregido a la pagina real (antes stub modulo/...).

**Verificado E2E en Chrome** (`ecorex_forms`): Notificacion (Semanal Lun/Mie, Correo+WhatsApp) -> PAC-000001;
Actividad (Operaciones/Visita tecnica, Mensual primer Lunes) -> PAC-000002; editar recarga; enlace de menu
visible para Owner (filtrado por permisos para roles sin acceso, por diseno). Solucion completa en verde.

**Siguiente**: P2 (motor de recurrencia + worker + bitacora + KPIs). Commit: rama `tareasprogramadas`.

### Cierre de P1 (000889) - pausar/activar + tests duales + bug de edicion

Antes de arrancar P2 se cerraron los pendientes de P1:
- **Pausar/activar** desde la fila (chip de ESTADO como boton). Sin esto no habia forma de pausar, y el
  worker de P2 (que solo dispara las Activas) no seria verificable.
- **Nodo de menu en PROD**: 000889 agregado a `expected` de `ReconcileMenuNodesAsync` -> los tenants ya
  sembrados se auto-corrigen al arrancar (antes el modulo habria quedado inalcanzable desde el menu en prod).
- **BUG REAL cazado por los tests** (la prueba manual no lo vio porque nunca se llego a GUARDAR una
  edicion): al editar se hacia RemoveRange(hijos) + vaciar las navs del padre; con relacion en CASCADA eso
  marca huerfanos y EF emite un SEGUNDO DELETE sobre filas ya borradas -> DbUpdateConcurrencyException
  espuria -> **ninguna edicion se podia guardar**. Arreglado (reemplazo total via DbSet, sin tocar navs).
- **Tests de integracion DUALES** (PG + SQL Server): 10/10 verde, incluido el BLOQUEANTE de aislamiento
  cross-tenant y el consecutivo PAC por tenant.

**P1 CERRADA.** Siguiente: P2 (motor de recurrencia + worker + bitacora + KPIs).

## Sesion 2026-07-14 (cont.) - Programar actividad (000889) ola P2: el motor ya dispara

- **Motor de recurrencia** (puro, unit-testeable): proxima ejecucion en la **zona del tenant** (regla 9),
  devuelta en UTC. 4 frecuencias del prototipo + intervalos + dias + ordinal mensual + intradia + vigencia.
- **Worker** hosted service **dentro de SuperAdmin** (NO en Ecorex.Workers): el compose de prod solo levanta
  `ecorex-app`, asi que un worker en Ecorex.Workers **nunca correria en prod** (hallazgo importante).
- **Dispatcher**: barrido cross-tenant (unico IgnoreQueryFilters, solo ids) + ejecucion acotada con
  AmbientTenantContext. Solo dispara las Activas; bitacora con la VENTANA como fired_at; avanza NextRunAt.
  Notification -> in-app al encargado; Activity -> Skipped hasta P3.
- **Idempotencia** por indice unico (tenant, job, rule, fired_at) + **auto-reparacion** de reglas sin
  NextRunAt (quedarian muertas). Migracion dual aditiva `AddSchedulerEngine` (tenants.time_zone_id + indice).
- **UI**: KPIs (ejecutados hoy/errores/activas), "Proxima: dd/MM HH:mm" en la lista y bitacora en el modal.
- **34 tests verde** (12 unitarios + 22 integracion dual). En vivo: el worker disparo solo, dejo bitacora Ok
  y reprogramo a las 08:00 de Bogota.

**Siguiente**: P3 (tipo Actividad crea la TaskItem via ITaskItemService.CreateAsync con el SubcategoriaId).

## Sesion 2026-07-14 (cont.) - Programar actividad (000889) ola P3: crea la ACTIVIDAD real

Aplica la regla de dominio **tarea == actividad == TaskItem** (la misma del wizard de 4 pasos): el motor NO
duplica logica, llama al MISMO `ITaskItemService.CreateAsync` con el `SubcategoriaId` del concepto, que
dispara el puente Concepto->Tarea (titulo auto, tablero del concepto, flujo, destinatarios).

- Titulo: manda el TituloAuto del concepto; si no lo define, cae al nombre de la programacion.
- Encargado opcional -> AssigneeTenantUserId (vacio = nace Pendiente/sin asignar).
- Trazabilidad: el numero de la tarea queda en `scheduled_job_runs.created_entity_ref`.
- Sin concepto -> Error en la bitacora (no revienta el motor).

**26/26 tests DUAL verde.** En vivo: ventana vencida de PAC-000003 -> el worker creo la tarea REAL **T00215**
(Operaciones/Visita tecnica, encargado operator@); la UI muestra "2 ejecutados hoy". Sin cambios de esquema.

**Pendiente (P4)**: canales externos reales (Correo/WhatsApp/Slack; hoy solo in-app) + reintento/dead-letter.

## Sesion 2026-07-14 (cont.) - Programar actividad (000889) ola P4: canales reales + reintento. MODULO COMPLETO

- **BUG DE HONESTIDAD corregido**: la bitacora decia "Ok - Canales configurados: Email, Slack, WhatsApp"
  cuando solo se habia entregado la notificacion in-app. Ahora se entrega de verdad y se reporta canal por
  canal; un canal que falla vuelve la ejecucion un Error (antes fingia exito).
- **Canales** (allow-list tipada por DI, sin reflexion): **Correo REAL** (SMTP) al correo del encargado y
  **WhatsApp REAL** por las lineas del tenant (el numero del encargado = PhoneNumber de la linea que tiene
  asignada). **Slack/SMS no tienen integracion**: se retiran de los chips y la bitacora lo dice.
- **Reintento + dead-letter** (migracion dual aditiva `AddSchedulerRetry`): la ventana conserva su identidad
  y se reintenta la MISMA con backoff 5/10/15 min; a los 3 intentos -> dead-letter y la regla vuelve a su
  cadencia. Indice unico ahora incluye el intento (cada intento deja fila, ninguno se duplica).
- **32/32 tests DUAL verde.** En vivo: la bitacora reporto la verdad y el reintento corrio solo (2 filas
  Error con el mismo fired_at, backoff 5 -> 10 min).

**MODULO 000889 COMPLETO (P1..P4).** Pendiente de configuracion (no de codigo): SMTP para que el correo
entregue; linea de WhatsApp asignada al encargado. Slack/SMS requeririan integrarse desde cero.

---

## Sesion 2026-07-15 - Dos pendientes menores (post-deploy 0bf057d)

**Agentes**: Claude (Opus 4.8). Contexto: ya en prod el arranque de tareas-proceso (olas A-D) +
tareasprogramadas (000889) + IMenuProvisioning. Cierre de los dos pendientes menores que quedaban.

- **Item 1 - config demo COT-COM (SQL, local)**: los 4 nodos del flujo de cotizacion/comercial ya
  tenian cargo salvo Facturacion y Entrega (sin `WorkflowNodePolicy`); ademas el concepto no tenia
  columna de cierre. Se insertaron 2 policies (Facturacion->Aprobador/admin; Entrega->Asesor
  Comercial/operator, `ON CONFLICT DO NOTHING`) y se fijo `task_board_column_id` = Completado.
  Verificado: los 4 nodos con cargo + columna de cierre = Completado.
- **Item 2 - guardia form-first vs form-por-nodo (`9a7982b`)**: en Conceptos (000270), aviso ambar no
  bloqueante cuando el formulario de ADMISION del concepto coincide con el del PRIMER nodo del flujo
  (se pediria dos veces). Se resuelve sobre el flujo GUARDADO
  (`ResolveFirstStepAsync`->NodeId->`GetWorkflowNodeFormAsync`); al cambiar el flujo en vivo se limpia
  la cache para no avisar en falso. Build SuperAdmin verde (0 errores). Push a main + fase-0/clon-backbone.

**Nota**: cambio solo-codigo/UI; prod sigue en 0bf057d (no requiere redeploy). El aviso viajara a prod
en el proximo despliegue rutinario.

---

## Sesion 2026-07-15 (cont.) - "Traer" de CUBOT.travels: mirada + backport selectivo

**Agentes**: Claude (Opus 4.8) + 6 subagentes (4 de reconocimiento + 2 de planificacion).

**Contexto**: el usuario pidio "traer todo" del proyecto hermano CUBOT.travels. Reconocimiento con
agentes revelo que travels NO es un modulo ajeno: es otro fork del MISMO backbone (cubotcrm), y
ECOREX ya tiene esos modulos. Veredicto por area:
- **Spine** (billing/Wompi, auth, Google, superadmin, Mi cuenta, API leads): YA en ECOREX 1:1
  (ancestros compartidos). Nada que traer.
- **Agentes IA + MCP**: ECOREX va POR DELANTE (function-calling/tools reales; travels usa el motor
  viejo de marcadores de texto). Duplicar/cupos/lista negra/dispatcher ya existen. MCP destinos = viajes.
- **WhatsApp/conversaciones/chat**: a la par o mas evolucionado (Emulator, dual-provider). Unico
  faltante real: proveedor **YCloud**.
- **Pipeline CRM**: dos CRMs ya (Pipeline heredado + Gestor 000740). Avances de travels = menores
  (asignar asesor inline, gestor de columnas). Decision estrategica pendiente (no se toco).
- **Destinos/Planes de Viaje**: especifico de viajes, descartado.

**Mecanica**: cherry-pick cross-repo NO viable (namespaces CubotTravels->Ecorex, net9->net10, dual
migrations). Port manual, archivo por archivo.

**Lo que se trajo (eleccion del usuario):**
1. **Robustez del runtime del agente** (`9c3de08`): candado de asesor humano (se calla si el lead esta
   asignado y activo; retoma si esta archivado), retry con backoff 503 en las 2 rutas de
   function-calling, pausa entre adjuntos. Sin migraciones. NO se porto el modelo de marcadores.
2. **Proveedor YCloud** (`92eb858`): 3er BSP de linea (par de Evolution/Cloud). Cliente HTTP portado
   1:1, enum+entidad+DTOs+servicios+UI, migracion DUAL AddYCloudProvider (3 cols + indice). El canal
   WhatsApp del 000889 lo hereda sin tocarse. Fuera de alcance: sometimiento real de HSM a Meta via
   YCloud y webhook entrante YCloud (el proveedor es saliente).

**Verificado**: build sln verde (0 errores), unit 379+35 verdes, migraciones duales simetricas.
Ambos commits en main + fase-0/clon-backbone. **No desplegado** (prod sigue en 0bf057d).

---

## Sesion 2026-07-15 (cont.) - DEPLOY a produccion (robustez agente + YCloud)

**Accion**: despliegue a prod (root@10.0.0.3, /opt/ecorex, build-from-git de fase-0/clon-backbone @ 50f46ec).

- **Verificacion previa**: main == origin/main == origin/fase-0/clon-backbone == 50f46ec; worktrees de
  agente (focused-joliot, funny-bell) limpios y ya en main; worktree de preview solo con scratch
  obsoleto (version vieja de ecorex-bpmn.js que main ya supera + HTMLs de prototipo) -> nada que traer.
- **Backup previo**: backups/ecorex-2026-07-15-0542.sql.gz.
- **Rebuild**: docker compose -f docker-compose.from-git.yml build --no-cache (BUILD_OK ~6 min) + up -d.
- **Migracion aplicada al arranque**: 20260715102503_AddYCloudProvider (log EF + columnas
  y_cloud_api_key_encrypted / y_cloud_phone_number_id / y_cloud_waba_id presentes en whats_app_lines).
- **Salud**: GET /login -> HTTP 200 al primer intento; "Now listening on http://0.0.0.0:8080".
- Incluye robustez del runtime del agente (9c3de08) y proveedor YCloud (92eb858).

---

## Sesion 2026-07-15 (cont.) - Consola SQL admin (000077), backport de VISAL

**Agentes**: Claude (Opus 4.8). Otro proyecto hermano del backbone (C:\DesarrolloIA\Visal, VISAL.git)
tenia una consola SQL para consultar el sistema. El menu de ECOREX ya listaba el item 000077 como
stub (modulo/sql-admin) sin modulo detras. Se desarrollo respetando el diseno de ECOREX.

- **Dominio**: SqlConsoleLog (append-only, NO tenant-scoped; el tenant del actor es dato, no filtro).
- **Application**: ISqlConsoleService + DTOs en Ecorex.Application.Admin.
- **Infrastructure/Sql**: SqlConsoleService ejecuta SQL crudo via DbConnection del EcorexDbContext
  (proveedor activo por DI). SELECT -> filas (hasta 1000); DML/DDL -> ExecuteNonQuery. AUDITA SIEMPRE
  en sql_console_logs. Explorador de tablas guardado por IsNpgsql() (pg_stat_user_tables) con mapa de
  descripciones PROPIO de ECOREX (tareas/flujos/formularios/conceptos/...), no el clinico de VISAL.
- **Migracion DUAL** AddSqlConsoleLogs (PG + SQL Server): tabla + indices.
- **UI** /sql-admin con el sistema de diseno de ECOREX (tokens --brand/--surface/--line/--ink,
  monocromo, theme-aware) NO el azul de VISAL: explorador arbol + editor Ctrl+Enter + resultados +
  historial + export CSV. Policy Perm:sql-admin:View (Owner/Admin por gobierno).
- **Menu**: item 000077 pasa de stub a pagina real 'sql-admin' + Ready; reconcile repunta el nodo ya
  sembrado en prod al arrancar (patron 000889).

**Verificado**: build sln verde, unit 379/379. Commit b3004e5 en main + fase-0/clon-backbone.
**Pendiente**: deploy a prod (aplica migracion AddSqlConsoleLogs al arrancar + reconcile repunta el menu).

---

## Sesion 2026-07-15 (cont.) - Gestion de cuenta/planes: YA EXISTIA + catalogo demo

**Peticion**: traer de "cubot.crm" la gestion de cuenta donde el tenant selecciona plan y el super
admin define planes. **Hallazgo (3a vez el mismo patron)**: es backbone compartido de la familia
cubotcrm y ECOREX YA lo tiene a paridad total: entidades SaasPlan/SaasPlanLimit/TenantSubscription,
servicios PlanAdminService/SubscriptionAdminService/RecurringBillingService/WompiCheckoutService,
pagina /plans (super admin define planes, policy PlatformOperator) y /mi-cuenta (tenant: plan, limites,
"Cambiar de plan", checkout Wompi). No existe carpeta cubot.crm; los tamanos de Cuenta/Plans son casi
identicos a los hermanos (mismo codigo del backbone). No habia nada que portar.

**Lo unico que faltaba (operativo)**: solo habia 1 plan sembrado, asi que la SELECCION no tenia
opciones. Se agrego `EnsureDemoPlansAsync` (idempotente por nombre): catalogo Free (\$0) / Pro (\$49k) /
Empresa (\$99k) con limites; global; enganchado al bloque demo de Program.cs (NO corre en prod bajo
SkipDemoSeed, donde el super admin define sus planes reales). Commit `5b3fee0`, sin migracion.

**Validado en Chrome** (demo SKY SYSTEM, Owner): /mi-cuenta "Cambiar de plan" lista los 3 planes;
cambio Empresa->Pro aplica y actualiza limites (25 usuarios / 500k IA / 5 lineas). BD: 3 filas en
saas_plans. No desplegado (cambio solo-demo; prod define planes por /plans).

---

## Sesion 2026-07-15 (cont.) - Contenedor de datos: duda de relaciones + ejemplo demo

**Duda del usuario**: "no veo como crear una relacion entre tablas". Diagnostico: la relacion NO se
dibuja arrastrando; es un CAMPO de tipo Reference (N:1) o RelationMany (N:N) con tabla destino. El
selector "Tabla destino" solo lista OTRAS tablas del modelo; con un modelo de 1 tabla salia vacio y
parecia que la funcion no existia.

- **Fix UX** (`f578b8f`): cuando se elige un tipo de relacion y no hay otra tabla, se muestra una guia
  ("crea y guarda una segunda tabla, luego elige la tabla destino") en vez de un selector vacio.
- **Ejemplo demo** (`f78d3e8`): EnsureDataModelDemoAsync siembra el modelo "Ventas (demo)" con 3 tablas
  (Clientes/Productos/Pedidos) + 2 relaciones (Pedidos.Cliente->Clientes N:1; Pedidos.Productos<->Productos
  N:N). Idempotente, solo Development. Validado en Chrome: el lienzo ER dibuja la linea morada (N:1) y la
  naranja punteada (N:N). Sin migracion. No desplegado (solo-demo).

---

## Sesion 2026-07-15 (cont.) - Contenedor de datos: relaciones como ENTIDAD (aristas del ER)

**Feedback del dueno**: el 'tipo' de un campo mezclaba dos propiedades ortogonales (tipo de dato vs.
relacion). Se separo: una relacion es ahora una entidad de primera clase.

- **Dominio**: `DataModelRelation` (ModelId, FromTableId->ToTableId, Kind N:1/N:N, Name) + enum
  `DataModelRelationKind`. Se elimina `ReferencedContainerId` de la columna; Reference/RelationMany
  quedan deprecados en el enum (solo para el backfill). Submodel (anidamiento) intacto.
- **Servicios**: DataModelService deriva relaciones de la entidad; AddRelation/DeleteRelationAsync;
  DeleteTableAsync limpia aristas primero. DataContainerService: columnas solo escalares.
- **UI**: se quitan Referencia/Relacion del dropdown de tipo; nuevo panel 'Relaciones' (lista +
  form origen/destino/cardinalidad + eliminar). El lienzo dibuja las lineas desde la entidad.
- **Migracion DUAL RelationsAsEntity + BACKFILL**: convierte relaciones-columna en aristas y neutraliza
  las columnas. Orden: crear tabla -> backfill -> borrar columnas -> drop referenced_container_id.
- **Fase 2 diferida**: re-cablear el vinculo dato-a-dato (fila-a-fila) contra la relacion-entidad; el
  backfill descarta esos links (el esquema de relacion si se preserva). Riesgo marcado para prod.

**Verificado**: build sln 0/0; unit 379/379; backfill aplicado en local (2 aristas Pedidos->Clientes N:1
/ Pedidos->Productos N:N, 0 columnas de relacion restantes). Commit `8b980e9` en main + fase-0. NO
desplegado. La verificacion visual en Chrome quedo pendiente por inestabilidad del dev server (se cae al
arrancar); la capa de datos si se verifico por psql.
