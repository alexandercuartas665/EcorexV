# ADR-0046 - Ejecucion en paralelo del motor de flujos (multi-token)

- Estado: aceptado
- Fecha: 2026-07-21
- Contexto D11, planteado por la sesion de implementacion tras cargar el flujo
  "Proceso de compras" (COMPRAS) del tenant SKY SYSTEM en produccion.

## Contexto

El flujo COMPRAS tiene un nodo, "Se aprueba compra por el cliente", con CUATRO salidas
simultaneas: entrega, pago, factura e ingreso a alegra. El negocio lo modelo asi porque es
lo que de verdad ocurre: las cuatro cosas pasan a la vez.

El informe recibido afirmaba que el motor era de un solo token y que tomaba `outgoing[0]`
descartando el resto, tanto en `WorkflowStartService` como en `WorkflowEngine.ResolveOutgoing`.

**Al revisar el codigo, ese diagnostico resulto incorrecto para el motor.** Los hechos:

- `WorkflowEngine.ResolveOutgoing` YA devuelve TODAS las aristas salientes de un nodo que no
  sea compuerta exclusiva, y el bucle de avance YA las recorre activando cada destino. La
  bifurcacion paralela existia.
- `WorkflowStepHistory.IsCurrent` admite varias filas vigentes por instancia, y
  `WorkflowInstance` NO guarda un "paso corriente" unico. El modelo de datos ya soportaba
  varios tokens; no hacia falta migracion.
- El `outgoing[0]` reportado esta en `WorkflowStartService`, que hace un recorrido EN SECO
  para calcular a quien asignar el primer paso. No es la ruta de ejecucion.

**El defecto real era otro y no estaba reportado:** al alcanzar un endEvent, el motor
llamaba a `CompleteInstance` de inmediato y a continuacion marcaba como `Skipped` todas las
ramas hermanas vivas. Es decir, la primera rama en llegar al final mataba a las otras tres.

## Decision

1. **Alcanzar un endEvent cierra la RAMA, no la instancia.** El paso queda registrado como
   completado y el avance continua con las ramas restantes.

2. **Cierre implicito:** la instancia se completa cuando no queda NINGUN paso vigente, sin
   importar si se alcanzo un endEvent explicito. Antes, quedarse sin pasos sin haber tocado
   un endEvent marcaba la instancia como `Stuck`.

3. **Un rechazo NO detiene las ramas hermanas.** Cada rama sigue su curso y el cierre se
   decide al final, cuando ya no queda trabajo.

## Alternativas consideradas

- **Exigir un endEvent por rama.** Mas riguroso y hace explicito donde termina cada camino,
  pero obligaba a editar el diagrama de compras (cinco endEvents nuevos) antes de poder
  publicarlo. Se descarto: el diagrama refleja el proceso real y se prefirio que corra tal
  cual.
- **Que un rechazo aborte todo el proceso.** Es lo habitual en aprobaciones, pero aqui las
  cuatro ramas son independientes entre si (facturar, entregar, registrar), y abortarlas
  todas por una perderia trabajo ya hecho.
- **Introducir `ParallelGateway` como tipo de nodo nuevo.** No hizo falta para este caso: el
  paralelismo sale de un nodo con varias salidas, que es lo que el negocio modelo. Queda
  como trabajo futuro si alguien quiere expresarlo explicitamente en el diagrama.

## Consecuencias

- El flujo COMPRAS ejecuta sus cuatro ramas sin tocar el diagrama.
- **Contrapartida asumida:** un flujo mal modelado que se queda sin salidas ahora se cierra
  en silencio, donde antes gritaba `Stuck`. El aviso natural es al PUBLICAR (advertir de
  ramas sin endEvent), no en ejecucion, porque en ejecucion ya es tarde para corregir el
  diseno. Queda pendiente.
- El barrido que marcaba `Skipped` las ramas vivas se conserva como red de seguridad para
  cualquier otra via que cierre una instancia con ramas abiertas (por ejemplo un cierre
  manual), pero ya no deberia dispararse por un endEvent.
- Sin migracion en ninguno de los dos motores.

## Verificacion

Test de integracion en matriz dual (`ParallelSplit_OpensAllBranches_AndInstanceStaysOpen...`)
que calca el caso real: un nodo con cuatro salidas, de las cuales solo una llega a endEvent.
Comprueba que nacen las cuatro ramas vivas, que la instancia sigue Running mientras quede
trabajo, que cierra al terminar la ultima y que ninguna rama queda `Skipped`.

Verde en PostgreSQL y SQL Server. Regresion del motor completa: 70/70.

## Pendiente (no incluido en esta decision)

- Aviso al publicar cuando haya ramas sin endEvent.
- `ParallelGateway` como quinto tipo de nodo (dominio, merger, parser y editor bpmn-js).
- Revisar bandeja (`IWorkflowInboxService`) y el scope "mis pendientes" del tablero con
  varias ramas vivas de la misma instancia: los tests actuales pasan, pero no se valido en
  pantalla con un caso de cuatro ramas.
