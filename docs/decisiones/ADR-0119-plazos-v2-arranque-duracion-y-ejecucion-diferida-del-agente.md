# ADR-0119: Plazos v2 - tiempo de arranque (y ejecucion diferida del agente) + duracion por paso

**Estado:** Aceptado (Fases A+B+C implementadas)
**Fecha:** 2026-10-05
**Contexto de codigo:** WorkflowNode.SlaJson, StepSla, StepDeadlineCalculator,
WorkflowEngine.ActivateNodeAsync + StampStepDeadlinesAsync, WorkflowStepHistory, TaskItem.StartDate/DueDate,
dispatcher de pasos de agente (WorkflowNodeAgent / AiStepOrchestrator), TenantOperatingDay.
**Extiende:** ADR-0106 (plazos por paso, Fase 1). **Relacionado:** ADR-0090 (agentes en nodos de flujo).

## Contexto

ADR-0106 dio UN solo valor de tiempo por nodo ("plazo") que se interpreta como VENCIMIENTO:
`DueAt = activacion_real + plazo`. Al calibrar el uso real, el usuario necesita separar DOS cosas por nodo:

1. **Cuando ARRANCA** el nodo ("tiempo para arrancar").
2. **Cuanto debe DEMORAR** el nodo ("tiempo estimado para entregar"), que es el plazo que ya existe.

Reglas del usuario:
- Para un nodo **con agente**, el arranque es **real**: la tarea del agente debe **ejecutarse en esa fecha**
  (retardo/seguimiento), no de inmediato.
- Para un nodo **humano/sistema**, el arranque es solo una **ayuda de programacion**: el sistema estampa la
  fecha de inicio en la tarea (campos "Inicio"/fecha de la tarjeta de actividad), pero NO se puede forzar a una
  persona a arrancar a una hora.
- El **recorrido por ramas YA funciona** y NO se toca: ADR-0106 ancla cada paso a su activacion real, asi que
  compuertas y atrasos se propagan solos. Esta decision mantiene ese anclaje paso-a-paso (no agrega ninguna
  proyeccion nueva por el grafo).

## Decision

1. **Dos atributos por nodo** (el panel "Plazo del paso" pasa a dos bloques):
   - **Tiempo para arrancar** (NUEVO): `Inmediato` (default) | `Esperar { days, hours, minutes, dayMode }`.
     Se persiste en `WorkflowNode.StartDelayJson` (mismo shape que `StepSla`). Null/ausente = Inmediato.
   - **Tiempo estimado para entregar** (CONVIVE): el actual `WorkflowNode.SlaJson` se re-significa como
     **duracion** del nodo. Mismo storage, mismo `StepSla`, sin migracion para esto.

2. **Fechas por paso** (estampado cuando el paso queda vigente, sobre la rama real, como ADR-0106):
   - `inicio(paso) = activacion_real + arranque`. Con Inmediato, `inicio = activacion_real` (identico a hoy).
   - `vence(paso) = inicio(paso) + duracion`. Hoy era `activacion + plazo`; ahora respeta el arranque.
   - Se estampan en `WorkflowStepHistory`: `DueAt` (ya existe) = vence; `StartAt` (NUEVO) = inicio planeado.

3. **Fechas de la actividad** (reuso de `TaskItem`, igual que ADR-0106): `StartDate` = inicio del primer paso
   (no pisa manual); `DueDate` = fecha final estimada que RUEDA. Esto llena los campos Inicio/entrega de la
   tarjeta de actividad (la "ayuda de programacion" pedida).

4. **Agente = ejecucion diferida** (lo nuevo de verdad): cuando el paso de un nodo-agente queda vigente, el
   dispatcher **no ejecuta antes de `inicio(paso)`** (honra un "no antes de" = inicio). Con arranque Inmediato
   corre ya (como hoy); con "Esperar X" el agente corre X despues de la activacion. Contrato aqui; detalle de
   implementacion en Fase C.

5. **Humano/sistema = arranque informativo:** se estampa `StartAt`/`StartDate` pero no bloquea; la persona
   arranca cuando puede. El arranque real solo aplica a nodos con agente.

