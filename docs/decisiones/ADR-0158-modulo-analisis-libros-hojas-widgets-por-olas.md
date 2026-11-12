# ADR-0158: Modulo de Analisis (Libros -> Hojas -> Widgets), plan por olas

**Status:** Proposed
**Date:** 2026-11-08
**Deciders:** Alexander Cuartas (dev principal)
**Origen:** hand-off "Modulo de Analisis con Hojas y Libros de widgets" (proyecto hermano PROPIA,
tablero `/dashboard-ph`), adaptado a la arquitectura real de ECOREX.

## Contexto

Se quiere un modulo para gestionar **libros de indicadores**: el usuario crea un **Libro**, dentro
del libro crea **Hojas** por nombre, y en cada hoja agrega los **widgets** (KPI, barras, dona,
linea/area, heatmap, semaforo, tabla) que quiera, los mueve/redimensiona y comparte el libro.

Los indicadores salen de tres fuentes del sistema:

| Fuente | Entidades | Estado en el motor de reportes |
|---|---|---|
| Directorio | `Tercero` (+ `TerceroCategoria`, `TerceroVinculo`, `TerceroNota` con `Valor`) | **NO existe fuente** |
| Tareas | `TaskItem` (+ `TaskBoard/Column`, `TaskWorkLog`, `WorkflowInstance`, `Project`, conceptos) | `native:taskitem` (`TaskItemReportSource`) |
| Contenedores | `DataModel` -> `DataContainer` -> `Row` -> `Cell` (EAV string) | `container:{guid}` (`ContainerReportReader`) |

El hand-off propone un registro nuevo `IWidgetDataProvider`. **ECOREX ya tiene esa capa**: el motor
de reportes (`Ecorex.Application/Reporting`, ADR-0051/0062/0064/0068):

- `ReportQuerySpec` declarativo (fuente + filtros + group by + agregados + orden + top), sin SQL.
- `IReportDataSource` = unico camino a los datos, valida contra `IReportCatalog`, tenant-safe por el
  filtro global.
- `PanelSpec` ya modela `Kpis` + `Widgets` (line/bar/donut/pareto/matrix/table) + `Filters` + `Where`.
- `EChartBuilders` (ECharts vendorizado en `wwwroot/lib/echarts`) y paneles en `/reportes/*`.

## Decisiones

### D1. Los datos de un widget = un `ReportQuerySpec` (no un registro paralelo)

Cada widget guarda su consulta como spec del motor existente. Se agrega SOLO una capa delgada
`Analisis/WidgetShapes` que convierte el `ReportDataSet` en las formas normalizadas del hand-off:

- `Escalar` (KPI, semaforo) - `Serie` (linea/area) - `Categorias` (barras/dona)
- `Heatmap` (celdas por dia) - `Tabla`

Para lo que el spec NO puede expresar (tiempo en etapa, semaforo al dia/por vencer/vencidas, delta
vs periodo anterior) existen **indicadores curados**: `IIndicadorCurado` (Key + Forma + QueryAsync)
registrados por DI, que devuelven las MISMAS formas. El widget referencia `spec` O `indicadorKey`.
Resultado: un solo motor de datos para reportes, paneles y libros.

### D2. Nuevas fuentes / campos en el catalogo

- `native:tercero` (NUEVA): Tipo, Perfiles (flags -> texto por perfil), Estado, Vendedor, Ciudad,
  Sector, Categoria, Empresa, CreatedAt.
- `native:tercero-nota` (NUEVA): Accion, Categoria, Subcategoria, Concepto, Autor, Valor, CreatedAt
  (bitacora comercial con monto).
- `native:taskitem` (EXTENDER): Cliente (`TerceroId` -> Nombre), Vendedor del cliente,
  `ColumnEnteredAt`, `CloseReason`, Proyecto (nombre), Vencida (calculado).
- `native:worklog` (NUEVA): horas por usuario/tarea/tablero/fecha desde `TaskWorkLog`.
- Contenedores: sin cambios de contrato. Cuidado de rendimiento (EAV parseado en memoria): tope de
  filas + recomendar tablas DERIVADAS (ADR-0127) como fuente de indicadores pesados.

