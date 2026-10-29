# ADR-0134: Concepto por defecto del agente (crear_tarea siembra el checklist)

Fecha: 2026-10-26
Estado: Aceptada

## Contexto

Cuando un agente de IA cierra/gana un lead crea la tarea con la herramienta `crear_tarea` (TasksToolset), que arma el
`CreateTaskItemRequest` SOLO con `ActivityTypeId` (sin `SubcategoriaId`). El checklist por defecto de una tarea se
siembra en `TaskItemService.CreateAsync` a partir de `ActividadSubcategoria.Chequeo` (concepto 000270). Como el
concepto llega null, la tarea del agente nace SIN checklist. Confirmado en prod (tenant EPRING, agente "Agente
Comercial"): el 100% de las tareas creadas por el agente tienen `subcategoria_id = NULL` y `checklist_items = 0`.
La entidad `AiAgent` no tenia ningun campo de "concepto por defecto". Esto corresponde a la decision previa del
usuario: concepto "Fijo por agente".

## Decision

1. **Dominio**: `AiAgent.DefaultSubcategoriaId` (Guid?, nullable). FK LOGICA a `actividad_subcategorias` con
   `OnDelete(SetNull)` (nunca cascade). Nullable = retrocompatibilidad total: agente sin default se comporta EXACTO
   como hoy (tarea sin concepto, sin checklist). Migracion DUAL `AddAiAgentDefaultSubcategoria` (PG + SQL Server).
2. **Cableado en `crear_tarea`** (TasksToolset.CreateTaskAsync): antes de armar el request, si el agente actual
   (`AiToolRunContext.AgentId`) tiene `DefaultSubcategoriaId` y el concepto EXISTE, es del MISMO tenant (filtro
   global) y NO esta archivado, se pasa como `SubcategoriaId`. Null o no valido -> `SubcategoriaId = null` (no se
   rompe el alta del lead). Asi `CreateAsync` siembra el checklist desde `ActividadSubcategoria.Chequeo` sin tocar
   el bloque de siembra (ya funciona).
3. **Tablero**: se MANTIENE `BoardId = board.Id` (el que eligio el agente). En `CreateAsync` el tablero del concepto
   solo se hereda cuando `BoardId` es null; con BoardId explicito gana el del agente. La tarea sigue cayendo en el
   tablero del agente; el concepto solo aporta el checklist.
4. **UI self-serve**: en el editor del agente (Agentes.razor), desplegable "Concepto por defecto de las tareas" que
   lista los conceptos NO archivados del tenant + opcion "(ninguno)", enlazado a `DefaultSubcategoriaId` y guardado
   con el agente. El usuario fija/cambia el concepto sin SQL.

## Consecuencias

- Migracion aditiva (una columna + FK SET NULL). Retrocompatible.
- El agente sigue usando `crear_tarea` (no se cambia su toolset); solo se le adjunta el concepto por defecto.
- Multi-tenant: el concepto se valida contra el tenant del agente (filtro global).
- Fuera de alcance (decidir con el usuario): que `crear_actividad` sin concepto caiga al `DefaultSubcategoriaId`;
  no se implementa porque el agente EPRING usa `crear_tarea`.
