# ADR-0149: Resolver candidatos de nodo EN LOTE (matar el N+1 de tablero/bandeja)

Fecha: 2026-11-04
Estado: Aceptada
Relacionada: ADR-0035 (asignacion por nodo / INodeAssigneeResolver), ADR-0038 (ruteo de tareas al
usuario en el tablero), ADR-0082 (acceso por cargo).

## Contexto

Usuarios reportaron el sistema LENTO. Diagnostico en prod (solo lectura): `ecorex-app` clavaba ~1
nucleo y disparaba ~113 consultas/seg, casi todas la misma:
`SELECT org_unit_id FROM workflow_node_policies WHERE tenant_id=? AND workflow_node_id=?`.

Causa: un **N+1** en el render de listados. Tanto `WorkflowInboxService` (bandeja "mis pasos") como
`ActivityBoardService.GetRoutedTaskIdsAsync` (tablero, alcance "Mis tareas", ADR-0038) recorren TODOS
los pasos `is_current + Pending` y, por cada paso sin reclamar, llaman
`INodeAssigneeResolver.ResolveCandidatesAsync(nodeId)`. Cada llamada hace 3 consultas: las policies del
nodo + TODO el organigrama (`org_units` + `org_unit_members`). Con un backlog de ~800 pasos pendientes
acumulados (tenant AGRO), cada apertura o refresh del tablero/bandeja lanzaba ~2.400 consultas, y con
varios usuarios + refresco en vivo el proceso quedaba pinneado. La bandeja ni siquiera cacheaba por
nodo dentro del loop; el tablero deduplicaba por nodo pero igual recargaba el organigrama por nodo
distinto.

El agente de IA en nodos (`WorkflowAgentStepWorker`, cada 30 s, tope 25 pasos/tenant/ciclo) NO era la
causa: esta acotado. El problema era el listado de UI.

## Decision

1. **Nuevo metodo EN LOTE** `INodeAssigneeResolver.ResolveCandidatesForNodesAsync(IReadOnlyCollection<Guid>
   nodeIds)` -> `IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>`. Carga las policies de TODOS los nodos en
   UNA consulta (`WHERE workflow_node_id IN (...)`) y el organigrama (unidades + miembros) UNA sola vez,
   y resuelve cada nodo en memoria con `OrgAssigneeTree.ResolveForUnits`. Un nodo sin policies no aparece
   en el mapa (= candidatos vacios), misma semantica que el metodo por-nodo.
2. **El tablero y la bandeja resuelven en lote ANTES del loop** (junto a los demas prefetch que ya hacen,
   como aristas/forms) y dentro del loop solo consultan el diccionario. ~2.400 consultas por render ->
   3 (policies-en-lote + unidades + miembros).
3. **Se conserva el metodo por-nodo** `ResolveCandidatesAsync` para los llamadores de un solo nodo
   (motor, runner, inicio de flujo, editor, chequeo al reclamar un paso): ahi no hay N+1.
4. **Log de EF a `Warning`**: `appsettings.json` baja `Microsoft.EntityFrameworkCore.Database.Command`
   a `Warning`. Loguear cada SQL a `Information` en prod sumaba CPU/IO y llenaba el log; `Default` sigue
   en `Information` para los logs de la app.

## Por que en lote y no una cache del organigrama

Una cache por-request del organigrama tambien evitaria la recarga, pero el lote ademas colapsa las N
consultas de policies en una sola y es explicito en el call-site (se ve que el listado precarga lo que
el loop consume, igual que ya hace con aristas y forms). No introduce estado compartido ni invalidacion.

## Consecuencias

- El render de tablero/bandeja deja de escalar con el numero de pasos pendientes: el costo es ~constante
  (3 consultas) en vez de lineal (3 x pasos).
- No cambia QUIEN es candidato: misma logica de `OrgAssigneeTree`, solo deja de repetirse. Sin migracion,
  sin cambio de contrato de datos.
- Queda PENDIENTE (otra sesion, mas invasivo): que cerrar/archivar una tarea CANCELE su `workflow_instance`
  Running + resuelva sus pasos `is_current` (hoy ~169 instancias Running pertenecen a tareas ya
  cerradas/archivadas y engrosan todo barrido), y una limpieza del backlog existente. Eso es
  correctness/higiene; el alivio de CPU lo da este fix del N+1.
