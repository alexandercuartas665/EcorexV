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

    private sealed record Scope(Guid? ConversationId, string? ImageBase64, string? ImageMime, IReadOnlyList<PendingAttachment>? Attachments);
    private static readonly AsyncLocal<Scope?> _current = new();

    public static Guid? ConversationId => _current.Value?.ConversationId;
    public static string? ImageBase64 => _current.Value?.ImageBase64;
    public static string? ImageMime => _current.Value?.ImageMime;
    public static IReadOnlyList<PendingAttachment>? PendingAttachments => _current.Value?.Attachments;

    public static IDisposable Begin(Guid? conversationId, string? imageBase64, string? imageMime,
        IReadOnlyList<PendingAttachment>? attachments = null)
    {
        var previous = _current.Value;
        _current.Value = new Scope(conversationId, imageBase64, imageMime, attachments);
        return new Resetter(previous);
    }

    private sealed class Resetter(Scope? previous) : IDisposable
    {
        public void Dispose() => _current.Value = previous;
    }
}
