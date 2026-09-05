# ADR-0090: Agentes de IA en nodos de flujo (decidir, elegir ruta de compuerta y llenar formularios)

**Estado:** Propuesta
**Fecha:** 2026-09-05
**Deciders:** Alexander Cuartas (orquestador)
**Relacionados:** ADR-0028 (infra IA), ADR-0035 (asignacion por cargo), ADR-0037 (compuertas
auto-resueltas), ADR-0043 (orquestacion server-side del paso de IA), ADR-0057 (API de agentes),
ADR-0067 (autoria de formularios por agente/MCP), ADR-0068 (compuerta/evento como punto de decision
humano asignable), ADR-0072 (compuerta atendida: elegir rama por destino), ADR-0077 (formulario
obligatorio por nodo para cerrar/decidir).

## Contexto

Los flujos de proceso (BPMN) ya soportan que un nodo lo atienda un HUMANO (asignacion por cargo,
formularios de paso, compuertas atendidas). Ademas existe una "ola 2" que permite que un AGENTE de IA
DECIDA un paso: `WorkflowNodeAgent` (agente + autonomia por nodo) -> `WorkflowAgentStepDispatcher`
(barrido async) -> `WorkflowAgentStepRunner` (gate de cupo, `AiUsageLog`, ramas autonomo/propone) ->
`WorkflowEngine.CompleteStepAsync(executedByAiAgentId)`. El contrato del agente es hoy una sola
pregunta cerrada: `{ puede_resolver, resultado, comentario }` (sin function-calling).

Se quiere que el agente pueda, ademas de decidir un paso:
1. LLENAR un formulario nuevo (el del paso actual) y enviarlo.
2. ELEGIR la ruta en una COMPUERTA EXCLUSIVA.

Estado real (inventario del codigo, 2026-09-05):

- DECIDIR un paso: construido de punta a punta y auditado. HUECO: la asignacion nodo->agente
  (`IWorkflowDesignService.SetNodeAgentAsync`) no esta cableada en el editor de flujos ni expuesta por
  endpoint (solo la ejercen tests). El editor no ofrece elegir agente/autonomia por nodo.
- AUTORIA de formularios por agente: completa (`FormAuthoringToolset`, ADR-0067, ~30 tools + puente
  MCP). Un agente puede crear/publicar formularios y enviar un registro SUELTO (`create_record`).
- LLENAR el formulario DEL PASO ACTUAL: NO existe. `create_record` crea un draft SIN `FormFlowLink`
  al `WorkflowInstance`/`WorkflowNode`, asi que enviarlo NO avanza el flujo. `FormResponseService.
  SaveAsync` y `CompleteStepAsync` no tienen `executedByAiAgentId` en la ruta de formulario.
- DECIDIR una COMPUERTA: NO posible. `SetNodeAgentAsync` prohibe nodos que no sean Task; las
  compuertas las auto-resuelve el motor (ADR-0037); `ChooseGatewayRouteAsync` no tiene ruta de agente.
  Atajo que YA funciona: un agente autonomo en el Task PREVIO fija `resultado` y la compuerta aguas
  abajo enruta por su `ConditionExpression`.

## Decision

Un UNICO marco de "agente de nodo" con UNA invocacion y TRES formas de salida segun el tipo de nodo.
La autonomia es CONFIGURABLE POR NODO (ya existe en `WorkflowNodeAgent.Autonomy`), con default seguro
`Proposes`. Se reutiliza al maximo lo construido; los cambios nuevos son minimos y localizados.

### 1. Contrato unificado (una invocacion, tres salidas)

El tipo de nodo determina el "trabajo" del agente y que campo del contrato usa el runner:

| Nodo | Trabajo | Salida usada |
|------|---------|--------------|
| Task de decision | aprobar / rechazar / cerrar | `resultado` + `comentario` (YA existe) |
| Task con `WorkflowNodeForm` | LLENAR el form del paso | `campos` (via tool-calling) + `comentario` |
| Compuerta atendida (ADR-0068/0072) | ELEGIR la ruta | `ruta` (nodo destino) + `comentario` |

Contrato ampliado (aditivo, backward-compatible):

```json
{ "puede_resolver": true, "resultado": "<opcional>", "ruta": "<opcional>",
  "comentario": "<justificacion>" }
```

`campos` NO viaja en este JSON: el llenado de formulario usa function-calling (ver 3).

### 2. Autonomia por nodo (ya existe)

