# ADR-0124: Etiquetar la tarea segun la salida del flujo (herramienta del agente)

**Status:** Aceptado
**Date:** 2026-10-07
**Deciders:** Alexander Cuartas
**Extiende:** ADR-0090 (agentes en nodos), ADR-0115 (ApplyTagId en enlaces de decision)

## Contexto

Cuando un flujo toma una salida (p.ej. la compuerta del agente enruta a "Cliente no desea comprar" o a
"Cliente decide comprar"), la tarea no queda CLASIFICADA: hay que etiquetarla a mano para reportar "cuantos
perdimos / ganamos / por que". Los enlaces de decision ya aplican una etiqueta al hacer clic
(`ApplyTagId`, ADR-0115), pero el agente de nodo no tiene herramienta para etiquetar al atender el paso.

Modelo existente reutilizado: `TaskItemTag` (catalogo por tenant) + `TaskItemTagAssignment` (tarea-etiqueta);
`TaskItemService` ya agrega/quita etiquetas.

> **Nota (revision 2026-10-07):** la primera version proponia ademas una capa DETERMINISTA por nodo
> (`WorkflowNode.ApplyTagsJson`: etiquetas que el motor aplicaba al activar el nodo, configuradas con un
> selector en el panel del nodo). Se DESCARTO por decision del usuario ("dejalo solo como herramientas del
> agente"): ocupaba espacio en el disenador y duplicaba la intencion del agente. El etiquetado del flujo queda
> SOLO como herramienta del agente (abajo). Se eliminaron la columna, su migracion y el selector del nodo.

## Decision

El agente de nodo recibe la herramienta `etiquetar_tarea(etiqueta)`: agrega a la tarea una etiqueta por
NOMBRE segun como termine el caso (ej. "Perdido", "Ganado", "Precio alto", "Se fue con la competencia").

- El **invoker** (`WorkflowAgentInvoker`) solo ACUMULA las etiquetas pedidas por el modelo (no toca la BD) y
  las devuelve en `WorkflowAgentInvocationResult.RequestedTagNames` (tope `MaxTagsPerStep`).
- El **runner** (`WorkflowAgentStepRunner`) las APLICA a la tarea despues de registrar la decision: resuelve
  por nombre (case-insensitive) contra el catalogo del tenant; si no existe, la CREA (para permitir motivos
  con matiz); dedup + idempotente. Best-effort: su fallo no frena el avance del paso.
- El **prompt** del agente incluye el CATALOGO de etiquetas del tenant (`AvailableTaskTags`) para que prefiera
  REUSAR una existente en vez de crear duplicados.
- La herramienta esta SIEMPRE disponible (no depende de recursos Colmena/voz/WhatsApp). El disenador le dice al
  agente CUANDO y CON QUE clasificar en el "prompt extra" del paso; una ayuda en acordeon dentro del modal de
  configuracion del agente documenta esta y las demas herramientas.

El color de cada etiqueta se administra en el modulo Tableros (catalogo `TaskItemTag`).

## Consecuencias

- El agente clasifica el resultado del caso sin inventar rutas ni pasos; los reportes por etiqueta salen del
  catalogo que se mantiene solo (reusa; crea solo si hace falta).
- Retrocompatible: un agente al que no se le pide etiquetar se comporta igual que hoy.
- Sin cambios de esquema: se reusa `TaskItemTag` / `TaskItemTagAssignment`. Se agrega solo un campo al
  resultado de la invocacion (`RequestedTagNames`) y una seccion al contexto (`AvailableTaskTags`).
- NO se agrego columna al nodo (la capa determinista se descarto).

## Action Items

1. [x] Herramienta `etiquetar_tarea` en el agente de nodo (decision y form); el invoker acumula, el runner aplica (resuelve/crea por nombre).
2. [x] Incluir el catalogo de etiquetas del tenant en el contexto/prompt del agente (para reusar existentes).
3. [x] Ayuda (acordeon) en el modal de configuracion del agente del nodo explicando como usar las herramientas.
4. [ ] E2E: el agente atiende la compuerta, enruta segun la charla y etiqueta la tarea con el motivo.
