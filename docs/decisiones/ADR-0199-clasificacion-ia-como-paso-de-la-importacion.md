# ADR-0199: Clasificacion con IA como paso de la importacion (+ limpiar marca)

- Estado: Aceptado
- Fecha: 2026-11-21
- Modulo: Contenedor de datos (000730) + Agentes de IA (000867) + MCP personal (ADR-0159)
- Relacionados: ADR-0194 (programaciones de importacion por MCP), ADR-0197 (clasificar_lote)

## Contexto

En BITCODE el contenedor "SECOP II - Procesos" se alimenta por una programacion de importacion (cron diario, upsert
por `id_proceso`) y se clasifica con un agente de IA usando `clasificar_lote` (ADR-0197). Dos problemas:

1. La clasificacion es MANUAL y aparte: alguien tiene que repetir `clasificar_lote` despues de cada importacion. Lo
   que se quiere es el patron del dron: importar -> clasificar SOLO lo nuevo, sin intervencion.
2. Al cambiar la taxonomia no hay como resetear la marca de las filas ya clasificadas: `eliminar_columna` no borra
   columnas con datos y no existe "vaciar columna". El workaround era renombrar la columna a `*_old` y crear otra.

En ADR-0197 se descarto el proceso programado "por ahora" porque escondia el consumo de tokens; aqui se resuelve con
topes (lotes por corrida, cupo del plan) y dejando el resultado en la bitacora de la corrida.

## Decision

1. **Paso opcional en la programacion de importacion.** `ImportProcess` gana `ClassifyStepJson`
   (`ImportClassifyStep`: tabla, agente por id, columna clave, marca, salidas, entradas, limite <= 50 filas por lote,
   `max_lotes` <= 200 por corrida, default 20) y `ClassifyRequestedAt`. La tabla por defecto es la destino del
   conector; para una programacion de FLUJO de extraccion (no tiene conector) se indica explicita.
2. **Disparo por la bitacora, ejecucion en un worker aparte.** Cuando una corrida cierra OK
   (`ImportRunLog.CloseAsync`, o `ScrapeFlowRunLog.CloseAsync` para las programaciones de un flujo) y la programacion
   tiene paso, se pone `ClassifyRequestedAt`. `ImportClassifyWorker` (hosted service en SuperAdmin, cada minuto,
   barrido cross-tenant de solo ids + `AmbientTenantContext`) toma la solicitud (la limpia ANTES de correr, para no
   repetirla en bucle) y corre `IImportClassificationService.EjecutarAsync`. Va aparte de `ImportSchedulerWorker`
   porque clasificar son minutos de llamadas a la IA y no debe frenar los disparos ni el hub que ingiere.
3. **El bucle reusa `ClasificarLoteAsync` (ADR-0197)**: mismas reglas (agente encendido, proveedor habilitado, cupo
   DURO, `AiUsageLog` fuente `clasificar-lote`, solo filas del lote y solo columnas de salida). Corta por: `completo`
   (no quedan pendientes), `tope_lotes`, `cupo` (no es error), `error_proveedor` (falla transitoria que no se recupero
   con 3 reintentos con espera 5s/20s/60s), `sin_avance` (2 lotes seguidos sin escribir nada: no quemar tokens) o
   `config`. `ClasificarLoteResultado` gana `Codigo` (config/cupo/transitorio) para distinguir que se reintenta.
   Idempotente por la marca: lo que no alcanzo queda pendiente para la siguiente corrida. El resumen (lotes,
   clasificadas, pendientes, motivo) se anexa al detalle de la ultima corrida (`listar_corridas_importacion`).
4. **`LimpiarMarcaAsync` server-side**: vacia (Value = null) la marca y, opcionalmente, las columnas de salida de las
   filas raiz de la tabla, todas o las que cumplan un filtro `{columna: valor}` (igualdad, AND). Un solo
   `ExecuteUpdate` tenant-scoped por el filtro global; no borra filas ni columnas.
5. **MCP personal: 4 herramientas** (permiso Edit de `contenedor-datos`, auditadas como toda escritura):
   `configurar_paso_clasificacion`, `quitar_paso_clasificacion`, `ejecutar_clasificacion_ahora` (encola; el worker la
   toma en < 1 min) y `limpiar_marca` (marcada destructiva). `listar_programaciones_importacion` muestra el paso y si
   hay una clasificacion pedida.

## Opciones consideradas

- **Clasificar dentro del `ProcessRunner`, justo despues de la ingesta**: solo sirve a los caminos sincronos
  (REST server-direct, derivadas); el camino via agente cierra la corrida despues, por el hub. Y bloquearia el
  ciclo del scheduler minutos. Descartada: se dispara desde el cierre de la corrida (comun a todos los caminos).
- **Que `ejecutar_clasificacion_ahora` corra sincrono en la llamada MCP**: muchos lotes superan el tiempo de una
  llamada MCP. Descartada: encola y el resultado queda en la bitacora.
- **Barrer cada minuto todas las tablas con paso buscando pendientes**: consulta `NOT EXISTS` sobre tablas grandes
  (434k filas) cada minuto. Descartada: solo se corre cuando hay una corrida ok o un pedido explicito.

## Consecuencias

- Migracion dual `AddImportClassifyStep` (2 columnas nullable en `import_processes`, PG + SQL Server).
- El MCP personal pasa de 151 a 155 herramientas.
- El consumo de tokens queda acotado por `max_lotes` x `limite` por corrida y por el cupo del plan.
- Pruebas de integracion dual `ImportClasificacionPasoTests` (solo pendientes + idempotencia, tope y cupo, reintento
  transitorio vs configuracion, limpiar por filtro/total + tenant-scoped, cierre ok -> solicitud -> barrido -> bitacora)
  y en `McpTablerosTests` el puente MCP.
- Caso SECOP (BITCODE): `configurar_paso_clasificacion` sobre "Recientes diaria" (tabla Procesos, clave
  `id_proceso`, marca `clasificado_en`, salida `sector`, entradas nombre_proceso/descripcion/modalidad/
  tipo_contrato/categoria_unspsc, limite 50). El backlog grande se sigue procesando con `ejecutar_clasificacion_ahora`
  o `clasificar_lote`; el paso cubre lo nuevo de cada dia.