- `Proposes` (default): el agente deja una PROPUESTA y el paso queda pendiente para un humano
  (formulario -> DRAFT con los valores propuestos; decision/ruta -> propuesta separada de `ApprovalResult`).
- `Autonomous`: el agente ENVIA / CIERRA / ENRUTA y el flujo avanza sin intervencion humana.

### 3. Llenar formularios con HERRAMIENTAS (tool-calling)

El agente de un Task con `WorkflowNodeForm` arma el formulario del PASO ACTUAL usando
`CompleteWithToolsAsync` + un TOOLSET DE PASO acotado (nuevo `IAgentToolset` de solo ese contexto):

- `get_step_form` -> esquema del/los formularios del paso (campos, tipos, obligatorios, `visible_when`,
  lookups declarados). Solo el/los del paso actual, no autoria libre.
- `lookup_options` / consultas a Inventario / Directorio / DataContainers para resolver codigos
  (reusa los toolsets existentes, acotados por tenant).
- `set_step_fields(fieldCode -> valor)` -> escribe los valores en el DRAFT anclado al `FormFlowLink`
  del paso (idempotente).
- `submit_step_form()` -> `FormResponseService.SaveAsync(submit: true, executedByAiAgentId)`, que corre
  la MISMA validacion y las reglas on-submit y, via el `FormFlowLink` Pending, completa el paso
  (`CompleteStepAsync(executedByAiAgentId)`), ADR-0077.

En modo `Proposes` el toolset NO expone `submit_step_form`: solo deja el draft lleno y el paso pendiente.

### 4. Compuertas: AMBOS caminos

- B1 (patron, sin cambio de motor): un agente autonomo en el Task PREVIO fija `resultado`; la compuerta
  AUTOMATICA (ADR-0037) enruta por `ConditionExpression`. La autoria valida que los `resultado`
  permitidos del agente coincidan con las condiciones de las aristas de la compuerta destino.
- B2 (directo): se levanta la restriccion "solo Task" de `SetNodeAgentAsync` para admitir agente en
  COMPUERTAS ATENDIDAS (`AllowsAssignment`/`WaitsForHuman`, ADR-0068/0072). El agente devuelve `ruta`
  (nodo destino) y el runner llama una variante de `ChooseGatewayRouteAsync` con `executedByAiAgentId`.
  `ruta` debe ser una salida DIRECTA real de la compuerta (se valida contra las aristas).

### 5. Guardarrailes (no negociables)

- Validacion: el llenado pasa por el MISMO `SubmitAsync` (tipos, obligatorios, `visible_when`, calculos,
  reglas on-submit). Si no valida -> el paso vuelve al humano con el error como `AgentFailureReason`
  (nunca se fuerza ni se inventa). La `ruta` debe ser salida real de la compuerta.
- Cupo: gate ANTES de gastar tokens (`AiQuotaDto.Hard`); agotado -> paso pendiente para humano.
  `AiUsageLog` con `UsageSource = "workflow-agent"` incluso en fallo.
- Fallback: `puede_resolver=false`, baja confianza, error de proveedor o validacion -> `ReturnToPerson`.
- Trazabilidad: `executedByAiAgentId` viaja JUNTO al humano (nunca en su lugar); en formularios se hila
  por `SaveAsync` -> `CompleteStepAsync`. Idempotencia por `AgentAttemptedAt`.
- Multi-tenant: catalogo de agentes/lookups tenant-safe (filtro global); nada cross-tenant.

## Opciones consideradas

### Compuertas
| Opcion | Complejidad | Cobertura |
|--------|-------------|-----------|
| B1 patron (Task previo) | Baja (cero motor) | Solo compuertas automaticas |
| B2 directo (agente en compuerta) | Media (nueva ruta en el motor) | Compuertas atendidas |
| **B1 + B2 (elegida)** | Media | Automaticas Y atendidas |

### Llenado de formulario
| Opcion | Tokens/latencia | Potencia |
|--------|-----------------|----------|
| Una pasada (invoker devuelve `campos`) | Baja | Forms simples |
| **Tool-calling (elegida)** | Alta | Forms con lookups/busquedas; reusa toolsets |

## Analisis de trade-offs

- Tool-calling para el llenado da al agente la capacidad de RESOLVER codigos (inventario, terceros,
  contenedores) antes de escribir, a costa de mas tokens y mas superficie; se acota con un toolset de
  PASO (no autoria libre) y el gate de cupo. La validacion final es identica a la de un humano, asi que
  la potencia extra no relaja la seguridad.
