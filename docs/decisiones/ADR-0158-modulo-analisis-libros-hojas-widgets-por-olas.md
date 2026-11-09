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

### Ola 4 - Organizar + mas tipos + embebible
- Drag & drop nativo + redimensionar por handle (cicla ColSpan) + layout batch.
- Modo Ver / Editar (acciones solo en edicion).
- Tipos: linea/area, heatmap, semaforo, tabla. Duplicar widget / duplicar hoja.
- Componente embebible (D9): `AnalisisLibroView` / `AnalisisHojaView` / `AnalisisWidgetView` con
  parametro `Contexto`; `ContextBindingsJson` por widget. Primer anfitrion: ficha de tercero del
  Directorio (hoja filtrada por el cliente). Segundo: Inicio (widgets sueltos).

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
