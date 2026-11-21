# ADR-0198: Reportes e indicadores por el MCP personal (fuente = contenedor de datos)

- Estado: Aceptado
- Fecha: 2026-11-21
- Modulo: MCP personal (ADR-0159) + Motor de reportes (ADR-0051) + Libros de indicadores (ADR-0158)

## Contexto

Por el MCP personal ya se arma un contenedor de datos (ADR-0194), se carga con drones (ADR-0193) y se clasifica con
un agente (ADR-0197), pero no habia como convertir esos datos en un reporte o un indicador sin abrir la app: el MCP no
tenia ninguna herramienta de reporteria. El motor SI soporta el contenedor como fuente de primera clase
(`ContainerReportReader`, clave `container:{guid}`), con tabla y agregado (un solo group by en v1), validacion contra
el catalogo y aislamiento por tenant.

Punto de diseno: un contenedor (DataModel) puede tener varias tablas (p.ej. "SECOP II - Procesos" con Procesos y
Tecnologia). Habia que decidir como referenciar una tabla concreta.

## Decision

1. **Una fuente por TABLA, sin claves nuevas.** En el motor cada tabla raiz (`DataContainer` con
   `ParentContainerId = null`) YA es su propia fuente `container:{id_tabla}`: el catalogo las enumera solas. No se crea
   la subclave `container:{guid}:{tabla}`. Las herramientas aceptan la fuente como clave del catalogo, como el par
   `contenedor` + `tabla` (id o nombre, igual que el resto del MCP de contenedores) o por nombre (si se repite en dos
   contenedores, pide el par). `listar_fuentes_reporte` dice a que contenedor pertenece cada tabla.
2. **Nueva partial `TableroMcpToolset.Reportes.cs`** con 9 herramientas, servicios opcionales inyectados
   (`IReportCatalog`, `IReportDataSource`, `IReportDefinitionService`, `IAnalisisService`, `IAnalisisWidgetRunner`):
   - lectura: `listar_fuentes_reporte`, `ver_fuente_reporte`, `listar_reportes`, `ver_reporte`,
     `listar_libros_indicadores`, `ver_libro_indicadores`;
   - escritura: `crear_reporte` (panel Dashboard: tabla/barras/torta/linea, categoria opcional),
     `crear_libro_indicadores`, `agregar_indicador` (kpi, barras, barras_horizontales, dona, embudo, treemap, linea,
     area, tabla; hoja por id/nombre, se crea si el nombre no existe).
3. **Consulta en NOMBRES de campo.** `consulta = {campos, filtros[{campo, operador, valor|valores}], agrupar_por,
   agregaciones[{funcion, campo}], orden[{campo, desc}], limite}`. La herramienta traduce cada nombre a la clave del
   catalogo (en un contenedor, el id de la columna). Agrupar sin agregaciones = contar. Se puede ordenar por la
   agregacion (`conteo`, `suma`...). Sumar/promediar exige un campo numero o decimal (mensaje claro si es texto).
4. **Ejecutar antes de guardar.** `crear_reporte` corre la consulta por `IReportDataSource` (mismo camino que la
   galeria) y `agregar_indicador` corre el widget con el runner de la hoja; si fallan no se guarda nada. La respuesta
   trae una muestra del resultado.
5. **Permisos de la matriz de roles** (iguales a las pantallas): fuentes = ver `reportes/galeria` o `analisis`;
   ver reportes = `reportes/galeria` (`reportes/admin` ve todos); crear reporte = `reportes/ia` Crear o
   `reportes/admin` Ver; indicadores = `analisis` (ver; crear/agregar = Crear o Editar). El libro solo lo edita su
   dueno o un administrador (lo valida `AnalisisService`).

## Opciones consideradas

- **Subclave `container:{guid}:{tabla}`**: innecesaria (cada tabla ya tiene su guid) y obligaba a tocar el catalogo,
  el lector y los reportes guardados. Descartada.
- **Imprimibles (RDL) por MCP**: el RDL se disenia en el editor visual; generarlo por IA da un layout pobre. Fuera de
  v1: se crean en pantalla.
- **Recibir el spec crudo del motor** (claves de catalogo, enums en ingles): fragil para la IA (las claves de
  contenedor son guids). Se prefirio la consulta en nombres con traduccion y validacion.

## Consecuencias

- Sin migracion ni cambios al motor: solo la capa MCP.
- El MCP personal pasa de 142 a 151 herramientas (6 de lectura, 3 de escritura, ninguna destructiva).
- Limites heredados del lector de contenedor: un solo `agrupar_por`; sin `agrupar_por` solo un KPI (una agregacion);
  la suma de un campo de texto requiere cambiar la columna a numero/decimal (`editar_columna`).
- Prueba de integracion dual en `McpTablerosTests` (contenedor con dos tablas; reporte con filtro + agrupacion +
  orden por conteo que corre por `RunAsync`; suma por sector; errores sin guardar; libro con KPI + barras en hoja
  nueva, y los widgets guardados corren con el runner).
