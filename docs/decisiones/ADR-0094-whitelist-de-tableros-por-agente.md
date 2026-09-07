# ADR-0094: Whitelist dura de tableros por agente para crear_tarea

**Estado:** Propuesta
**Fecha:** 2026-09-07
**Deciders:** Alexander Cuartas (orquestador)
**Relacionados:** ADR-0057 (API de agentes), TasksToolset (herramienta crear_tarea / listar_tableros del
agente conversacional).

## Contexto

El agente de IA cierra una conversacion creando una tarea con la herramienta `crear_tarea`
(`TasksToolset`), eligiendo el tablero por NOMBRE. Hasta ahora NO habia restriccion real: la herramienta
resolvia el nombre contra TODOS los tableros no archivados del tenant y, si no calzaba, devolvia la lista
completa. La unica "restriccion" vivia en el `system_prompt` (blanda: el modelo podia ignorarla). Se
necesita una restriccion DURA, configurable POR AGENTE, de uno o varios tableros permitidos (caso real:
SARA solo debe cargar en el tablero "AGENTE COMERCIAL IA").

## Decision

`AiAgent` gana `AllowedBoardIdsJson` (jsonb, arreglo de GUID = board ids; migracion DUAL aditiva). La
whitelist llega al toolset por el contexto ambiental `AiToolRunContext.AllowedBoardIds` (la inyecta
`AiInferenceService` al abrir el bucle de herramientas, leyendo `AllowedBoardIdsJson`), sin cambiar firmas.

### Semantica (clave)

- **Lista vacia o null = SIN restriccion**: el agente usa todos los tableros no archivados del tenant
  (preserva el comportamiento historico; los demas agentes no cambian de conducta).
- **1 o varios ids = SOLO esos tableros**. `listar_tableros` muestra unicamente los permitidos.
- **La whitelist NUNCA se escapa**: `crear_tarea` resuelve el nombre SOLO entre los permitidos.
  - Con EXACTAMENTE 1 permitido, si el modelo no pasa `tablero` o pasa un nombre que no calza, se usa ese
    unico permitido por defecto (comodidad; jamas cae a un tablero fuera de la lista).
  - Con 2+ permitidos y nombre ausente/invalido, se devuelve un error listando SOLO los permitidos para
    que el modelo reintente (no se adivina el destino).

### Enforcement

- `TasksToolset.ListBoardsAsync`: si hay whitelist, filtra `Where(b => allowed.Contains(b.Id))`.
- `TasksToolset.CreateTaskAsync`: resuelve el tablero dentro de la whitelist; aplica el default de 1-solo;
  si no hay tablero valido, error con los nombres permitidos. El resto del alta (ITaskItemService.
  CreateAsync, adjuntos, ticket) queda igual.
- Paridad: los endpoints mgmt que reescriben el agente (prompt-set, tools-set) ahora reenvian
  `AllowedBoardIds` para no borrar la whitelist al tocar otro campo.

## Guardarrailes

- Multi-tenant intacto (filtro global; los board ids son del tenant activo). Sin SQL crudo.
- Fail-safe hacia el comportamiento previo: si la columna es null (agentes existentes), no hay restriccion.

## Consecuencias

Mas facil: acotar a un agente a su(s) tablero(s) sin depender del prompt. A vigilar: si se archiva/borra un
tablero de la whitelist, el agente pierde ese destino (con 1 solo permitido y archivado, `crear_tarea`
devolvera "sin tablero permitido"); se corrige reconfigurando la whitelist.

## Plan de accion

- [x] Esquema: `AiAgent.AllowedBoardIdsJson` + migracion DUAL aditiva (PG jsonb / SQL Server nvarchar(max)).
- [x] DTO/servicio: `AllowedBoardIds` en `UpdateAiAgentRequest` y `AiAgentDto`; Serialize/ParseBoards.
- [x] Contexto: `AiToolRunContext.AllowedBoardIds` inyectado por `AiInferenceService`.
- [x] Enforcement en `TasksToolset` (crear_tarea + listar_tableros) + paridad en endpoints mgmt.
- [x] UI: seccion "Tableros permitidos" (checkboxes) en el editor del agente ("Vacio = TODOS").
- [x] Tests unitarios (TasksToolsetBoardWhitelistTests, 7/7) + build+solucion verde. Entregado en v0.15.186.
- [ ] Config de datos (ops, tras desplegar): whitelist de SARA.agente_comercial_v1 = [AGENTE COMERCIAL IA].
