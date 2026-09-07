namespace Ecorex.Application.Workflows;

/// <summary>
/// Costura (ADR-0091) para que un agente de FLUJO -que vive en la capa Application- use un cliente COLMENA
/// (el navegador on-prem, cuyo canal vive en SuperAdmin) SIN que Application dependa de SuperAdmin. La
/// implementacion real (SuperAdmin) navega a una URL con el cliente indicado y devuelve su contenido
/// legible; el default no-op dice que no esta disponible. NUNCA lanza: un cliente offline o un timeout
/// vienen como Ok=false con motivo legible, para que el agente los trate como "no consegui el dato".
/// </summary>
public interface IAgentBrowserFetch
{
    /// <summary>Navega con el cliente Colmena <paramref name="clientId"/> (Guid del DataClient del tenant) a
    /// <paramref name="url"/> y devuelve el contenido legible (opcionalmente acotado a un <paramref name="selector"/>
    /// CSS). <paramref name="sessionKey"/> reusa un perfil logueado (ej. "linkedin"); null = sesion efimera.</summary>
    Task<AgentBrowserFetchResult> FetchAsync(
        Guid clientId, string? sessionKey, string url, string? selector, Guid tenantId,
        CancellationToken cancellationToken = default);
}

/// <summary>Resultado de una busqueda web del agente: el contenido legible o el motivo por el que no se pudo.</summary>
public sealed record AgentBrowserFetchResult(bool Ok, string? Content, string? Error)
{
    public static AgentBrowserFetchResult Fail(string error) => new(false, null, error);
    public static AgentBrowserFetchResult Content_(string content) => new(true, content, null);
}

/// <summary>Default cuando NO hay canal de navegador (ej. host de solo API): la busqueda web no esta
/// disponible. La implementacion real la reemplaza en el host que corre el agente (SuperAdmin).</summary>
public sealed class NoOpAgentBrowserFetch : IAgentBrowserFetch
{
    public Task<AgentBrowserFetchResult> FetchAsync(
        Guid clientId, string? sessionKey, string url, string? selector, Guid tenantId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(AgentBrowserFetchResult.Fail("La busqueda web (Colmena) no esta disponible en este contexto."));
}