### D3. Render: SVG/CSS server-side (v1), ECharts como widget avanzado

Se adopta el enfoque del hand-off para la v1: KPI, barras, dona, semaforo, heatmap y tabla en
**SVG/CSS inline renderizado por Blazor**, sin interop. Razones: imprime/exporta bien (libros
compartidos y PDF), sin carreras de ciclo de vida, theme-aware con tokens CSS. Linea/area tambien en
SVG (receta del hand-off). ECharts (ya integrado) queda como tipo "avanzado" opcional (zoom, series
largas) en una ola posterior. **Regla:** todo numero que vaya a atributo SVG/CSS pasa por
`Inv()` (InvariantCulture); es-CO usa coma decimal y rompe el atributo.
Tokens visuales: los del prototipo `ECOREX.dc.html` (regla de fidelidad), nada de hex sueltos.

### D4. Sin API HTTP para la consola

A diferencia de PROPIA (pagina -> HTTP -> API), la consola de ECOREX inyecta servicios de
`Application` directo (asi trabajan `TareasDashboardPanel` y los demas paneles). El tenant lo
resuelve el contexto del circuito + filtro global. Endpoints `/api/analisis/*` solo si luego se
necesitan para movil/MCP/agentes. Permisos por policy: `Analisis.Ver`, `Analisis.Editar`,
`Analisis.Compartir`.

### D5. Concurrencia del DbContext (ADR-0157) - bloqueante de diseno

Una hoja con N widgets dispara N consultas. Con el DbContext unico por circuito esto produce el
"second operation was started". Regla: **cada consulta de widget abre su propio contexto via
`IApplicationDbContextFactory`** (ADR-0138) y los widgets cargan en paralelo con contextos
independientes (o en serie si la fuente no esta migrada a factory). El motor de reportes
(`ReportDataSource` y sus fuentes) debe aceptar contexto efimero; esto se hace en la Ola 1.

### D6. Modelo de datos (tenant-scoped, dual PG + SQL Server)

```
AnalisisLibro  : TenantEntity, ISoftDeletable
  Nombre, Descripcion, OwnerTenantUserId, Visibilidad (Privado|CompartidoEnTenant|EnlacePublico),
  AllowedUserIdsJson (patron ADR-0117), Version (concurrencia optimista)
AnalisisHoja   : TenantEntity, ISoftDeletable
  LibroId, Nombre, Orden, FiltrosJson (filtros globales de la hoja: rango de fechas, tablero...)
AnalisisWidget : TenantEntity
  HojaId, TipoWidget (kpi|barras|dona|linea|area|heatmap|semaforo|tabla|echarts),
  SpecJson (ReportQuerySpec) | IndicadorKey, ConfigJson (titulo, formato, colores, umbrales),
  Orden, ColSpan (1..4), RowSpan
AnalisisLibroToken : TenantEntity
  LibroId, TokenHash (SHA-256, patron FormToken), CreatedAt, RevokedAt, ExpiresAt
```

Migraciones en AMBOS motores; borrado de libro/hoja = soft-delete; cambios de layout en transaccion.
Aislamiento: filtro global como el resto del repo (y RLS cuando el repo la aplique de forma general).

### D7. Compartir

1. **Dentro del tenant:** `CompartidoEnTenant` (todos con `Analisis.Ver`) o lista `AllowedUserIdsJson`.
   Solo el dueno (o Admin) edita; los demas ven en modo lectura.
2. **Enlace publico de solo lectura:** pagina anonima `/a/{token}` (mismo patron que `/f/{Token}` y
   `/d/{Token}`): token opaco, se guarda el hash, rotable/revocable, con expiracion opcional,
   rate-limit. Se comparte la DEFINICION (datos en vivo), no una foto. Solo `Analisis.Compartir`
   puede activarlo (expone datos del tenant fuera del sistema).

### D8. Mover / redimensionar

