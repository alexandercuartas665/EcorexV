# ADR-0128: Asignacion round-robin por usuarios configurados en el agente

Fecha: 2026-10-22
Estado: Aceptada

## Contexto

El agente de IA crea tareas (`crear_tarea`, TasksToolset) y actividades (`crear_actividad`, ActividadesToolset).
Hasta ahora:

- `crear_tarea` repartia en round-robin sobre los ASESORES del tenant marcados `AssignableByAgent`
  (`PickNextAssigneeAsync`: menos-recientemente-asignado por `Asesor.LastAgentAssignmentAt`).
- `crear_actividad` NO asignaba a nadie (no seteaba `AssigneeTenantUserId`).

Pedido (data-ops): poder definir EN LA CONFIGURACION DEL AGENTE una lista ordenada de usuarios a los que se
reparten las tareas/actividades que ese agente crea, rotando en round-robin (una ronda al usuario A, la
siguiente al B, y asi), y aplicarlo en AMBOS verbos. Con fallback al comportamiento anterior.

## Decision

1. **Config por agente** (jsonb, igual que `AllowedBoardIdsJson`/`CierreJson`/`ReactivacionJson`):
   - `AiAgent.AssigneeUserIdsJson`: lista ORDENADA de `TenantUser` ids.
   - `AiAgent.AssigneeCursor` (int): indice del PROXIMO usuario a asignar; avanza modulo el tamano de la lista.
   Editable en la UI del agente (`Agentes.razor`): acordeon "Usuarios para asignacion (round-robin)" con agregar
   / quitar / subir / bajar (orden = orden de rotacion).

2. **Picker compartido** `AgentAssigneePicker.PickAsync(db, agentId, ct)` -> `(Guid? UserId, string? Name)`:
   - Si el agente tiene lista: round-robin sobre ella por `AssigneeCursor` (salta ids que ya no sean un
     `TenantUser` activo; si ninguno sirve, cae al pool). Persiste el cursor.
   - Si no (lista vacia o sin agente en contexto): FALLBACK al pool de asesores del tenant marcados
     `AssignableByAgent`, menos-recientemente-asignado (comportamiento anterior de `crear_tarea`).

3. **Aplicado en ambos**: `TasksToolset.PickNextAssigneeAsync` delega en el picker; `ActividadesToolset`
   resuelve el asignado con el picker y lo pasa en `CreateTaskItemRequest.AssigneeTenantUserId` (antes no asignaba).

4. **Migracion dual aditiva**: `AddAgentRoundRobinAssignees` (PG `assignee_user_ids_json` jsonb nullable +
   `assignee_cursor` integer default 0; SQL Server analogo). Columnas nacen con valores por defecto -> OPT-IN,
   los agentes ya existentes no cambian (lista NULL => pool del tenant como antes).

## Consecuencias

- Con 2+ usuarios en el agente, los registros consecutivos rotan A, B, A, B...; con 1 usuario, siempre ese;
  con ninguno, el comportamiento anterior (pool `AssignableByAgent`).
- `crear_actividad` ahora SI asigna (antes no), usando la misma logica que `crear_tarea`.
- El cursor se persiste con un read-modify-save sobre `AiAgent` (mismo nivel de robustez que la marca
  `LastAgentAssignmentAt` del pool de asesores). Concurrencia alta en el MISMO agente podria repetir un turno
  de rotacion; aceptable para el volumen actual.
