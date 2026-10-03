using System.Diagnostics;
using Ecorex.Application.Common;
using Microsoft.Extensions.Logging;

namespace Ecorex.Infrastructure.Media;

/// <summary>
/// <see cref="IAudioTranscoder"/> por ffmpeg (binario del sistema, instalado en la imagen SuperAdmin). Escribe
/// la entrada a un archivo temporal, invoca ffmpeg para producir un WAV PCM 16 kHz mono (liviano y suficiente
/// para STT) y devuelve los bytes. ffmpeg detecta el formato de entrada por CONTENIDO, asi que sirve igual
/// para ogg/opus (WhatsApp), m4a, etc. BEST-EFFORT: ante cualquier fallo devuelve null (el agente cae al
/// fallback y nunca queda mudo). Siempre limpia los temporales.
/// </summary>
public sealed class FfmpegAudioTranscoder : IAudioTranscoder
{
    private readonly ILogger<FfmpegAudioTranscoder> _logger;

    public FfmpegAudioTranscoder(ILogger<FfmpegAudioTranscoder> logger) => _logger = logger;

    public async Task<byte[]?> ToWavAsync(byte[] input, string? sourceMime, CancellationToken cancellationToken = default)
    {
        if (input is null || input.Length == 0) { return null; }

        var dir = Path.Combine(Path.GetTempPath(), "ecorex-audio");
        Directory.CreateDirectory(dir);
        var inPath = Path.Combine(dir, $"in-{Guid.NewGuid():N}");
        var outPath = Path.Combine(dir, $"out-{Guid.NewGuid():N}.wav");

        try
        {
            await File.WriteAllBytesAsync(inPath, input, cancellationToken);

            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            // -nostdin: no se cuelga esperando entrada; -y: sobreescribe; salida WAV PCM 16kHz mono.
            foreach (var a in new[] { "-nostdin", "-y", "-i", inPath, "-ar", "16000", "-ac", "1", "-f", "wav", outPath })
            {
                psi.ArgumentList.Add(a);
            }

            using var proc = Process.Start(psi);
            if (proc is null) { return null; }

            // Tope de seguridad: una nota de voz no deberia tardar; evita procesos colgados.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            var stderr = await proc.StandardError.ReadToEndAsync(cts.Token);
            await proc.WaitForExitAsync(cts.Token);

            if (proc.ExitCode != 0 || !File.Exists(outPath))
            {
                _logger.LogWarning("ffmpeg no pudo transcodificar el audio (mime {Mime}, exit {Code}): {Err}",
                    sourceMime, proc.ExitCode, stderr);
                return null;
            }

            var wav = await File.ReadAllBytesAsync(outPath, cancellationToken);
            return wav.Length > 0 ? wav : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallo la transcodificacion de audio (mime {Mime}).", sourceMime);
            return null;
        }
        finally
        {
            try { if (File.Exists(inPath)) { File.Delete(inPath); } } catch { /* limpieza best-effort */ }
            try { if (File.Exists(outPath)) { File.Delete(outPath); } } catch { /* limpieza best-effort */ }
        }
    }
}
