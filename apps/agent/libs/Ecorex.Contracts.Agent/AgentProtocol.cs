namespace Ecorex.Contracts.Agent;

/// <summary>
/// Contrato del canal SignalR entre el servidor (la app web) y el agente on-prem (doc 02).
/// Vive en la libreria de contratos porque es la fuente de verdad del protocolo: el CLIENTE
/// (este agente) y el futuro HUB del backend deben coincidir en ruta, nombres de metodo y forma
/// de los mensajes. El agente solo depende de estos contratos; NUNCA de internals del backend.
/// </summary>
public static class AgentProtocol
{
    // Version del protocolo (doc 02 s11). El servidor puede rechazar agentes por debajo.
    public const string Version = "1.0";

    // Ruta del hub en el servidor (doc 02 s1): wss://&lt;host&gt;/hubs/agente.
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

// ---- Ejecutor REST (Kind == "RestApi"): analogo a QuerySpec pero para HTTP GET ----
//
// RestFetchSpec es al conector RestApi lo que QuerySpec es al Database: describe -declarativamente,
// sin hardcode de ninguna fuente- que URL(s) leer, como autenticar, como paginar, donde esta el
// arreglo objetivo en el JSON, y opcionalmente el patron fan-out lista->detalle con aplanado de un
// arreglo anidado. El agente NO conoce OCS ni ninguna API: recibe este spec por el canal y lo
// ejecuta. La credencial viaja aparte, en ConnectorSpec.Secret (ADR-0040), NUNCA aqui.

/// <summary>
/// Paginacion del ejecutor REST. Modo:
///   - "None"   -> una sola llamada (sin paginar).
///   - "Offset" -> reescribe <see cref="OffsetParam"/>=start+page*PageSize y <see cref="LimitParam"/>=PageSize
///                 (OCS: ?start=0&amp;limit=N). Corta cuando una pagina trae menos de PageSize elementos.
///   - "Page"   -> reescribe <see cref="PageParam"/>=StartValue+page y opcionalmente LimitParam=PageSize.
/// </summary>
public sealed record RestPagingSpec(
    string Mode = "None",
    string OffsetParam = "start",
    string LimitParam = "limit",
    string PageParam = "page",
    int StartValue = 0,
    int PageSize = 100,
    int MaxPages = 1000);

/// <summary>
/// Mapea UN valor del JSON a UNA columna destino. <see cref="Path"/> es una ruta con puntos que
/// admite indices de arreglo: <c>hardware.NAME</c>, <c>bios[0].SSN</c>, <c>accountinfo[0].TAG</c>.
/// Un segmento vacio (dos puntos seguidos, o el path <c>""</c>) referencia la propiedad de clave
/// vacia <c>""</c> (caso OCS: el software resuelto cuelga de la clave ""). <see cref="Column"/> es
/// el NOMBRE de la columna destino: el ingestor del servidor mapea por nombre.
/// </summary>
public sealed record RestFieldMap(string Column, string Path, string? Default = null);

/// <summary>
/// Patron fan-out lista->detalle con aplanado (caso OCS Inventory). Por cada item de la lista se hace
/// un GET al detalle, se ubica un arreglo anidado (p.ej. el software instalado) y se emite UNA fila
/// por elemento de ese arreglo, repitiendo las columnas del padre (<see cref="ParentFields"/>) y
/// agregando las del hijo (<see cref="ChildFields"/>). Declarativo: ningun nombre de OCS esta en el
/// codigo, todo viene aqui.
/// </summary>
public sealed record RestFanoutSpec(
    // Plantilla del endpoint detalle relativa a BaseUrl; <c>{id}</c> se sustituye por el id del item. Ej: <c>/computer/{id}</c>.
    string DetailPathTemplate,
    // De donde sale el id del item: "key" = la clave del objeto-indexado (OCS); "field" = el valor en <see cref="IdField"/>.
    string IdSource = "key",
    // Ruta al id dentro del item cuando <see cref="IdSource"/>="field".
    string? IdField = null,
    // Si el detalle viene envuelto en un objeto-indexado por id (<c>{"228":{...}}</c>), desanida un nivel antes de buscar el arreglo hijo. OCS lo requiere.
    bool DetailUnwrapIndexed = true,
    // Ruta al arreglo anidado en el detalle. null=tolerante; <c>""</c>=propiedad de clave vacia (OCS software resuelto).
    string? ChildArrayPath = null,
    // Columnas denormalizadas del padre (item de la lista).
    List<RestFieldMap>? ParentFields = null,
    // Columnas del hijo (elemento del arreglo anidado del detalle).
    List<RestFieldMap>? ChildFields = null);

/// <summary>Un header HTTP estatico que se envia en TODA request del conector (ej. <c>Partner-Id</c>
/// del caso Siigo). NO es un secreto: se configura por el usuario del cliente. Viaja en el
/// <see cref="RestFetchSpec.Headers"/>.</summary>
public sealed record RestHeader(string Name, string Value);

/// <summary>
/// Intercambio de token (auth de 2 pasos). Antes del fetch real el agente hace un POST (o el metodo
/// configurado) a <see cref="TokenUrl"/> con un cuerpo que lleva el usuario y el SECRETO del login
/// (que viaja aparte, en <see cref="ConnectorSpec.Secret"/>, NUNCA aqui), extrae el token por
/// <see cref="TokenJsonPath"/> y lo aplica en las llamadas reales como header
/// <see cref="ApplyHeaderName"/> = <see cref="ApplyPrefix"/> + token. Todo declarativo (caso Siigo:
/// login -> access_token -> Authorization: Bearer), nada hardcodeado.
/// </summary>
public sealed record RestTokenExchangeSpec(
    string TokenUrl,
    string Method = "POST",
    // Nombre del campo del usuario en el cuerpo del login (ej. "username").
    string UsernameParam = "username",
    // Valor del usuario (no secreto).
    string? Username = null,
    // Nombre del campo del secreto en el cuerpo del login (ej. "access_key", "password"). El VALOR va en ConnectorSpec.Secret.
    string SecretParam = "password",
    // Ruta con puntos al token en el JSON de respuesta (ej. "access_token", "data.token").
    string TokenJsonPath = "access_token",
    // Header donde se aplica el token en las llamadas reales.
    string ApplyHeaderName = "Authorization",
    // Prefijo del valor del header (ej. "Bearer ").
    string ApplyPrefix = "Bearer ",
    // Formato del cuerpo del login: "json" (por defecto) o "form".
    string BodyFormat = "json");

/// <summary>
/// Orden de extraccion REST (analoga a <see cref="QuerySpec"/> para SQL). Viaja dentro de
/// <see cref="FetchRequestMsg.Rest"/>. Solo GET (solo-lectura). La credencial va en
/// <see cref="ConnectorSpec.Secret"/>.
/// </summary>
public sealed record RestFetchSpec(
    // Base absoluta http(s). Ej: <c>https://host/ocsapi/v1</c>.
    string BaseUrl,
    // Path (o URL absoluta) del endpoint LISTA. Ej: <c>/computers</c>.
    string ListPath,
    string HttpMethod = "GET",
    // None|Basic|Bearer|ApiKey|TokenExchange (mismo criterio que ConnectorAuthKind del servidor).
    string AuthKind = "None",
    // Ruta al arreglo/coleccion objetivo en la LISTA. null/""=tolerante (raiz array, objeto-indexado, o envoltorios data/items/results/records/rows).
    string? ArrayPath = null,
    RestPagingSpec? Paging = null,
    // Cuando esta presente: patron fan-out lista->detalle. Cuando es null: fila directa por item con <see cref="Fields"/>.
    RestFanoutSpec? Fanout = null,
    // Mapeo campo->columna para el modo simple (sin fan-out): una fila por item de la lista.
    List<RestFieldMap>? Fields = null,
    int TimeoutSeconds = 30,
    int MaxRows = 100000,
    int MaxDetailCalls = 5000,
    // Headers estaticos arbitrarios aplicados en TODA request (lista + detalle + login del token). NO secretos.
    List<RestHeader>? Headers = null,
    // Auth de 2 pasos. null = auth simple por <see cref="AuthKind"/>. El secreto del login va en ConnectorSpec.Secret.
    RestTokenExchangeSpec? TokenExchange = null);

/// <summary>Orden "traeme estos datos" empujada por el servidor (doc 02 s5).</summary>
public sealed record FetchRequestMsg(
    string CorrelationId,
    string TenantId,
    ConnectorSpec Connector,
    QuerySpec Query,
    PagingSpec? Paging = null,
    // Spec del ejecutor REST cuando <see cref="ConnectorSpec.Kind"/>=="RestApi". null para Database.
    RestFetchSpec? Rest = null);

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

/// <summary>Respuesta del endpoint de token: JWT corto para conectar al hub. <see cref="TenantName"/>
/// es el nombre legible del tenant/cliente dueno del DataClient (para mostrarlo en el agente); opcional
/// por compatibilidad con servidores viejos que no lo envian.</summary>
public sealed record AgentTokenResponse(string AccessToken, DateTimeOffset ExpiresAt, string? TenantName = null);

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