CSS grid de 4 columnas, `grid-column: span ColSpan`. Primero botones (mover izq/der, ancho 1..4);
luego drag & drop con HTML5 nativo, igual que ya lo hacen `FormDesigner` / `ConfiguracionMenu` (no se
agrega SortableJS). Persistencia del layout en batch.

### D9. Componente reutilizable desde otros modulos

El modulo `/analisis` es solo UNA superficie. El nucleo es un componente Razor compartido
(`Shared/Analisis/`) con tres niveles de embebido:

- `<AnalisisLibroView LibroId="..." />` - libro completo (tabs de hojas).
- `<AnalisisHojaView HojaId="..." />` - una sola hoja.
- `<AnalisisWidgetView WidgetId="..." />` - un widget suelto (p.ej. un KPI en el Inicio).

Todos aceptan `Contexto` (diccionario de parametros) que se inyecta como filtro a cada spec. Asi una
hoja definida una vez se reutiliza filtrada por el registro donde se embebe:

- Ficha de tercero (Directorio): `Contexto = { tercero = <id> }` -> la hoja muestra solo ese cliente.
- Tablero / proyecto: `{ tablero = <id> }` / `{ proyecto = <id> }`.
- Contenedor publicado: `{ contenedorFila = <id> }`.

Cada widget declara que parametros de contexto entiende (`ContextBindingsJson`: parametro -> campo del
spec por fuente). Un parametro que el widget no entiende se ignora (no rompe). El modo (Ver/Editar)
y los permisos los decide el host; embebido = Ver por defecto. Los modulos anfitriones solo guardan
`LibroId`/`HojaId` en su propia configuracion (p.ej. "hoja de indicadores de la ficha" por categoria
del directorio). Mismo patron que `PanelSpec` embebido en la galeria.

### D10. Widgets que combinan datos de todo el sistema

Un widget puede mezclar fuentes (Directorio + Tareas + Contenedores). Dos mecanismos, ambos en
memoria sobre `ReportDataSet` ya filtrados por tenant (nunca SQL cruzado):

1. **Series compuestas**: cada serie/KPI del widget tiene su propio spec y fuente; se alinean por la
   dimension comun (mes, vendedor, etapa). Ej: linea "Tareas cerradas (Tareas)" vs "Ventas
   (Contenedor Siigo)" por mes. Ej: KPI "Ventas por cliente activo" = suma(contenedor) / conteo(directorio).
2. **Cruce por llave**: un spec principal + N specs secundarios unidos por una llave mapeada
   (`JoinsJson`: fuenteA.campo = fuenteB.campo, tipo left/inner). Llaves tipicas:
   `TaskItem.Cliente(TerceroId)` = `Tercero.Id`; `Tercero.IdValor (NIT)` = columna NIT del contenedor;
   `Tercero.Vendedor` = columna vendedor del contenedor. Generaliza el `PanelJoin` existente.
   Normalizacion de llave configurable (trim, mayusculas, quitar digito de verificacion del NIT).

Formulas entre series (`a / b`, `% de`) para ratios. Topes de filas por fuente antes del cruce y
mensaje claro si se exceden. Los cruces se prueban con datos de ambos motores.

### D11. Alcance v1 = COMPLETO

Decidido 2026-11-08: la v1 incluye compartir dentro del tenant Y enlace publico (`/a/{token}`),
el componente embebible y los widgets combinados. Las olas 1-6 son todas v1; solo la Ola 7 es
opcional.

## Plan por olas

Cada ola cierra con build verde, tests verdes en ambos motores, entrada en `PROGRESO.md` y commit.
Deploy solo con OK explicito.

### Ola 1 - Datos (sin UI)
- Fuentes nuevas `native:tercero`, `native:tercero-nota`, `native:worklog`; extender `native:taskitem`.
- Capa `WidgetShapes` (ReportDataSet -> Escalar/Serie/Categorias/Heatmap/Tabla) + `IIndicadorCurado`
  con 2 curados de ejemplo: semaforo de vencimiento de tareas y tiempo promedio por etapa.
