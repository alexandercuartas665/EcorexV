namespace Ecorex.Application.Tenancy;

/// <summary>
/// Contexto AMBIENTAL (AsyncLocal) de una ejecucion de inferencia del agente. Lleva, para las herramientas
/// que lo necesiten, la conversacion en curso y/o una imagen pendiente de analizar (sandbox/emulador), sin
/// cambiar la firma de todos los toolsets. Lo fija el motor (AiInferenceService) antes del bucle de
/// herramientas y lo limpia al terminar. La usa el toolset de medidas de cabello.
/// </summary>
public static class AiToolRunContext
{
    /// <summary>Archivo ya almacenado (con URL) pendiente de adjuntar por una herramienta (ej. crear_tarea).
    /// Lo usa la herramienta de pruebas del agente para simular "el cliente envio un archivo".</summary>
    public sealed record PendingAttachment(string Url, string FileName, string? MimeType);

    private sealed record Scope(Guid? ConversationId, string? ImageBase64, string? ImageMime, IReadOnlyList<PendingAttachment>? Attachments, IReadOnlyList<Guid>? AllowedBoardIds, Guid? AgentId)
    {
        /// <summary>Resultados de cierre ya producidos en ESTE turno, por herramienta (crear_tarea /
        /// crear_actividad). Guardia intra-turno de idempotencia (ADR-0101): una segunda llamada a la misma
        /// herramienta en el mismo turno devuelve este resultado en vez de crear otra tarea. Se descarta con
        /// el Scope al terminar el turno.</summary>
        public Dictionary<string, string> TurnResults { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
    private static readonly AsyncLocal<Scope?> _current = new();

    /// <summary>Hay un turno de agente en curso (contexto activo).</summary>
    public static bool IsActive => _current.Value is not null;

    /// <summary>Resultado de cierre ya emitido por esta herramienta en el turno actual, o null. Ver TurnResults.</summary>
    public static string? TryGetTurnResult(string toolKey)
        => _current.Value is { } s && s.TurnResults.TryGetValue(toolKey, out var v) ? v : null;

    /// <summary>Recuerda el resultado de cierre de una herramienta para el resto del turno (idempotencia intra-turno).</summary>
    public static void SetTurnResult(string toolKey, string resultJson)
    {
        if (_current.Value is { } s) { s.TurnResults[toolKey] = resultJson; }
    }

    public static Guid? ConversationId => _current.Value?.ConversationId;
    public static string? ImageBase64 => _current.Value?.ImageBase64;
    public static string? ImageMime => _current.Value?.ImageMime;
    public static IReadOnlyList<PendingAttachment>? PendingAttachments => _current.Value?.Attachments;

    /// <summary>Whitelist de tableros permitidos para el agente en curso (board ids). Null o vacio = sin
    /// restriccion (todos los tableros del tenant). La consume TasksToolset (crear_tarea / listar_tableros).</summary>
    public static IReadOnlyList<Guid>? AllowedBoardIds => _current.Value?.AllowedBoardIds;

    /// <summary>Id del AiAgent en ejecucion. Lo usa ActividadesToolset para registrar al agente como autor
    /// del formulario que llena (executedByAiAgentId en IFormResponseService.SaveAsync).</summary>
    public static Guid? AgentId => _current.Value?.AgentId;

    public static IDisposable Begin(Guid? conversationId, string? imageBase64, string? imageMime,
        IReadOnlyList<PendingAttachment>? attachments = null, IReadOnlyList<Guid>? allowedBoardIds = null,
        Guid? agentId = null)
    {
        var previous = _current.Value;
        _current.Value = new Scope(conversationId, imageBase64, imageMime, attachments, allowedBoardIds, agentId);
        return new Resetter(previous);
    }

    private sealed class Resetter(Scope? previous) : IDisposable
    {
        public void Dispose() => _current.Value = previous;
    }
}
