namespace Ecorex.Application.Tenancy;

/// <summary>
/// Contexto AMBIENTAL (AsyncLocal) de una ejecucion de inferencia del agente. Lleva, para las herramientas
/// que lo necesiten, la conversacion en curso y/o una imagen pendiente de analizar (sandbox/emulador), sin
/// cambiar la firma de todos los toolsets. Lo fija el motor (AiInferenceService) antes del bucle de
/// herramientas y lo limpia al terminar. La usa el toolset de medidas de cabello.
/// </summary>
public static class AiToolRunContext
{
    private sealed record Scope(Guid? ConversationId, string? ImageBase64, string? ImageMime);
    private static readonly AsyncLocal<Scope?> _current = new();

    public static Guid? ConversationId => _current.Value?.ConversationId;
    public static string? ImageBase64 => _current.Value?.ImageBase64;
    public static string? ImageMime => _current.Value?.ImageMime;

    public static IDisposable Begin(Guid? conversationId, string? imageBase64, string? imageMime)
    {
        var previous = _current.Value;
        _current.Value = new Scope(conversationId, imageBase64, imageMime);
        return new Resetter(previous);
    }

    private sealed class Resetter(Scope? previous) : IDisposable
    {
        public void Dispose() => _current.Value = previous;
    }
}