- B1+B2 cubre tanto las compuertas que hoy auto-enrutan como las que hoy atiende un humano, sin obligar
  a rediseniar flujos existentes (B1 no toca el motor; B2 es una ruta nueva y opcional).

## Consecuencias

Se vuelve mas facil:
- Automatizar decisiones, ruteos y diligenciamiento repetitivos con un humano "en el lazo" por defecto.
- Reusar toda la infra de IA (proveedores, cupos, auditoria) y de formularios (validacion, reglas).

Se vuelve mas dificil / a vigilar:
- Costo de tokens del llenado con tools (mitigado por cupo por plan y toolset acotado).
- Diseniar prompts de agente por nodo que produzcan salidas validas (mitigado por el fallback a humano
  y por validar la ruta/campos contra el esquema real).

Se revisara:
- Si conviene un modo "confianza minima" (umbral) que fuerce `Proposes` aunque el nodo sea autonomo.
- Metricas de aceptacion de propuestas para calibrar que nodos pasar a autonomos.

## Plan de accion (olas; mismo diseno, entrega incremental)

- [ ] Ola A - Cablear lo existente: UI en el editor de flujos (`FlowEditor.razor`) para elegir agente +
      autonomia por nodo Task (`Set/Get/Remove/ListAgentCatalog`), + endpoint. Desbloquea DECISIONES de
      paso sin tocar el motor. Riesgo casi nulo.
- [ ] Ola B - Compuertas: B1 (validacion del patron Task-previo) + B2 (levantar "solo Task" para
      compuertas atendidas, variante de `ChooseGatewayRouteAsync` con `executedByAiAgentId`, campo `ruta`
      en el contrato del invoker).
- [ ] Ola C - Llenar formularios: toolset de PASO (`get_step_form` / `set_step_fields` /
      `submit_step_form`) sobre el `FormFlowLink` del paso; hilar `executedByAiAgentId` por
      `SaveAsync` -> `CompleteStepAsync`; runner: `Proposes` = draft lleno, `Autonomous` = submit.
- [ ] Pruebas: matriz dual; unit del parser de contrato (resultado/ruta/campos), del gate de cupo, del
      fallback a humano; integracion del avance de paso por submit de agente y del ruteo de compuerta por
      agente. Nota en este ADR al cerrar cada ola.

Sin migracion NUEVA obligatoria en Ola A (todo existe). Ola B/C pueden requerir solo columnas nullable
aditivas si se decide separar la propuesta de ruta/campos de los campos existentes; se evaluara al
implementar (el modelo ya trae `AgentProposalResult/Comment`).

## Nota (v0.15.178) - Ola A cerrada: asignacion nodo->agente en el editor

Cableada la asignacion que ya existia en el servicio (`IWorkflowDesignService.{ListAgentCatalog,
GetNodeAgent, SetNodeAgent, RemoveNodeAgent}Async`) al editor de flujos (`FlowEditor.razor`):

- Nuevo acordeon "Agente de IA" en el panel del nodo (junto a "Asignar usuarios"). Solo para pasos
  Task; en compuertas/eventos muestra el aviso "solo un paso (Task) admite agente".
- Selector de agente (catalogo del tenant, activos primero, marca "(apagado)" el inactivo) + selector de
  Autonomia (Propone | Autonomo). Default al asignar por primera vez = Propone (seguro). Upsert por el
  indice unico (TenantId, NodeId); "Sin agente" quita la asignacion.
- Se carga el agente del nodo al seleccionarlo (`GetNodeAgentAsync`) y el catalogo una vez; las
  escrituras van bajo el mutex de DbContext del editor. Sin cambio en el motor ni migracion (todo
  existia). Nota de guardarrailes visible en el panel (fallback a humano, cupo, auditoria).
- Verificado en dev (AGROMETALICAS, flujo ORDENES DE TRABAJO, paso "Recibir OT impresion"): asignar
  persiste `WorkflowNodeAgent`, cambiar autonomia persiste, el estado inicial en recarga refleja lo
  guardado, quitar borra la fila, y un nodo no-Task muestra el gating. Build Release verde.

Pendiente: Ola B (compuertas: patron Task-previo + agente directo en compuertas atendidas) y Ola C
(llenar el form del paso con toolset acotado + hilar executedByAiAgentId por SaveAsync->CompleteStep).
