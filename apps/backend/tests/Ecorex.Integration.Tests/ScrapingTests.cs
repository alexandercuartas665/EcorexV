using System.Net;
using System.Text;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Scraping;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion del modulo EXTRACCION DE DATOS (000730, ADR-0025) en matriz dual
/// PostgreSQL / SQL Server, reutilizando los fixtures de TenantIsolation. La corrida usa el
/// fetcher HTTP REAL contra un endpoint local que replica /api/demo/scrape-sample (mismo
/// criterio que la fuente demo: sin depender de internet; el guard SSRF corre con
/// AllowLoopback=true igual que la consola en Development). Cubre: CRUD con validaciones +
/// corrida exitosa persistida con conteo/preview, historial que persiste exito Y fallo con
/// transicion de estado de la fuente, y aislamiento cross-tenant de fuentes y corridas.
/// </summary>
public abstract class ScrapingTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected ScrapingTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    /// <summary>Opciones dev: loopback permitido SOLO para el endpoint demo local (ADR-0025).</summary>
    private static readonly ScrapeGuardOptions DevOptions = new() { AllowLoopback = true };

    [Fact]
    public async Task Crud_YRunContraEndpointDemo_PersisteCorridaExitosa()
    {
        using var server = await LocalJsonServer.StartAsync();
        var tenantId = await SeedTenantAsync("Scraping CRUD");
        await using var ctx = _fixture.CreateContext(tenantId);
        var service = BuildService(ctx, tenantId);

        // --- Validaciones tipadas del alta ---
        Assert.Equal(ScrapeOpStatus.Invalid, (await service.SaveSourceAsync(
            new SaveScrapeSourceRequest(null, "", server.Url("/sample"), ScrapeSourceKind.Json, null))).Status);
        Assert.Equal(ScrapeOpStatus.Invalid, (await service.SaveSourceAsync(
            new SaveScrapeSourceRequest(null, "Mala", "ftp://x/archivo", ScrapeSourceKind.Json, null))).Status);
        Assert.Equal(ScrapeOpStatus.Invalid, (await service.SaveSourceAsync(
            new SaveScrapeSourceRequest(null, "Html sin selector", server.Url("/pagina"), ScrapeSourceKind.Html, null))).Status);

        // --- Alta valida ---
        var created = await service.SaveSourceAsync(new SaveScrapeSourceRequest(
            null, "Items demo", server.Url("/sample"), ScrapeSourceKind.Json, null));
        Assert.True(created.IsOk, created.Error);

        // Nombre duplicado dentro del tenant: rechazado.
        Assert.Equal(ScrapeOpStatus.Invalid, (await service.SaveSourceAsync(
            new SaveScrapeSourceRequest(null, "Items demo", server.Url("/sample"), ScrapeSourceKind.Json, null))).Status);

        // --- Edicion ---
        var updated = await service.SaveSourceAsync(new SaveScrapeSourceRequest(
            created.Value!.Id, "Items demo v2", server.Url("/sample"), ScrapeSourceKind.Json, null));
        Assert.True(updated.IsOk, updated.Error);
        Assert.Equal("Items demo v2", (await service.GetSourceAsync(created.Value.Id))!.Name);

        // --- Ejecutar contra el endpoint demo local ---
        var run = await service.RunAsync(created.Value.Id);
        Assert.True(run.IsOk, run.Error);
        Assert.Equal(ScrapeRunStatus.Success, run.Value!.Status);
        Assert.Equal(8, run.Value.ItemCount); // los 8 items del JSON demo

        // La corrida quedo PERSISTIDA con el documento de resultado (JSON valido, con preview).
        var persisted = await ctx.ScrapeRuns.AsNoTracking().SingleAsync();
        Assert.Equal(ScrapeRunStatus.Success, persisted.Status);
        Assert.Equal(8, persisted.ItemCount);
        using (var doc = JsonDocument.Parse(persisted.ResultJson!))
        {
            Assert.Equal(8, doc.RootElement.GetProperty("itemCount").GetInt32());
            Assert.Contains("sku", doc.RootElement.GetProperty("columns").EnumerateArray().Select(e => e.GetString()));
        }

        // Y la fuente actualizo LastRunAt / resumen / metricas.
        var after = await service.GetSourceAsync(created.Value.Id);
        Assert.NotNull(after!.LastRunAt);
        Assert.Contains("8 items", after.LastResultSummary);
        Assert.Equal(1, after.RunCount);
        Assert.Equal(8, after.TotalItems);

        // Con historial NO se elimina (Invalid tipado, criterio ADR-0023/0025).
        Assert.Equal(ScrapeOpStatus.Invalid, (await service.DeleteSourceAsync(created.Value.Id)).Status);
    }

    [Fact]
    public async Task Historial_PersisteFalloYExito_YTransicionaElEstadoDeLaFuente()
    {
        using var server = await LocalJsonServer.StartAsync();
        var tenantId = await SeedTenantAsync("Scraping Historial");
        await using var ctx = _fixture.CreateContext(tenantId);
        var service = BuildService(ctx, tenantId);

        var created = await service.SaveSourceAsync(new SaveScrapeSourceRequest(
            null, "Fuente inestable", server.Url("/roto"), ScrapeSourceKind.Json, null));
        Assert.True(created.IsOk, created.Error);
        var sourceId = created.Value!.Id;

        // 1. Corrida FALLIDA (HTTP 500): se persiste con su motivo y la fuente pasa a Error.
        var failed = await service.RunAsync(sourceId);
        Assert.True(failed.IsOk, failed.Error); // el fallo de red NO es error de la operacion
        Assert.Equal(ScrapeRunStatus.Failed, failed.Value!.Status);
        Assert.Contains("500", failed.Value.ErrorMessage);

        var afterFail = await service.GetSourceAsync(sourceId);
        Assert.Equal(ScrapeSourceStatus.Error, afterFail!.Status);
        Assert.StartsWith("Fallo:", afterFail.LastResultSummary);

        // 2. Corregida la URL, la corrida exitosa vuelve la fuente a Active.
        var fixedSave = await service.SaveSourceAsync(new SaveScrapeSourceRequest(
            sourceId, "Fuente inestable", server.Url("/sample"), ScrapeSourceKind.Json, null, ScrapeSourceStatus.Error));
        Assert.True(fixedSave.IsOk, fixedSave.Error);
        var success = await service.RunAsync(sourceId);
        Assert.Equal(ScrapeRunStatus.Success, success.Value!.Status);
        Assert.Equal(ScrapeSourceStatus.Active, (await service.GetSourceAsync(sourceId))!.Status);

        // 3. El historial conserva AMBAS corridas, la mas reciente primero.
        var runs = await service.ListRunsAsync(sourceId);
        Assert.Equal(2, runs.Count);
        Assert.Equal(ScrapeRunStatus.Success, runs[0].Status);
        Assert.Equal(ScrapeRunStatus.Failed, runs[1].Status);
        Assert.Equal(2, await ctx.ScrapeRuns.CountAsync());
    }

    [Fact]
    public async Task FuentesYCorridas_EstanAisladasPorTenant()
    {
        using var server = await LocalJsonServer.StartAsync();
        var tenantA = await SeedTenantAsync("Scraping Tenant A");
        var tenantB = await SeedTenantAsync("Scraping Tenant B");

        Guid sourceId;
        await using (var ctxA = _fixture.CreateContext(tenantA))
        {
            var serviceA = BuildService(ctxA, tenantA);
            var created = await serviceA.SaveSourceAsync(new SaveScrapeSourceRequest(
                null, "Fuente de A", server.Url("/sample"), ScrapeSourceKind.Json, null));
            Assert.True(created.IsOk, created.Error);
            sourceId = created.Value!.Id;
            Assert.Equal(ScrapeRunStatus.Success, (await serviceA.RunAsync(sourceId)).Value!.Status);
        }

        await using (var ctxB = _fixture.CreateContext(tenantB))
        {
            var serviceB = BuildService(ctxB, tenantB);

            // B no ve nada de A (filtro global): ni la fuente ni sus corridas.
            Assert.Empty(await serviceB.ListSourcesAsync());
            Assert.Null(await serviceB.GetSourceAsync(sourceId));
            Assert.Empty(await serviceB.ListRunsAsync(sourceId));
            Assert.Empty(await ctxB.ScrapeRuns.AsNoTracking().ToListAsync());

            // Ejecutar o borrar la fuente de A desde B es imposible por construccion.
            Assert.Equal(ScrapeOpStatus.NotFound, (await serviceB.RunAsync(sourceId)).Status);
            Assert.Equal(ScrapeOpStatus.NotFound, (await serviceB.DeleteSourceAsync(sourceId)).Status);

            // Y B puede usar el MISMO nombre sin chocar con el indice unico por tenant.
            var own = await serviceB.SaveSourceAsync(new SaveScrapeSourceRequest(
                null, "Fuente de A", server.Url("/sample"), ScrapeSourceKind.Json, null));
            Assert.True(own.IsOk, own.Error);
        }

        // A sigue viendo exactamente lo suyo.
        await using (var ctxA = _fixture.CreateContext(tenantA))
        {
            var sources = await BuildService(ctxA, tenantA).ListSourcesAsync();
            Assert.Single(sources);
            Assert.Equal(1, sources[0].RunCount);
        }
    }

    // =========================================================================

    private static ScrapeService BuildService(IApplicationDbContext ctx, Guid tenantId)
    {
        var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });
        var fetcher = new ScrapeHttpFetcher(http, DevOptions);
        return new ScrapeService(ctx, new TestTenantContext(tenantId), fetcher, DevOptions, TimeProvider.System);
    }

    private async Task<Guid> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId: null);
        ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
        await ctx.SaveChangesAsync();
        return tenantId;
    }

    private sealed class TestTenantContext(Guid? tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId => null;
    }

    /// <summary>
    /// Mini servidor HTTP local (HttpListener en localhost, puerto libre) que replica el
    /// endpoint demo /api/demo/scrape-sample de la consola: /sample devuelve el JSON de 8
    /// items y /roto devuelve 500. Evita depender de internet en la matriz dual.
    /// </summary>
    private sealed class LocalJsonServer : IDisposable
    {
        private const string SampleJson =
            """
            {"items":[
              {"sku":"PIN-1001","nombre":"Pintura interior blanca 1 gl","precio":89900,"stock":42},
              {"sku":"PIN-1002","nombre":"Pintura interior marfil 1 gl","precio":92500,"stock":31},
              {"sku":"PIN-1003","nombre":"Pintura exterior gris 1 gl","precio":104900,"stock":18},
              {"sku":"PIN-1004","nombre":"Esmalte sintetico negro 1/4 gl","precio":38900,"stock":77},
              {"sku":"PIN-1005","nombre":"Vinilo tipo 1 blanco 5 gl","precio":319000,"stock":12},
              {"sku":"PIN-1006","nombre":"Anticorrosivo rojo 1/4 gl","precio":27400,"stock":54},
              {"sku":"PIN-1007","nombre":"Barniz mate 1/2 gl","precio":61200,"stock":23},
              {"sku":"PIN-1008","nombre":"Rodillo felpa 9 pulgadas","precio":15900,"stock":96}
            ]}
            """;

        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly int _port;

        private LocalJsonServer(HttpListener listener, int port)
        {
            _listener = listener;
            _port = port;
        }

        public string Url(string path) => $"http://localhost:{_port}{path}";

        public static Task<LocalJsonServer> StartAsync()
        {
            // Puerto libre + HttpListener (el prefijo localhost no requiere URLACL de admin).
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var port = FreePort();
                var listener = new HttpListener();
                listener.Prefixes.Add($"http://localhost:{port}/");
                try
                {
                    listener.Start();
                }
                catch (HttpListenerException)
                {
                    continue; // carrera por el puerto: reintentar con otro
                }
                var server = new LocalJsonServer(listener, port);
                _ = server.LoopAsync();
                return Task.FromResult(server);
            }
            throw new InvalidOperationException("No se pudo iniciar el servidor local de pruebas.");
        }

        private async Task LoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception) when (_cts.IsCancellationRequested || !_listener.IsListening)
                {
                    return;
                }

                try
                {
                    if (context.Request.Url!.AbsolutePath == "/sample")
                    {
                        var bytes = Encoding.UTF8.GetBytes(SampleJson);
                        context.Response.StatusCode = 200;
                        context.Response.ContentType = "application/json; charset=utf-8";
                        await context.Response.OutputStream.WriteAsync(bytes);
                    }
                    else
                    {
                        context.Response.StatusCode = 500;
                    }
                }
                finally
                {
                    context.Response.Close();
                }
            }
        }

        private static int FreePort()
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { /* mejor esfuerzo */ }
            _listener.Close();
        }
    }
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class ScrapingTests_Postgres
    : ScrapingTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public ScrapingTests_Postgres(PostgresTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class ScrapingTests_SqlServer
    : ScrapingTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public ScrapingTests_SqlServer(SqlServerTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}
