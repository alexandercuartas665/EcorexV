# ADR-0106: Plazos (SLA) por paso de flujo y fechas estimadas de la actividad

**Estado:** Aceptado (Fase 1)
**Fecha:** 2026-09-21
**Contexto de codigo:** WorkflowNode, WorkflowStepHistory, WorkflowEngine.ActivateNodeAsync, TaskItem.

## Contexto

Los flujos BPMN no tenian ningun concepto de tiempo: ni plazo por paso, ni vencimiento, ni calendario.
El usuario necesita definir el **tiempo estimado de cada paso** (dias + horas + minutos) y que de ahi salgan
las **fechas** de la actividad. Regla del usuario: el reloj real de cada paso arranca cuando el paso anterior
**termina de verdad**; el flujo solo da un **estimado**; y la **fecha final se recalcula (rueda)** si un paso
se atrasa. Los DIAS pueden contarse como **calendario** o como **habil** (saltando fines de semana y los dias
no operativos del tenant).

## Decision

1. **Plazo por nodo** en `WorkflowNode.SlaJson` (jsonb/nvarchar): `{ days, hours, minutes, dayMode:"calendar|business" }`
   (ver `StepSla`). Solo los dias respetan el modo; horas y minutos son tiempo real. Null = sin plazo.
2. **Calculo puro y testeable** en `StepDeadlineCalculator` (dias calendario vs habiles, encadenamiento para el
   total). Trabaja en hora LOCAL del tenant; el motor convierte con `ScheduledJobRecurrence.ResolveTimeZone`
   (arreglando la deuda del "UTC-5" cableado) y persiste UTC.
3. **Ancla = activacion real del paso.** En `WorkflowEngine.ActivateNodeAsync`, cuando un paso queda vigente
   (Pending), se estampa `WorkflowStepHistory.DueAt = inicio_real + plazo_del_nodo`. El inicio real es el
   `CreatedAt` del paso y el fin real su `CompletedAt` (ya existian). Anclar a "cuando el paso se activa" (en vez
   de literalmente "al anterior") resuelve compuertas/ramas y hace que los atrasos se propaguen solos.
4. **Fechas de la actividad (reuso de `TaskItem`):** `StartDate` = inicio real del primer paso (no pisa una
   fecha manual); `DueDate` = fecha final ESTIMADA que **rueda**: en cada activacion = inicio real del paso
   actual + suma de los plazos del paso actual y los siguientes (por `StepNumber`).
5. **Calendario operativo por tenant:** nueva entidad `TenantOperatingDay` (fecha no operativa + motivo), que el
   modo habil salta ademas de sabados/domingos. Se administra en la configuracion de la entidad (Fase 2).
6. **Best-effort:** el estampado nunca tumba el avance del flujo (try/catch); si el flujo no configura plazos,
   no se tocan las fechas (se respetan las manuales).

## Alcance por fases

- **Fase 1 (esta):** modelo + calculo + estampado en el motor + migracion dual (`sla_json`, `due_at`,
  `tenant_operating_days`). Verificado en integracion dual (PG + SQL Server).
- **Fase 2:** editor del plazo por nodo en el diseñador de flujos (junto a las alertas) + pantalla del
  calendario operativo del tenant + hora visible/editable en fecha inicial/final de la actividad.
- **Futuro (no ahora):** alertas/escalamiento por vencimiento (reusarian ADR-0100 + el patron de worker);
  que las HORAS respeten una jornada laboral (hoy solo los DIAS respetan el calendario).

## Consecuencias

- Se agrega tiempo a los flujos reusando `TaskItem.StartDate/DueDate` (visibles en tablero/calendario/Gantt) sin
  UI nueva de fechas.
- El calculo de fechas pasa a leer la zona horaria real del tenant (mejora transversal).
- Pendiente: exponer `SlaJson` en los DTOs/editor del flujo (Fase 2) para que el usuario lo configure sin SQL.
