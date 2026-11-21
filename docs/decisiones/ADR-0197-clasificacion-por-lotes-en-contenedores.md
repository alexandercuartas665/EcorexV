# ADR-0197: Clasificacion por lotes de filas de un contenedor con un agente de IA

- Estado: Aceptado
- Fecha: 2026-11-21
- Modulo: Contenedor de datos (000730) + Agentes de IA (000867) + MCP personal (ADR-0159)

## Contexto

En BITCODE se armo (via MCP, ADR-0194/0195) el contenedor "SECOP II - Procesos" con la tabla "Tecnologia"
(~5.300 procesos de tecnologia, upsert por `id_proceso`) y las columnas vacias `sector` y `clasificado_en`, mas el
agente "Clasificador de Procesos SECOP" (DeepSeek, apagado) que por cada proceso emite
`{id_proceso, es_tecnologia, sector}`. Faltaba el motor para clasificar las filas y escribir el resultado de vuelta:

1. `agregar_filas_a_contenedor` SIEMPRE inserta: un agente que "escribe de vuelta" duplicaria filas.
2. `consultar_contenedor` busca por texto con tope 50, sin filtro de "no clasificadas" ni paginacion, y carga la
   tabla completa en memoria (inviable para tablas grandes como la cruda de 434k procesos).

## Decision

1. **Herramienta del agente `actualizar_filas_por_clave`** (en `ContenedorDatosToolset`, habilitada por defecto
   como el resto del grupo). Ubica cada fila por el valor de `columna_clave` y escribe SOLO las columnas recibidas
   (actualiza la celda o la crea; no toca las demas). No duplica: las claves inexistentes se reportan en
   `no_encontradas`, salvo `crear_si_falta=true`. Tope 500 filas por llamada. Resuelve la tabla por nombre; si el
   nombre se repite, acepta `contenedor` (modelo) + `tabla`. Un solo `SaveChanges` (atomico).
2. **Runner `IContenedorClasificacionService.ClasificarLoteAsync`** + herramienta del MCP personal
   **`clasificar_lote`** (permiso Edit de `contenedor-datos`). Por lote:
   - toma hasta `limite` (max 50) filas raiz CON clave y con la `columna_marca` vacia, las mas antiguas primero
     (consulta `NOT EXISTS` en BD, sin cargar la tabla);
   - envia al agente las `columnas_entrada` (por defecto todas menos la marca y las de salida), con tope por celda
     (1.500) y por prompt (60.000); lo que no cabe queda para el siguiente lote;
   - prompt = el del agente + contrato: arreglo JSON, un objeto por registro, con la clave tal cual;
   - escribe SOLO las `columnas_salida` declaradas + la marca (fecha UTC ISO-8601) y SOLO en filas del lote
     (claves ajenas se ignoran; columnas de entrada no se pisan);
   - una fila que el agente no devuelve queda sin marca (se reintenta) y se reporta en `sin_respuesta`;
   - devuelve `procesados`, `actualizados`, `sin_respuesta`, `ignorados`, `restantes` y tokens.
3. **Gobierno de IA**: exige el agente ENCENDIDO y el proveedor habilitado, respeta el cupo DURO del plan y
   registra cada llamada en `AiUsageLog` (fuente `clasificar-lote`).

## Opciones consideradas

- **Proceso programado (tipo `DataImportSchedule` con cron)**: corre solo, pero esconde el consumo de tokens y es
  mas dificil de validar. Se descarta por ahora; el runner es reutilizable desde un worker si luego se quiere.
- **Que el agente lea y escriba con sus herramientas (bucle con tool-calling)**: mas tokens, sin control del
  tamano del lote y con riesgo de que escriba filas fuera del lote. Se descarta: el runner arma el lote y aplica
  la respuesta con reglas fijas.

## Consecuencias

- Sin migracion: la marca y las salidas son columnas normales de la tabla (EAV).
- El MCP personal pasa de 141 a 142 herramientas (`clasificar_lote` solo se publica con el runner y el modulo
  de contenedores inyectados).
- Pruebas de integracion dual `ContenedorClasificacionTests` (actualizar por clave sin duplicar, tenant-scoped,
  crear_si_falta; lotes por marca, solo salida, claves ajenas, sin respuesta, AiUsageLog, agente apagado) y en
  `McpTablerosTests` el puente MCP.
- Para clasificar SECOP: encender el agente y repetir `clasificar_lote` (contenedor "SECOP II - Procesos", tabla
  "Tecnologia", clave `id_proceso`, marca `clasificado_en`, salida `["sector"]`) hasta `restantes = 0`.
