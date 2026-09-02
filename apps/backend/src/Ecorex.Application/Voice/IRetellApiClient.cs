namespace Ecorex.Application.Voice;

/// <summary>
/// Cliente HTTP de Retell. La API key del tenant se entrega DESENCRIPTADA por llamada (nunca se guarda en
/// el cliente ni se loguea). Todos los metodos devuelven resultados sin throw (4xx/5xx/red -> Ok=false +
/// StatusCode) para que la orquestacion diferencie transitorios. La implementacion vive en Infrastructure
/// y se registra con AddHttpClient (patron de YCloudApiClient).
/// </summary>
public interface IRetellApiClient
{
    /// <summary>Crea un Retell LLM (Response Engine) con general_prompt = el prompt de ECOREX.</summary>
    Task<RetellCreateLlmResult> CreateLlmAsync(string apiKey, RetellCreateLlmRequest request, CancellationToken cancellationToken = default);

    /// <summary>Crea un agente Retell ligado a un LLM.</summary>
    Task<RetellCreateAgentResult> CreateAgentAsync(string apiKey, RetellCreateAgentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Coloca una llamada saliente (POST /v2/create-phone-call).</summary>
    Task<RetellCreateCallResult> CreatePhoneCallAsync(string apiKey, RetellCreateCallRequest request, CancellationToken cancellationToken = default);

    /// <summary>Importa un numero propio (Telnyx) a Retell.</summary>
    Task<RetellImportNumberResult> ImportPhoneNumberAsync(string apiKey, RetellImportNumberRequest request, CancellationToken cancellationToken = default);

    /// <summary>Validacion liviana de la key (sin colocar llamadas ni costo).</summary>
    Task<RetellPingResult> PingAsync(string apiKey, CancellationToken cancellationToken = default);
}
