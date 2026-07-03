namespace Ecorex.Application.Tenancy;

/// <summary>
/// Lee un recurso del agente (imagen/video/audio/pdf) a base64 para enviarlo por WhatsApp.
/// La implementacion vive en el host que sirve los archivos (SuperAdmin, sobre wwwroot); asi la capa
/// Application no depende del sistema de archivos ni del web root.
/// </summary>
public interface IAgentAssetReader
{
    /// <summary>Devuelve el contenido en base64 del recurso ubicado en la URL local (ej. /uploads/agents/x.png), o null si no existe.</summary>
    Task<string?> ReadBase64Async(string? localUrl, CancellationToken cancellationToken = default);
}
