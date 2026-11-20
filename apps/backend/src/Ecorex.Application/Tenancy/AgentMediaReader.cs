using Ecorex.Application.Pdf;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

/// <summary>Proveedor + credenciales + modelo con que se hace UNA lectura de medios.</summary>
public sealed record AgentMediaModel(AiProvider Provider, string ApiKey, string? BaseUrl, string Model);

/// <summary>
/// LECTOR DE MEDIOS de los agentes (ADR-0174). Antes de que responda el modelo principal, cada imagen, PDF o nota
/// de voz del cliente se convierte en TEXTO, que se anexa al turno ("[Lectura automatica ...]", "Transcripcion del
/// audio: ..."); asi lo ven el modelo principal Y el extractor de cache (que solo mira texto).
/// <list type="bullet">
/// <item>Imagen: pasada de vision con el prompt del lector del agente (<see cref="ReaderPromptFor"/>).</item>
/// <item>PDF: nativo en Gemini/Claude; en DeepSeek/ChatGPT se RASTERIZA (hasta <see cref="MaxPdfPages"/> paginas)
/// y se lee como imagenes.</item>
/// <item>Audio: lo transcribe quien oiga audio (Gemini). Elegir con que modelo es cosa del llamador (el del agente
/// si es Gemini; si no, el transcriptor de plataforma).</item>
/// </list>
/// Nunca lanza por un fallo del proveedor: devuelve el <see cref="AiChatResult"/> (Ok=false) o null si no aplica.
/// </summary>
public sealed class AgentMediaReader
{
    public const int MaxPdfPages = 5;
    public const int PdfDpi = 150;
    public const string DefaultTranscriptionModel = "gemini-2.5-flash";

    /// <summary>Lector por defecto (agente sin prompt propio): describe y transcribe todo lo legible.</summary>
    public const string GenericReaderPrompt = @"Eres el lector de archivos de un agente de atencion por WhatsApp. Recibes UNA imagen o UN documento que
envio un cliente. Responde SOLO en texto plano, sin markdown:
QUE ES: una linea breve con lo que es (foto de producto, lista de pedido, factura, cotizacion, plano, captura de
pantalla, documento de identidad, foto de un lugar, etc.).
TEXTO: la transcripcion de TODO el texto legible, fiel y en orden: productos, referencias, medidas, cantidades,
valores, nombres, empresas, NIT, telefonos, direcciones, ciudades y fechas. Si no hay texto, describe en una o dos
lineas lo que se ve.
Si una parte no se lee, escribe NO_LEGIBLE en su lugar. No inventes datos ni agregues opiniones.";

    /// <summary>
    /// Plantilla HISTORICA (hasta v0.16.334 era el lector fijo de TODOS los agentes). La migracion
    /// AddAgentMediaReaderPrompt la copia como MediaReaderPrompt de los agentes que dependen de sus campos
    /// (EPRING "Agente Comercial"), para que no cambien de comportamiento. Se conserva aqui como referencia.
    /// </summary>
    public const string EpringLegacyReaderPrompt = @"Eres un extractor. Miras UNA imagen o documento que envio un cliente por WhatsApp y devuelves
SOLO un bloque de texto plano con estos campos (una linea por campo). Si un campo no aplica o no se lee, pon
NO_LEGIBLE. No agregues nada mas, sin markdown.
TIPO_IMAGEN: (FACTURA_ENERGIA | FOTO_TECHO_AREA | PLACA_MOTOBOMBA | OTRO)
COMERCIALIZADORA:
VALOR_TOTAL_COP: (solo digitos, el valor total a pagar del mes)
CONSUMO_KWH: (solo digitos)
ESTRATO:
PERIODO_FACTURADO:
CIUDAD:
TIPO_TECHO: (teja | losa | metalico | otro, solo si es FOTO_TECHO_AREA)
MOTOBOMBA: (MARCA/MODELO/POTENCIA_HP/VOLTAJE/FASES/AMPERAJE, solo si es PLACA_MOTOBOMBA)";

    private const string TranscriberPrompt = @"Eres un transcriptor. Escuchas UNA nota de voz que envio un cliente por WhatsApp y
devuelves SOLO la transcripcion literal en el idioma hablado (normalmente espanol), sin comillas, sin
markdown y sin agregar nada. Si no se entiende nada, devuelve una cadena vacia.";

    private const string ReadInstruction = "Lee el archivo adjunto y responde siguiendo tus instrucciones.";

    private readonly IAiProviderClient _client;
    private readonly IPdfRasterizer? _rasterizer;

    public AgentMediaReader(IAiProviderClient client, IPdfRasterizer? rasterizer)
    {
        _client = client;
        _rasterizer = rasterizer;
    }

