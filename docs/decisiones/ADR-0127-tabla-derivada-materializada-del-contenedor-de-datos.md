# ADR-0127: Tabla derivada MATERIALIZADA del Contenedor de datos (agrupar otra tabla con agregados)

Fecha: 2026-10-22
Estado: Aceptada

## Contexto

El Contenedor de datos (`DataModel` -> tablas `DataContainer` -> columnas tipadas + filas EAV cuyo valor
siempre es string) ya tenia dos formas de "una tabla a partir de otra":

- **Composicion** (`ParentContainerId` / `ParentFieldId`, columnas Submodel): una matriz POR FILA del padre.
- **Alimentacion** por conector (`DataConnector` Excel / RestApi / Database) y `ImportProcess` (manual /
  intervalo / cron) con bitacora `ImportRun`, ejecutado por `ProcessRunner` (boton "Actualizar datos" y
  scheduler) sobre el nucleo de ingesta `IRowIngestService` (Append / Replace / Upsert por columna clave).

Lo que faltaba es otra cosa: una tabla cuyas FILAS SE CALCULAN agrupando otra tabla del mismo contenedor
por columnas elegidas, con agregados (cantidad, suma, promedio, minimo, maximo), que el usuario arme sin
codigo y que pueda usar como cualquier otra tabla (modulo publicado, lookups de formularios, reportes,
agente, reglas).

Se evaluaron dos opciones:

- **A. Derivada materializada**: filas reales en una tabla hija, recalculadas por un refresco.
- **B. Vista virtual**: calcular al leer, sin filas.

## Decision

**Opcion A: tabla derivada MATERIALIZADA**, en el MISMO `DataModel`, alimentada por un conector nuevo
`ConnectorKind.FromTable` ("Desde otra tabla") y refrescada por el MISMO `ImportProcess` / `ProcessRunner` /
`ImportRun` que los demas conectores. Se descarto la vista virtual porque TODO lo que consume contenedores
espera filas reales (`DataContainerRow` + `DataContainerCell`): modulo publicado con paginado en servidor,
lookups, reportes, agente, reglas. Una vista virtual habria obligado a reescribir cada consumidor o a
mantener dos caminos.

### Piezas

1. **Dominio**: `DataContainer.DerivedFromContainerId` (FK a `DataContainer`, **Restrict en ambos
   motores**) marca la hija y apunta al origen. Restrict a proposito: el origen no se borra mientras exista
   una derivada (el servicio avisa con el nombre de la derivada), borrar la derivada no toca al origen, y no
   se suma otra ruta de cascada auto-referenciada (SQL Server error 1785). Migracion dual `AddDerivedTables`.
2. **Spec en el conector**: la receta vive TIPADA en `DataConnector.MappingJson` (jsonb) como
   `DerivationSpec { SourceTableId, GroupBy[columnId], Aggregates[{ SourceColumnId?, Fn, TargetColumnName }],
   Filter?[{ ColumnId, Op, Value }], KeyColumnName="Clave" }`. `DataConnector.ContainerId` es la tabla hija
   (igual que los otros conectores). Sin credenciales ni endpoint. Reusa el patron de `ConnectorRunPlanner`
   (REST): la definicion del conector se parsea a una forma de Application testeable sin UI.
3. **Motor puro** `DerivedRowsBuilder` (Application, sin EF): NORMALIZA cada valor por tipo antes de armar
   la clave de grupo (Number/Decimal con InvariantCulture y sin ceros a la derecha, Date a ISO, Boolean a
   true/false, Text trim), asi "10" y "10.0" caen en el mismo grupo. Agrega: Count -> Number; Sum/Avg ->
   Decimal; Min/Max heredan el tipo del origen y comparan por tipo (no como texto). Avg sin valores
   numericos = null (nunca division por cero). Lo que no parsea en una columna tipada no participa en ningun
   agregado (como NULL en SQL). Devuelve filas por NOMBRE de columna destino, listas para `IRowIngestService`.
   `Plan()` es la unica fuente de verdad del esquema de la hija: lo usa el wizard para crear columnas y el
   refresco para casarlas por nombre.
4. **Clave + Upsert + poda**: la hija lleva una columna Text "Clave" (concatenacion normalizada de las
   columnas de agrupacion, separador " | "), que es la `KeyColumn` del `ImportProcess` en modo Upsert. Como
   el Upsert de `RowIngestService` CONSERVA las celdas de columnas no mapeadas, las columnas PROPIAS que el
   usuario agregue a la derivada (notas, responsables, estados) sobreviven a cada refresco. Tras el Upsert,
   `DerivedTableService.RefreshAsync` PODA las filas cuya clave ya no esta entre los grupos calculados (el
   grupo desaparecio del origen). Ingesta + poda van en UNA transaccion; si algo falla la hija queda como
   estaba. `ImportRun.Deleted` incluye la poda.
5. **Un solo nivel = validacion, no estructura**: una derivada no puede ser origen de otra derivada. Se
   rechaza en `DerivedTableService` (alta, vista previa y refresco) y el lienzo oculta "Derivar tabla" en las
   derivadas. No se modela con una columna aparte ni con un tipo de tabla: `DerivedFromContainerId != null`
   ya lo dice, y si manana se quisiera permitir niveles solo habria que relajar la validacion.
6. **Refresco server-direct**: `ProcessRunner.RunNowAsync` tiene una rama FromTable que abre la corrida
   (idempotente por ventana), llama al servicio y la cierra con Inserted/Updated/Deleted. Nunca pasa por el
   agente Colmena: el origen esta en la misma base.
