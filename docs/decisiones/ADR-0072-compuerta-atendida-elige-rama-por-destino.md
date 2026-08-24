# ADR-0072: Compuerta atendida - el humano elige la rama por su destino

- Estado: Aceptado
- Fecha: 2026-08-24
- Contexto: motor de flujos (ADR-0014), compuertas/eventos atendidos (ADR-0068), diagrama del
  flujo en el detalle de la tarea (ADR-0051/0070/0071).

## Contexto

Una compuerta exclusiva ATENDIDA (AllowsAssignment=true, ADR-0068) es un PUNTO DE DECISION
HUMANO: el encargado debe ELEGIR por que rama sigue el proceso. En la practica los usuarios
dibujan la compuerta con dos salidas hacia dos pasos distintos pero **no** nombran los edges ni
les ponen ConditionExpression (nombrar/condicionar ramas es un concepto tecnico que el autor del
flujo no siempre aplica).

Con eso, el sistema no ofrecia rutas en la compuerta:
- Las rutas del menu se derivaban del NOMBRE de los edges (`OwnRoutes`), asi que edges sin nombre
  -> cero opciones. El menu de la compuerta vigente caia en "Cerrar actividad" sin dejar decidir.
- Ademas el motor solo enrutaba una compuerta por `ConditionExpression`; sin condiciones, dos
  ramas "default" eran ambiguas.

## Decision

Una compuerta ATENDIDA deja elegir la rama **por su NODO DESTINO**, aunque el edge no tenga
nombre ni condicion:

1. **Rutas del diagrama** (`GetTaskFlowDiagramAsync`): para una compuerta, se listan TODAS sus
   salidas; la etiqueta es el nombre del edge si lo tiene, si no el nombre del paso destino; cada
   ruta lleva `TaskFlowRouteDto.TargetNodeId`.
2. **Motor** (`IWorkflowEngine.ChooseGatewayRouteAsync`): completa la compuerta y sigue SOLO la
   rama cuyo destino eligio el humano (validando que sea salida directa de la compuerta), con el
   mismo manejo por-arista que el avance normal (reinicio / fin / activacion). No depende de
   `ConditionExpression`.
3. **Bandeja** (`IWorkflowInboxService.CompleteGatewayChoiceAsync`): autoriza al asignado/candidato
   del paso o a un Owner/Admin y delega en el motor.
4. **UI**: en el menu de la compuerta vigente, los botones de ruta llaman a la eleccion por destino
   (`ChooseGatewayRouteAsync`). Un paso Task con una compuerta AUTOMATICA adelante conserva el
   camino previo (completa con approvalResult y el motor enruta por condicion).

## Consecuencias

- Una compuerta atendida es usable sin exigir al autor nombrar/condicionar ramas: se decide por
  destino, que es lo que el usuario ve.
- El resultado de aprobacion (`ApprovalResult`) de la compuerta queda con el nombre del edge si lo
  tiene (auditoria), o null. La rama seguida NO depende de ese valor sino del destino elegido.
- Las compuertas AUTOMATICAS (sin asignacion) siguen enrutando por condicion (ADR-0037), sin
  cambios. Solo las ATENDIDAS usan la eleccion por destino.

## Nota relacionada (no es este ADR): origen del asignado por nodo

En la misma revision se observo que los nodos Cotizacion/compuerta de un flujo de prueba estaban
configurados con `AssigneeSource = InheritStart` (heredan al que INICIO el flujo), no `Policy`
(cargo). Resolver el encargado "por el cargo" es una eleccion de configuracion por nodo en el
editor (AssigneeSource = Policy + cargo), no un cambio de codigo. Se deja registrado para no
confundirlo con este ADR.
