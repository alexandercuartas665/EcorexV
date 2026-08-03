using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Pruebas de los wrappers de la API de configuracion (FASE 1, ADR-0058): el hasher del token opaco
/// (resolver de token -> tenant se basa en este hash determinista) y el planeador de corrida
/// (mapeo del conector persistido -> ApiImportRequest, incluido el upsert por columna clave).
/// </summary>
public class ConfigApiTests
{
    // ---------- ApiTokenHasher (base del resolver token -> tenant) ----------

    [Fact]
    public void Hash_IsDeterministic_And64HexChars()
    {
        const string token = "ecx_demo-token-value";
        var a = ApiTokenHasher.Hash(token);
        var b = ApiTokenHasher.Hash(token);

        Assert.Equal(a, b);                        // determinista: por eso resuelve el tenant
        Assert.Equal(64, a.Length);                // SHA-256 en hex
        Assert.Matches("^[0-9a-f]+$", a);          // hex minusculas
    }

    [Fact]
    public void Hash_DiffersForDifferentTokens()
        => Assert.NotEqual(ApiTokenHasher.Hash("token-a"), ApiTokenHasher.Hash("token-b"));

    [Fact]
    public void Generate_HasPrefix_And_IsUnique()
    {
        var t1 = ApiTokenHasher.Generate();
        var t2 = ApiTokenHasher.Generate();

        Assert.StartsWith(ApiTokenHasher.TokenPrefix, t1);
        Assert.NotEqual(t1, t2);                    // alta entropia
        Assert.NotEqual(ApiTokenHasher.Hash(t1), ApiTokenHasher.Hash(t2));
    }

    // ---------- ConnectorRunPlanner (mapeo del conector -> ApiImportRequest) ----------

    private static readonly Guid ConnectorId = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid TargetId = Guid.Parse("00000000-0000-0000-0000-0000000000ff");
    private static readonly Guid ColSiigoId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid ColName = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static Dictionary<string, Guid> Columns() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["siigo_id"] = ColSiigoId,
        ["nombre"] = ColName,
    };

    // MappingJson con la MISMA forma que persiste la API (RestFetchSpec camelCase): arrayPath + paginacion
    // page/page_size + mapeo campo->columna (incluye ruta anidada id_type.name del caso Siigo).
    private static string SiigoMappingJson() => JsonSerializer.Serialize(new
    {
        baseUrl = "https://api.siigo.com/v1/customers",
        listPath = "",
        httpMethod = "GET",
        authKind = "TokenExchange",
        arrayPath = "results",
        paging = new { mode = "Page", offsetParam = "start", limitParam = "page_size", pageParam = "page", startValue = 1, pageSize = 100, maxPages = 50 },
        fields = new[]
        {
            new { column = "siigo_id", path = "id" },
            new { column = "nombre", path = "name.0" },
        }
    });

    [Fact]
    public void Build_Upsert_MapsColumnsPagingAndKey()
    {
        var plan = ConnectorRunPlanner.Build(
            ConnectorId, TargetId, SiigoMappingJson(), Columns(),
            ApiImportMode.Upsert, keyColumnName: "siigo_id");

        Assert.True(plan.Ok, plan.Error);
        var req = plan.Request!;
        Assert.Equal(ConnectorId, req.ConnectorId);
        Assert.Equal(TargetId, req.TargetContainerId);
        Assert.Equal(ApiImportMode.Upsert, req.Mode);
        Assert.Equal(ColSiigoId, req.KeyColumnId);

        // Mapeo columnaId -> ruta del JSON.
        Assert.Equal("id", req.ColumnToField[ColSiigoId]);
        Assert.Equal("name.0", req.ColumnToField[ColName]);

        // Paginacion page/page_size.
        Assert.NotNull(req.Paging);
        Assert.Equal(PagingMode.Page, req.Paging!.Mode);
        Assert.Equal("page", req.Paging.PageParam);
        Assert.Equal("page_size", req.Paging.LimitParam);
        Assert.Equal(100, req.Paging.PageSize);

        Assert.Equal("results", req.ArrayPath);
    }

    [Fact]
    public void Build_Upsert_WithoutKeyColumn_Fails()
    {
        var plan = ConnectorRunPlanner.Build(
            ConnectorId, TargetId, SiigoMappingJson(), Columns(),
            ApiImportMode.Upsert, keyColumnName: null);

        Assert.False(plan.Ok);
        Assert.NotNull(plan.Error);
    }

    [Fact]
    public void Build_Upsert_WithUnmappedKeyColumn_Fails()
    {
        // "nombre" existe pero la clave debe estar mapeada Y existir; aqui pedimos una columna inexistente.
        var plan = ConnectorRunPlanner.Build(
            ConnectorId, TargetId, SiigoMappingJson(), Columns(),
            ApiImportMode.Upsert, keyColumnName: "no_existe");

        Assert.False(plan.Ok);
        Assert.NotNull(plan.Error);
    }

    [Fact]
    public void Build_Append_NoKeyNeeded()
    {
        var plan = ConnectorRunPlanner.Build(
            ConnectorId, TargetId, SiigoMappingJson(), Columns(),
            ApiImportMode.Append, keyColumnName: null);

        Assert.True(plan.Ok, plan.Error);
        Assert.Null(plan.Request!.KeyColumnId);
        Assert.Equal(ApiImportMode.Append, plan.Request.Mode);
    }

    [Fact]
    public void Build_WithoutMapping_Fails()
    {
        var plan = ConnectorRunPlanner.Build(
            ConnectorId, TargetId, mappingJson: null, Columns(),
            ApiImportMode.Append, keyColumnName: null);

        Assert.False(plan.Ok);
        Assert.NotNull(plan.Error);
    }

    [Fact]
    public void Build_WithoutTargetTable_Fails()
    {
        var plan = ConnectorRunPlanner.Build(
            ConnectorId, Guid.Empty, SiigoMappingJson(), Columns(),
            ApiImportMode.Append, keyColumnName: null);

        Assert.False(plan.Ok);
        Assert.NotNull(plan.Error);
    }

    [Fact]
    public void Build_ArrayPathOverride_Wins()
    {
        var plan = ConnectorRunPlanner.Build(
            ConnectorId, TargetId, SiigoMappingJson(), Columns(),
            ApiImportMode.Append, keyColumnName: null, arrayPathOverride: "data");

        Assert.True(plan.Ok, plan.Error);
        Assert.Equal("data", plan.Request!.ArrayPath);
    }
}
