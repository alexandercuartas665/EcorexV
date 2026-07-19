# ADR-0042: bitacora dedicada (ScrapeFlowRun) para las corridas de un flujo de extraccion

Fecha: 2026-07-18
Estado: Aceptada
Contexto: modulo 000730 "Extraccion de Datos", Ola 3 (runtime determinista).

## Contexto

El runtime de un flujo de extraccion (compilar -> empujar al sub-agente Navegador -> ingerir ->
cerrar) necesita una BITACORA: un flujo corre sin nadie mirando, y sin registro un fallo es
indistinguible de "no habia datos". El doc 03 del capitulo dice "cerrar la corrida en `ImportRun`",
reusando la bitacora del Contenedor de datos.

El problema: `ImportRun` **cuelga de un `ImportProcess`** (`ProcessId` obligatorio, con indice unico
`(TenantId, ProcessId, FiredAt)` que da la idempotencia del horario). `ImportProcess` es la
PROGRAMACION, atada a un conector del Contenedor. El disparo manual de un flujo ("Ejecutar ahora") no
tiene un `ImportProcess`, y forzar uno por flujo solo para poder registrar una corrida manual acopla el
runtime del flujo a toda la maquinaria de horarios antes de tiempo.

## Decision

Se crea una bitacora **dedicada**, `ScrapeFlowRun` (FK dura al `ScrapeFlow`, cascada), espejo de
`ImportRun` en forma y ciclo (Running -> Ok/Error/PendingOffline, puente por `CorrelationId`), pero sin
depender de `ImportProcess`. Reusa los enums `ImportRunTrigger` y `ImportRunResult` (no se inventan
estados nuevos). Su servicio `IScrapeFlowRunLog` es el analogo de `IImportRunLog`.

## Consecuencias

- El disparo manual de un flujo registra corridas sin necesitar una programacion. La UI del flujo
  tiene su propio "Historial de corridas".
- Cuando la Ola 5 cablee la PROGRAMACION, un `ImportProcess` podra apuntar a un flujo (campo `FlowId` o
  generalizando su objetivo) y seguir dejando su corrida en `ScrapeFlowRun`, sin remodelar nada: el
  disparo (manual u horario) solo cambia el `Trigger`.
- Coste: una tabla y un servicio de bitacora mas, casi identicos a los de importacion. Se acepta a
  cambio de no acoplar el runtime del flujo al modelo de horarios del Contenedor todavia.
- La ingesta de los pasos Extract SI reusa el nucleo compartido `IRowIngestService` (no se duplica), y
  el patron de despacho/correlacion/offline/sweep replica el de `AgentImportService`.

## Alternativas descartadas

- **Reusar `ImportRun` con un `ImportProcess` por flujo**: obliga a crear y mantener un proceso "manual"
  fantasma por cada flujo, y a generalizar el indice de idempotencia; mas acoplamiento por menos
  claridad. Se pospone a la Ola 5, cuando la programacion sea real.
- **No registrar corridas manuales**: rompe la razon de ser de la bitacora (operabilidad sin nadie
  mirando) y deja "Ejecutar ahora" sin rastro.
