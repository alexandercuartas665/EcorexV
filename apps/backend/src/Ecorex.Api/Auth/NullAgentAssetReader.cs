using Ecorex.Application.Tenancy;

namespace Ecorex.Api.Auth;

/// <summary>
/// Implementacion no-op de IAgentAssetReader para el host Api: este host no sirve
/// los archivos de agentes (viven en el wwwroot de la consola), asi que no puede
/// resolver URLs locales a contenido. Devuelve null y el flujo de agentes envia
/// el mensaje sin adjunto. Sin este registro, ValidateOnBuild impide arrancar el Api
/// porque AgentConversationService depende de IAgentAssetReader.
/// </summary>
public sealed class NullAgentAssetReader : IAgentAssetReader
{
    public Task<string?> ReadBase64Async(string? localUrl, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);
}
