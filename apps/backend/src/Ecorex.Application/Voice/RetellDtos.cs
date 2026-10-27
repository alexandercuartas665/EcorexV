namespace Ecorex.Application.Voice;

// Tipos request/response del API de Retell (https://api.retellai.com). Todos los resultados son "sin throw":
// el cliente mapea 4xx/5xx/red a (Ok=false, Error, StatusCode) para que la orquestacion decida (no reintentar
// 4xx, reintentar solo 5xx/red). Nunca contienen secretos.

/// <summary>POST /create-retell-llm. general_prompt = el prompt COMPUESTO por ECOREX (reemplaza el del agente).</summary>
public sealed record RetellCreateLlmRequest(
    string GeneralPrompt,
    string StartSpeaker = "agent",
    string? Model = null,
    string? BeginMessage = null);

public sealed record RetellCreateLlmResult(bool Ok, string? LlmId, string? Error, int StatusCode);

/// <summary>POST /create-agent. response_engine = { type: "retell-llm", llm_id }.</summary>
public sealed record RetellCreateAgentRequest(
    string LlmId,
    string VoiceId,
    string? Language = null,
    string? AgentName = null);

public sealed record RetellCreateAgentResult(bool Ok, string? AgentId, string? Error, int StatusCode);

/// <summary>POST /v2/create-phone-call. Coloca la llamada saliente.</summary>
public sealed record RetellCreateCallRequest(
    string FromNumber,
    string ToNumber,
    string? OverrideAgentId = null,
    IReadOnlyDictionary<string, string>? DynamicVariables = null,
    IReadOnlyDictionary<string, string>? CustomSipHeaders = null,
    IReadOnlyDictionary<string, string>? Metadata = null,
    bool IgnoreE164Validation = false);

public sealed record RetellCreateCallResult(bool Ok, string? CallId, string? AgentId, string? CallStatus, string? Error, int StatusCode);

/// <summary>POST /import-phone-number (Telnyx). Importa el numero propio a Retell.</summary>
public sealed record RetellImportNumberRequest(
    string PhoneNumber,
    string TerminationUri,
    string? SipTrunkAuthUsername = null,
    string? SipTrunkAuthPassword = null,
    IReadOnlyList<RetellAgentWeight>? OutboundAgents = null,
    IReadOnlyList<RetellAgentWeight>? InboundAgents = null);

/// <summary>{ agent_id, weight } (los weights de la lista suman 1).</summary>
public sealed record RetellAgentWeight(string AgentId, double Weight);

public sealed record RetellImportNumberResult(bool Ok, string? PhoneNumber, string? Error, int StatusCode);

/// <summary>Resultado de una validacion liviana de la key (ej. listar agentes) sin colocar llamadas.</summary>
public sealed record RetellPingResult(bool Ok, string? Error, int StatusCode);

/// <summary>GET /get-agent/{id}: config del agente (voz/idioma/llm). El prompt vive en el LLM (ver GetLlm).</summary>
public sealed record RetellGetAgentResult(
    bool Ok, string? AgentId, string? AgentName, string? VoiceId, string? Language, string? LlmId, string? Error, int StatusCode);

/// <summary>GET /get-retell-llm/{id}: el general_prompt y el modelo del Response Engine.</summary>
public sealed record RetellGetLlmResult(
    bool Ok, string? LlmId, string? GeneralPrompt, string? Model, string? Error, int StatusCode);

/// <summary>GET /v2/get-call/{id}: estado+resultado de la llamada (el objeto "call" viene al root). RawCall es
/// el JSON de ese objeto para que el servicio lo vuelque a la VoiceCall (igual que haria el webhook). Es el
/// FALLBACK de sondeo cuando el webhook de Retell no llega (ej. dev sin URL publica para el webhook).</summary>
public sealed record RetellGetCallResult(
    bool Ok, string? RawCall, string? CallStatus, string? Error, int StatusCode);