- Fuentes de reportes compatibles con contexto efimero (D5).
- Tests: unitarios de las formas; integracion dual de cada fuente nueva + aislamiento cross-tenant.
- Verificable: las fuentes nuevas aparecen en el catalogo de `/reportes/tablero` y `/reportes/ia`.
- **Estado 2026-11-08: HECHA.** Motor comun `InMemoryReportEngine`; fuentes nuevas con
  contexto efimero; `native:taskitem` usa el motor en memoria solo si el spec toca campos extendidos,
  varios grupos o Sum/Avg (la ruta EF clasica queda intacta). Pendiente: Categoria en `native:tercero`;
  contenedores y formularios siguen en el DbContext scoped (Ola 2 corre cada widget en su scope).

### Ola 2 - Esqueleto del modulo
- Entidades D6 + migraciones dual + `AnalisisService` (CRUD libro/hoja/widget, soft-delete,
  transacciones, Version).
- Modulo en el seeder (area Sistema, ruta `/analisis`) + policies + nodo de menu.
- Pagina `/analisis`: selector de libro, "Nuevo libro", tabs de hojas, "+ Nueva hoja" (pide nombre),
  grilla 4 columnas.
- Widgets SVG: KPI, barras, dona. Mover por botones + selector de ancho. Widget agregado con un spec
  fijo de ejemplo (sin picker aun).
- **Estado 2026-11-08: HECHA.** Entidades + migracion dual `AddAnalisisLibros`; `IAnalisisService`
  (contexto efimero, soft-delete, transacciones, Version) + `IAnalisisWidgetRunner` + 8 widgets de
  ejemplo; modulo `000900` en la seccion Reportes; pagina `/analisis` y componente reutilizable
  `Shared/Analisis/AnalisisWidgetCard`. Los widgets de una hoja corren en serie (no en paralelo con
  scopes propios: eso queda para cuando todas las fuentes usen la fabrica). Arreglado de paso un bug
  previo de `TaskItemReportSource` (agrupar por Etapa/Tablero con datos nulos lanzaba excepcion).

### Ola 3 - Catalogo + picker + configuracion
- Catalogo: tipos de widget x formas compatibles x fuentes del `IReportCatalog` + curados.
- Modal "Agregar / editar widget": tipo -> fuente -> medida (agg + campo) -> dimension -> filtros ->
  titulo/formato/colores/umbrales. Vista previa en vivo.
- Filtros globales de la hoja (rango de fechas, tablero, vendedor) que se inyectan al spec de cada
  widget. Estados vacio/error/cargando por widget (un widget roto no tumba la hoja).
- **Estado 2026-11-08: HECHA.** `WidgetDiseno`/`WidgetDisenoMapper` (diseno <-> spec + validacion),
  `WidgetConfig` (formato, tono, top, umbrales), `HojaFiltros` + `HojaFiltroBinder` (mapa conocido de
  fuentes nativas, convencion por nombre en contenedores y override por widget en ContextBindingsJson),
  KPI sin agrupacion resuelto en memoria sobre el modo tabular, modal reutilizable
  `AnalisisWidgetEditor` con vista previa, editar widget, barra de filtros de hoja y
  renombrar/eliminar libro. Deuda: limites de fecha en UTC (no zona del tenant).

### Ola 4 - Organizar + mas tipos + embebible
- Drag & drop nativo + redimensionar por handle (cicla ColSpan) + layout batch.
- Modo Ver / Editar (acciones solo en edicion).
- Tipos: linea/area, heatmap, semaforo, tabla. Duplicar widget / duplicar hoja.
- Componente embebible (D9): `AnalisisLibroView` / `AnalisisHojaView` / `AnalisisWidgetView` con
  parametro `Contexto`; `ContextBindingsJson` por widget. Primer anfitrion: ficha de tercero del
  Directorio (hoja filtrada por el cliente). Segundo: Inicio (widgets sueltos).
