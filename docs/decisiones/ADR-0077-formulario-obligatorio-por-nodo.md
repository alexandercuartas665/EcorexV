# ADR-0077: Formulario OBLIGATORIO por nodo para cerrar/decidir un paso

- Estado: Aceptado
- Fecha: 2026-08-25
- Contexto: formularios por nodo (ADR-0015, WorkflowNodeForm), cierre directo con formulario
  opcional (ADR-0070), compuerta atendida que elige rama (ADR-0072).

## Contexto

ADR-0070 permitio CERRAR un paso directo con el formulario OPCIONAL (nota opcional). Pero hay
pasos donde el formulario ES un requisito: p.ej. en la compuerta "Cliente decide si compra" hay
que dejar registrado (formulario) que el cliente acepta ANTES de elegir la ruta / dar cierre. Se
necesita marcar, POR NODO, que su formulario es obligatorio.

## Decision

Marca por vinculo formulario-nodo: `WorkflowNodeForm.IsRequired` (bool, default false; migracion
dual PG + SQL Server). Es opcional por diseño: cada flujo decide en cuales pasos el formulario es
requisito de cierre.

- **Editor** (FlowEditor, acordeon Recursos): cada formulario del nodo lleva un checkbox
  "obligatorio" -> `IWorkflowDesignService.SetNodeFormRequiredAsync(nodeId, formDefId, required)`.
  El canvas expone `FlowNodeFormDto.IsRequired`.
- **Enforcement** (WorkflowInboxService): al CERRAR (`CompletePendingStepAsync`) o ELEGIR RUTA en
  una compuerta atendida (`CompleteGatewayChoiceAsync`) se bloquea si el nodo tiene un formulario
  obligatorio SIN enviar. "Enviado" = FormFlowLink `Completed` para (instancia, nodo) de esa
  definicion, o (robustez) una FormResponse `Submitted` anclada al numero de la tarea. Mensaje
  claro pidiendo diligenciar y enviar.
- **Derivacion**: `EnsureDraftAsync` copia `IsRequired` (y ahora tambien `SortOrder`) al derivar el
  borrador de un flujo publicado, para que la marca no se pierda al editar/re-publicar.

## Consecuencias

- Un paso con formulario obligatorio ya no se puede cerrar "directo" (se respeta ADR-0070 solo
  cuando el formulario NO es obligatorio).
- En una compuerta atendida, no se puede decidir la ruta hasta enviar el formulario requerido: es
  el punto natural para exigir "el cliente acepta" antes de enrutar.
- Es aditivo y opcional: los flujos existentes siguen igual (IsRequired = false por defecto).

## Pendiente / nota

- UX en el detalle de la tarea: cuando el nodo exige formulario, convendria que el menu del nodo
  guie a "Diligenciar formulario" en vez de ofrecer el cierre directo (hoy el cierre directo se
  intenta y el backend lo bloquea con el mensaje). Mejora de UI para una proxima pasada.
