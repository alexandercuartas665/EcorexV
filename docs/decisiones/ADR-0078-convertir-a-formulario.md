# ADR-0078: Verbo CONVERTIR_A_FORMULARIO (transformar un registro en otro formulario)

- Estado: Aceptado
- Fecha: 2026-08-25
- Contexto: motor de reglas (ADR-0016), formularios dinamicos (ADR-0015), verbo de accion
  one-shot IMPRIMIR_PLANTILLA, anclaje de formularios a la tarea por Reference.

## Contexto

En el cotizador COT de AGROMETALICAS se pidio un boton, paralelo al de imprimir, que en vez de
imprimir DISPARE UNA TRANSFORMACION: convertir la cotizacion actual en un registro NUEVO del
formulario "ORDEN DE TRABAJO" (FT-C-008), copiando los datos mapeables y ABRIENDO la OT para
completarla. Si la cotizacion se trabaja DENTRO de una tarea, la OT debe quedar anclada a ESA
MISMA tarea (caer en su pestana Formularios).

Es el patron de reglas existente: se EXTIENDE, no se reinventa.

## Decision

1. **RuleActionKind.OpenForm** (agregado AL FINAL del enum, preserva ordinales) + factory
   `RuleAction.OpenForm(responseId)`. Es un efecto one-shot como PrintTemplate.

2. **Verbo `CONVERTIR_A_FORMULARIO`** (`ConvertirAFormularioVerb`, copia la forma de
   IMPRIMIR_PLANTILLA; inyecta IApplicationDbContext + IFormResponseService). Params:
   `targetCode` (Text, req), `mapping` (Json {origen:destino}, opc), `openMode` (Text, opc).
   Resuelve la definicion destino por code (activa, no archivada), y delega en el servicio.

3. **`IFormResponseService.CreateDerivedFormAsync(sourceResponseId, targetDefinitionId, fieldMapping)`**:
   copia los campos por field_code que el destino CONOCE (con el mapeo explicito para renombres),
   HEREDA el anclaje a la tarea del origen (misma tarea, ordinal nuevo via Reference "{tarea}-{n}")
   o crea sin anclaje si el origen es suelto, y devuelve el nuevo responseId (borrador).

4. **Renderer** (DynamicFormRenderer): tras la rama PrintTemplate, si hay una accion OpenForm emite
   `OnOpenFormRequested(responseId)`. NO navega: lo decide el HOST.

5. **Hosts**: TaskDetailModal abre el registro creado en su modal `_openForm` (Fill) y lo muestra en
   la pestana Formularios; FormModule (standalone) lo abre en un modal Fill propio. (TaskWizard queda
   sin cablear: el guard HasDelegate evita fallos si el boton se usara durante la creacion.)

6. **Visibilidad en la tarea** (el crux): `IFormResponseService.GetTaskRelatedFormsAsync(taskItemId)`
   descubre las respuestas ancladas por Reference al numero de la tarea cuya definicion NO es la del
   concepto ni la de un paso del flujo (p.ej. la OT). TaskDetailModal las pinta como tarjetas
   "Formularios derivados". Reusa el anclaje por Reference que ya usa el wizard (minima friccion).

## Consecuencias

- La transformacion vive en el patron de reglas: el tenant configura un boton (campo) + una regla con
  el verbo, sin codigo. Multi-tenant intacto (todo bajo el tenant del RuleContext).
- Copiar solo field_codes conocidos por el destino evita ensuciar el registro con campos ajenos; la
  grilla 'items' se copia si el destino tambien la tiene (columnas no coincidentes se ignoran).
- El anclaje por Reference hace que la OT aparezca en la MISMA tarea sin una tabla de enlace nueva.

## Nota (v0.15.80): destino que ademas es formulario de un paso

`GetTaskRelatedFormsAsync` NO excluye la definicion de paso completa. Un mismo formulario puede ser paso del
flujo Y destino de una conversion (FT-C-008 es nodo del flujo Y la Orden de Trabajo derivada de la COT). La
respuesta PROPIA del paso se ancla al numero BASE de la tarea ("{tarea}") y la cubre "Formularios del proceso";
las DERIVADAS llevan ordinal ("{tarea}-{n}") y aparecen como "Formularios derivados". Se excluye por respuesta
(base del paso), no por definicion, para que el derivado no quede invisible.

## Alternativas descartadas

- Tabla de enlace tarea<->response para los derivados: mas infraestructura; el anclaje por Reference
  ya existe y es suficiente.
- Que el renderer navegue a la OT: rompe el patron (el host decide como abrir: modal en la tarea).
