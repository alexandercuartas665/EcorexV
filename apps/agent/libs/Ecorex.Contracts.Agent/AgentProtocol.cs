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

    // Sub-agente Navegador (doc 06 s3.2 + prior-art doc 07). Servidor -> agente: BrowserRequest.
    // Agente -> servidor: BrowserResult.
    public const string BrowserRequest = "BrowserRequest";
    public const string BrowserResult = "BrowserResult";

    // Sub-agente Archivos (doc 06 s3.2). Servidor -> agente: FileRequest. Agente -> servidor: FileResult.
    public const string FileRequest = "FileRequest";
    public const string FileResult = "FileResult";
}

/// <summary>Saludo del agente al conectar (doc 02 s5): version, host y capacidades.</summary>
public sealed record AgentHelloMsg(
    string ClientId,
    string AgentVersion,
    string ProtocolVersion,
    string Host,
    string Os,
    string[] Capabilities);

/// <summary>
/// Fuente que el agente debe consultar (doc 02 s5). Kind: Database | RestApi.
///
/// <see cref="Secret"/> es la credencial de la FUENTE en claro dentro del mensaje: el servidor la
/// descifra de `DataConnector.CredentialsEncrypted` y la manda (ADR-0040, opcion a). Si viene null,
/// el agente cae a su cadena de conexion LOCAL (opcion b, como la Ola C; sigue disponible).
///
/// Que la credencial VIAJE es exactamente por lo que el canal debe ser wss/TLS estricto en
/// produccion (Ola 6): por aqui pasa la contrasena de la base de datos del cliente.
/// </summary>
public sealed record ConnectorSpec(
    string Kind,
    string? DbEngine = null,
    string? Host = null,
    int? Port = null,
    string? Database = null,
    string? Username = null,
    string? SecretRef = null,
    string? Secret = null);

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
/// Servidor -> agente (doc 02 s4/s10): aborta el <c>FetchRequest</c> en curso con este
/// <c>CorrelationId</c>. Lo manda el servidor cuando ya no le interesa el resultado (vencio el plazo,
/// o alguien cancelo a mano): sin esto, el agente seguiria consultando la BD y enviando chunks al
/// vacio. Es best-effort: si la consulta ya termino o el correlationId no esta en curso, no hace nada.
/// </summary>
public sealed record CancelMsg(
    string CorrelationId,
    string? Reason = null);

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

/// <summary>
/// Firma del JS que el servidor inyecta en el navegador (doc 06 s4). HMAC-SHA256 del secreto del
/// cliente sobre "correlationId|payload" (hex minusculas). Ligar al correlationId evita reusar una
/// firma en otra orden (versionado/anti-replay ligero). Misma implementacion en agente y servidor.
/// </summary>
public static class AgentSign
{
    public static string SignJs(string secret, string correlationId, string payload)
    {
        using var mac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret));
        var hash = mac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(correlationId + "|" + payload));
        return System.Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Verifica en tiempo constante que <paramref name="signature"/> corresponda al payload.</summary>
    public static bool Verify(string secret, string correlationId, string payload, string? signature)
    {
        if (string.IsNullOrEmpty(signature)) { return false; }
        var expected = SignJs(secret, correlationId, payload);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(expected), System.Text.Encoding.UTF8.GetBytes(signature));
    }
}

// ---- Sub-agente Navegador (doc 06 s3.2 + prior-art doc 07: catalogo browser.*) ----

/// <summary>
/// Accion TIPADA del sub-agente Navegador. NO es "ejecuta lo que sea": cada accion es acotada
/// (doc 06 s4). Segun <see cref="Kind"/> se usan unos campos u otros.
/// </summary>
public enum BrowserActionKind
{
    /// <summary>Abre una URL http/https (sujeta a la allow-list de dominios LOCAL del agente).</summary>
    Navigate,

    /// <summary>Ejecuta JavaScript en la pagina actual y devuelve el resultado (acotado por dominio).</summary>
    Eval,

    /// <summary>Espera unos ms, o hasta que una condicion JS sea truthy.</summary>
    Wait,

