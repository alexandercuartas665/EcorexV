using System.Text.Json;
using Ecorex.Application.DataContainers;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.SuperAdmin.Agents;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// Camino "via agente" del conector RestApi (ADR-0061, follow-up B de ADR-0059/0060): re-habilita como
/// OPCION la rama que despacha un RestApi a un agente Colmena, armando el <c>RestFetchSpec</c> COMPLETO
/// (la version POST-TokenExchange, no la pre-TokenExchange que el commit de scheduling dejo sin usar).
/// Se verifica que <see cref="RestSpecBuilder"/> saca el spec ENTERO desde el conector Siigo persistido:
/// baseUrl + arrayPath + paging + fields ANIDADOS + Headers estaticos (Partner-Id) + TokenExchange
/// (login de 2 pasos). No necesita hub ni BD: es puro armado de spec.
/// </summary>
public class AgentRestSpecBuilderTests
{
    // MappingJson del conector Siigo con la MISMA forma que escribe la Config API (RestFetchSpec camelCase):
    // arrayPath + paginacion Page + mapeo campo->columna con rutas ANIDADAS (name.0, address.city.name).
    private static string SiigoMappingJson() => JsonSerializer.Serialize(new
    {
        baseUrl = "https://api.siigo.com/v1/customers",
        arrayPath = "results",
        paging = new { mode = "Page", pageParam = "page", limitParam = "page_size", startValue = 1, pageSize = 100, maxPages = 50 },
        fields = new[]
        {
            new { column = "Siigo Id", path = "id" },
            new { column = "nombre", path = "name.0" },
            new { column = "ciudad", path = "address.city.name" },
        }
    });

    private static DataConnector SiigoConnector() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        Name = "Siigo AGROMETALICAS",
        Kind = ConnectorKind.RestApi,
        EndpointUrl = "https://api.siigo.com/v1/customers",
        HttpMethod = "GET",
        AuthKind = ConnectorAuthKind.TokenExchange,
        MappingJson = SiigoMappingJson(),
        // Header estatico arbitrario del caso Siigo (NO secreto).
        HeadersJson = ConnectorRestConfig.Serialize(new[] { new ConnectorHeader("Partner-Id", "EcorexApp") }),
        // Auth de 2 pasos: login -> access_token -> Authorization: Bearer. El secreto del login viaja
        // aparte (CredentialsEncrypted), NUNCA en este JSON.
        TokenExchangeJson = ConnectorRestConfig.Serialize(new TokenExchangeConfig(
            TokenUrl: "https://api.siigo.com/auth",
            Method: "POST",
            Username: "usuario@agrometalicas.co",
            SecretParamName: "access_key",
            TokenJsonPath: "access_token",
            ApplyHeaderName: "Authorization",
            ApplyPrefix: "Bearer ",
            BodyFormat: "json")),
    };

    [Fact]
    public void Build_arma_el_RestFetchSpec_COMPLETO_del_conector_Siigo()
    {
        var (spec, error) = RestSpecBuilder.Build(SiigoConnector());

        Assert.Null(error);
        Assert.NotNull(spec);

        // baseUrl + arrayPath + metodo + authKind (heredado del conector porque el MappingJson no lo trae).
        Assert.Equal("https://api.siigo.com/v1/customers", spec!.BaseUrl);
        Assert.Equal("results", spec.ArrayPath);
        Assert.Equal("GET", spec.HttpMethod);
        Assert.Equal("TokenExchange", spec.AuthKind);

        // paging (paginacion por pagina, con los params del caso Siigo).
        Assert.NotNull(spec.Paging);
        Assert.Equal("Page", spec.Paging!.Mode);
        Assert.Equal("page", spec.Paging.PageParam);
        Assert.Equal("page_size", spec.Paging.LimitParam);
        Assert.Equal(1, spec.Paging.StartValue);
        Assert.Equal(100, spec.Paging.PageSize);
        Assert.Equal(50, spec.Paging.MaxPages);

        // fields ANIDADOS preservados (mismas rutas que el server-direct).
        Assert.NotNull(spec.Fields);
        var byColumn = spec.Fields!.ToDictionary(f => f.Column, f => f.Path);
        Assert.Equal("id", byColumn["Siigo Id"]);
        Assert.Equal("name.0", byColumn["nombre"]);
        Assert.Equal("address.city.name", byColumn["ciudad"]);

        // Headers estaticos (Partner-Id) desde la columna dedicada del conector.
        Assert.NotNull(spec.Headers);
        Assert.Contains(spec.Headers!, h => h.Name == "Partner-Id" && h.Value == "EcorexApp");

        // TokenExchange (login de 2 pasos) desde la columna dedicada del conector.
        Assert.NotNull(spec.TokenExchange);
        Assert.Equal("https://api.siigo.com/auth", spec.TokenExchange!.TokenUrl);
        Assert.Equal("access_key", spec.TokenExchange.SecretParam);
        Assert.Equal("access_token", spec.TokenExchange.TokenJsonPath);
        Assert.Equal("Authorization", spec.TokenExchange.ApplyHeaderName);
        Assert.Equal("Bearer ", spec.TokenExchange.ApplyPrefix);
    }

    [Fact]
    public void Build_rechaza_TokenExchange_sin_URL_de_token()
    {
        var connector = SiigoConnector();
        // TokenExchange declarado pero SIN url de token: el agente no podria autenticar -> error legible.
        connector.TokenExchangeJson = ConnectorRestConfig.Serialize(new TokenExchangeConfig(
            TokenUrl: null, Method: "POST", Username: "u", SecretParamName: "access_key",
            TokenJsonPath: "access_token", ApplyHeaderName: "Authorization", ApplyPrefix: "Bearer ", BodyFormat: "json"));

        var (spec, error) = RestSpecBuilder.Build(connector);

        Assert.Null(spec);
        Assert.NotNull(error);
        Assert.Contains("token", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_exige_mapeo_para_el_agente()
    {
        var connector = SiigoConnector();
        connector.MappingJson = null;

        var (spec, error) = RestSpecBuilder.Build(connector);

        Assert.Null(spec);
        Assert.NotNull(error);
    }
}
