# ADR-0103: Herramienta de agente para ESCRIBIR filas en un Contenedor de datos

**Status:** Accepted
**Date:** 2026-09-14
**Deciders:** desarrollo + operacion de agentes

## Contexto

Un agente de IA que EXTRAE datos estructurados de un archivo (caso real: "Clasificador de productos y
precios", tenant SKY SYSTEM) necesita ESCRIBIR el resultado en un Contenedor de datos del tenant. Hasta
ahora NO existia ningun toolset de contenedor: el agente ya extraia bien y ya llamaba a `cargar_productos`,
pero recibia "Herramienta no disponible o deshabilitada: cargar_productos" porque la herramienta no estaba
implementada ni registrada.

El Contenedor de datos guarda en EAV: `data_containers` (tabla) -> `data_container_columns` -> filas
`data_container_rows` + celdas `data_container_cells` (valor SIEMPRE como texto). El aislamiento por tenant
lo da el filtro global.

## Decision

Nuevo `ContenedorDatosToolset : IAgentToolset` (mismo patron que TasksToolset) registrado entre los
`IAgentToolset`. Tres herramientas:

- **`cargar_productos`** (nombre EXACTO que ya usa el prompt del agente): `{ productos: [ {campo:valor}, ... ] }`.
  Atajo que escribe en el contenedor fijo llamado **"Productos"** del tenant.
- **`agregar_filas_a_contenedor`**: `{ contenedor:<nombre>, filas:[ {col:valor}, ... ] }`. Misma logica con el
  contenedor parametrico. `cargar_productos` es un wrapper de esta con `contenedor="Productos"`.
- **`listar_contenedores`**: lista los contenedores del tenant con sus columnas (analogo a `listar_tableros`).

Reglas de escritura:
- El contenedor se resuelve por NOMBRE (case-insensitive), tenant-scoped; si no existe -> error claro con la
  lista de contenedores disponibles. NO se hardcodean ids.
- Por cada item se inserta 1 fila + 1 celda por cada clave que corresponda a una COLUMNA (case-insensitive).
  Las claves que NO son columnas se ignoran. Se excluyen columnas Submodel/RelationMany (no tienen celda simple).
- Todo se guarda como TEXTO (numeros -> su texto), coherente con el modelo EAV del resto del sistema.
- Solo INSERTA: no borra ni actualiza filas existentes.
- Devuelve `{ ok:true, cargados:N, celdas:M, contenedor:"Productos" }`.

## Consecuencias

- Un agente puede poblar un contenedor sin SQL ni herramienta a medida por tenant; beneficia a cualquier
  agente extractor (productos, precios, listados). Sin migraciones (no hay schema nuevo). La herramienta
  queda habilitada por defecto (los agentes la ven salvo que este en su DisabledToolsJson).
- Combinado con ADR (v0.16.67, nombre del archivo al contexto), el agente puede llenar la columna 'archivo'
  con el nombre real del documento de origen.

## Referencias

- Codigo: `Ecorex.Application/Tenancy/ContenedorDatosToolset.cs`; registro en `DependencyInjection.cs`.
- Tests: `ContenedorDatosToolsetTests` (inserta filas/celdas, ignora campos no-columna, contenedor inexistente,
  listar_contenedores).
- Relacionado: contenedor de datos (modelo EAV), ADR de nombre de archivo al contexto (v0.16.67).
