namespace Ecorex.Application.DataContainers;

/// <summary>
/// Enmascara lo que puede ser secreto en el endpoint de un conector antes de mostrarlo fuera de la pantalla
/// (ADR-0203, herramienta MCP ver_conector). Las credenciales del conector viven cifradas aparte, pero hay APIs
/// que llevan la clave en la URL (?api_key=, ?$$app_token=, ?access_token=...) o en la userinfo
/// (https://usuario:clave@host). Se enmascara el VALOR de esos parametros y la userinfo completa; el resto de la
/// URL (ruta, $order, $limit, filtros) queda legible para poder ajustarla.
/// </summary>
public static class ConnectorSecretMask
{
    /// <summary>Marcador de valor oculto.</summary>
    public const string Mask = "***";

    /// <summary>Si el nombre del parametro CONTIENE alguno, se oculta (access_token, client_secret, ...).</summary>
    private static readonly string[] ContainsFragments =
    {
        "token", "secret", "password", "passwd", "credential", "apikey", "signature"
    };

    /// <summary>Solo si el nombre TERMINA en alguno (api_key, x-auth, pass, sig) para no ocultar "keyword" o "design".</summary>
    private static readonly string[] SuffixFragments = { "key", "pwd", "pass", "sig", "auth", "session" };

    /// <summary>Devuelve el endpoint con los valores sensibles ocultos y si oculto algo.</summary>
    public static (string? Endpoint, bool Masked) MaskEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) { return (endpoint, false); }
        var url = endpoint.Trim();
        var masked = false;

        // userinfo: esquema://usuario:clave@host -> esquema://***@host
        var schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd > 0)
        {
            var authorityStart = schemeEnd + 3;
            var authorityEnd = url.IndexOfAny(new[] { '/', '?', '#' }, authorityStart);
            if (authorityEnd < 0) { authorityEnd = url.Length; }
            var at = url.LastIndexOf('@', authorityEnd - 1, authorityEnd - authorityStart);
            if (at >= authorityStart)
            {
                url = url[..authorityStart] + Mask + url[at..];
                masked = true;
            }
        }

        var q = url.IndexOf('?');
        if (q < 0) { return (url, masked); }
        var hash = url.IndexOf('#', q);
        var query = hash < 0 ? url[(q + 1)..] : url[(q + 1)..hash];
        var tail = hash < 0 ? "" : url[hash..];

        var parts = query.Split('&');
        for (var i = 0; i < parts.Length; i++)
        {
            var eq = parts[i].IndexOf('=');
            if (eq <= 0) { continue; }
            var name = Uri.UnescapeDataString(parts[i][..eq]).TrimStart('$').ToLowerInvariant();
            if (IsSensitive(name) && parts[i].Length > eq + 1)
            {
                parts[i] = parts[i][..(eq + 1)] + Mask;
                masked = true;
            }
        }
        return (url[..(q + 1)] + string.Join('&', parts) + tail, masked);
    }

    private static bool IsSensitive(string name)
    {
        var compact = name.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal)
            .Replace(".", "", StringComparison.Ordinal);
        return ContainsFragments.Any(f => compact.Contains(f, StringComparison.Ordinal))
               || SuffixFragments.Any(f => compact.EndsWith(f, StringComparison.Ordinal));
    }
}
