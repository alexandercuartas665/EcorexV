# ADR-0171: Programacion de flujos por tramos (secuencia), varias programaciones por flujo y candado por flujo

**Estado:** Aceptado (rama `feat/flujo-secuencia-tramos`; pendiente de integrar al tronco y de desplegar con OK del usuario)
**Fecha:** 2026-11-15
**Decisores:** usuario (dueno del producto), sesion ECOREX.dian

## Contexto

Recuperando el historico DIAN de SOLDARCO (2026-11-14/15) se confirmo que:

1. La busqueda de "Documentos recibidos" de la DIAN con un rango de varios dias devuelve solo los ultimos
   2-4 dias del rango; con ventanas de 1 dia devuelve el dia completo (VENTANA_DIAS=1).
2. La sesion DIAN vence a los ~30 min sin avisar: pasado ese tiempo la grilla responde "Mostrando 0
   registros". Con el captcha invisible (re-buscar) cada dia tarda 2-4 min, asi que una corrida solo cubre
   unos 3 dias de forma confiable.
3. Para cubrir 60 dias hubo que lanzar a mano ~20 corridas de 3 dias (DIAS_DESDE=0,3,6,...), con pausas y
   reintentos cuando el correo de acceso no llegaba (la DIAN frena correos pedidos seguidos).

La programacion del modulo Extraccion de datos (Ola 5) solo permitia UN horario por flujo, siempre con los
valores guardados, sin saltar lo ya cargado (CUFES_CONOCIDOS vivia en la pagina del conciliador), sin
candado (una corrida programada podia arrancar sobre otra viva del mismo flujo) y sin reintento con pausa.

## Decision

- **Varias programaciones por flujo.** `ImportProcess` ya era una fila por horario; se quita el supuesto de
  "una por flujo" en `ScrapeFlowService` (`ListSchedulesAsync`, `SaveScheduleAsync` por Id,
  `DeleteScheduleAsync`). La UI lista las programaciones del flujo con nombre (p.ej. "Diaria" y "Barrido
  semanal") y un editor para cada una.
- **Secuencia de tramos (generica, configurable por el usuario).** Nueva columna `ImportProcess.SequenceJson`
  (`FlowSequenceSpec`, migracion `AddImportProcessSequenceJson` PG + SQL Server). El usuario elige la variable
  de desplazamiento y la de largo (por defecto DIAS_DESDE / DIAS_ATRAS), el rango (Desde..Hasta), el largo del
  tramo, la pausa entre tramos, los reintentos y la espera antes de reintentar. Tramo k = {VarDesde =
  Desde + k*Paso, VarLargo = Paso} (el ultimo se recorta). Maximo 120 tramos.
- **`FlowSequenceRunner` (singleton).** Corre los tramos uno tras otro con `RunFlowStepwiseAsync` (la misma
  via del boton "Importar del bot"), pasando los valores del tramo como overrides de corrida (nunca se
  persisten). Espera a que termine cada tramo, pausa, reintenta los que fallan y sigue con el siguiente si se
  agotan los reintentos; si el agente queda offline, aborta. Deja una fila de resumen en la bitacora del
  flujo ademas de la de cada tramo. Se puede lanzar ya ("Ejecutar secuencia") y detener entre tramos.
- **Gancho de modulo `IFlowRunOverrideProvider`.** El motor no conoce a los modulos (ADR-0169). El modulo DIAN
  registra `ConciliacionDianFlowOverrides`, que inyecta CUFES_CONOCIDOS si el flujo es una extraccion del
  conciliador. El calculo se movio de la pagina a `IConciliacionDianService.CufesConocidosAsync`, que usan el
  boton y la secuencia (hacen exactamente lo mismo).
- **Candado por flujo.** `IBrowserRunService.IsFlowRunning(flowId)` (corridas vivas por flujo). El dispatcher
  salta (y anota) la ventana de un flujo ocupado; la secuencia espera a que termine una corrida viva antes de
  cada tramo; el conciliador no lanza "Importar del bot" si el flujo esta ocupado.

- **Desencadenador estilo Programador de tareas de Windows (2026-11-16, pedido del usuario: "nada de cron").**
  Nueva columna `ImportProcess.TriggerJson` (`ScheduleTrigger`, migracion `AddImportProcessTriggerJson` PG + SQL
  Server): fecha/hora de inicio; Una vez / Diaria cada N dias / Semanal cada N semanas con dias / Mensual por meses
  + dias (1..31, ultimo) u ordinal ("el ultimo viernes"); repetir cada X min durante Y; caducidad. Si existe, manda
  sobre el cron en `ImportRecurrence.ComputeNextRun` (se guarda ademas un cron aproximado para lectores viejos).
  Nuevo `ImportScheduleProblem.Finished`: una "una vez" ya corrida o caducada se deshabilita sin alarma. La UI es un
  asistente de 5 pasos (General, Desencadenador, Accion, Configuracion, Finalizar con las proximas 5 ejecuciones) y
  una biblioteca con Ejecutar / Propiedades / Habilitar / Eliminar. `FlowSequenceSpec.MaxHoras` = "detener la
  tarea si dura mas de N horas" (se revisa entre tramos).

## Opciones consideradas

- **Cursor persistente (un tramo por disparo y avanzar DIAS_DESDE en BD):** descartado; ensucia la
  configuracion del flujo (ADR-0169 ya descarto persistir valores de una corrida) y no da pausa/reintento.
- **Lista explicita de tramos:** el rango + paso cubre los casos reales y es mas facil de configurar; la lista
  se puede agregar despues sin migracion (vive en el JSON).
- **Estado de la secuencia en BD:** descartado por ahora; en memoria basta (los tramos ya corridos quedan en
  la bitacora y el siguiente horario empieza de nuevo). Si un reinicio corta una secuencia larga, el barrido
  siguiente la cubre (los CUFE ya cargados se saltan).

## Consecuencias

- Configuracion recomendada para el DRON DIAN (SOLDARCO): "Diaria" cron `0 6 * * *`, tramos Desde 0 Hasta 6
  Paso 3; "Barrido semanal" cron `0 1 * * 0`, Desde 0 Hasta 60 Paso 3, pausa 5, 1 reintento a los 20 min
  (~20 tramos, unas 5 h de madrugada). VENTANA_DIAS=1.
- Un reinicio del servidor a mitad de secuencia la corta (como el despliegue de v0.16.326 corto un tramo
  manual el 2026-11-15); conviene no desplegar durante el barrido semanal.
- La programacion existente de cualquier flujo sigue igual (una corrida, `RunFlowNowAsync`) si no tiene
  secuencia.
