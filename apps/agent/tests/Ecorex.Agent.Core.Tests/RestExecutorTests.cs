using System.Text.Json;
using Ecorex.Agent.Core.Services;
using Ecorex.Contracts.Agent;

namespace Ecorex.Agent.Core.Tests;

/// <summary>
/// Ejecutor REST de punta a punta con un fetcher enlatado (sin red): paginacion, fan-out
/// lista->detalle, aplanado del arreglo anidado y streaming en chunks. El caso estrella es el mapeo a
/// las 18 columnas de OCS Inventory.
/// </summary>
public class RestExecutorTests
{
    private const string Base = "https://inv.example/ocsapi/v1";

    /// <summary>Fetcher que responde segun el path del URI y registra lo pedido.</summary>
    private sealed class CannedApi
    {
        private readonly Dictionary<string, string> _byPath = new(StringComparer.OrdinalIgnoreCase);
        public List<Uri> Requested { get; } = new();

        public CannedApi Map(string pathSuffix, string json) { _byPath[pathSuffix] = json; return this; }

        public Func<Uri, string, string?, string, CancellationToken, Task<JsonDocument>> Fetch => (uri, _, _, _, _) =>
        {
            Requested.Add(uri);
            var path = uri.GetLeftPart(UriPartial.Path);
            foreach (var (suffix, json) in _byPath)
            {
                if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(JsonDocument.Parse(json));
                }
            }
            throw new GatewayException("TEST_404", $"Sin canned para {path}");
        };
    }

    private static async Task<(List<Dictionary<string, string?>> Rows, string[]? Fields, int Chunks, bool LastFlag)> RunAsync(
        RestExecutor exec, RestFetchSpec spec, ConnectorSpec? connector = null)
    {
        var rows = new List<Dictionary<string, string?>>();
        string[]? fields = null;
        var chunks = 0;
        var last = false;
        await foreach (var chunk in exec.ExecuteAsync(connector, "corr1", spec))
        {
            chunks++;
            if (chunk.Fields is not null) { fields = chunk.Fields; }
            rows.AddRange(chunk.Rows);
            if (chunk.IsLast) { last = true; }
        }
        return (rows, fields, chunks, last);
    }

    // ---- OCS: fan-out + aplanado + 18 columnas ----

    private static RestFetchSpec OcsSpec() => new(
        BaseUrl: Base,
        ListPath: "/computers",
        AuthKind: "Basic",
        ArrayPath: null,
        Paging: new RestPagingSpec("Offset", "start", "limit", "page", 0, 100, 10),
        Fanout: new RestFanoutSpec(
            DetailPathTemplate: "/computer/{id}",
            IdSource: "key",
            DetailUnwrapIndexed: true,
            ChildArrayPath: "",
            ParentFields: new()
            {
                new("Equipo", "hardware.NAME"),
                new("TAG", "accountinfo[0].TAG"),
                new("Serial", "bios[0].SSN"),
                new("Modelo", "bios[0].SMODEL"),
                new("Usuario", "hardware.USERID"),
                new("SO", "hardware.OSNAME"),
                new("IP", "hardware.IPADDR"),
                new("RAM_MB", "hardware.MEMORY"),
                new("CPU", "hardware.PROCESSORT"),
                new("Dominio", "hardware.WORKGROUP"),
                new("UltimoInventario", "hardware.LASTDATE"),
            },
            ChildFields: new()
            {
                new("Programa", "NAME"),
                new("Version", "VERSION"),
                new("Publisher", "PUBLISHER"),
                new("FechaInstalacion", "INSTALLDATE"),
                new("Carpeta", "FOLDER"),
                new("Bits", "BITSWIDTH"),
                new("GUID", "GUID"),
            }));

    private const string OcsList = """
    {
      "1": {
        "hardware": {"NAME":"PC-1","USERID":"user1","OSNAME":"Win11","IPADDR":"10.0.0.1","MEMORY":"8192","PROCESSORT":"Intel i7","WORKGROUP":"BITCODE","LASTDATE":"2026-07-01 10:00:00"},
        "bios": [{"SSN":"SN-0001","SMODEL":"OptiPlex 7090"}],
        "accountinfo": [{"TAG":"BOG-01"}]
      },
      "2": {
        "hardware": {"NAME":"PC-2","USERID":"user2","OSNAME":"Win10","IPADDR":"10.0.0.2","MEMORY":"16384","PROCESSORT":"AMD Ryzen","WORKGROUP":"BITCODE","LASTDATE":"2026-07-02 09:00:00"},
        "bios": [{"SSN":"SN-0002","SMODEL":"Latitude 5420"}],
        "accountinfo": [{"TAG":"MED-02"}]
      }
    }
    """;

    private const string OcsDetail1 = """
    {"1":{"":[
      {"NAME":"Chrome","VERSION":"120.0","PUBLISHER":"Google","INSTALLDATE":"2026-01-01","FOLDER":"C:/Chrome","GUID":"g-chrome","BITSWIDTH":"64"},
      {"NAME":"Office","VERSION":"2021","PUBLISHER":"Microsoft","INSTALLDATE":"2025-06-01","FOLDER":"C:/Office","GUID":"g-office","BITSWIDTH":"64"}
    ]}}
    """;

    private const string OcsDetail2 = """
    {"2":{"":[
      {"NAME":"Firefox","VERSION":"115","PUBLISHER":"Mozilla","INSTALLDATE":"2026-02-02","FOLDER":"C:/FF","GUID":"g-ff","BITSWIDTH":"64"}
    ]}}
    """;

    [Fact]
    public async Task Ocs_FanOut_FlattensSoftwareWithRepeatedParentColumns()
    {
        var api = new CannedApi()
            .Map("/computers", OcsList)
            .Map("/computer/1", OcsDetail1)
            .Map("/computer/2", OcsDetail2);
        var exec = new RestExecutor(api.Fetch);

        var (rows, fields, _, last) = await RunAsync(exec, OcsSpec(), new ConnectorSpec("RestApi", Secret: "user:pass"));

        Assert.True(last);
        // 2 software (PC-1) + 1 (PC-2) = 3 filas.
        Assert.Equal(3, rows.Count);

        // Las 18 columnas, en orden padre+hijo.
        Assert.NotNull(fields);
        Assert.Equal(18, fields!.Length);
        Assert.Equal(new[]
        {
            "Equipo","TAG","Serial","Modelo","Usuario","SO","IP","RAM_MB","CPU","Dominio","UltimoInventario",
            "Programa","Version","Publisher","FechaInstalacion","Carpeta","Bits","GUID"
        }, fields);

        // Fila 0: PC-1 + Chrome. Columnas del padre repetidas, columnas del hijo propias.
        var r0 = rows[0];
        Assert.Equal("PC-1", r0["Equipo"]);
        Assert.Equal("BOG-01", r0["TAG"]);
        Assert.Equal("SN-0001", r0["Serial"]);
        Assert.Equal("OptiPlex 7090", r0["Modelo"]);
        Assert.Equal("user1", r0["Usuario"]);
        Assert.Equal("Win11", r0["SO"]);
        Assert.Equal("10.0.0.1", r0["IP"]);
        Assert.Equal("8192", r0["RAM_MB"]);
        Assert.Equal("Intel i7", r0["CPU"]);
        Assert.Equal("BITCODE", r0["Dominio"]);
        Assert.Equal("2026-07-01 10:00:00", r0["UltimoInventario"]);
        Assert.Equal("Chrome", r0["Programa"]);
        Assert.Equal("120.0", r0["Version"]);
        Assert.Equal("Google", r0["Publisher"]);
        Assert.Equal("64", r0["Bits"]);
        Assert.Equal("g-chrome", r0["GUID"]);

        // Fila 1: PC-1 + Office -> mismas columnas de padre, distinto programa.
        Assert.Equal("PC-1", rows[1]["Equipo"]);
        Assert.Equal("Office", rows[1]["Programa"]);
        Assert.Equal("SN-0001", rows[1]["Serial"]);

        // Fila 2: PC-2 + Firefox.
        Assert.Equal("PC-2", rows[2]["Equipo"]);
        Assert.Equal("MED-02", rows[2]["TAG"]);
        Assert.Equal("Firefox", rows[2]["Programa"]);
    }

    [Fact]
    public async Task Ocs_ListPaging_RewritesOffsetAndLimit()
    {
        var api = new CannedApi()
            .Map("/computers", OcsList)
            .Map("/computer/1", OcsDetail1)
            .Map("/computer/2", OcsDetail2);
        var exec = new RestExecutor(api.Fetch);

        await RunAsync(exec, OcsSpec(), new ConnectorSpec("RestApi", Secret: "user:pass"));

        var listReq = api.Requested.First(u => u.AbsolutePath.EndsWith("/computers"));
        Assert.Contains("start=0", listReq.Query);
        Assert.Contains("limit=100", listReq.Query);
    }

    [Fact]
    public async Task Ocs_ComputerWithoutSoftware_StillEmitsParentRow()
    {
        var api = new CannedApi()
            .Map("/computers", """{"9":{"hardware":{"NAME":"PC-9"},"bios":[{"SSN":"SN9"}],"accountinfo":[{"TAG":"T9"}]}}""")
            .Map("/computer/9", """{"9":{"":[]}}""");
        var exec = new RestExecutor(api.Fetch);

        var (rows, _, _, _) = await RunAsync(exec, OcsSpec(), new ConnectorSpec("RestApi", Secret: "u:p"));

        Assert.Single(rows);
        Assert.Equal("PC-9", rows[0]["Equipo"]);
        Assert.False(rows[0].ContainsKey("Programa")); // sin software: columnas hijo ausentes
    }

    // ---- Modo simple (sin fan-out) ----

    [Fact]
    public async Task Simple_ArrayResponse_OneRowPerElement()
    {
        var api = new CannedApi().Map("/clientes", """[{"id":"1","nombre":"Ana"},{"id":"2","nombre":"Beto"}]""");
        var exec = new RestExecutor(api.Fetch);

        var spec = new RestFetchSpec(Base, "/clientes",
            Fields: new() { new("Codigo", "id"), new("Nombre", "nombre") });

        var (rows, fields, _, _) = await RunAsync(exec, spec);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "Codigo", "Nombre" }, fields);
        Assert.Equal("Ana", rows[0]["Nombre"]);
        Assert.Equal("2", rows[1]["Codigo"]);
    }

    [Fact]
    public async Task Simple_IndexedObjectResponse_OneRowPerEntry()
    {
        var api = new CannedApi().Map("/things", """{"a":{"v":"1"},"b":{"v":"2"}}""");
        var exec = new RestExecutor(api.Fetch);

        var spec = new RestFetchSpec(Base, "/things", Fields: new() { new("Val", "v") });
        var (rows, _, _, _) = await RunAsync(exec, spec);

        Assert.Equal(2, rows.Count);
        Assert.Equal("1", rows[0]["Val"]);
        Assert.Equal("2", rows[1]["Val"]);
    }

    [Fact]
    public async Task Simple_OffsetPaging_WalksPagesUntilShortPage()
    {
        // page 0 -> 2 elementos (== PageSize) -> sigue; page 1 -> 1 elemento (< PageSize) -> corta.
        var api = new CannedApi();
        var exec = new RestExecutor((uri, _, _, _, _) =>
        {
            api.Requested.Add(uri);
            var start = uri.Query.Contains("start=2") ? 2 : 0;
            var json = start == 0
                ? """[{"id":"1"},{"id":"2"}]"""
                : """[{"id":"3"}]""";
            return Task.FromResult(JsonDocument.Parse(json));
        });

        var spec = new RestFetchSpec(Base, "/p",
            Paging: new RestPagingSpec("Offset", "start", "limit", "page", 0, 2, 10),
            Fields: new() { new("Id", "id") });

        var (rows, _, _, _) = await RunAsync(exec, spec);

        Assert.Equal(3, rows.Count);
        Assert.Equal(2, api.Requested.Count); // exactamente dos paginas
        Assert.Contains(api.Requested, u => u.Query.Contains("start=0"));
        Assert.Contains(api.Requested, u => u.Query.Contains("start=2"));
    }

    [Fact]
    public async Task Simple_MaxRows_StopsEarly()
    {
        var api = new CannedApi().Map("/big", """[{"id":"1"},{"id":"2"},{"id":"3"},{"id":"4"}]""");
        var exec = new RestExecutor(api.Fetch);

        var spec = new RestFetchSpec(Base, "/big", MaxRows: 2, Fields: new() { new("Id", "id") });
        var (rows, _, _, last) = await RunAsync(exec, spec);

        Assert.Equal(2, rows.Count);
        Assert.True(last);
    }

    [Fact]
    public async Task NonGetMethod_Throws()
    {
        var exec = new RestExecutor((_, _, _, _, _) => Task.FromResult(JsonDocument.Parse("[]")));
        var spec = new RestFetchSpec(Base, "/x", HttpMethod: "POST");

        await Assert.ThrowsAsync<GatewayException>(async () =>
        {
            await foreach (var _ in exec.ExecuteAsync(null, "c", spec)) { }
        });
    }

    [Fact]
    public void Combine_RelativeAndAbsolute()
    {
        Assert.Equal("https://inv.example/ocsapi/v1/computers", RestExecutor.Combine(Base, "/computers").ToString());
        Assert.Equal("https://inv.example/ocsapi/v1/computers", RestExecutor.Combine(Base, "computers").ToString());
        Assert.Equal("https://other/x", RestExecutor.Combine(Base, "https://other/x").ToString());
    }
}
