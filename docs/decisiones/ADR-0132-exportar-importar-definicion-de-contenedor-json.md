# ADR-0132: Exportar / importar la definicion de un contenedor a JSON

Fecha: 2026-10-26
Estado: aceptado

## Contexto

El modulo Contenedor de datos permite armar modelos (tablas, columnas, submodelos, tablas derivadas),
relaciones del ER, conectores (fuentes) y procesos de refresco (programacion). Hacia falta mover esa
DEFINICION entre tenants y entornos (p.ej. de un entorno de pruebas a produccion, o replicar un contenedor
en otro tenant) sin copiar los datos ni los secretos.

## Decision

Se agrega exportar/importar la DEFINICION de un contenedor a un archivo JSON:

- Se exporta la ESTRUCTURA (tablas raiz, submodelos y derivadas; columnas con su tipo/orden/obligatoriedad;
  submodelos por su columna; derivadas con su receta), las RELACIONES del ER, los CONECTORES (fuentes) y los
  PROCESOS (programacion del refresco), mas el destino 1:1.
- NO se exportan los DATOS (filas/celdas) ni los SECRETOS (credenciales cifradas, secreto del cliente
  remoto). De cada conector/destino solo viaja un flag "tenia credenciales" para avisar al operador que debe
  volver a capturarlas (regla #6: el repo y los paquetes no llevan secretos).
- Las referencias entre piezas viajan por CLAVES locales del paquete (t1, c1, ...) y por NOMBRE de columna,
  nunca por GUID. Al importar se crea un contenedor NUEVO con ids nuevos y se remapea todo, de modo que el
  paquete es portable entre tenants y entornos. El nombre del contenedor se hace unico en el tenant destino
  ("X", "X (importado)", "X (importado 2)"...).
- La receta de una tabla DERIVADA (ADR-0127) se guarda en el paquete con la tabla y columnas origen por
  clave/nombre (no por el GUID que lleva el MappingJson del conector FromTable); al importar se reconstruye
  el DerivationSpec con los ids nuevos. Si el origen de una derivada esta FUERA del paquete (derivada
  cross-contenedor), se guarda por nombre y al importar se intenta casar por nombre con una tabla existente;
  si no se encuentra, el conector de esa derivada queda inactivo y se reporta un aviso.
- Los procesos cuya fuente usaba credenciales se importan INACTIVOS (para no disparar un horario que fallaria
  por falta de secreto); los de las derivadas (FromTable, sin secreto) se importan activos y funcionan de
  una. El cliente remoto de un proceso se casa por su id publico en el tenant destino; si no existe, el
  proceso queda sin cliente y se avisa.

El JSON lleva `formatVersion` para poder evolucionar el formato sin romper paquetes viejos. Toda la
importacion ocurre en UNA transaccion.

Capas: `IDataContainerPortabilityService` (Application) con `ExportAsync(modelId)` ->
`DataExportResult` (bytes JSON + nombre de archivo) e `ImportAsync(json)` -> `ContainerImportResult`
(contenedor nuevo + avisos). La UI (ContenedorDatos) agrega "Exportar" por tarjeta (descarga reusando el
helper `ecorexDcCanvas.downloadBase64`, mime application/json) e "Importar contenedor" (InputFile .json).
SIN migracion (no hay tablas nuevas).

## Consecuencias

- Mover la definicion de un contenedor es un archivo JSON: portable, revisable y versionable. Nunca arrastra
  datos ni secretos, asi que es seguro compartirlo.
- Tras importar hay que re-capturar credenciales de las fuentes que las usaban (los avisos lo indican) y, si
  aplica, reasignar el cliente remoto y reactivar sus procesos.
- Alcance: se exporta/importa UN contenedor. Una derivada cuyo origen vive en otro contenedor solo se resuelve
  al importar si ese otro contenedor (su tabla origen) ya existe por nombre en el tenant destino.
- Pruebas: round-trip dual (PG + SQL Server) que verifica estructura, relacion, derivada con receta
  remapeada a ids nuevos, ausencia de datos y ausencia de secretos, y el aviso de re-captura.