6. **Retrocompatibilidad y seguridad:** `SlaJson` (=duracion) intacto; sin `StartDelayJson` => Inmediato =>
   comportamiento identico al de hoy. Best-effort (no tumba el avance). La navegacion por ramas NO se toca. El
   estimado total hacia adelante sigue usando `StepNumber` como hoy (aproximacion conocida; no se cambia, por
   la advertencia del usuario de no tocar lo que ya funciona en el recorrido).

## Opciones consideradas

### Opcion A: dejar un solo "plazo" (hoy)
Simple, pero no separa arranque de duracion ni permite diferir la ejecucion del agente. Es justo lo que se
queda corto. **Rechazada.**

### Opcion B: dos atributos (arranque + duracion), arranque real solo para agentes **(elegida)**
| Dimension | Evaluacion |
|-----------|------------|
| Complejidad | Media (un campo nuevo + un estampado + diferir al agente) |
| Retrocompatibilidad | Alta (Inmediato por defecto = comportamiento actual) |
| Riesgo al recorrido de ramas | Nulo (no se toca; se mantiene el anclaje por paso) |
| Fidelidad a lo pedido | Alta (inicio para agente, fechas estampadas en la tarea, convivencia) |

### Opcion C: nodo "timer/espera" dedicado (BPMN timer event)
Mas fiel a BPMN, pero agrega un tipo de nodo, UI y navegacion nuevos. El usuario pidio explicitamente que el
tiempo **conviva** en el nodo (no un nodo aparte). **Rechazada por alcance.**

## Alcance por fases

- **Fase A (este ADR):** modelo + contrato.
- **Fase B:** `StartDelayJson` + lectura; `StepDeadlineCalculator` (inicio = activacion + arranque;
  vence = inicio + duracion); estampado de `StartAt`/`DueAt` y `StartDate`/`DueDate`; migracion dual
  (`start_delay_json` en `workflow_nodes`, `start_at` en `workflow_step_history`); UI del disenador (dos
  bloques: arranque | entrega). Verificada en integracion dual (PG + SQL Server).
- **Fase C (aparte):** ejecucion diferida real del agente (el dispatcher honra "no antes de" = inicio del paso).

## Consecuencias

- Cada tarea queda con **Inicio** y **entrega** programadas por el flujo (lo que el usuario ve en la tarjeta).
- Los nodos de agente pueden **ejecutarse diferidos** (seguimiento a los X min/dias).
- **Retrocompatible:** lo publicado sigue igual (Inmediato por defecto); sin plazos, no se tocan fechas.
- El `StartAt` planeado de un paso humano es **informativo** (si la persona arranca antes/despues, es estimado).
- **Riesgo:** la ejecucion diferida del agente (Fase C) depende del worker; con `ECOREX_DISABLE_WORKERS` no
  corre (igual que hoy los agentes). Se documenta.
- No cambia la navegacion por ramas ni el estimado por `StepNumber` (se respeta lo que ya funciona).

## Action items

1. [x] (B) `WorkflowNode.StartDelayJson` + lectura via `StepSla` (reuso del parser).
2. [x] (B) `StepDeadlineCalculator`: `inicio = activacion + arranque`; `vence = inicio + duracion` (composicion en el motor; +3 tests).
3. [x] (B) `WorkflowEngine.StampStepDeadlinesAsync`: estampar `StartAt` + `DueAt`; `StartDate`/`DueDate` de la actividad.
4. [x] (B) Migracion dual: `start_delay_json` (workflow_nodes), `start_at` (workflow_step_histories).
5. [x] (B) UI del disenador: dos bloques (Tiempo para arrancar | Tiempo estimado para entregar).
6. [x] (C) Dispatcher de pasos de agente honra "no antes de" = `StartAt` (worker barre cada 30s).

## Estado de implementacion

Commits en `fase-0/clon-backbone`: ADR (`a32a1978`), Fase B (`932ab27b`), Fase C (`bec917d8`). Build verde;
App 1112 / Domain 35 / SuperAdmin 173 tests verdes; migracion dual auto-aplicada en dev. Sin desplegar a prod.
Pendiente: validacion E2E (configurar arranque en un nodo-agente y ver la ejecucion diferida) + deploy.
