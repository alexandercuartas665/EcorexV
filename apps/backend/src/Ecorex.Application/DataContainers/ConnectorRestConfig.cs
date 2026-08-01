using System.Text.Json;

namespace Ecorex.Application.DataContainers;

/// <summary>
/// Un header HTTP estatico arbitrario del conector RestApi (ej. <c>Partner-Id: xxxx</c> del caso Siigo).
/// NO es un secreto: se guarda en claro en <c>DataConnector.HeadersJson</c> y viaja al agente en el
/// RestFetchSpec. Todo lo configura el usuario del cliente; nada esta hardcodeado a ninguna fuente.
/// </summary>
public sealed record ConnectorHeader(string Name, string Value);

/// <summary>
/// Config NO secreta del intercambio de token (auth de 2 pasos). Se guarda en
/// <c>DataConnector.TokenExchangeJson</c>. El SECRETO del login (password/access_key) NO va aqui:
/// reutiliza <c>DataConnector.CredentialsEncrypted</c> (cifrado).
///
/// Flujo: POST a <see cref="TokenUrl"/> con un cuerpo que incluye <see cref="Username"/> y el secreto
/// descifrado bajo la llave <see cref="SecretParamName"/>; la respuesta trae el token en la ruta JSON
/// <see cref="TokenJsonPath"/>; ese token se aplica en las llamadas reales como
/// header <see cref="ApplyHeaderName"/> = <see cref="ApplyPrefix"/> + token.
/// </summary>
public sealed record TokenExchangeConfig(
    string? TokenUrl,
    string? Method,
    string? Username,
    /// <summary>Nombre del campo del secreto en el cuerpo del login (ej. "access_key", "password").</summary>
    string? SecretParamName,
    /// <summary>Ruta con puntos al token dentro del JSON de respuesta (ej. "access_token", "data.token").</summary>
    string? TokenJsonPath,
    /// <summary>Header donde se aplica el token en las llamadas reales (ej. "Authorization").</summary>
    string? ApplyHeaderName,
    /// <summary>Prefijo del valor del header (ej. "Bearer ").</summary>
    string? ApplyPrefix,
    /// <summary>Formato del cuerpo del login: "json" (por defecto) o "form".</summary>
    string? BodyFormat)
{
    /// <summary>Nombre del campo del usuario en el cuerpo del login (fijo por convencion: "username").</summary>
    public string UsernameParamName => "username";
}

/// <summary>
/// Serializacion/parseo compartido de la config REST no secreta de un conector (headers estaticos y
/// token exchange). Vive en Application para que lo reusen el motor in-process (<c>ApiImportService</c>)
/// y el despachador via agente (<c>ProcessRunner</c>). Web defaults (camelCase, case-insensitive) para
/// que el JSON case con lo que escribe la UI y con RestFetchSpec del agente.
/// </summary>
public static class ConnectorRestConfig
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Lee los headers estaticos de un JSON (lista de {name,value}); tolerante a null/blanco/basura.</summary>
    public static List<ConnectorHeader> ParseHeaders(string? headersJson)
    {
        var result = new List<ConnectorHeader>();
        if (string.IsNullOrWhiteSpace(headersJson)) { return result; }
        try
        {
            var parsed = JsonSerializer.Deserialize<List<ConnectorHeader>>(headersJson, JsonOptions);
            if (parsed is null) { return result; }
            foreach (var h in parsed)
            {
                if (h is null || string.IsNullOrWhiteSpace(h.Name)) { continue; }
                result.Add(new ConnectorHeader(h.Name.Trim(), h.Value ?? ""));
            }
        }
        catch (JsonException) { /* JSON invalido: se ignora (no hay headers) */ }
        return result;
    }

    /// <summary>Lee la config de token exchange de un JSON; null si vacio o invalido.</summary>
    public static TokenExchangeConfig? ParseTokenExchange(string? tokenExchangeJson)
    {
        if (string.IsNullOrWhiteSpace(tokenExchangeJson)) { return null; }
        try { return JsonSerializer.Deserialize<TokenExchangeConfig>(tokenExchangeJson, JsonOptions); }
        catch (JsonException) { return null; }
    }

    public static string Serialize(IEnumerable<ConnectorHeader> headers) =>
        JsonSerializer.Serialize(headers, JsonOptions);

    public static string Serialize(TokenExchangeConfig cfg) =>
        JsonSerializer.Serialize(cfg, JsonOptions);

    /// <summary>
    /// Resuelve una ruta con puntos dentro de un JSON (ej. "access_token", "data.token"). Ruta vacia
    /// devuelve el propio elemento. Devuelve null si algun segmento no existe o el destino no es escalar.
    /// </summary>
    public static string? ResolveJsonPath(JsonElement root, string? path)
    {
        var cur = root;
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (var seg in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(seg, out var next)) { return null; }
                cur = next;
            }
        }
        return cur.ValueKind switch
        {
            JsonValueKind.String => cur.GetString(),
            JsonValueKind.Number => cur.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }
}