7. **Wizard determinista** (`DerivedTableWizard`, componente aparte para no crecer `ContenedorDatos.razor`):
   nombre -> agrupar -> agregados -> filtro -> VISTA PREVIA (primeros 20 grupos con el MISMO motor) ->
   refresco (reusa `FriendlySchedule`) -> crear. Al crear, en UNA transaccion: tabla hija + columnas +
   conector FromTable + proceso (Upsert por "Clave"); luego el primer refresco por el runner (queda en la
   bitacora). Lienzo: nodo verde "derivada de X" y arista punteada origen -> hija.
8. **Borrado**: borrar la derivada se lleva su conector FromTable, su proceso y sus corridas (cascada
   diferida, regla #5; en SQL Server la FK del conector a la tabla es Restrict). Borrar el origen esta
   bloqueado mientras exista una derivada.

### Lo que NO se hizo (fase 2)

- Chat IA asistido para armar la derivacion: queda para una fase 2; el wizard es determinista a proposito.
- Agregados sobre columnas Text (solo Count / Min / Max por tipo escalar) y expresiones calculadas: fuera.
- Ordenacion tipada de la hija en el modulo publicado: hereda la limitacion EAV existente (orden
  alfabetico de celdas string).

## Consecuencias

- La derivada se comporta como cualquier tabla (filas reales): se publica, se consulta, se exporta.
- El dato de la hija es tan fresco como el ultimo refresco; la bitacora `ImportRun` dice cuando corrio y
  que paso. Tope de lectura del origen: 200.000 filas por refresco (por lotes de 5.000).
- Si el usuario borra o renombra una columna generada de la hija, el refresco falla con mensaje claro
  ("la columna X no existe en la tabla derivada"); las columnas propias no se tocan.
- Tests: 30 unit del motor (normalizacion, tipos, Avg sin division por cero, filtro, validacion, round-trip
  de la spec) + 4 x 2 integracion dual (alta, refresco, poda, columna propia sobrevive, un solo nivel, guard
  de borrado, aislamiento por tenant).

## Extension 2026-10-23: la derivada puede vivir en OTRO contenedor del tenant

Caso real: el usuario creo el contenedor TEST_HIJO en BITCODE y quiere alli una tabla derivada de
ALEGRA/Items. Decision (sin migracion): `CreateDerivedTableRequest.ModelId` pasa a ser el contenedor
DESTINO elegido; la tabla hija, su conector FromTable y su ImportProcess cuelgan del destino, y la spec
sigue apuntando al origen por id. Se quitan las validaciones "mismo contenedor" del alta y del refresco:
el tenant lo garantiza el filtro global y la regla de un solo nivel y el guard de borrado del origen van
por id. Si el destino es otro, la hija no se pone "al lado" del origen sino en un sitio libre del lienzo
destino (a la derecha de su tabla mas a la derecha, o (40,40) si esta vacio). El lienzo destino muestra
"derivada de <Contenedor> / <Tabla>" y NO dibuja arista (el origen no esta en ese lienzo); el lienzo del
origen no cambia. Borrar el CONTENEDOR del origen se bloquea con mensaje mientras exista una derivada en
otro contenedor (FK Restrict). El wizard gana el selector "Crear en el contenedor" (por defecto el
actual); el asistente IA acepta `contenedor_destino` (nombre o id) en proponer/crear y el selector del
wizard se inyecta cuando el modelo no lo manda.


## Extension 2026-10-24: crear y EDITAR la derivada desde el contenedor donde vive

El usuario pidio poder crear y editar la tabla derivada desde el contenedor DESTINO (donde queda), no
solo desde el contenedor del origen ("el padre"). Dos capacidades nuevas, sin migracion:

- Crear desde el destino: boton "+ Derivada" en la barra del lienzo del contenedor. Abre el wizard con el
  destino fijo a ESE contenedor y un selector "Derivar de..." que lista las tablas normales del tenant
  (`IDerivedTableService.ListSourceTablesAsync`), de este u otro contenedor, nunca una derivada. El boton
  sigma de una tabla normal (crear derivada desde el origen) se conserva como atajo; ambos abren el mismo
  wizard.
- Editar: engranaje en el nodo de una tabla derivada. Abre el wizard en modo edicion, precargado desde
  `GetEditInfoAsync` (receta leida del conector + horario). El origen y el contenedor destino quedan FIJOS
  (son la identidad de la derivada); solo cambian agrupacion, agregados, filtro y refresco. `UpdateAsync`
  reescribe el MappingJson, reconcilia las columnas producidas (agrega/quita; al quitar una produccion
  borra sus celdas) CONSERVANDO las columnas propias del usuario, renombra si cambio el nombre y recalcula
  la programacion, todo en una transaccion; luego el llamador dispara un refresco por el runner. El lapiz
  del nodo (editor generico de columnas) sigue disponible para agregar columnas propias a la derivada.

Consecuencia a tener presente: si al editar cambian las columnas de AGRUPACION, los grupos cambian de
identidad (otras claves), de modo que el refresco Upsert+poda reconstruye la tabla y las filas y columnas
propias atadas a los grupos viejos se pierden. Cambiar solo agregados/filtro/horario mantiene las claves y
conserva las columnas propias. El motor, la ingesta Upsert+poda y la regla de un solo nivel no cambian.