- **Estado 2026-11-08: HECHA.** Modo Ver/Editar, arrastrar y soltar nativo + layout en lote, asa de
  ancho, duplicar widget/hoja; tipos linea, area, mapa de calor, semaforo y tabla (segunda agrupacion
  solo en fuentes nativas); `AnalisisHojaView`/`AnalisisWidgetView`/`AnalisisLibroView` con `Contexto`
  (`AnalisisContexto`: parametro `tercero`); anfitriones ficha del tercero (pestana Indicadores) e
  Inicio (widgets fijados), configurados por un Admin y guardados en `TenantConfiguration`. Compuerta
  unica por circuito reutilizando `CircuitFormGate`. Pendiente: arrastre tactil y parametros de
  contexto tablero/proyecto/fila de contenedor.

### Ola 5 - Widgets combinados (todo el sistema)
- Series compuestas (cada serie con su fuente) alineadas por dimension + formulas entre series.
- Cruce por llave (`JoinsJson`) con normalizacion (NIT sin DV, trim, mayusculas), generalizando
  `PanelJoin`. Paso "Combinar fuentes" en el modal del widget.
- Topes de filas por fuente + mensaje claro. Tests de cruce Directorio x Tareas x Contenedor en
  ambos motores.

### Ola 6 - Compartir + imprimir
- Visibilidad en tenant + lista de usuarios.
- Enlace publico `/a/{token}` rotable/revocable, expiracion opcional, rate-limit.
- Imprimir / PDF del libro (una hoja por pagina) con la tuberia PDF existente (Puppeteer, paginador).

### Ola 7 - Opcional (fuera de v1)
- Widget "avanzado" ECharts (zoom, series largas).
- Armado de hojas por IA (reusar la autoria por IA de `/reportes/ia`).
- Snapshots historicos (foto congelada de un libro), si se pide.

## Consecuencias

- (+) Un solo motor de datos: lo que se agregue al catalogo sirve para reportes, paneles y libros.
- (+) Directorio queda reportable (hoy no lo es).
- (-) La Ola 1 toca el motor de reportes (contexto efimero) y puede afectar los paneles existentes:
  regresion obligatoria de `/reportes/tareas`, `/reportes/siigo-ventas`, `/reportes/ocs`.
- (-) Contenedores grandes: el EAV se agrega en memoria; se mitiga con tope y tablas derivadas.

## Preguntas resueltas (2026-11-08)

1. Prototipo: `ECOREX.dc.html` tiene la pantalla "Indicadores del sistema" (KPIs con icono y delta,
   barras semanales con degradado de marca, barras de progreso "Uso por modulo"). NO tiene libros ni
   hojas: el estilo de widgets sale de ahi; la estructura libro/hojas es nueva (maqueta aparte).
2. `/analisis` es un modulo nuevo Y un componente reutilizable desde otros modulos (D9).
3. Alcance completo en v1, incluido el enlace publico (D11).
4. Si: un libro puede tener hojas con widgets que combinan datos de todo el sistema (D10).

## Addendum 2026-11-09 - Olas 5 y 6 + pendientes (implementado, SIN deploy)

- **Galeria + modal grande:** `/analisis` lista los libros como tarjetas; el libro se ve y se edita en
  un modal grande (pantalla completa en telefono). `?libro=&hoja=` abre el modal directo.
- **Filtro de tablero en "Crear indicador":** por `BoardId` (campo nuevo de Actividades, solo filtrable)
  y `IsArchived = false`; ya no por nombre (los homonimos se mezclaban) ni con archivadas.
- **Fechas de la hoja en la zona del tenant (regla 9):** `IAnalisisZonaHoraria` lee `Tenant.TimeZoneId`;
  en fuentes NATIVAS (marcas de tiempo) los limites del rango son las 00:00 locales con su offset. En
  contenedores (fechas sin hora) se mantiene el dia calendario: poner offset excluiria el primer dia.
  Pendiente menor: `MonthKey` (agrupar por mes) sigue en UTC.
- **Sugerencias de valores:** los filtros de texto agrupables del modal ofrecen (datalist) los 50
  valores mas frecuentes de la fuente, consultados por la misma compuerta que la vista previa.
