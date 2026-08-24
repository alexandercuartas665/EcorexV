# ADR-0074: Visuales del diagrama de flujo en la tarea - rama descartada en gris + eventos mas grandes

- Estado: Aceptado
- Fecha: 2026-08-24
- Contexto: diagrama del flujo en el detalle de la tarea (ADR-0051), compuerta atendida que elige
  rama por destino (ADR-0072).

## Contexto

Dos observaciones del usuario sobre el diagrama que se ve en el modal de una tarea que ejecuta un
flujo:

1. Al decidir una compuerta, la otra rama (la NO tomada) seguia viendose normal; deberia quedar en
   gris para dejar claro que el proceso no fue por ahi.
2. Los nodos de inicio/fin (eventos) se veian diminutos (38px) frente a las tareas: perdian
   proporcion.

## Decision

1. **Rama descartada en gris**: `TaskFlowNodeDto.IsAbandoned` marca un nodo que ya NO es alcanzable
   hacia adelante desde ningun paso vigente y no tiene historial (la compuerta tomo otra ruta). En
   el diagrama ese nodo se atenua (opacity 0.5 + grayscale) y su arista de entrada se pinta gris y
   punteada. Mientras la compuerta NO se ha decidido, sus dos salidas siguen siendo alcanzables desde
   ella (vigente) y NO se griselan.
2. **Eventos mas grandes**: los nodos de inicio/fin pasan de 38px a 72px. Se re-centran en
   `FlowNodeLeft/Top` (restando la mitad del crecimiento) para que agrandar el circulo NO descoloque
   las aristas, que conectan al centro del nodo. El padding base del canvas sube a 30px para dar aire
   al re-centrado.

## Consecuencias

- El diagrama comunica de un vistazo por donde fue el proceso y por donde no.
- Un nodo descartado sigue siendo consultable (menu de solo lectura + notas del equipo, ADR-0071);
  el gris es solo visual.
- `IsAbandoned` se recalcula en cada carga del diagrama; si un paso se reabre (ADR-0070) y el estado
  cambia, el griselado se ajusta solo.