    /// <summary>Captura el navegador (PNG en base64).</summary>
    Screenshot,

    /// <summary>Devuelve el HTML de la pagina o de un selector CSS.</summary>
    Html,

    /// <summary>Ejecuta un guion "MouseBot" (JSON de pasos: click/type por selector) acotado al dominio.</summary>
    Mouse,

    /// <summary>Devuelve el historial reciente de descargas del navegador.</summary>
    Downloads,
}

/// <summary>
/// Una accion del navegador. Los campos aplicables dependen de <see cref="Kind"/>. Las acciones que
/// inyectan JS del SERVIDOR (Eval, Mouse, Wait con condicion) deben venir FIRMADAS en
/// <see cref="Signature"/> (doc 06 s4: JS firmado por el servidor); el agente las rechaza si la firma
/// no cuadra. Las ordenes por MCP local (loopback) van sin firma (confianza local).
/// </summary>
public sealed record BrowserAction(
    BrowserActionKind Kind,
    string? Url = null,
    string? Script = null,
    int? WaitMs = null,
    string? ConditionScript = null,
    string? Selector = null,
    string? ScriptJson = null,
    bool Screenshot = false,
    string? Signature = null);

/// <summary>Orden del servidor: una secuencia de acciones tipadas para el sub-agente Navegador.</summary>
public sealed record BrowserRequestMsg(
    string CorrelationId,
    string TenantId,
    IReadOnlyList<BrowserAction> Actions);

/// <summary>Resultado de una accion individual del navegador.</summary>
public sealed record BrowserActionResult(
    int Index,
    BrowserActionKind Kind,
    bool Ok,
    string? Value = null,
    string? ScreenshotBase64 = null,
    string? Error = null);

/// <summary>Resultado de la secuencia completa (agente -> servidor).</summary>
public sealed record BrowserResultMsg(
    string CorrelationId,
    bool Ok,
    IReadOnlyList<BrowserActionResult> Results,
    string? Error = null);

// ---- Sub-agente Archivos (doc 06 s3.2) ----

/// <summary>
/// Accion TIPADA del sub-agente Archivos. Cada accion es acotada (doc 06 s4): opera SOLO dentro de
/// las rutas raiz de la allow-list local; NO es un shell generico.
/// </summary>
public enum FileActionKind
{
    /// <summary>Lista el contenido de un directorio.</summary>
    List,

    /// <summary>Lee el contenido de un archivo (texto UTF-8, con tope de tamano).</summary>
    Read,

    /// <summary>Lee un archivo BINARIO y lo devuelve en base64 (con tope de tamano).</summary>
    ReadBytes,

    /// <summary>Escribe (crea/reemplaza) un archivo con el contenido dado.</summary>
    Write,

    /// <summary>Borra un archivo.</summary>
    Delete,

    /// <summary>Informa si una ruta existe y si es archivo o directorio.</summary>
    Exists,

    /// <summary>Crea un directorio (y los intermedios).</summary>
    MakeDir,
}

/// <summary>Entrada de un directorio (para <see cref="FileActionKind.List"/>).</summary>
public sealed record FileEntry(string Name, bool IsDirectory, long Size);

/// <summary>Una accion de archivos. Los campos aplicables dependen de <see cref="Kind"/>.</summary>
public sealed record FileAction(
    FileActionKind Kind,
    string? Path = null,
    string? Content = null,
    bool Recursive = false);

/// <summary>Orden del servidor: una secuencia de acciones tipadas de archivos.</summary>
public sealed record FileRequestMsg(
    string CorrelationId,
    string TenantId,
    IReadOnlyList<FileAction> Actions);

/// <summary>Resultado de una accion individual de archivos.</summary>
public sealed record FileActionResult(
    int Index,
    FileActionKind Kind,
    bool Ok,
    string? Value = null,
    IReadOnlyList<FileEntry>? Entries = null,
    string? Error = null);

/// <summary>Resultado de la secuencia completa (agente -> servidor).</summary>
public sealed record FileResultMsg(
    string CorrelationId,
    bool Ok,
    IReadOnlyList<FileActionResult> Results,
    string? Error = null);
