# ADR-0131: Asistente de requerimiento (IA, imagen/PDF -> tabla) como control de formulario

Fecha: 2026-10-23
Estado: Aceptada

## Contexto

En la COT de AGROMETALICAS (y cualquier formulario) se necesita que el comercial suba la FOTO o el PDF del
requerimiento del cliente y que un agente de IA extraiga las piezas (nombre + medidas) y las deje en la grilla
de detalle del MISMO formulario, sin salir del formulario y sin SQL. Debe ser self-serve (configurable en el
FormDesigner) y con el HUMANO en el bucle (no escribe sin confirmar).

## Decision

Nuevo control `FormControlType.AiIntake` que es un AGENTE de IA con una herramienta ACOTADA al propio formulario.

1. **Control + render** (`DynamicFormRenderer`): dropzone / "elegir archivo" (image/PDF, valida tipo+tamano por
   config), estado "Analizando...", PREVISUALIZACION de las filas propuestas con [Aplicar a la tabla] / [Descartar]
   (el humano confirma antes de escribir). Al aplicar, las filas caen en la grilla destino reusando el motor de
   filas del renderer (`SetValue` -> recalcula columnas calculadas/lookup), donde quedan editables/borrables. El
   archivo origen se guarda en StoredFile (IFileStorageService) para auditoria + miniatura.

2. **Servicio** `IFormAiIntakeService` (Application/Forms): recibe responseId + fieldCode + archivo (base64+mime);
   lee la config del control; resuelve el AGENTE referenciado (proveedor/modelo/prompt base), cupo y AiUsageLog;
   corre un tool-loop con `IAiProviderClient.CompleteWithToolsAsync` pasando la imagen/PDF inline y exponiendo SOLO
   el toolset acotado; devuelve FILAS PROPUESTAS (no las escribe). Tenant-scoped. Solo Gemini/Claude (leen imagen/PDF).

3. **Toolset acotado** `FormScopedToolset`: se CONSTRUYE con la definicion del formulario actual; `get_grid_schema`,
   `rellenar_tabla` (RECOLECTA filas propuestas; ignora columnas calculadas/consecutivas; tope maxRows) y `set_campo`
   (campos simples). Prohibido por construccion tocar otros formularios/registros/tenants o SQL. No se registra en
   DI (no es un IAgentToolset del agente): lo instancia el servicio por llamada.

4. **Config** (`AiIntakeConfig`, en OptionsJson bajo "ai", editable en FormDesigner): agente, prompt, grilla destino,
   guia de columnas, accept (image/pdf), max_mb, max_rows, modo (append/replace), modelo opcional. Nada de SQL.

5. **Gobernanza/seguridad**: el archivo del cliente es DATO, NO instrucciones (el system prompt lo dice y manda;
   se ignoran ordenes embebidas en la imagen/PDF). Confirmacion humana antes de escribir. AiUsageLog (source
   "form-ai-intake") + cupos por plan. Todo tenant-scoped. Solo en formularios autenticados (no en el link publico).

## Decisiones (confirmadas con el usuario)

- El control REFERENCIA un agente del tenant (reusa proveedor/modelo/prompt base), no un proveedor suelto.
- Solo funciona autenticado (dentro de la tarea/vista interna), no en el link publico /f/{token}.
- El archivo origen SI se guarda (StoredFile) para auditoria.

## Consecuencias

- Sin migracion: la config vive en OptionsJson del control; las filas en el jsonb de la grilla como cualquier otra.
- El AiIntake no captura valor propio (FormFieldValidator.IsNonInput): escribe en la grilla destino.
- La previsualizacion actual es de solo lectura; la edicion fina se hace en la grilla tras Aplicar (human-in-the-loop
  completo: revisar -> aplicar -> editar/quitar).
