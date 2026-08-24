# ADR-0075: Herencia de encargado robusta + el evento de FIN auto-finaliza la tarea

- Estado: Aceptado
- Fecha: 2026-08-24
- Contexto: motor de flujos (ADR-0014), origen del asignado por nodo (ADR-0056), eventos
  atendidos (ADR-0068, se revierte para el fin), compuerta atendida elige rama (ADR-0072).

## Contexto

Probando T00032 (usuario direccionventas, asignado por cargo al primer paso), al cerrar la primera
Tarea la COMPUERTA siguiente no dejaba elegir la ruta: su menu salia en solo lectura.

Diagnostico con datos:
- La compuerta usa `AssigneeSource = InheritStart` (hereda al INICIADOR del flujo).
- El iniciador de la instancia (`StartedByTenantUserId`) estaba guardado con el id de un **PLATFORM
  user** (`calidad@...`), NO un tenant_user. Origen: la actividad se creo pasando a
  `StartInstanceAsync` un `actorUserId` que era el id de plataforma del creador (bug de una capa
  superior, ver "Pendiente").
- Resultado: la compuerta quedaba "asignada" a un id fantasma que no es tenant_user -> `IsMine`
  falso para todos -> nadie podia decidir (y con el cierre estricto de ADR-0073, tampoco un
  Owner/Admin).

Ademas, el usuario aclaro como debe comportarse un EVENTO DE FIN: no lo cierra el usuario; al
alcanzarlo debe dar por terminada la tarea, ejecutar las reglas del nodo y (si tiene) saltar a otro
flujo.

## Decision

1. **Herencia de encargado robusta** (`ResolveDynamicAssigneeAsync`): `InheritStart` e
   `InheritPrevious` VALIDAN que el id resuelto sea un tenant_user real del tenant. Si no lo es
   (iniciador nulo, o guardado con un id que no es de tenant, o usuario borrado), caen al encargado
   del PASO ANTERIOR. Asi el caso lo sigue llevando quien lo tomo (el primer paso se resolvio por
   cargo) y ningun paso queda "asignado" a un fantasma. Helper `FirstValidTenantUserAsync`.

2. **El evento de FIN auto-finaliza** (`FinalizeEndEventAsync`): al alcanzar un EndEvent, el motor lo
   AUTO-completa (no queda Pending esperando cierre manual) y ejecuta el hook de reglas del nodo. La
   tarea se marca terminada cuando ya no queda ningun paso vigente (CompleteInstance). Se revierte la
   parte de ADR-0068 que dejaba un fin ATENDIDO en espera de confirmacion humana. Aplica tanto en el
   avance normal (`AdvanceAsync`) como al elegir rama en una compuerta (`ChooseGatewayRouteAsync`).

## Consecuencias

- Un flujo cuyo iniciador quedo mal grabado (id de plataforma) ya no se atasca: los pasos InheritStart
  se resuelven al que viene llevando el caso.
- Los eventos de fin cierran la tarea solos; el usuario decide en la COMPUERTA, no en el fin.

## Pendiente (deuda declarada, no en este ADR)

- **Raiz del iniciador fantasma**: la creacion de la actividad debe pasar a `StartInstanceAsync` el
  id de TENANT del actor, no el de plataforma. Mientras tanto, el fix (1) lo neutraliza.
- **Reglas del nodo en runtime**: el hook (`IWorkflowRuleHook`) es NoOp; ejecutar de verdad las
  reglas del fin (y de cualquier nodo) requiere cablear el RulesEngine (ola FASE 4). El punto de
  invocacion ya queda puesto en `FinalizeEndEventAsync`.
- **Salto a otro flujo** (`JumpToDefinitionId`): sin runtime (deuda ADR-0056). `FinalizeEndEventAsync`
  es el punto natural para dispararlo; falta definir la semantica (misma tarea que continua vs nueva
  actividad) y construirlo.
