# ADR-0098: Herramienta de agente "crear_actividad" (actividad tipada por concepto + formulario lleno)

**Status:** Accepted
**Date:** 2026-09-12
**Deciders:** sesion de agentes (Ana / SOLDARCO) + desarrollo

## Contexto

Un agente conversacional (Ana, SOLDARCO) debe CERRAR creando una ACTIVIDAD de un CONCEPTO
(`ActividadSubcategoria`) que ya trae tablero/columna/formulario/flujo, y ademas LLENAR y ENVIAR el
formulario del concepto con lo capturado en la charla. NO crea contacto en el Directorio (`Tercero`): solo
la actividad + su formulario.

La herramienta existente `crear_tarea` (`TasksToolset`) NO puede cambiar: SARA (AGROMETALICAS) depende de
ella y debe quedar identica (firma, comportamiento, tests).

## Decision

Un **toolset NUEVO y SEPARADO**, `ActividadesToolset` (`IAgentToolset`, GroupKey `"actividades"`), que
coexiste con `crear_tarea`. Cada agente elige sus herramientas por `disabled_tools_json`: Ana usa
`actividades`; SARA sigue con `crear_tarea`. `TasksToolset` no se toca.

Dos herramientas:
- `ver_formulario_concepto(concepto)`: resuelve el concepto por nombre/codigo y devuelve los campos del
  formulario (field_code, label, tipo, requerido) y las opciones validas de los campos de seleccion.
- `crear_actividad(concepto, titulo?, datos)`: crea la actividad via `ITaskItemService.CreateAsync` con
  `SubcategoriaId` y SIN `BoardId` (hereda tablero/columna/flujo del concepto), crea la respuesta del
  formulario (`IFormResponseService.CreateTaskConceptFormAsync`) y la ENVIA
  (`SaveAsync(submit:true, executedByAiAgentId)`), adjuntando la media entrante de la conversacion.

El id del agente en ejecucion se hila por `AiToolRunContext.AgentId` (nuevo), seteado por
`AiInferenceService` en el mismo punto donde ya carga el agente; el toolset lo usa como
`executedByAiAgentId` para que el agente quede como autor del formulario.

## Comportamiento ante datos invalidos (no dejar actividad huerfana)

`crear_actividad` **PRE-VALIDA** los `datos` contra la definicion ANTES de crear nada: requeridos presentes
y valores de campos de seleccion (Radio/Select) que sean una opcion valida. Si falla, devuelve
`{ ok:false, error, campos:{field_code:msg} }` **sin crear** actividad ni respuesta (el modelo reintenta con
los datos corregidos). Si `SaveAsync` fallara de todos modos (una regla que la pre-validacion no cubre), se
ARCHIVA la actividad recien creada (best-effort) y se devuelven los errores por campo. Asi nunca queda una
actividad a medias.

## Consecuencias

- Ana cierra por una via tipada (concepto) con su formulario lleno y el flujo avanzado (si el nodo de inicio
  tiene `FormFlowLink`), sin tocar `crear_tarea`.
- Sin migraciones (no hay schema nuevo). Config por agente (habilitar `actividades`, deshabilitar
  `crear_tarea`/`crear_lead`) y el tablero destino del concepto se ajustan por fuera (ops/SQL).

## Referencias

- Codigo: `Ecorex.Application/Tenancy/ActividadesToolset.cs`, `AiToolRunContext.cs` (AgentId),
  `AiInferenceService.cs` (Begin con agentId), `DependencyInjection.cs` (registro).
- Tests: `tests/Ecorex.Application.Tests/ActividadesToolsetTests.cs` (5) + `TasksToolsetBoardWhitelistTests`
  intactos (regresion de `crear_tarea`).
