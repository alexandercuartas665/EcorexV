using Ecorex.Application.Tenancy;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>
/// Lee los recursos del agente desde wwwroot (donde se guardan los uploads) y los devuelve en base64
/// para enviarlos por WhatsApp. Implementacion del host para IAgentAssetReader.
/// </summary>
public sealed class WebRootAgentAssetReader(IWebHostEnvironment env) : IAgentAssetReader
{
    public async Task<string?> ReadBase64Async(string? localUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(localUrl)) { return null; }
        var rel = localUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var path = Path.Combine(env.WebRootPath, rel);
        if (!File.Exists(path)) { return null; }
        return Convert.ToBase64String(await File.ReadAllBytesAsync(path, cancellationToken));
    }
}