- **Tactil:** el HTML5 drag & drop no existe en touch; se agrega "tocar el asa -> Soltar aqui" (sin JS
  ni librerias), que tambien sirve con raton. El arrastre con raton se conserva.
- **Directorio:** campo `Categoria` (pertenencias `TerceroCategoria` del motor Modular, con el titulo de
  `DirectorioCategoria`; un tercero en varias categorias muestra "A, B").
- **Ola 5 (combinado):** `CombinadoDiseno` en `WidgetConfig.Combinado` con `IndicadorKey =
  "analisis.combinado"` (sin columnas nuevas ni migracion). Hasta 4 series, cada una con fuente,
  medida y campo de cruce; llave normalizada (texto sin tildes/mayusculas, NIT sin DV, exacta); union o
  interseccion; hasta 3 formulas (A-D, + - * /, parentesis; parser propio, sin eval). Tope de 5.000
  llaves por serie con aviso. KPI = primera formula sobre los TOTALES. Las series se consultan en serie.
  Se generaliza la idea de `PanelJoin` alineando agregados por llave (no un join fila a fila).
- **Ola 6 (compartir):** dialogo "Compartir": Privado + lista de usuarios, o Compartido en el tenant
  (dueno o Admin). Enlace publico `/a/{token}`: token de 32 bytes, se guarda solo el SHA-256, rotar
  revoca el anterior, expiracion opcional, revocar vuelve a Compartido. Lo activa SOLO un Admin (no
  existe la accion de permiso "Compartir"; se usa Admin por exponer datos fuera del sistema). Tope de 30
  aperturas por minuto por IP (`UseRateLimiter` solo en `/a`). El visor fija el tenant del token como
  ambient (`AmbientTenantContext.Begin`) en cada lectura. Imprimir: hoja de estilos de impresion del
  visor (`window.print`); el PDF por la tuberia Puppeteer queda pendiente.

## Addendum 2026-11-10 - Cierre de las 3 deudas (PDF, mes en zona del tenant, filtros por serie) (SIN deploy)

- **PDF del libro (boton "Descargar PDF"):** render REAL por la tuberia Puppeteer (`IQuotePdfRenderer`),
  no mas `window.print`. Mecanica: un endpoint AUTORIZADO por la cookie (`GET /analisis/libros/{id}/pdf`,
  policy `Perm:analisis:View`) comprueba que el usuario puede ver el libro (`ObtenerLibroAsync`), emite un
  ticket de UN SOLO USO y corta vida (`IAnalisisPdfTickets`, singleton sobre `IMemoryCache`, token de 32
  bytes, 2 min, se borra al leerlo) y pide al Chromium interno que navegue a `/a/pdf/{ticket}`. Esa pagina
  (`AnalisisPdf.razor`) es ESTATICA (SSR, sin render mode interactivo) y AllowAnonymous: corre TODOS los
  widgets de TODAS las hojas dentro de `OnInitializedAsync` (antes de emitir el HTML, con el tenant del
  ticket como ambient), por lo que Chromium recibe la pagina ya pintada y no necesita circuito SignalR
  (el visor publico `/a/{token}` SI es interactivo y seria fragil para el render headless). Reusa
  `AnalisisWidgetCard` (mismo SVG/tono que en pantalla) y el CSS de la app; opt-in del pie "Pagina X de Y"
  via `<meta ecorex-pdf-footer>`. El ticket evita exponer el libro: sin BD nueva y sin enlace publico
  (sirve libros privados/compartidos que su enlace publico no serviria). Decision: ticket en memoria del
  proceso (el Chromium corre en el mismo contenedor) en vez de un token persistido.
- **Agrupar por mes en la zona del tenant (regla 9):** `InMemoryReportEngine.MonthKey` admite
  `TimeZoneInfo?` (null = UTC, retrocompatible). La zona viaja por `ReportContext.TenantZone`, que el
  `AnalisisWidgetRunner` puebla una vez por circuito desde `IAnalisisZonaHoraria`; las fuentes
  (Actividades `CreatedMonth`/`ClosedMonth`, Horas, Directorio, bitacora) la pasan a `MonthKey`. Un
  reporte generico sin zona agrupa igual que antes. Cierra el pendiente del addendum anterior.
