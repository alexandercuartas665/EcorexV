# ADR-0019: Suite E2E con Playwright para .NET contra la consola Blazor

- **Fecha**: 2026-07-03
- **Estado**: aceptado
- **Contexto**: FASE 7 (calidad). La Estrategia de Testing del vault define una capa E2E
  sobre las capas unitaria (Domain/Application) y de integracion (Testcontainers dual).
  El frontend es 100 % Blazor Server (regla firme, sin Node), asi que el runner E2E
  tambien debe ser .NET: **Microsoft.Playwright + xunit** (xunit por consistencia con
  las suites existentes), Chromium headless.

## Decision

Proyecto nuevo `apps/backend/tests/Ecorex.E2E.Tests` (net10.0, en `Ecorex.sln`) con 7
escenarios contra la consola `Ecorex.SuperAdmin` y la BD dev de Postgres (5442, seed
demo SKY SYSTEM): login y KPIs de /inicio, wizard de creacion de actividad (toast +
kanban), transicion de estado desde el detalle, worklog manual, recorrido del flujo
demo COT-COM con FRM-001 ("Formularios del paso"), visor publico /f/{token} con token
de un solo uso, y aislamiento visual del tenant.

### Resolucion de la app bajo prueba: BASEURL con skip, arranque automatico como extra

La estrategia BASE (robusta para el CI futuro) es la variable `ECOREX_E2E_BASEURL`:
si esta definida, la suite usa esa consola ya corriendo; si la URL no responde
`/login` 200, **toda la suite se salta** (Xunit.SkippableFact, `Skip.If`) con el motivo
en el mensaje, sin fallar en falso. Como conveniencia local, si la variable NO esta
definida el fixture arranca la consola el mismo (`dotnet run --no-build` en el primer
puerto libre 5250+, Development, `ECOREX_DB_CONNECTION` al Postgres 5442), espera
`/login` 200 hasta 120 s y al terminar mata el arbol de procesos. Cualquier
imposibilidad (falta el build, no hay Postgres, faltan los binarios de Chromium)
degrada a Skip explicativo, nunca a rojo enganoso.

Los binarios de navegador se instalan una vez por maquina:
`pwsh bin/Debug/net10.0/playwright.ps1 install chromium` (README del proyecto).

### Por que el cambio de estado usa el dropdown del detalle y no drag and drop

El kanban usa la API nativa de drag and drop de HTML5 cableada a Blazor
(`@ondragstart/@ondrop`). Automatizar esa secuencia con Playwright es notoriamente
fragil: `DragToAsync` sintetiza mouse events (no siempre los eventos `dragstart/drop`
reales), y sobre Blazor Server cada evento viaja por SignalR con re-render entre medio,
lo que produce falsos negativos intermitentes. El dropdown de transiciones del detalle
ejercita exactamente el MISMO camino de negocio (`TaskItemService.ChangeStatusAsync`
-> `TaskItemStateMachine`) por una interaccion determinista; el test ademas verifica
que el menu solo ofrece las transiciones validas desde Pending. El drag and drop como
gesto queda cubierto manualmente y podra automatizarse si algun dia se cambia a una
libreria de DnD no nativa.

### Backdoor puntual para el paso sin UI (documentado, no un atajo silencioso)

El flujo demo arranca en el paso "Requerimiento", que no exige formulario y HOY no
tiene UI para completarse (la bandeja de pasos del flujo es deuda declarada de
ADR-0014; /flujos sigue siendo stub). Para ejercitar "Formularios del paso" de punta a
punta, el test completa ese primer paso con el MISMO motor
(`WorkflowEngine.CompleteStepAsync` via `E2eDbBackdoor`, que espeja el patron de
Ecorex.Integration.Tests) y verifica contra el motor que el submit del formulario
avanza la instancia a la compuerta. Cuando exista la bandeja de pasos, ese backdoor se
reemplaza por la interaccion real y se elimina.

### Convenciones de la suite

- **Selectores**: rol/texto accesible (`GetByRole`) o clases CSS estables del
  prototipo. El producto NO tiene `data-testid` y la suite no puede agregarlos.
  Los labels de formularios no estan asociados por `for`/`id` (no sirve `GetByLabel`):
  cada control se ancla a su `div.field`/columna por el texto del label.
- **Independencia e idempotencia**: una sola coleccion xunit (secuencial), un contexto
  de navegador nuevo por test (cookies aisladas) y datos con sufijo unico por corrida
  (`E2E {guid8}`) sobre la BD dev persistente.
- **@onchange y Playwright**: los controles del renderer/wizard usan `@onchange`;
  `FillAsync` solo dispara `input`, asi que los helpers hacen blur explicito (o el foco
  del siguiente campo) para que el servidor vea el valor.

## Hallazgo de producto (bug real detectado por la suite)

Llenar rapido un formulario dinamico con reglas vinculadas (FRM-001: campos
`nombre_solicitante` y `prioridad` con RUL-005) tumba el circuito: cada `@onchange`
despacha `IFormRuleDispatcher` con consultas EF sobre el DbContext scoped del circuito
y dos cambios casi simultaneos producen `A second operation was started on this
context instance`; el circuito muere en silencio y "Enviar" deja de responder. Un
humano tabulando rapido puede reproducirlo. Fix sugerido (fuera del alcance de esta
tarea, que no toca producto): scope EF propio por dispatch o `SemaphoreSlim`, como ya
hace `TaskKanban.ReloadAsync` por el mismo motivo. Mitigacion mientras tanto en
`PublicFormFiller`: los campos con reglas se llenan con espera deterministica (la
copia de PASAR_CAMPOS hacia `descripcion`) o pausa fija.

## Estado del seed dev y escenario de aislamiento

La BD dev actual se sembro con una version anterior del seeder (solo
`demo-admin@ecorex.tareas`; el seed inicial corre solo con `platform_users` vacia),
asi que el escenario (g) con `owner@sky-system.local` se salta con motivo explicito.
En una BD recien sembrada (`docker compose down -v && up` + arranque Development) el
caso corre completo.

## Plan de CI (NO aplicado aun; el yml no se toca en esta tarea)

Integrar como **job separado** de `pr-check.yml` (ADR-0018), no dentro de `build-test`:

1. Job `e2e` (ubuntu-latest, `needs: build-test`): servicio Postgres 16 (o
   Testcontainers), `dotnet build`, sembrar via arranque Development,
   `playwright.ps1 install chromium --with-deps`, arrancar la consola en background,
   `ECOREX_E2E_BASEURL=http://localhost:5250 dotnet test tests/Ecorex.E2E.Tests`.
2. Gate NO bloqueante al inicio (continue-on-error) hasta medir estabilidad real en
   Actions; luego promover a gate de merge.
3. La estrategia BASEURL + Skip ya deja el fixture listo para ese modo: en CI la app
   se arranca como paso previo y la suite jamas se salta en silencio (el job debe
   fallar si la URL no responde, chequeo previo con curl).

## Consecuencias

- (+) Cobertura de los recorridos criticos del prototipo con el navegador real, en
  .NET puro y sin tocar codigo de producto.
- (+) El fixture degrada a Skip con motivo (nunca rojo por entorno), apto para dev
  local y CI.
- (-) Dos esperas fijas inevitables (radio con regla sin efecto visible) hasta que el
  producto arregle la carrera del dispatcher de reglas.
- (-) El escenario del flujo depende del backdoor del motor hasta que exista la
  bandeja de pasos (ADR-0014).
