# ADR-0174: Lector de medios de los agentes (imagenes, PDF y audio) independiente del proveedor

Fecha: 2026-11-16
Estado: aceptado
Version: v0.16.335

## Contexto

Los agentes en Gemini 2.5 Pro cuestan mucho y el usuario quiere pasarlos a DeepSeek (deepseek-v4-flash; ANA,
ALEJITO y DEMO Datos Externos de SOLDARCO ya corren ahi). Con DeepSeek el agente respondia pero NO entendia lo que
mandaba el cliente (diagnostico en prod de la sesion data-ops):

- Imagen: el lector previo usaba una plantilla FIJA de EPRING (TIPO_IMAGEN factura de energia / techo /
  motobomba, VALOR_TOTAL_COP, CONSUMO_KWH...) para TODOS los agentes de TODOS los tenants: lo que no encajaba se
  descartaba. Caso real: una foto con "Electrodo E7018 1/8 - 10 cajas / Ciudad: Manizales" -> solo llego la
  ciudad y ANA contesto "no logro leer tu imagen". Ademas `OpenAiCompatibleWithTools` solo mandaba imagenes al
  modelo principal si el proveedor era Gemini; en DeepSeek se descartaban en silencio.
- PDF: el lector solo corria en Gemini/Claude; en DeepSeek el agente solo veia el nombre del archivo.
- Nota de voz: solo Gemini transcribia; en DeepSeek quedaba "(adjunto)".

Volumen 30 dias (entrantes en lineas con agente): EPRING 53 img / 51 audio / 2 pdf; AGRO 27 / 20 / 8. Sin esto
no se podian migrar EPRING ni AGRO.

## Decision

1. **Lector de medios generico y configurable por agente** (`AgentMediaReader`). Por defecto describe el archivo
   ("QUE ES:") y transcribe todo el texto legible ("TEXTO:"). Campo nuevo nullable `ai_agents.media_reader_prompt`
   (acordeon "Lector de archivos" en el editor del agente; null = generico; metodo aparte
   `SetMediaReaderPromptAsync` para que los llamadores de `UpdateAsync` que no lo conocen no lo borren). Las
   etiquetas del texto anexado no cambian ("[Lectura automatica de la imagen adjunta]", "[Lectura automatica del
   documento adjunto]", "Transcripcion del audio: ...").
2. **Migracion de datos** (en la misma migracion `AddAgentMediaReaderAndTranscriber`, PG y SQL Server): a los
   agentes cuyo prompt base, datos cache o prompts enrutados nombran VALOR_TOTAL_COP / CONSUMO_KWH / TIPO_IMAGEN /
   FOTO_TECHO_AREA / PLACA_MOTOBOMBA se les copia la plantilla historica como su lector (idempotente, solo si es
   null). En prod el filtro devuelve exactamente "Agente Comercial" de EPRING (verificado con SELECT, solo lectura).
3. **Imagenes al modelo principal tambien en DeepSeek/ChatGPT** (`image_url` data-uri en el mensaje de usuario,
   el unico donde DeepSeek las acepta). Si el modelo no tiene vision (400/422 que no sea de cupo), UN reintento
   sin imagenes; el aviso (`AiCompletion.Notice`) queda en la bitacora de prompts y las imagenes se quitan del
   historial para las vueltas siguientes. El agente nunca queda mudo.
4. **PDF en DeepSeek/ChatGPT via rasterizado**: `IPdfRasterizer` (PDFium, el del Analizador de documentos), hasta
   5 paginas a 150 dpi, leidas con `CompleteVisionAsync` + el lector del agente. Gemini/Claude siguen leyendo el
   PDF nativo. PDF protegido/corrupto: best-effort con log. Las paginas NO se pasan ademas al modelo principal
   (el lector ya entrega su texto; se evita duplicar costo).
5. **Transcriptor de plataforma**: si el proveedor del agente no oye audio (todo lo que no es Gemini), la nota de
   voz se transcribe con la cuenta de Gemini de Servidores de IA y un modelo barato configurable
   (`ai_provider_configs.transcription_model`, solo en la fila de Gemini, editable en la UI sin deploy; null =
   gemini-2.5-flash), nunca con el modelo del agente. Se reutiliza el transcodificado a wav. El consumo se
   registra en `AiUsageLog` del tenant del agente (source "transcripcion"). Si Gemini esta sin cupo, el freno de
   v0.16.332 (ADR-0172) corta sin reintentos y el agente sigue con "(adjunto)". Sin Gemini habilitado: como
   antes, con UN log por proceso.

## Alternativas consideradas

- Pasar los agentes a gemini-2.5-flash sin codigo: abarata pero no resuelve DeepSeek ni la plantilla de EPRING.
- "Proveedor de medios" Gemini Flash para TODO (imagen, PDF y audio): DeepSeek ya ve imagenes y el costo por
  imagen es bajo; se deja Gemini solo para el audio, donde DeepSeek no tiene soporte oficial.
- Guardar el modelo del transcriptor en appsettings: obliga a reiniciar el contenedor; el campo en Servidores de
  IA lo cambia el admin de plataforma al instante.

## Consecuencias

- Los agentes sin lector propio pasan de la plantilla de EPRING al generico (incluidos los que siguen en Gemini):
  reciben mas informacion de cada imagen/PDF. EPRING conserva su comportamiento por la migracion de datos.
- Los agentes de vision especializados (p. ej. "Extractor de medidas (planos)" de AGRO) no se migran de proveedor
  hasta probarlos aparte.
- El consumo de las lecturas de imagen/PDF con el modelo del agente sigue sin registrarse aparte (como antes); el
  del transcriptor de plataforma si se registra porque usa otro proveedor.
