# ADR-0068: Compuerta exclusiva y evento como PUNTO DE DECISION HUMANO (asignable)

- Estado: Aceptada
- Fecha: 2026-08-21
- Extiende: ADR-0035 (asignacion por nodo), ADR-0037 (gateways auto-resueltos), origen del asignado por nodo.

## Contexto

Hasta ahora solo el evento de inicio y las Tareas admitian asignacion de dependencia/cargo. La
compuerta exclusiva se AUTO-RESOLVIA (ADR-0037): heredaba la decision del paso anterior y se
completaba en el acto; el evento de fin cerraba la rama de inmediato. Por eso el panel "Asignar
usuarios" del editor bloqueaba compuertas y eventos: un cargo asignado ahi no se usaba en runtime.

El usuario modela decisiones como compuertas ("Cliente Decide si compra") y necesita que ESA
decision la atienda un usuario/cargo concreto, eligiendo la ruta desde la bandeja/tarjeta. Igual
para un evento de fin: un responsable puede tener que CONFIRMAR el cierre.

## Decision

Un nodo puede ser un **punto de atencion humano** que espera a un asignado antes de avanzar. Se
expresa con la propiedad CALCULADA `WorkflowNode.WaitsForHuman` (no columna, sin migracion):

- **Task**: siempre (salvo auto-cierre por regla, decidido en runtime) — como hoy.
- **ExclusiveGateway / EndEvent**: SOLO si el disenador activa la asignacion (`AllowsAssignment`).
  Entonces el nodo queda `Pending` al activarse y el usuario/cargo asignado **elige la ruta**
  (compuerta) o **confirma el cierre** (fin). Sin asignacion siguen siendo AUTOMATICOS (ADR-0037):
  compatibilidad total con los flujos existentes.
- **StartEvent**: nunca espera (se completa solo; el asignado es el iniciador).

Reutiliza toda la maquinaria existente, que ya era agnostica al tipo de nodo:

- Motor: `ActivateNodeAsync` deja `Pending` la compuerta/fin atendidos (en vez de auto-completar);
  `AdvanceAsync` enruta al cerrarse por `ResolveOutgoing` contra el `ApprovalResult` que pone el
  humano (misma semantica que la compuerta automatica). La resolucion del asignado dinamico
  (Heredar/FormField, origen del asignado) se extiende a los nodos atendidos.
- Bandeja: los pasos `Pending` de compuerta/fin ya aparecen; se resuelven candidatos por policy para
  ellos y una compuerta atendida ofrece SUS PROPIAS rutas (`OwnRoutes`) como opciones de decision.
  El paso ANTERIOR a una compuerta atendida ya NO ofrece esas rutas (la decision vive en la compuerta).
- Asignacion: `WorkflowNodePolicyService` y `NodeAssigneeResolver` ya eran agnosticos al tipo.
- UI: el panel "Asignar usuarios" del editor habilita compuerta y fin con un interruptor claro
  ("Punto de decision" / "Punto de cierre") y el selector de origen del asignado.

## Consecuencias

- (+) La decision se modela y atiende donde el usuario la ve (la compuerta), sin duplicar nodos.
- (+) Cero migracion; comportamiento historico intacto para compuertas/fines sin asignacion.
- (-) Un flujo con una compuerta atendida se DETIENE hasta que alguien elige la ruta (es el objetivo).
- (Deuda) El rechazo que atraviesa una compuerta atendida sigue reactivando el ultimo Task humano
  anterior (la compuerta se re-activa y vuelve a esperar): correcto en efecto, no re-selecciona la
  compuerta como blanco directo. Se acepta para esta ola.
- (Deuda) Nodos de agente sobre compuertas/fines: no se aborda aqui (los agentes ya se pintan aparte).
