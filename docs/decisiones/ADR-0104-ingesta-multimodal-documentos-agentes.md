# ADR-0104: Ingesta multimodal de DOCUMENTOS entrantes al modelo (flujo de agentes)

- Estado: Aceptado
- Fecha: 2026-09-15
- Version: v0.16.78
- Relacionado: ADR-0103 (ContenedorDatosToolset / cargar_productos), captura de MediaFileName (v0.16.67)

## Contexto

El agente "Clasificador de productos y precios" (SKY SYSTEM) debe recibir por WhatsApp un PDF o un Excel
(o una imagen) y que ESE binario llegue al modelo (Gemini) para extraer productos y cargarlos al contenedor
con `cargar_productos` (ADR-0103).

Hallazgo al arrancar (cambia el alcance): en el flujo REAL (WhatsApp) y en el emulador `/api/test/agent`,
NINGUN adjunto llegaba al modelo, ni siquiera las imagenes. El binario se descargaba y persistia bien
(Message con MediaUrl/MediaMimeType/MediaFileName en `wwwroot/uploads/chat`), pero `RespondAsync` pasaba
todo en null y el modelo solo veia el texto "(adjunto)". La unica ruta que mandaba una imagen a Gemini era
el "Chat de prueba". Habia 5 huecos: DTOs sin tipo documento; el bucle de herramientas no ensamblaba
documentos; `RespondAsync` no propagaba media; `AgentConversationService` no leia el binario del Message
entrante; y `AiProviderClient` no emitia parte documento a Gemini.

Ademas, el path de tools de Gemini usa el endpoint OpenAI-compatible (`/openai/chat/completions`), que
acepta imagen (`image_url`) y audio (`input_audio`) pero **NO** PDF por inlineData.

## Decision

Reenviar el adjunto del ULTIMO turno del cliente hasta el modelo, con una ruta por tipo:

- **Imagen** -> se pasa como imagen (el modelo la VE; ya funcionaba por vision, ahora tambien en el flujo real).
- **PDF / otro binario** -> se pasa como DOCUMENTO por la ruta **NATIVA de Gemini** `generateContent`
  (`inlineData { mimeType, data }` + `functionDeclarations`). El endpoint nativo SI soporta function calling,
  asi que el bucle de herramientas corre igual y `cargar_productos` se invoca normalmente. Maneja catalogos
  multipagina nativamente (inline hasta ~20MB). Se elige la ruta nativa SOLO cuando el turno trae documento;
  sin documento, Gemini sigue por OpenAI-compat (imagen/audio intactos).
- **Excel / CSV** -> se EXTRAE a texto tabular en el servidor (ClosedXML, ya en el repo) y se inyecta como
  texto del turno (Gemini no acepta xlsx nativo). Se acotan hojas/filas/columnas/tamano.

Alternativa descartada para PDF: renderizar cada pagina a imagen y mandarla por el path de imagen. Reusa
plumbing pero pesa mucho en catalogos grandes (35 imagenes = muchos tokens) y obliga a trocear. La ruta
nativa es la correcta para el caso real (catalogo PDF escaneado).

## Cambios

- `AiInferenceDtos.cs`: nuevo `AiInlineDocument(Base64, Mime, FileName)`; `AiToolMessage` gana `Documents`;
  `RespondAsync` acepta imagen y documento entrantes (parametros opcionales; sin adjunto, todo igual).
- `AiInferenceService.cs`: `RunCoreAsync`/`RunToolLoopAsync` propagan el documento y lo adjuntan SOLO al
  ultimo turno de usuario (igual que hoy con imagen/audio).
- `AiProviderClient.cs`: nuevo `GeminiNativeWithTools` (generateContent con inlineData + functionDeclarations,
  functionCall/functionResponse; empareja resultados por NOMBRE y fusiona resultados consecutivos en un turno
  `user`). `CompleteWithToolsAsync` enruta a el cuando el provider es Gemini y hay documento.
- `AgentConversationService.cs`: lee el binario del ultimo Message entrante (via `IAgentAssetReader`) y lo
  enruta (imagen / PDF / Excel-a-texto).
- `SpreadsheetText.cs` (nuevo): xlsx/xls/csv -> texto tabular con topes.
- `Program.cs` (SuperAdmin, emulador `/api/test/agent`): `TestAgentRequest` gana `FileBase64/FileMime/FileName`
  y guarda un Message inbound `Document`, para validar PDF/Excel end-to-end por la MISMA ruta real.

Multi-tenant intacto (el Message y el contenedor son tenant-scoped por el filtro global). Sin migraciones,
sin cambios de esquema, sin dependencias nuevas (ClosedXML ya estaba). ASCII.

## Consecuencias

- El caso estrella (catalogo PDF -> productos -> `cargar_productos`) queda cubierto, ademas de Excel (texto)
  e imagen (regresion positiva: ahora tambien llega en el flujo real).
- Solo Gemini procesa el documento nativo; otros proveedores lo ignoran (como el audio). Excel es texto para
  todos. El adjunto se manda una sola vez (ultimo turno), no en todo el historial: acota tokens.
- Limite practico del inline nativo ~20MB; PDFs mayores requeririan la Files API de Gemini (fuera de alcance).

## Pruebas

- `AiProviderClientVisionTests`: Gemini con documento usa `:generateContent` (no `/openai/`), emite
  `inlineData`+`application/pdf`+`functionDeclarations` y parsea el `functionCall` como `AiToolCall`; sin
  documento sigue por OpenAI-compat.
- `SpreadsheetTextTests`: extrae encabezados/valores de un xlsx, CSV como texto, base64 invalido -> null,
  `IsSpreadsheet` por mime/nombre.
- Build de la solucion verde.
