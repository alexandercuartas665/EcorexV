# ADR-0207: Grilla comun (EcxGrid) y su estreno en el Directorio General

- Estado: Aceptado
- Fecha: 2026-11-24
- Modulo: componentes compartidos de la consola (Ecorex.SuperAdmin) + Directorio General (000232, ADR-0088)
- Origen: pedido del usuario: traer al Directorio y al Cargador de contactos "lo mejor" de la parrilla del
  Conciliador DIAN (filtro, agrupacion, ordenamiento, filtros guardados, exportacion a Excel).

## Contexto

El motor de tabla del Conciliador DIAN (`ConciliacionDian.razor`, 4.9k lineas) trae filtro Y/O anidado, filtros
guardados del equipo (`SavedFilter`), ordenar multi-criterio, agrupar apilable con subtotales, columnas
moviles/ocultables/redimensionables/fijas con preferencias por usuario en BD (`UserTablePreference`) y exportar a
Excel lo que se ve. Esta escrito DENTRO de la pagina y ya se copio una vez en Requerimientos de Compra. El
Directorio tenia una lista fija de 5 columnas con paginacion y nada de eso; los campos de ficha (sector,
subsector, segmento...) no se podian ver ni agrupar en la lista.

## Decision

1. **Componente generico `EcxGrid<TItem>`** (`Components/Shared/Grid`): la pagina declara columnas
   (`GridColumn<TItem>`: clave, etiqueta, tipo Texto/Numero/Fecha/Si-No, valor crudo, texto, celda a medida,
   agrupable/ordenable/filtrable/sumable, oculta por defecto, seccion) y entrega las filas ya acotadas por sus
   filtros rapidos (pestanas, buscador). El componente hace en memoria: filtro Y/O anidado
   (`GridFilter`/`GridFilterGroup`, mismo JSON que el del Conciliador), filtros guardados por modulo, ordenar,
   agrupar con conteo (subtotales solo si hay columnas sumables visibles) y total general, columnas (mover,
   ocultar, ancho, fijas, menu por encabezado), paginacion, detalle por fila y exportar a Excel (ClosedXML) la
   vista exacta (todas las paginas). La vista se guarda por usuario con `UserTablePreference` (clave por tabla);
   columnas nuevas se respetan con la lista `known` (las nuevas ocultas por defecto si la columna lo pide).
   JS por instancia (`wwwroot/js/ecx-grid.js`: columnas fijas, redimensionar, barra de scroll superior).
2. **Directorio General** (las dos variantes, Ligero y Especializado, via `DirectorioTabla`): columnas del
   tercero (por defecto las mismas de antes: Cliente, Identificacion, Vendedor, Estado, Acciones) + Naturaleza,
   Perfiles, Ciudad, Sector/Cargo, Correo, Telefono, Empresa, Contactos (ocultas) + TODOS los campos de ficha
   como columnas ocultas por seccion "Ficha: ...". `TerceroListFilter.IncludeAllFields` hace que el listado
   traiga en `Filtrables` todos los campos de ficha (no solo los marcados como filtro; Lookup resuelto a
   etiqueta) y el DTO suma Email y Telefono. Se conservan las pestanas, el buscador, los filtros rapidos por
   campo y el detalle de contactos al expandir.
3. **Lecturas de preferencias y filtros guardados en contexto efimero** (ADR-0138): la grilla las pide al abrir,
   en paralelo con las cargas de la pagina; asi no compiten con el DbContext del circuito. Las escrituras siguen
   en el contexto scoped.

## Opciones consideradas

- **Copiar el motor del Conciliador en el Directorio** (como se hizo en Requerimientos): descartada, triplica el
  codigo y cada mejora habria que hacerla tres veces.
- **Grilla de terceros (MudBlazor/QuickGrid)**: descartada; QuickGrid no agrupa ni guarda vistas, y una libreria
  completa trae otro look (fidelidad al prototipo) y dependencias que el resto de la consola no usa.
- **Filtrar/agrupar en SQL**: innecesario hoy; el Directorio ya carga todo en memoria (~1.2k filas en el tenant
  mas grande) y la grilla trabaja igual sobre cualquier fuente.

## Consecuencias

- Nuevo modulo de filtros guardados `DirectorioGeneral` y preferencia `directorio-general`. Sin migracion.
- Siguiente paso: pasar la pestana "Prospectos" del Cargador de contactos (000740) a la grilla, y despues migrar
  el Conciliador DIAN y Requerimientos de Compra al componente para borrar sus copias del motor.
- Pruebas: `EcxGridFilterTests` (Y/O, anidados, incompletas, vacio, numero, fecha, lista, formatos).
