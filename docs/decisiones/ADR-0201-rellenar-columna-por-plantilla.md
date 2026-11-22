# ADR-0201: Rellenar una columna de contenedor con una plantilla de otras columnas (MCP `rellenar_columna`)

- Estado: Aceptado
- Fecha: 2026-11-22
- Modulo: Contenedor de datos (000730) + MCP personal (ADR-0159)
- Relacionados: ADR-0193 (drones por MCP), ADR-0199 (limpiar_marca), v0.16.373 (upsert por columna clave en drones)

## Contexto

Desde v0.16.373 la ingesta de un dron (extract -> contenedor) hace upsert si el paso tiene columna clave de ingesta.
En el dron FOMAG (BITCODE) ninguna columna existente es unica (`url` se repite entre procesos), asi que la llave
debe ser compuesta: una columna nueva `clave_ingesta` = `{numero_proceso}|{url}`. Las filas ya cargadas quedan con
esa columna vacia y el primer upsert las duplicaria como huerfanas.

Las salidas que habia: `eliminar_tabla` + recrear (cambia el `tabla_id` y rompe reportes y el `contenedor_destino`
del paso) o vaciar la tabla y re-correr el dron (se pierde lo que el dron ya no traiga). ECOREX.mcp propuso
`vaciar_tabla` y, como opcional, un backfill por plantilla.

## Decision

Se construye SOLO el backfill (decision del usuario 2026-11-22): `IContenedorClasificacionService.RellenarColumnaAsync`
y la herramienta MCP `rellenar_columna(contenedor, tabla, columna, plantilla, solo_vacias=true)`.

1. La plantilla es texto con `{columna}`; cada marcador se reemplaza por el valor (trim) de esa columna en la misma
   fila, o vacio si no tiene. Debe nombrar al menos una columna; todas deben existir; el destino no puede estar en
   la plantilla.
2. Solo filas raiz de la tabla con al menos un valor de origen. Con `solo_vacias=true` (defecto) solo las que tienen
   el destino vacio: repetirla no cambia nada (idempotente). Con `false` sobrescribe (y cuenta `sin_cambio` las que
   ya tenian ese valor).
3. Hasta 20000 filas por llamada, en lotes de 1000, dentro de UNA transaccion (todo o nada). Con `solo_vacias` lo
   que no alcanzo queda en `restantes` y se repite; sobrescribir mas de 20000 filas se rechaza (pide `solo_vacias`).
4. Tenant-scoped por el filtro global; crea la celda si no existe. No borra filas ni toca otras columnas.
5. MCP: permiso Edit de `contenedor-datos` (mismo gate que `clasificar_lote` / `limpiar_marca`), auditada como toda
   escritura del MCP personal. Devuelve rellenadas, sin_cambio, restantes y una muestra de 3 valores calculados.

## Opciones consideradas

- **`vaciar_tabla` (soft-delete de todas las filas conservando `tabla_id`)**: desbloquea, pero pierde datos que el
  dron ya no vuelva a traer y es destructiva. Descartada por ahora (decision del usuario).
- **Columna derivada calculada al vuelo (ADR-0127)**: es otra tabla; el upsert del dron necesita la llave como columna
  fisica de la misma tabla. Descartada.
- **Expresiones (funciones, condicionales) en la plantilla**: no hacen falta para una llave compuesta. Descartada (se
  puede ampliar despues sin romper la firma).

## Consecuencias

- Sin migracion. El MCP personal pasa de 155 a 156 herramientas.
- Flujo FOMAG: crear columna `clave_ingesta` -> `rellenar_columna` con `{numero_proceso}|{url}` -> configurar el
  paso Extraer (script que emite la clave + mapeo + `ingesta_llave = clave_ingesta`) -> re-correr el dron = upsert
  sin duplicados y sin cambiar el `tabla_id`.
- Pruebas de integracion dual (`ImportClasificacionPasoTests`): solo vacias + idempotencia, sobrescribir, filas sin
  origen ignoradas, columnas invalidas, tenant-scoped; y el puente MCP en `McpTablerosTests`.
