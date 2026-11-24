# ADR-0206: MCP de tableros - filas, agrupar por periodo y ciclo de vida de reportes

- Estado: Aceptado
- Fecha: 2026-11-24
- Modulo: MCP personal de tableros (ADR-0159) + motor de reportes (ADR-0051) + libros de indicadores (ADR-0158) +
  contenedor de datos (ADR-0194)
- Origen: prueba de humo de la guia de los MCP (ADR-0204, spec seccion 5): una IA nueva, solo con `guia_ecorex`, creo
  "un contenedor con una tabla de pedidos y un reporte de ventas por mes" sin errores, pero destapo tres huecos.

## Contexto

1. **No habia como quitar un reporte** creado por MCP: el de prueba quedo huerfano al borrar su contenedor. Tampoco
   habia como corregirlo ni borrar indicadores o libros.
2. **"Ventas por mes" era imposible**: `agrupar_por` agrupa por el valor EXACTO; la IA tuvo que inventar una columna de
   texto "AAAA-MM".
3. **No habia como cargar pocas filas** por MCP (excel/bd son de pantalla, la API necesita un endpoint externo y
   `agregar_filas_a_contenedor` solo lo tienen los agentes), ni como leerlas para verificar.

## Decision

1. **Periodo en el motor** (`ReportQuerySpec.GroupPeriod`, enum `ReportDatePeriod`: dia, semana ISO, mes, trimestre,
   anio). Aplica al UNICO campo de agrupacion cuando es FECHA (lo valida `ReportDataSource`; las fuentes externas no lo
   admiten). Se resuelve en UN solo punto (`ReportPeriodGrouping`): el despachador pide a la fuente la consulta TABULAR
   (fecha + campos medidos, mismos filtros) y agrupa en memoria por la clave del periodo, ordenable cronologicamente
   (`2026-01-15`, `2026-W03`, `2026-01`, `2026-T1`, `2026`). Asi funciona igual en toda fuente (contenedor, nativa,
   formulario) y en PG y SQL Server, sin SQL por proveedor. Zona del tenant para instantes con hora; las fechas sin hora
   (medianoche UTC, ej. columna fecha de un contenedor) no se corren de zona. Se persiste en `ReportSpec.GroupPeriod`
   (reportes guardados) y en el spec JSON de los widgets. En el MCP: `consulta.periodo`.
2. **Ciclo de vida de reportes e indicadores**: `editar_reporte` (parcial: tarjeta por `UpdateCardAsync`; grafico y
   consulta se validan EJECUTANDO antes de guardar, como al crear; solo paneles de consulta simple), `archivar_reporte`
   (Status Archived, recuperable desde la galeria), `eliminar_indicador` y `eliminar_libro_indicadores` (servicios de
   analisis existentes; solo dueno o admin). Las tres de quitar son DESTRUCTIVAS en la clasificacion del MCP. Las
   sugerencias de categoria de la galeria ya no incluyen categorias de reportes archivados.
3. **Filas**: `agregar_filas` (maximo 100 por llamada; valida cada valor contra el tipo de su columna y las obligatorias;
   columna desconocida o valor invalido rechaza TODO el lote; con `columna_clave` hace upsert reusando
   `ContenedorFilasPorClave`; no admite tablas derivadas) y `ver_filas` (lectura acotada, filtros columna+texto, tope
   100, mas recientes primero). Permiso `contenedor-datos` Editar / Ver.

## Opciones consideradas

- **Periodo como formato en `rellenar_columna`** (columna "mes" derivada): descartada como solucion principal; obliga a
  duplicar datos y no sirve para trimestre/semana sin otra columna. El periodo en la consulta no guarda nada extra.
- **Periodo traducido a SQL por proveedor**: mas eficiente en volumen, pero el contenedor ya agrega en memoria (EAV) y
  las fuentes nativas usan el motor en memoria; un solo punto en memoria es igual de correcto y mucho mas simple.
- **`correr_importacion` por MCP**: queda para otra entrega (ya existe `ejecutar_clasificacion_ahora` como patron).

## Consecuencias

- MCP de tableros: 158 -> 164 herramientas. Sin migracion.
- Pruebas: dual `McpTablerosTests.Filas_PeriodoMes_EditarArchivar_EliminarIndicador_ADR0206` (lote invalido no guarda,
  obligatorias, upsert, ver_filas, ventas por mes y por trimestre, reporte guardado conserva el periodo, periodo sobre
  no-fecha = error, editar parcial y con consulta, archivar, indicador por mes, eliminar indicador y libro) y conteos del
  catalogo; unitarias `ReportPeriodGroupingTests` (zona, fechas sin hora, semana ISO).