    /// <summary>Prompt del lector: el propio del agente o, si no tiene, el generico.</summary>
    public static string ReaderPromptFor(string? agentPrompt) =>
        string.IsNullOrWhiteSpace(agentPrompt) ? GenericReaderPrompt : agentPrompt.Trim();

    /// <summary>Proveedores que aceptan el PDF tal cual (el resto lo recibe rasterizado).</summary>
    public static bool ReadsPdfNatively(AiProvider provider) => provider is AiProvider.Gemini or AiProvider.Claude;

    /// <summary>Proveedores que transcriben audio (generateContent con inlineData).</summary>
    public static bool HearsAudio(AiProvider provider) => provider == AiProvider.Gemini;

    /// <summary>
    /// Con que se transcriben las notas de voz: el modelo del agente si su proveedor oye audio; si no, el
    /// TRANSCRIPTOR DE PLATAFORMA (cuenta de Gemini habilitada + su modelo de transcripcion, por defecto
    /// <see cref="DefaultTranscriptionModel"/>, nunca el modelo del agente). Null = no hay con que transcribir.
    /// </summary>
    public static AgentMediaModel? PickTranscriber(AgentMediaModel agentModel, string? geminiApiKey, string? geminiBaseUrl,
        string? transcriptionModel)
    {
        if (HearsAudio(agentModel.Provider)) { return agentModel; }
        if (string.IsNullOrWhiteSpace(geminiApiKey)) { return null; }
        var model = string.IsNullOrWhiteSpace(transcriptionModel) ? DefaultTranscriptionModel : transcriptionModel.Trim();
        return new AgentMediaModel(AiProvider.Gemini, geminiApiKey, geminiBaseUrl, model);
    }

    public Task<AiChatResult> ReadImageAsync(AgentMediaModel m, string? agentPrompt, string imageBase64, string? imageMime,
        CancellationToken ct)
    {
        var content = new List<AiVisionPart>
        {
            new(Text: ReadInstruction),
            new(ImageBase64: imageBase64, ImageMime: string.IsNullOrWhiteSpace(imageMime) ? "image/jpeg" : imageMime!)
        };
        return _client.CompleteVisionAsync(m.Provider, m.ApiKey, m.BaseUrl, m.Model, ReaderPromptFor(agentPrompt), content, ct);
    }

    /// <summary>Lee un PDF: nativo si el proveedor lo acepta; si no, rasterizado a imagenes. Null si no se puede
    /// (sin rasterizador o PDF sin paginas). Un PDF protegido/corrupto lanza desde el rasterizador: el llamador
    /// lo trata como best-effort.</summary>
    public async Task<AiChatResult?> ReadDocumentAsync(AgentMediaModel m, string? agentPrompt, string docBase64, string? docMime,
        CancellationToken ct)
    {
        var prompt = ReaderPromptFor(agentPrompt);
        if (ReadsPdfNatively(m.Provider))
        {
            var native = new List<AiVisionPart>
            {
                new(Text: ReadInstruction),
                new(DocBase64: docBase64, DocMime: string.IsNullOrWhiteSpace(docMime) ? "application/pdf" : docMime!)
            };
            return await _client.CompleteVisionAsync(m.Provider, m.ApiKey, m.BaseUrl, m.Model, prompt, native, ct);
        }

        if (_rasterizer is null) { return null; }
        var pages = _rasterizer.Rasterizar(Convert.FromBase64String(docBase64), PdfDpi, MaxPdfPages, ct);
        if (pages.Count == 0) { return null; }
        var content = new List<AiVisionPart>
        {
            new(Text: pages.Count == 1
                ? ReadInstruction + " Es un documento PDF de 1 pagina, convertido a imagen."
                : $"{ReadInstruction} Es un documento PDF; van sus primeras {pages.Count} paginas como imagenes, en orden.")
        };
        content.AddRange(pages.Select(p => new AiVisionPart(ImageBase64: Convert.ToBase64String(p.Image), ImageMime: p.Mime)));
        return await _client.CompleteVisionAsync(m.Provider, m.ApiKey, m.BaseUrl, m.Model, prompt, content, ct);
    }

    /// <summary>Transcribe una nota de voz (wav/mp3) con el modelo dado; debe ser un proveedor que oiga audio.</summary>
    public Task<AiChatResult> TranscribeAsync(AgentMediaModel m, string audioBase64, string audioMime, CancellationToken ct)
    {
        var content = new List<AiVisionPart>
        {
            new(Text: "Transcribe la nota de voz."),
            new(AudioBase64: audioBase64, AudioMime: audioMime)
        };
        return _client.CompleteVisionAsync(m.Provider, m.ApiKey, m.BaseUrl, m.Model, TranscriberPrompt, content, ct);
    }
}
