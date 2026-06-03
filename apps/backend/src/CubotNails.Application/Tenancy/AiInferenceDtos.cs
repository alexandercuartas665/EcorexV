using CubotNails.Domain.Enums;

namespace CubotNails.Application.Tenancy;

/// <summary>
/// Un turno de la conversacion de prueba. Role: "user" (cliente) o "model" (agente).
/// Attachments es opcional: lleva los recursos que el agente adjunto en ese turno (cuando es model).
/// El proveedor de IA ignora este campo; solo se usa para que el extractor de cache vea TODO el contexto.
/// </summary>
public sealed record AiChatTurn(string Role, string Text, IReadOnlyList<AiChatAttachment>? Attachments = null);

/// <summary>Recurso que el agente decidio entregar en el chat (imagen, video, pdf, ubicacion o texto).</summary>
public sealed record AiChatAttachment(string Name, AgentResourceType ResourceType, string? FileUrl, string? FileName, string? Detail);

/// <summary>
/// Entrada del log de depuracion de prompts. El motor agrega una entrada por cada llamada al
/// proveedor de IA (prompt principal del agente, extractor de cache de datos, etc.) con su
/// fecha/hora, el contenido enviado y, opcionalmente, la respuesta del LLM (util para ver lo
/// que devolvio el extractor de cache antes de parsearlo).
/// </summary>
public sealed record AiDebugPrompt(string Title, DateTimeOffset SentAt, string Content, string? Response = null);

/// <summary>Resultado de una llamada de inferencia, con el consumo de tokens y los recursos a adjuntar.</summary>
/// <param name="DebugPrompts">
/// Log de los prompts enviados a la IA en esta vuelta (uno o mas, en orden cronologico). Util
/// para depurar el chat de prueba; en chat real conviene no enviarlo al cliente final.
/// </param>
public sealed record AiChatResult(bool Ok, string? Text, string? Error, int InputTokens = 0, int OutputTokens = 0,
    IReadOnlyList<AiChatAttachment>? Attachments = null, IReadOnlyList<AiDebugPrompt>? DebugPrompts = null);

/// <summary>
/// Cliente HTTP que habla con cada proveedor de IA (Gemini, OpenAI/ChatGPT, DeepSeek, Claude).
/// Recibe la API key ya descifrada; no persiste ni loggea secretos.
/// </summary>
public interface IAiProviderClient
{
    Task<AiChatResult> CompleteAsync(
        AiProvider provider,
        string apiKey,
        string? baseUrl,
        string model,
        string systemPrompt,
        IReadOnlyList<AiChatTurn> turns,
        CancellationToken cancellationToken = default);
}

/// <summary>Inferencia de agentes del tenant: arma el prompt con la config del agente y llama al proveedor.</summary>
public interface IAiInferenceService
{
    /// <summary>
    /// Ejecuta una conversacion de prueba contra el agente indicado. Usa la API key/proveedor/modelo
    /// configurados por la plataforma. systemPromptOverride permite probar un prompt aun sin guardar.
    /// </summary>
    Task<AiChatResult> TestChatAsync(Guid agentId, IReadOnlyList<AiChatTurn> turns, string? systemPromptOverride = null, CancellationToken cancellationToken = default);
}