- **Filtros por serie del combinado:** el modelo (`SerieCombinada.Filtros`) y `CombinadoDiseno.SpecDe`
  (suma los filtros de la serie a los de la hoja/contexto) YA existian; faltaba la UI. Se agrega el
  editor de filtros por serie en `AnalisisCombinadoEditor` (campo/operador/valor con los campos
  filtrables de la fuente de esa serie), asi la serie A puede contar solo aprobadas y la B todas. Cambiar
  la fuente de la serie limpia sus filtros (son especificos de la fuente).

## Addendum 2026-11-10 - Exportar / importar un libro entre sistemas (SIN deploy)

Peticion: poder llevar un libro de un sistema/tenant a otro (enviarlo). Se agrega un PAQUETE JSON
portable, siguiendo el precedente del export/import de Contenedor (ADR-0132): se lleva la DEFINICION,
nunca los datos ni las referencias que no cruzan sistemas.

- **Paquete (`AnalisisPortabilidad.cs`, Application):** `AnalisisLibroPaquete` -> `AnalisisHojaPaquete`
  -> `AnalisisWidgetPaquete`. Lleva SOLO nombre, descripcion, hojas (con su `FiltrosJson`) y widgets
  (Tipo, Titulo, SpecJson, IndicadorKey, ConfigJson, ColSpan, ContextBindingsJson). NO lleva ids, tenant,
  dueno, visibilidad, enlaces ni filas de datos. Formato `ecorex.analisis.libro` version 1; topes
  defensivos al importar un JSON arbitrario (50 hojas, 400 widgets).
- **Exportar:** `ExportarAsync(libroId, actor)` reusa `ObtenerLibroAsync`, que aplica la visibilidad
  (devuelve null si el actor no puede ver el libro), mapea a paquete y serializa con `AnalisisJson.Options`.
  Nombre de archivo `<libro saneado>.ecorex-libro.json`. Endpoint `GET /analisis/libros/{id}/export`
  (policy `Perm:analisis:View`, actor por cookie + `IsUnrestrictedAsync`) que descarga el JSON.
- **Importar:** `ImportarAsync(json, actor)` valida formato/version y, en UNA transaccion, crea un libro
  NUEVO privado del usuario que importa, con sus hojas y widgets (ids nuevos). Decisiones:
  - *Fuentes nativas* (Directorio/Actividades/Horas/bitacora) son iguales en todo sistema: viajan intactas.
  - *Referencias que pueden no existir en el destino* NO abortan la importacion, se AVISAN: un widget que
    usa un Contenedor (`container:` en el spec/config, sus ids son locales) o un indicador curado que no
    existe aqui queda importado pero con un aviso para revisar/reasignar la fuente en el destino.
  - *Widget con definicion incompleta* (sin titulo, o con spec Y curado a la vez, o sin ninguno) se saltea
    con un aviso, sin romper el resto del libro.
  - Paquete sin hojas -> nace con "Hoja 1" (como `CrearLibroAsync`).
- **UI:** galeria con boton "Importar libro" (`<InputFile>` oculto, `.json`, solo si el usuario puede
  crear; lee por la compuerta del circuito, tope 8 MB, abre el libro importado y muestra los avisos);
  modal del libro con boton "Exportar" junto a "Descargar PDF".
- **Verificado:** build Application + SuperAdmin verde; unitarias Ola 5/6 27/27; integracion DUAL
  (PG + SQL Server) `Portabilidad_ExportaEnUnTenant_ImportaEnOtro_ConEstructuraYAislamiento` 2/2 (round-trip
  cross-tenant con estructura/filtros/curado intactos, aislamiento por tenant y rechazo de JSON ajeno);
  format limpio; ASCII.

## Addendum 2026-11-12 - Ola 7a (motor visual ECharts) y 7b (tipos nuevos + KPI con periodo) (SIN deploy)

