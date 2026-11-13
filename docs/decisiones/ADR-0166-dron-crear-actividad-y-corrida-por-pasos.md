# ADR-0166 - Dron: paso "Crear actividad", corrida por pasos con bucles y variables escapadas

- Estado: Aceptado (implementado en rama `feat/agentes-sistema`, SIN deploy)
- Fecha: 2026-11-12
- Relacionado: ADR-0151 (navegador delegado), ADR-0098 (crear actividad desde agente), ADR-0021 (GridDetail)

## Contexto

El DRON PLATAFORMAS INGENIOS (tenant SOLDARCO) lee el portal pdpartner de INCAUCA: ramal Pendientes (pedidos
nuevos) y ramal Historico (Concluidas), ambos con bucle ForEach + paginacion, e ingiere cabeceras e items al
Contenedor "Drones Ingenios". El usuario pidio que cada pedido NUEVO caiga como UNA tarea con un formulario ya
diligenciado con los datos del pedido, sin duplicar, configurable desde /extraccion-datos; el historico solo debe
seguir llegando al Contenedor.

Hallazgos:
- "Ejecutar ahora" y la programacion (ImportScheduleDispatcher) usan RunFlowNowAsync, que COMPILA el flujo lineal:
  no recorre bucles (LoopOverVar/IsLoopEnd/IsPageNext) ni ingiere por IngestPath, y falla con ENSAMBLADO. El dron
  solo procesaba pedidos en el modo paso a paso manual.
- La sustitucion de {{VAR}} / @@ruta@@ pegaba el valor crudo en el JS: una clave con comilla o barra rompia el
  login. Todos los scripts existentes usan los placeholders dentro de comillas.
- ITaskItemService.CreateAsync abre su propia transaccion (EF no anida transacciones).

## Decision

1. Nuevo tipo de paso `ScrapeStepKind.CrearActividad = 22`, server-side (no va al navegador), con su config en
   `ScrapeStep.MappingJson` (mismo patron que OTP/NEWTON/ProcesarArchivos; SIN migracion): concepto, plantillas
   de titulo/descripcion/solicitante, clave del registro, contenedor + columna clave + columna donde anotar la
   tarea, mapeo campo<-ruta, campo GridDetail + mapeo columna<-propiedad del item y una guarda opcional
   "no crear si ruta == valor". Implementado en `ScrapeCrearActividad`.
2. Anti-duplicado por la fila del Contenedor: si la columna de la tarea (p.ej. TAREA) ya tiene numero, se omite.
   Como no se puede envolver CreateAsync en otra transaccion, la columna se RESERVA ("CREANDO...") antes de
   crear: si el proceso cae a mitad, la siguiente corrida no duplica (lo reporta para revision). Si la creacion
   falla, la reserva se libera. Sin contenedor configurado, el anti-duplicado es por titulo dentro del concepto.
3. El formulario del concepto se diligencia como borrador ligado al numero de la tarea (mismo formato
   { campo: { value, type } } que el resto; la grilla = arreglo JSON de filas), con normalizacion por tipo
   (fecha dd/MM/yyyy -> yyyy-MM-dd, numero, toggle).
4. Corrida POR PASOS con bucles: al llegar a un paso con LoopOverVar ejecuta el bucle completo (RunLoopNowAsync,
   con paginacion) y no vuelve a correr sueltos su cuerpo ni sus pasos de avance. RunFlowNowAsync deriva a esta
   corrida (en segundo plano, sesion limpia y cierre del navegador al final) cuando el flujo tiene bucles o un
   paso Crear actividad. Los flujos sin bucles (drones DIAN) siguen exactamente por su camino de siempre.
5. Escape de valores al sustituir en scripts (`ScrapeRunContext.SubstituteScript` / `JsEscape`): \ ' " ` $,
   saltos de linea, U+2028/2029 y "</". URL y selector NO se escapan. Lo no resuelto se deja igual.

## Consecuencias

- El historico no lleva el paso, asi que no crea tareas; ademas la guarda estado == Concluida lo blinda.
- Correr "Crear actividad" suelto (boton de paso) no tiene DETALLE en sesion: el paso va dentro del bucle.
- Config por tenant en BD (no en git): para prod hay que replicar formulario, concepto, columna TAREA y el paso.
- Tests: unitarios (escape, rutas/plantillas, grilla, coercion, pasos del bucle) e integracion dual PG + SQL Server
  (crea 1 tarea con formulario, segunda corrida no duplica, Concluida no crea, sin fila no crea).
