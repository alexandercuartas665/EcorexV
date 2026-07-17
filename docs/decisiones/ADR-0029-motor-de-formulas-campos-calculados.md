# ADR-0029 - Motor de formulas para campos calculados configurables

- Estado: aceptado (implementado 2026-07-17)
- Fecha: 2026-07-16
- Contexto: campos configurables de Terceros (000232) e Items de inventario (000066)

## Contexto

Los campos configurables de ECOREX se calcaron de `PipelineFieldDefinition` (CUBOT.travels),
pero a medias: quedaron 2 anchos de columna en vez de 3, sin campos calculados, y sin poder
mover un campo de un grupo a otro.

El proyecto hermano NO tiene un motor de formulas: su unico campo calculado es un tipo `Total`
que **suma** las claves listadas en `TotalSourceKeys` (texto separado por comas), evaluado
dentro de la propia pagina Blazor. No hay operadores, ni parentesis, ni precedencia, ni
deteccion de ciclos, ni orden de evaluacion.

El usuario pidio explicitamente un motor de formulas de verdad, no la suma.

## Decision

Se construye un motor de expresiones propio, pequeno y sin dependencias externas, en
`Ecorex.Application/Formulas/`, compartido por Terceros e Items (ambos ya comparten el enum
`TerceroFieldType`).

### Sintaxis

- Referencia a campo: `{clave_del_campo}` (la FieldKey, que es estable y no cambia).
- Operadores: `+`, `-`, `*`, `/`, menos unario, con precedencia estandar y parentesis.
- Literales numericos con punto decimal (cultura invariante: `1234.56`).
- Funciones: `ROUND(x, n)`, `MIN(a, b, ...)`, `MAX(a, b, ...)`, `ABS(x)`, `SUM(a, b, ...)`.

Ejemplo: `ROUND(({valor_base} + {flete}) * 1.19, 2)`

### Semantica

- Todo se evalua en `decimal` (dinero: nada de `double`).
- Un campo vacio o no numerico vale `0`. Es lo que ya hace el hermano y evita que la ficha
  se rompa mientras se captura.
- Division por cero: el campo queda vacio y se avisa en la UI. No lanza excepcion.
- Un calculado PUEDE depender de otro calculado: se evalua en **orden topologico**.
- **Ciclos**: se detectan al guardar la definicion y se rechazan con el ciclo concreto en el
  mensaje. Es la diferencia principal con el hermano, que no los detecta.

### Alcance de las referencias

- **Terceros**: cualquier campo de **cualquier ficha** del mismo tercero. Los valores viven
  todos en `Tercero.FichasJson`, asi que un calculado comercial puede referenciar un dato
  fiscal. Es el caso de uso real.
- **Items**: campos del **mismo tipo de item**, porque un item tiene un solo tipo y los campos
  son por tipo (`ItemFieldDefinition.ItemTypeId`).

### Validacion

Al guardar la definicion del campo:
1. Se parsea la formula; error de sintaxis -> se rechaza indicando la posicion.
2. Toda `{clave}` referenciada debe existir y estar en el alcance permitido.
3. La referencia debe ser a un campo numerico (`Number`, `Currency` o `Calculated`).
4. No puede cerrar un ciclo con los calculados ya existentes.

### Evaluacion

- **En vivo en la UI**: el campo es readonly y se recalcula al escribir en sus origenes.
- **Al guardar**: el resultado se materializa en el JSON de valores (cultura invariante), para
  que reportes, API y exportaciones no tengan que reimplementar el motor. Igual que el hermano.

## Alternativas descartadas

- **Portar el tipo `Total` del hermano**: es solo suma. El usuario lo descarto por corto.
- **Libreria de expresiones (NCalc, DynamicExpresso, Roslyn scripting)**: dependencia nueva y
  superficie de ataque (ejecutar expresiones de usuario) desproporcionada para aritmetica sobre
  campos propios. Un evaluador acotado es mas seguro: no hay acceso a tipos ni a I/O.
- **Evaluar en la pagina Blazor** (como el hermano): no es testeable ni reutilizable entre los
  dos modulos, y ya se probo que se queda corto.

## Consecuencias

- Hay que mantener un parser propio. Se acota a aritmetica y a las funciones listadas; cualquier
  crecimiento (condicionales, fechas, texto) es una decision aparte.
- El motor es logica pura sin dependencia de EF, asi que se cubre con tests unitarios (sin Docker).
- `TerceroFieldType` gana `Calculated`. `Separator` ya existia pero solo lo renderizaba
  Configuracion de entidad; pasa a estar disponible en Terceros e Items.

## Addendum 2026-07-17 - lo que los datos reales obligaron a cambiar

Al implementarlo aparecio un caso que este ADR no habia previsto: **la clave de un campo solo es
unica DENTRO de su ficha**, y en los datos existe `dias_de_pago` en cliente Y en proveedor. La
decision original ("en terceros una formula puede referenciar cualquier ficha") no se sostiene tal
cual: `{dias_de_pago}` no dice a cual de los dos apunta, y como los valores viven por ficha en
FichasJson, elegir uno seria adivinar.

**Resolucion**: referenciar entre fichas SIGUE valiendo; lo que se rechaza es la clave ambigua,
nombrando las fichas en conflicto para que se renombre una. En la UI esas claves se muestran
tachadas y no se pueden pinchar, en vez de ofrecerlas y rechazarlas al guardar. Se descarto
desambiguar con `{ficha.clave}`: complica la sintaxis para todos por un caso que el usuario puede
resolver renombrando.

Consecuencias derivadas:
- **Mover un campo** avisa si la ficha/tipo destino ya tiene esa clave (reventaria contra el indice
  unico), en vez de dejar que falle el SaveChanges.
- En **items**, mover ademas se niega si alguna formula del tipo origen referencia el campo: se
  quedaria sin ese dato al evaluar.
- Las claves de campos NUEVOS de tercero se generan unicas por TENANT (no por ficha), para no
  fabricar mas ambiguedad a futuro. Las que ya existen se quedan como estan.
