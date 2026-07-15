namespace Ecorex.Contracts.Agent;

/// <summary>
/// Contrato del canal SignalR entre el servidor (la app web) y el agente on-prem (doc 02).
/// Vive en la libreria de contratos porque es la fuente de verdad del protocolo: el CLIENTE
/// (este agente) y el futuro HUB del backend deben coincidir en ruta, nombres de metodo y forma
/// de los mensajes. El agente solo depende de estos contratos; NUNCA de internals del backend.
/// </summary>
public static class AgentProtocol
{
    /// <summary>Version del protocolo (doc 02 s11). El servidor puede rechazar agentes por debajo.</summary>
    public const string Version = "1.0";

    /// <summary>Ruta del hub en el servidor (doc 02 s1): wss://&lt;host&gt;/hubs/agente.</summary>
    public const string HubRoute = "/hubs/agente";
}

/// <summary>
/// Nombres de los metodos del canal (SignalR usa strings). Centralizados para que cliente y hub
/// no diverjan. "ServerToClient" = el servidor los invoca en el agente (push). "ClientToServer" =
/// el agente los invoca en el servidor.
/// </summary>
public static class AgentHubMethods
{
    // Servidor -> agente (push por el grupo client:{clientId}) - doc 02 s4.
    public const string FetchRequest = "FetchRequest";
    public const string Ping = "Ping";
    public const string Cancel = "Cancel";

    // Agente -> servidor - doc 02 s3.
    public const string AgentHello = "AgentHello";
    public const string FetchResult = "FetchResult";
    public const string FetchFailed = "FetchFailed";
    public const string Heartbeat = "Heartbeat";
}

/// <summary>Saludo del agente al conectar (doc 02 s5): version, host y capacidades.</summary>
public sealed record AgentHelloMsg(
    string ClientId,
    string AgentVersion,
    string ProtocolVersion,
    string Host,
    string Os,
    string[] Capabilities);

/// <summary>Fuente que el agente debe consultar (doc 02 s5). Kind: Database | RestApi.</summary>
public sealed record ConnectorSpec(
    string Kind,
    string? DbEngine = null,
    string? Host = null,
    int? Port = null,
    string? Database = null,
    string? Username = null,
    string? SecretRef = null);

/// <summary>Consulta a ejecutar (doc 02 s5). En la Ola B NO se ejecuta (eso es Ola C).</summary>
public sealed record QuerySpec(
    string Text,
    Dictionary<string, string?>? Params = null,
    int TimeoutSeconds = 60);

/// <summary>Paginacion opcional (doc 02 s5).</summary>
public sealed record PagingSpec(string Mode = "None", int PageSize = 500, int MaxRows = 100000);

/// <summary>Orden "traeme estos datos" empujada por el servidor (doc 02 s5).</summary>
public sealed record FetchRequestMsg(
    string CorrelationId,
    string TenantId,
    ConnectorSpec Connector,
    QuerySpec Query,
    PagingSpec? Paging = null);

/// <summary>Respuesta con datos (posiblemente en chunks) del agente al servidor (doc 02 s5).</summary>
public sealed record FetchResultMsg(
    string CorrelationId,
    int ChunkIndex,
    bool IsLast,
    string[]? Fields,
    List<Dictionary<string, string?>> Rows,
    int RowCount);

/// <summary>Reporte de que el agente no pudo ejecutar la orden (doc 02 s5).</summary>
public sealed record FetchErrorMsg(
    string CorrelationId,
    string Code,
    string Message,
    bool Retryable);

/// <summary>
/// Handshake opcion A (doc 02 s2): el agente pide un token corto probando la posesion del secreto
/// del <c>DataClient</c> con un HMAC de (clientId|ts|nonce). <c>Ts</c> = segundos unix UTC.
/// </summary>
public sealed record AgentTokenRequest(string ClientId, long Ts, string Nonce, string Hmac);

/// <summary>Respuesta del endpoint de token: JWT corto para conectar al hub.</summary>
public sealed record AgentTokenResponse(string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>
/// HMAC compartido del handshake (misma implementacion en agente y servidor para no divergir):
/// hex minusculas de HMAC-SHA256(secret, "clientId|ts|nonce").
/// </summary>
public static class AgentHmac
{
    public static string Canonical(string clientId, long ts, string nonce) => $"{clientId}|{ts}|{nonce}";

    public static string Compute(string secret, string clientId, long ts, string nonce)
    {
        using var mac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret));
        var hash = mac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Canonical(clientId, ts, nonce)));
        return System.Convert.ToHexString(hash).ToLowerInvariant();
    }
}