Peticion (tras una auditoria del modulo): graficos mucho mas dinamicos. La auditoria encontro que los 8 tipos
se dibujaban con SVG/CSS a mano (D3): estaticos, sin animacion, tooltip basico del navegador, sin zoom ni
leyenda interactiva; que el valor reservado `Echarts = 8` nunca se implemento; que `EscalarShape.Delta`
(variacion del KPI) estaba modelado pero nunca se calculaba; y que el importador no validaba el tipo.

**Decision (reemplaza D3 en la app):** los graficos se pintan con Apache ECharts, que YA estaba vendorizado
(ADR-0051, `wwwroot/lib/echarts`), sin dependencias nuevas ni Node. Sin migraciones: el tipo se guarda como
int y los tipos nuevos van al final del enum.

- **Separacion pura / interop.** `AnalisisGraficos` (Application, puro y testeable) traduce cada forma +
  tipo + presentacion a la "option" de ECharts. Los colores viajan como tokens del prototipo
  (`"var(--t-blue)"`, `"var(--t-blue)/0.4"` con alfa) y `analisis-graficos.js` los resuelve contra el tema
  ACTIVO (y repinta al cambiar claro/oscuro); el formato de numeros (`__fmt`) y los formateadores por tipo
  (`__kind`) se completan en JS porque JSON no lleva funciones. `ResizeObserver` reacomoda cada grafico al
  cambiar el ancho del widget o abrir el modal (el interop de Reportes solo escuchaba window.resize).
- **Componente `AnalisisGrafico`.** En circuito interactivo pinta por interop (canvas, animacion, tooltips,
  leyenda clicable, zoom en series largas). En render ESTATICO (pagina del PDF, sin circuito) deja la option
  en `data-aw-grafico` y el JS la pinta en SVG sin animacion al cargar (Puppeteer espera Networkidle0); el
  SVG lleva viewBox para escalar al ancho de impresion sin colapsar su contenedor.
- **Siguen en HTML** (no son graficos): KPI clasico, semaforo y tabla, con el estilo del prototipo.
- **Tipos nuevos (7b):** Velocimetro (KPI contra una meta, bandas de los umbrales), Embudo, Barras apiladas
  (eje + "apilar por"), Barras horizontales (rankings), Barras + linea (una fuente = Pareto con % acumulado;
  combinado = primera serie en barras y el resto en lineas con doble eje), Calendario de actividad por dia y
  Mapa de arbol (uno o dos niveles con migas de pan y zoom al nodo). La dona pasa la leyenda abajo en
  tarjetas angostas (media query de ECharts).
- **KPI con periodo (7b):** `WidgetConfig.Periodo` (mes, trimestre, anio en curso; ultimos 30 o 7 dias). El
  KPI mide solo ese periodo, muestra la variacion contra el anterior y una mini tendencia (6 puntos). En
  periodos de calendario la comparacion es AL MISMO CORTE (12 dias de este mes vs los mismos 12 del
  anterior), no medio mes contra un mes entero; un rango COMPLETO de la hoja manda y se compara con el rango
  anterior de igual largo. El chip respeta "menor es mejor". Calculo puro en `KpiPeriodos`/`AnalisisFechas`.
- **Una sola consulta por widget de fecha:** calendario y KPI con periodo piden en modo tabular el campo de
  fecha (el que resuelve `HojaFiltroBinder`, o el elegido en el editor) y el campo medido, con los filtros
  del widget, de la hoja (sin su rango) y del contexto; luego agrupan por dia/ventana en memoria, en la zona
  del tenant (regla 9). Si se alcanza el tope de 50.000 filas se avisa en la tarjeta.
- **Editor:** los 15 tipos como chips; el calendario solo en modo Consulta (necesita fecha) y el combinado
  no ofrece mapa de calor ni calendario; meta para el velocimetro; selector de periodo para KPI/velocimetro;
  la validacion exige campo de fecha para calendario y periodos. `LimpiarConfig` quita de la presentacion lo
  que el tipo no usa.
- **Importador:** omite (con aviso) widgets de un tipo que no existe en el sistema o del reservado.
- **Verificado:** ver PROGRESO 2026-11-12.
