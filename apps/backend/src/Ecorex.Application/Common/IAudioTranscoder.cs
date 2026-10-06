namespace Ecorex.Application.Common;

/// <summary>
/// Transcodifica audio a WAV PCM para los proveedores de IA que solo aceptan wav/mp3 (Gemini). WhatsApp
/// entrega las notas de voz en OGG/opus, que esos endpoints rechazan (HTTP 400). La implementacion (ffmpeg,
/// en Infrastructure) detecta el formato por CONTENIDO, no por extension. BEST-EFFORT: devuelve null si no
/// se puede convertir (el llamador cae al fallback y nunca deja al agente mudo).
/// </summary>
public interface IAudioTranscoder
{
    /// <summary>Convierte <paramref name="input"/> (p.ej. ogg/opus) a WAV PCM. Devuelve null si falla.</summary>
    Task<byte[]?> ToWavAsync(byte[] input, string? sourceMime, CancellationToken cancellationToken = default);
}
