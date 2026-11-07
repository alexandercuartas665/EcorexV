# ADR-0155: Conciliacion DIAN - comparar NEWTON vs DIAN por periodo (filas fantasma)

**Status:** Accepted
**Date:** 2026-11-07
**Deciders:** Alexander Cuartas (dev principal), sesion data-ops/conciliador (ECOREX.dian)

> Renumerado: el hand-off proponia ADR-0153, pero ese numero (y el 0154) ya estaban
> tomados en el tronco (instalador del agente y red de cierre del agente). Este es 0155.

## Contexto

El conciliador DIAN cruza las facturas descargadas de la DIAN contra el ERP. Pero el
proveedor tecnologico NEWTON (que tambien recibe la facturacion electronica) a veces
tiene documentos del periodo que NO llegaron a la descarga de la DIAN, y viceversa. El
usuario necesitaba ver, dentro de la misma parrilla y para el periodo del CCD, que esta
en NEWTON y no en DIAN (y al reves), sin ensuciar los conteos reales ni generar tareas
sobre documentos que aun no son facturas DIAN.

## Decision

Se agrega la comparacion NEWTON<->DIAN por periodo en cuatro piezas:

1. **NEWTON enriquecido.** Cada fila de `conciliacion_dian_newton_dummies` guarda ahora
   sus datos de negocio (fecha, NIT y nombre del emisor, numero de factura, tipo de
   documento y de pago, subtotales, IVA, retenciones, total). Se llenan en el ingest, y
   un backfill ("Rellenar NEWTON", `BrowserRunService.BackfillNewtonDesdeBlobAsync`)
   re-parsea el XML ya guardado en Azure Blob para las filas viejas, SIN volver a NEWTON.
2. **Filas fantasma por periodo.** Al importar, por cada CUFE de NEWTON del mes del CCD
   que NO esta en DIAN se materializa un renglon con `Origen = Newton` (enum nuevo
   `OrigenRenglonConciliacionDian`: `Dian=0` por defecto, `Newton=1`), visible con un chip
   ambar "NEWTON". Si mas tarde la DIAN trae ese mismo CUFE, el renglon se promueve a Dian.
3. **Filtro integrado.** Campo nuevo "Presencia DIAN/Newton" (`solo_dian | ambos |
   solo_newton`) dentro del constructor de filtros Y/O existente (`ConciliacionFilter`),
   acotado al periodo del CCD.
4. **Candado.** Una fila fantasma (solo NEWTON) NO produce tarea: guard en la celda de la
   grilla (etiqueta "Solo NEWTON"), en `OnCrearTareaClick`, en `CrearTareaCoreAsync`
   (cubre el boton y la IA), en `OnAssigneeChanged`, y excluida de los candidatos de la IA.

## Opciones consideradas

### Opcion A: volcar NEWTON a un Contenedor de datos y cruzar con el motor de reglas
Reutiliza la maquinaria de VLOOKUP. Pero NEWTON es por CUFE y por periodo, y la comparacion
"quien esta en uno y no en el otro" no es un simple lookup; ademas duplicaria el dato.

### Opcion B (elegida): filas fantasma en la misma parrilla + presencia por CUFE
| Dimension | Evaluacion |
|-----------|------------|
| Complejidad | Media (enum + columnas + guard de tarea) |
| UX | Alta (todo en la misma grilla y el mismo filtro) |
| Integridad de conteos | Alta (el fantasma no cuenta como factura DIAN ni genera tarea) |

## Consecuencias

- El usuario ve en una sola vista los tres conjuntos (solo DIAN, ambos, solo NEWTON) para el
  periodo, y puede filtrarlos.
- El backfill recupera el enriquecimiento de las filas NEWTON viejas leyendo el blob, sin
  recargar NEWTON.
- Un fantasma nunca se vuelve trabajo (tarea) hasta que la DIAN lo confirme; el candado cubre
  boton, IA y asignacion.
- DAL dual: 2 migraciones (PG `20261107112830`, SqlServer `20261107113152`) agregan la columna
  `origen` del renglon y las columnas de enriquecimiento de NEWTON; snapshots de ambos
  proveedores actualizados. Toda la logica de comparacion es en consulta (no hay tablas nuevas).

## Verificacion

Local (5263 / docker 5442, tenant SOLDARCO): build verde; backfill 326/326 filas enriquecidas
(0 sin archivo); comparacion 11/2026 -> ambos 76, solo NEWTON 2 (UNITORNILLOS SAS, MARIA PERALTA
MOSQUERA), solo DIAN 9; filtro y candado OK. Matriz dual de integracion + aislamiento cross-tenant:
gate del CI (ADR-0018) en el PR a main.

## Action Items

1. [x] Enum `OrigenRenglonConciliacionDian` + columna `origen` del renglon.
2. [x] Enriquecimiento de NEWTON + backfill desde blob.
3. [x] Filas fantasma por periodo + chip + filtro de presencia.
4. [x] Candado: fantasma no produce tarea (boton, IA, asignacion).
5. [ ] Deploy a prod: pendiente del OK por version del usuario.
