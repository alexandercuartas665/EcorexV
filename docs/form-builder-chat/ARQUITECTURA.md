# Asistente de creacion de formularios por chat (Excel/PDF/imagen -> formulario)

> Solo ASCII (convencion del repo). Feature nueva, branch `worktree-form-builder-chat`.

## Objetivo
En el modal del disenador de formularios, una 3a/4a columna tipo CHAT que, subiendo un
Excel/PDF/imagen y conversando de forma guiada, crea o edita un formulario dinamico (campos,
secciones, grillas con calculo/lookups) y su plantilla de impresion. Usa la IA del sistema (AI
Gateway) + un agente con herramientas (FormAuthoringToolset) para construir el formulario EN VIVO.

## Decisiones del usuario (2026-09-26)
- Implementa el codigo la sesion de config en un WORKTREE aparte (excepcion a "solo config").
- Flujo: guiado + construccion EN VIVO (el form se arma en las otras columnas mientras se conversa).
- Entradas: Excel + PDF + imagen.
- Salida: crear nuevo (campos+grillas), editar existente, generar plantilla de impresion.
- Confirmacion: el agente PROPONE cada accion importante y el usuario la CONFIRMA antes de ejecutarla.
- Alcance: todos los tenants.
- Historial: conversacion PERSISTENTE (retomar/auditar).

## Piezas existentes que se reusan (mapa del codigo)
- `AiInferenceService.RunToolLoopAsync` (Ecorex.Application/Tenancy): loop de tool-calling en proceso,
  agrega todos los `IAgentToolset` (incluye `FormAuthoringToolset`). MaxToolRounds=6.
- `FormAuthoringToolset` (~34 tools): create_form, add_container/update/move, add_question/update/move/
  delete (grids via options_json con calc/lookup/rollup/resolve), set_transactional/set_module,
  create_template/update_template/set_default_template/wire_print_button, create_share_link, create_record,
  discovery (describe_components, list_data_containers, list_tercero_fields, get_form, ...).
- `AiProviderClient`: CompleteAsync / CompleteVisionAsync / CompleteWithToolsAsync. Providers Claude,
  Gemini (nativo generateContent para PDF/inlineData), OpenAI-compat, DeepSeek. SIN streaming.
- `SpreadsheetText.cs` (ClosedXML): xlsx/xls/csv -> texto tabular acotado (para inyectar en el prompt).
- Vision: `AiInferenceService.ReadImageAsync` (una pasada de vision extrae texto); `CompleteVisionAsync`
  solo Claude/Gemini.
- UI: `FormDesigner.razor` (route /formularios/{id}/disenar) = modal 3 columnas .fb-left / .fb-canvas /
  .fb-right dentro de .fb-body. Chat de referencia: `Agentes.razor` (burbujas ag-bubble, InputFile
  imagen/archivo/audio, log de debug). `ClientChat.razor` componente compartido.
- Persistencia agente: `AiAgentRunLog` (bitacora, tenant-scoped, ConversationId/AgentId) + cache de sesion
  (AiAgentCacheField/Value). NO hay tabla de "conversacion+mensajes" para un chat de UI.

## Huecos a construir
1. **Invocador "agente disenador"** bajo tenant/usuario logueado (no el path REST/mgmt-key cross-tenant).
   El tool loop existe pero esta atado a un AiAgent de linea WhatsApp; hace falta un invocador delgado
   analogo al test-chat de Agentes.razor pero para el disenador, con GATE humano (confirmar cada accion).
2. **Gate humano en el loop**: variante de RunToolLoopAsync donde las tool-calls NO se ejecutan solas: se
   emiten como PROPUESTAS; la UI muestra Confirmar/Rechazar; al confirmar se ejecuta la tool y se continua.
3. **Persistencia de conversacion**: tabla nueva `form_builder_conversations` + `..._messages`
   (tenant-scoped, ligada al form_definition en construccion). Guarda mensajes, adjuntos, propuestas y su
   resultado (auditoria).
4. **Ingesta de archivos**: Excel via SpreadsheetText; imagen/PDF via vision (Gemini nativo para PDF).
   Cross-proveedor de PDF es hueco -> MVP: PDF via Gemini o extraer texto.
5. **Tools faltantes en FormAuthoringToolset** (los DTOs ya lo soportan): AllowedCargosJson y
   VisibleWhenJson en contenedores; FieldVisibilityJson/VisibleWhenJson/CascadeConfigJson/
   SubformDefinitionId/DefaultDynamic en preguntas; y una tool para autorar gridDerive/CONVERTIR_A_FORMULARIO.
6. **UI**: 4a columna "chat" en .fb-body de FormDesigner.razor (reusar markup de Agentes.razor) +
   recarga en vivo del lienzo tras cada accion confirmada + tarjetas de propuesta/confirmacion.
7. **Streaming** (opcional, fase tardia): hoy todo es request/response; un "escribiendo..." basta para MVP.

## Flujo de construccion en vivo con gate
1. Usuario abre el disenador (form nuevo en BORRADOR) y la columna de chat.
2. Sube Excel/PDF/imagen y/o describe lo que quiere.
3. El agente ANALIZA (discovery + lectura del archivo) y PROPONE un plan (secciones, campos, columnas de
   grilla, calculos) en lenguaje claro.
4. Por cada accion importante, el agente emite una PROPUESTA (ej. "crear seccion Datos del cliente con los
   campos X,Y,Z"); la UI la muestra con Confirmar/Rechazar.
5. Al confirmar, se ejecuta la(s) tool(s) del FormAuthoringToolset; el lienzo del disenador RECARGA y
   muestra lo construido. Se persiste la propuesta+resultado.
6. Se itera hasta terminar; el usuario revisa y publica (activate) el formulario.

## Fases
- F0: worktree + andamiaje + ARNES (system-prompt) + esta arquitectura. [en curso]
- F1 (MVP): invocador agente disenador con gate + persistencia (tabla+migracion) + ingesta Excel + crear
  formulario nuevo en vivo (sin UI final, probado por endpoint/harness).
- F2: UI 4a columna en el disenador + upload + recarga en vivo + tarjetas de confirmacion.
- F3: vision (imagen) + PDF (Gemini) + editar existente + generar plantilla de impresion.
- F4: tools faltantes (cargo/visibilidad/gridDerive) + pulido + streaming opcional.

## Reglas de trabajo
- Solo ASCII en archivos nuevos. Multi-tenant real (todo bajo el tenant logueado). Auditoria de mutaciones.
- El formulario se construye en BORRADOR (Draft) hasta que el usuario publique.
- Deploy lo corre el usuario; yo compilo + commit + push en esta rama y pido OK para desplegar.
