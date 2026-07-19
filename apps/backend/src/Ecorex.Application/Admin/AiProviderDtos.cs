using Ecorex.Domain.Enums;

namespace Ecorex.Application.Admin;

public sealed record AiProviderDto(
    AiProvider Provider,
    string DisplayName,
    string? Model,
    string? BaseUrl,
    string? ApiKeyMasked,
    bool HasApiKey,
    bool IsEnabled,
    string DefaultModel,
    IReadOnlyList<string> SuggestedModels);

public sealed record SaveAiProviderRequest(
    AiProvider Provider,
    string? ApiKey,
    string? Model,
    string? BaseUrl,
    bool IsEnabled);

/// <summary>Proveedor disponible para que una agencia lo use en sus agentes (sin datos sensibles).
/// <paramref name="ConfigId"/> es el Id de la fila del Super Admin: sirve como referencia estable cuando
/// un modulo (p.ej. Extraccion de datos) necesita fijar EXACTAMENTE cual proveedor eligio el operador.</summary>
public sealed record AiProviderOptionDto(AiProvider Provider, string DisplayName, string DefaultModel, Guid ConfigId);

/// <summary>
/// Cuentas maestras de IA de la plataforma (Super Admin). Un registro por proveedor; la API key
/// se cifra y nunca se devuelve en claro. Las agencias usan los proveedores habilitados en sus agentes.
/// </summary>
public interface IAiServerConfigService
{
    Task<IReadOnlyList<AiProviderDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<AiProviderDto> SaveAsync(SaveAiProviderRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Proveedores habilitados (con API key) que las agencias pueden elegir en sus agentes.</summary>
    Task<IReadOnlyList<AiProviderOptionDto>> ListEnabledAsync(CancellationToken cancellationToken = default);
}

/// <summary>Metadata estatica de cada proveedor (nombre visible, modelo por defecto, modelos sugeridos).</summary>
public static class AiProviderCatalog
{
    public sealed record Meta(string DisplayName, string DefaultModel, IReadOnlyList<string> Models, string? DefaultBaseUrl);

    public static Meta For(AiProvider p) => p switch
    {
        AiProvider.Claude => new("Anthropic Claude", "claude-opus-4-8",
            new[] { "claude-opus-4-8", "claude-sonnet-5", "claude-haiku-4-5" }, "https://api.anthropic.com"),
        AiProvider.Gemini => new("Google Gemini", "gemini-2.5-pro",
            new[] { "gemini-2.5-pro", "gemini-2.5-flash", "gemini-2.0-flash" }, "https://generativelanguage.googleapis.com"),
        AiProvider.ChatGpt => new("OpenAI ChatGPT", "gpt-4o",
            new[] { "gpt-4o", "gpt-4o-mini", "o3", "o3-mini" }, "https://api.openai.com/v1"),
        AiProvider.DeepSeek => new("DeepSeek", "deepseek-chat",
            new[] { "deepseek-chat", "deepseek-reasoner" }, "https://api.deepseek.com"),
        _ => new(p.ToString(), "", Array.Empty<string>(), null)
    };
}