    // Verifica en tiempo constante que <paramref name="signature"/> corresponda al payload.
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
    // Abre una URL http/https (sujeta a la allow-list de dominios LOCAL del agente).
    Navigate,

    // Ejecuta JavaScript en la pagina actual y devuelve el resultado (acotado por dominio).
    Eval,

    // Espera unos ms, o hasta que una condicion JS sea truthy.
    Wait,

    // Captura el navegador (PNG en base64).
    Screenshot,

    // Devuelve el HTML de la pagina o de un selector CSS.
    Html,

    // Ejecuta un guion "MouseBot" (JSON de pasos: click/type por selector) acotado al dominio.
    Mouse,

    // Devuelve el historial reciente de descargas del navegador.
    Downloads,

    // Devuelve CONTENIDO LEGIBLE ya renderizado (texto visible + labels de resultados + enlaces) como
    // JSON {title,url,text,items,links}, con auto-scroll opcional (ScrollRounds) para feeds perezosos
    // (Maps y sitios JS). Es JS FIJO del agente -no del servidor-, asi que NO requiere firma (como Html).
    // OJO COMPAT: va al FINAL a proposito; el enum viaja numerico por el hub, asi que un agente viejo
    // (<1.6.0) recibe un valor desconocido y cae en el 'default' del switch (Fail), sin romper la orden.
    ExtractReadable,
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
    string? Signature = null,
    // ExtractReadable: nº de scrolls al contenedor de resultados antes de extraer (feeds perezosos).
    int? ScrollRounds = null);

/// <summary>Orden del servidor: una secuencia de acciones tipadas para el sub-agente Navegador.</summary>
public sealed record BrowserRequestMsg(
    string CorrelationId,
    string TenantId,
    IReadOnlyList<BrowserAction> Actions,
    // Clave de PERFIL persistente del navegador (ej. "linkedin","facebook","instagram"): reusa cookies y
    // login entre ordenes -> permite scraping LOGUEADO. Null = sesion EFIMERA (comportamiento historico:
    // carpeta unica que se borra al terminar). Va al FINAL (opcional) a proposito por COMPAT: un backend
    // viejo no lo envia, y un agente viejo (<1.7.0) ignora el campo y cae al modo efimero de siempre.
    string? SessionKey = null);

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
    // Lista el contenido de un directorio.
    List,

    // Lee el contenido de un archivo (texto UTF-8, con tope de tamano).
    Read,

    // Lee un archivo BINARIO y lo devuelve en base64 (con tope de tamano).
    ReadBytes,

    // Escribe (crea/reemplaza) un archivo con el contenido dado.
    Write,

    // Borra un archivo.
    Delete,

    // Informa si una ruta existe y si es archivo o directorio.
    Exists,

    // Crea un directorio (y los intermedios).
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
