using System.Text.Json;
using Ecorex.Application.DataContainers;
using Ecorex.Contracts.Agent;
using Ecorex.Domain.Enums;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>
/// Arma el <see cref="RestFetchSpec"/> COMPLETO de un conector RestApi para despacharlo al agente
/// (camino "via agente", follow-up B de ADR-0059/0060 -> ADR-0061). Es la version RE-HABILITADA (y
/// completada) del antiguo <c>ProcessRunner.BuildRestSpec</c> que el commit de scheduling elimino al
/// dejar RestApi solo server-direct: aqui se restaura como OPCION cuando el proceso/run tiene
/// <c>ClientId</c> (agente Colmena), armando el spec ENTERO que el commit `12adfc4` habilito en el
/// <c>RestExecutor</c>: baseUrl + arrayPath + paging + fields (mapeo ANIDADO) + Headers estaticos (ej.
/// Partner-Id) + TokenExchange (auth de 2 pasos).
///
/// La parte declarativa (arrayPath, paginacion, fan-out lista->detalle y mapeo campos->columnas) vive
/// en <c>connector.MappingJson</c> como JSON del propio RestFetchSpec (la MISMA forma que escribe la
/// Config API y que lee <see cref="ConnectorRunPlanner"/> para el server-direct: NO se duplica el
/// modelo de mapeo). BaseUrl, metodo y tipo de auth se toman de los campos normales del conector
/// (EndpointUrl/HttpMethod/AuthKind) si el JSON no los trae. Headers y TokenExchange son autoritativos
/// desde las columnas dedicadas del conector (HeadersJson/TokenExchangeJson). La credencial NO va aqui:
/// viaja aparte, descifrada, en <see cref="ConnectorSpec.Secret"/> (ADR-0040).
///
/// Se extrajo a una clase propia (antes era un metodo privado del runner) para poder unit-testear que
/// el spec sale completo sin levantar hub ni BD.
/// </summary>
public static class RestSpecBuilder
{
    private static readonly JsonSerializerOptions RestJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Devuelve el spec listo para el agente, o un error legible si el conector no esta configurado
    /// para el camino via agente (sin mapeo, sin endpoint, o TokenExchange sin URL de token).
    /// </summary>
    public static (RestFetchSpec? Spec, string? Error) Build(Domain.Entities.DataConnector connector)
    {
        if (string.IsNullOrWhiteSpace(connector.MappingJson))
        {
            return (null, "El conector REST no tiene mapeo para el agente: define el RestFetchSpec (endpoints, arrayPath, fan-out y mapeo campos->columnas) en el mapeo del conector.");
        }

        RestFetchSpec? parsed;
        try { parsed = JsonSerializer.Deserialize<RestFetchSpec>(connector.MappingJson, RestJsonOptions); }
        catch (JsonException ex) { return (null, $"El mapeo REST del conector no es un RestFetchSpec valido: {ex.Message}"); }
        if (parsed is null) { return (null, "El mapeo REST del conector quedo vacio tras interpretarlo."); }

        var baseUrl = string.IsNullOrWhiteSpace(parsed.BaseUrl) ? connector.EndpointUrl : parsed.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl) && string.IsNullOrWhiteSpace(parsed.ListPath))
        {
            return (null, "El conector REST no tiene endpoint: define EndpointUrl en el conector o BaseUrl/ListPath en el mapeo.");
        }

        var authKind = parsed.AuthKind is null or "None" && connector.AuthKind != ConnectorAuthKind.None
            ? connector.AuthKind.ToString()
            : (parsed.AuthKind ?? "None");

        var method = string.IsNullOrWhiteSpace(parsed.HttpMethod)
            ? (string.IsNullOrWhiteSpace(connector.HttpMethod) ? "GET" : connector.HttpMethod!)
            : parsed.HttpMethod;

        // Headers estaticos y token exchange: fuente autoritativa son las columnas dedicadas del conector
        // (HeadersJson/TokenExchangeJson). Si estan vacias, se respeta lo que trajera el propio MappingJson.
        var headers = ConnectorRestConfig.ParseHeaders(connector.HeadersJson)
            .Select(h => new RestHeader(h.Name, h.Value)).ToList();

        RestTokenExchangeSpec? tokenExchange = null;
        if (connector.AuthKind == ConnectorAuthKind.TokenExchange)
        {
            var cfg = ConnectorRestConfig.ParseTokenExchange(connector.TokenExchangeJson);
            if (cfg is null || string.IsNullOrWhiteSpace(cfg.TokenUrl))
            {
                return (null, "El conector usa intercambio de token pero no tiene URL de token configurada.");
            }
            tokenExchange = new RestTokenExchangeSpec(
                TokenUrl: cfg.TokenUrl!.Trim(),
                Method: string.IsNullOrWhiteSpace(cfg.Method) ? "POST" : cfg.Method!.Trim(),
                UsernameParam: cfg.UsernameParamName,
                Username: cfg.Username,
                SecretParam: string.IsNullOrWhiteSpace(cfg.SecretParamName) ? "password" : cfg.SecretParamName!.Trim(),
                TokenJsonPath: string.IsNullOrWhiteSpace(cfg.TokenJsonPath) ? "access_token" : cfg.TokenJsonPath!.Trim(),
                ApplyHeaderName: string.IsNullOrWhiteSpace(cfg.ApplyHeaderName) ? "Authorization" : cfg.ApplyHeaderName!.Trim(),
                ApplyPrefix: cfg.ApplyPrefix ?? "Bearer ",
                BodyFormat: string.IsNullOrWhiteSpace(cfg.BodyFormat) ? "json" : cfg.BodyFormat!.Trim());
        }

        var spec = parsed with
        {
            BaseUrl = baseUrl ?? string.Empty,
            AuthKind = authKind,
            HttpMethod = method,
            Headers = headers.Count > 0 ? headers : parsed.Headers,
            TokenExchange = tokenExchange ?? parsed.TokenExchange
        };
        return (spec, null);
    }
}
