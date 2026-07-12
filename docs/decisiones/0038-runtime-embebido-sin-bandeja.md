# ADR-0038: Runtime de flujos embebido en la tarea; se retira la bandeja "/mis-pasos"

- Estado: aceptada
- Fecha: 2026-07-12
- Supersede (parcial) a: ADR-0036 (runtime operativo - bandeja "mis pasos").
- Relacionada con: ADR-0035 (asignacion por nodo), ADR-0014 (workflow engine),
  ADR-0020 (tableros de actividades unificados), ADR-0015 (formularios dinamicos).

## Contexto

ADR-0036 entrego el runtime de flujos en DOS superficies a la vez:

1. una pagina independiente `/mis-pasos` (MisPasos.razor) + item de menu 000637 "Mis pasos"
   en la seccion "Mis Procesos", y
2. la seccion "Flujo" embebida en el detalle de la tarea (`TaskDetailModal.razor`).

Decision del usuario: **los flujos se ejecutan DENTRO de la tarea**. No debe existir un
modulo/pagina "Mis pasos" separado. El paso pendiente que cae en el usuario debe DESCUBRIRSE
en el TABLERO, dentro de "mis pendientes", no en una bandeja aparte. La bandeja duplicaba la
superficie y partia el modelo mental (una cosa es la tarea, otra "mis pasos").

## Decisiones

### 1. Superficie unica de ejecucion = el detalle de la tarea

Tomar / Atender / Completar / Aprobar / Reasignar viven en la seccion "Flujo" del detalle de la
tarea, reusando `IWorkflowInboxService` (que se conserva como servicio). El usuario abre la tarea
y atiende su paso actual ahi; no hay otra pantalla para "atender pasos".

### 2. Descubrimiento = el TABLERO ("mis pendientes")

El tablero de actividades es la bandeja. El alcance "Pendientes mios" debe incluir no solo las
tareas donde el usuario es encargado/asignado (M:N actual), sino tambien las tareas cuyo PASO
ACTUAL del flujo esta ruteado al usuario: por `AssignedToTenantUserId` directo, o por el CARGO del
nodo cuando el usuario es candidato (`INodeAssigneeResolver.ResolveCandidates`). Asi, un paso que
cae en tu cargo aparece en tus pendientes del tablero, abres la tarea y lo atiendes.

### 3. Se retira la bandeja independiente

Quedan RETIRADOS de producto y documentacion: la pagina/ruta `/mis-pasos` (MisPasos.razor), el
item de menu 000637 "Mis pasos" (seed y tenants ya sembrados), y la policy `MisPasos.Ver`. Se
CONSERVA `IWorkflowInboxService` (lo consumen el detalle de la tarea y el filtro del tablero).

## Consecuencias

- Un solo modelo mental para el usuario: la tarea. Menos superficie, menos duplicidad.
- El tablero pasa a ser la bandeja: requiere EXTENDER el alcance "Mine" del tablero para considerar
  el paso actual del flujo (asignado o candidato por cargo). Esta ADR es la DECISION; el cambio de
  codigo (filtro del tablero + retiro de la pagina/menu) va en una ola aparte.
- Migracion de menu: quitar/ocultar el nodo 000637 en el seed del menu y reconciliar en los tenants
  demo ya sembrados.

## Deudas / pendientes de implementacion

- Extender el scope `Mine` del tablero (ActivityBoardService.ApplyScope) para incluir tareas con
  paso actual ruteado al usuario (asignado o candidato por cargo).
- Retirar MisPasos.razor + ruta + item 000637 + policy MisPasos.Ver; reconciliar seeds.
- (Opcional) contador de "mis pendientes" con pasos de flujo en el chip del tablero.
