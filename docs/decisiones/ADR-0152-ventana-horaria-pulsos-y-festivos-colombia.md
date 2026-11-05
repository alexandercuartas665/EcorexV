# ADR-0152: Ventana horaria de los pulsos del agente + calendario operativo en la entidad + autocarga de festivos de Colombia

**Status:** Accepted
**Date:** 2026-11-05
**Deciders:** Usuario (producto) + agente de desarrollo (Claude Opus 4.8)

## Contexto

Los "pulsos" (recordatorios/seguimiento) de un nodo de flujo se programan con una cadencia relativa al
paso anterior (`WorkflowNode.AgentFollowUpJson`: lista ordenada de esperas `[{days,hours,minutes}]`). El
proximo reintento se fija como `step.AgentNextRetryAt = now + espera` (UTC) y lo re-despacha
`WorkflowAgentStepDispatcher`. Problema real reportado: un pulso caia a las 23:00 o la 01:00 porque el
calculo es puramente relativo y no respeta el horario en que las personas reciben mensajes.

El usuario pidio una **ventana horaria** en la que el pulso puede transmitirse; si un pulso cae fuera, se
difiere al **inicio de la proxima ventana disponible**. Decisiones confirmadas por el usuario:

- La configuracion vive en el **modal "Plazos" del nodo**, junto al bloque "Seguimiento (plazos)".
- Alcance: **solo los pulsos del agente** (no todo plazo de flujo).
- Ventana por **horas + dias**, reutilizando el **calendario operativo** (festivos) del tenant.

En el camino salieron dos temas conexos:
1. El calendario operativo (dias no operativos) estaba en una pagina suelta `/config-calendario`; el
   usuario pidio llevarlo a **"Configuracion de la entidad"** como un boton que abre un modal.
2. El sistema **no sabia** que dias son festivos: el calendario era 100% manual. El usuario pidio
   **autocargar los festivos de Colombia** del anio (por defecto Colombia).

## Decision

1. **Ventana horaria embebida en la cadencia**, SIN migracion. `AgentFollowUpJson` pasa a admitir, ademas
   del array plano legacy, un objeto `{ items:[...], window:{ enabled, startMin, endMin, days[], respectHolidays } }`.
   `days` en ISO (1=lunes..7=domingo); vacio = todos. Lectura retrocompatible: un array plano se sigue
   leyendo como filas sin ventana.
2. **Clamp al programar el pulso**. `WorkflowAgentStepRunner`, al fijar `AgentNextRetryAt`, llama a
   `PulseSendWindow.ClampUtc(rawUtc, tz, nonWorking)`: si cae dentro de la ventana no cambia; si es antes
   de abrir ese mismo dia activo, al inicio de la ventana; si no, al inicio del proximo dia activo
   (saltando fines de semana, dias fuera de `days`, y festivos del calendario operativo si
   `respectHolidays`). La zona es la del tenant (`Tenant.TimeZoneId`, Bogota UTC-5 por defecto).
3. **Calendario operativo reutilizable y reubicado**. Se extrae el editor a un componente
   `CalendarioOperativoEditor.razor` que usan tanto la pagina `/config-calendario` como un **modal en
   "Configuracion de la entidad"** (boton "Calendario habil").
4. **Autocarga de festivos de Colombia**. `ColombianHolidays.ForYear(year)` calcula los 18 festivos
   nacionales (fijos + Ley Emiliani al lunes siguiente + los de Pascua por computus gregoriano) y
   `ITenantOperatingCalendarService.SeedColombianHolidaysAsync(year)` los siembra en el calendario del
   tenant, idempotente por fecha. Un boton "Cargar festivos de Colombia [anio]" en el editor lo dispara.
   Siguen siendo editables/borrables a mano; los fines de semana se detectan solos en el motor de plazos.

## Opciones consideradas

### Ventana: dato propio (nueva columna/tabla) vs embebida en el JSON existente
- **Nueva columna/tabla**: mas explicito, pero exige migracion EF en los dos motores (PG + SQL Server) y
  toca el modelo. Complejidad media/alta.
- **Embebida en `AgentFollowUpJson`** (elegida): cero migracion, retrocompatible, la ventana viaja con la
  cadencia a la que aplica. Complejidad baja. Contra: hay que normalizar con cuidado para no perder la
  ventana al reescribir el JSON (ver Consecuencias).

### Festivos: motor manual vs autocalculo por pais
- **Solo manual** (estado previo): el cliente escribe cada festivo; cero codigo pero tedioso y propenso a
  olvidos.
- **Autocalculo (elegida, Colombia por defecto)**: `ColombianHolidays` puro, sin estado; un clic siembra
  el anio. Editable despues. Facil de extender a otros paises cambiando solo el calculador.

## Trade-offs

- Guardar la ventana dentro del JSON evita migracion pero obligo a corregir un bug latente: el servicio
  re-normalizaba la cadencia y descartaba la ventana. Se arreglo pasando la ventana en la reescritura.
- El clamp usa la zona del tenant y el calendario operativo del tenant: correcto multi-tenant, una lectura
  extra por programacion de pulso (barata, `AsNoTracking`, solo cuando la ventana esta activa).
- La autocarga replica la Ley Emiliani y la Pascua en codigo; si la ley cambiara habria que ajustar el
  calculador (riesgo bajo, la ley es estable desde 1983).

## Consecuencias

- **Mas facil**: configurar que los pulsos no molesten de madrugada ni en fines de semana/festivos, sin
  codigo; precargar los festivos del anio de un clic.
- **Mas dificil / a vigilar**: cualquier reescritura de `AgentFollowUpJson` debe preservar la ventana
  (`StepSlaList.Build(rows, PulseSendWindow.Read(json))`). Ya cubierto en `WorkflowDesignService`.
- **A revisar luego**: extender la autocarga a otros paises (hoy solo Colombia); opcionalmente exponer la
  zona horaria por tenant en la UI si algun cliente no es Bogota.

## Archivos tocados

- `Ecorex.Application/Workflows/StepSla.cs` (PulseSendWindow + Build con ventana + lectura tolerante).
- `Ecorex.Application/Workflows/WorkflowAgentStepRunner.cs` (clamp al fijar AgentNextRetryAt).
- `Ecorex.Application/Workflows/WorkflowDesignService.cs` (preservar ventana al normalizar).
- `Ecorex.Application/Tenancy/ColombianHolidays.cs` (NUEVO, calculador puro).
- `Ecorex.Application/Tenancy/ITenantOperatingCalendarService.cs` + `TenantOperatingCalendarService.cs`
  (SeedColombianHolidaysAsync idempotente).
- `Ecorex.SuperAdmin/Components/Shared/FlowEditor.razor` (UI de la ventana en el modal Plazos).
- `Ecorex.SuperAdmin/Components/Shared/CalendarioOperativoEditor.razor` (NUEVO, editor reutilizable +
  boton de festivos de Colombia).
- `Ecorex.SuperAdmin/Components/Pages/ConfiguracionCalendario.razor` (usa el componente).
- `Ecorex.SuperAdmin/Components/Pages/ConfiguracionEntidad.razor` (boton "Calendario habil" + modal).
- Tests: `PulseSendWindowTests.cs`, `ColombianHolidaysTests.cs`.

## Action items

1. [x] Ventana horaria + clamp + fix de persistencia (con tests).
2. [x] Calendario operativo reubicado a la entidad (modal).
3. [x] Autocarga de festivos de Colombia (con tests).
4. [ ] Reflejar este ADR en el vault de Obsidian.
