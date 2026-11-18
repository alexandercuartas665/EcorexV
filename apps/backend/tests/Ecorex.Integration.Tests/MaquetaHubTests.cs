extern alias superadmin;

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ecorex.Application.Integraciones.Maqueta;
using Ecorex.Application.Storage;
using Ecorex.Application.Tenancy;
using superadmin::Ecorex.SuperAdmin.Maqueta;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Hub de maquetas (ADR-0178), sin base de datos:
/// - El area solo se activa con Enabled + (Development o AllowOutsideDevelopment).
/// - El registro descarta ids invalidos/repetidos y carpetas inexistentes o relativas.
/// - /maqueta/{id}/... exige sesion de usuario de tenant (anonimo -> /login; sin tenant -> 403), sirve solo archivos
///   dentro de la carpeta (ni '..' ni dot-files como .env) y sin listado de directorios.
/// </summary>
public sealed class MaquetaHubTests : IDisposable
{
    private readonly string _base;
    private readonly string _root;

    public MaquetaHubTests()
    {
        _base = Path.Combine(Path.GetTempPath(), "maqueta-hub-" + Guid.NewGuid().ToString("N"));
        _root = Path.Combine(_base, "demo.maqueta");
        Directory.CreateDirectory(Path.Combine(_root, "anotador"));
        File.WriteAllText(Path.Combine(_root, "anotador", "anotador.html"), "<html>anotador</html>");
        File.WriteAllText(Path.Combine(_root, "index.html"), "<html>indice</html>");
        File.WriteAllText(Path.Combine(_root, ".env"), "SECRET=1");
        File.WriteAllText(Path.Combine(_base, "fuera.txt"), "fuera de la maqueta");
    }

    public void Dispose()
    {
        try { Directory.Delete(_base, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void Registro_SoloActivoEnDevelopmentOConBandera()
    {
        var opts = Options(enabled: true);
        Assert.True(MaquetaHub.Resolve(opts, isDevelopment: true).IsActive);
        Assert.False(MaquetaHub.Resolve(opts, isDevelopment: false).IsActive);
        Assert.Empty(MaquetaHub.Resolve(opts, isDevelopment: false).Projects);

        opts.AllowOutsideDevelopment = true;
        Assert.True(MaquetaHub.Resolve(opts, isDevelopment: false).IsActive);

        Assert.False(MaquetaHub.Resolve(Options(enabled: false), isDevelopment: true).IsActive);
    }

    [Fact]
    public void Registro_DescartaIdsInvalidosRepetidosYCarpetasInexistentes()
    {
        var opts = Options(enabled: true);
        opts.Projects.Add(new MaquetaHubProjectOptions { Id = "../otro", Nombre = "Malo", RutaFisica = _root });
        opts.Projects.Add(new MaquetaHubProjectOptions { Id = "con espacio", Nombre = "Malo", RutaFisica = _root });
        opts.Projects.Add(new MaquetaHubProjectOptions { Id = "demo", Nombre = "Repetido", RutaFisica = _root });
        opts.Projects.Add(new MaquetaHubProjectOptions { Id = "noexiste", Nombre = "X", RutaFisica = Path.Combine(_base, "nada") });
        opts.Projects.Add(new MaquetaHubProjectOptions { Id = "relativa", Nombre = "X", RutaFisica = "carpeta/relativa" });

        var reg = MaquetaHub.Resolve(opts, isDevelopment: true);
        var p = Assert.Single(reg.Projects);
        Assert.Equal("demo", p.Id);
        Assert.Equal("/maqueta/demo/anotador/anotador.html", p.AnotadorUrl);
        Assert.Same(p, reg.Find("DEMO"));
        Assert.Null(reg.Find("otro"));
    }

    [Fact]
    public async Task Archivos_ExigenSesionDeTenant_YNoSalenDeLaCarpeta()
    {
        await using var app = await StartAsync(MaquetaHub.Resolve(Options(enabled: true), isDevelopment: true));
        var client = app.GetTestClient();

        // Anonimo: al login (no se sirve el archivo).
        var anon = await client.SendAsync(Get("/maqueta/demo/anotador/anotador.html", user: null));
        Assert.Equal(HttpStatusCode.Redirect, anon.StatusCode);
        Assert.Equal("/login", anon.Headers.Location?.OriginalString);

        // Sesion sin tenant (operador de plataforma): 403.
        var noTenant = await client.SendAsync(Get("/maqueta/demo/anotador/anotador.html", user: "platform"));
        Assert.Equal(HttpStatusCode.Forbidden, noTenant.StatusCode);

        // Usuario de tenant: se sirve, con cabeceras de seguridad.
        var ok = await client.SendAsync(Get("/maqueta/demo/anotador/anotador.html", user: "tenant"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("<html>anotador</html>", await ok.Content.ReadAsStringAsync());
        Assert.Equal("nosniff", ok.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("SAMEORIGIN", ok.Headers.GetValues("X-Frame-Options").Single());

        // Dot-files, salida de la carpeta y listado de directorios: NO los sirve el hub (cae al resto del pipeline).
        foreach (var path in new[] { "/maqueta/demo/.env", "/maqueta/demo/%2e%2e/fuera.txt", "/maqueta/demo/..%2ffuera.txt", "/maqueta/demo/", "/maqueta/demo/anotador/" })
        {
            var res = await client.SendAsync(Get(path, user: "tenant"));
            var body = await res.Content.ReadAsStringAsync();
            Assert.Equal("fallthrough", body);
        }

        // Proyecto no registrado: tampoco.
        Assert.Equal("fallthrough", await (await client.SendAsync(Get("/maqueta/otro/index.html", user: "tenant"))).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MaquetaEnLaNube_SeSirveDesdeLaCache_PorVersion_YSinOcultos()
    {
        var files = new MockupProjectsTestsBase.MemoryFileStorage();
        var z1 = await files.SaveAsync(new FileStorageSaveRequest("maqueta-sitio", "p", "1", "v1.zip", "application/zip", MockupProjectsTestsBase.SiteZip("v1")));
        var z2 = await files.SaveAsync(new FileStorageSaveRequest("maqueta-sitio", "p", "2", "v2.zip", "application/zip", MockupProjectsTestsBase.SiteZip("v2")));
        var tenant = Guid.NewGuid();
        var fake = new FakeMockups(new Dictionary<int, MockupServeTarget>
        {
            [1] = new(tenant, Guid.NewGuid(), z1.Id, "site/", MockupService.AnotadorEntry),
            [2] = new(tenant, Guid.NewGuid(), z2.Id, "site/", MockupService.AnotadorEntry)
        }, currentNumber: 2);

        await using var app = await StartAsync(MaquetaHub.Resolve(Options(enabled: false), isDevelopment: false), fake, files);
        var client = app.GetTestClient();

        // Version actual (v2) y una anterior por ~n.
        var current = await client.SendAsync(Get("/maqueta/nube/anotador/anotador.html", user: "tenant"));
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal("<html>anotador v2</html>", await current.Content.ReadAsStringAsync());
        Assert.Equal("nosniff", current.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("<html>v1</html>", await (await client.SendAsync(Get("/maqueta/nube~1/index.html", user: "tenant"))).Content.ReadAsStringAsync());

        // Ocultos (.env, marcador .ok), proyecto inexistente, version inexistente y anonimo.
        Assert.Equal("fallthrough", await (await client.SendAsync(Get("/maqueta/nube/.env", user: "tenant"))).Content.ReadAsStringAsync());
        Assert.Equal("fallthrough", await (await client.SendAsync(Get("/maqueta/nube/.ok", user: "tenant"))).Content.ReadAsStringAsync());
        Assert.Equal("fallthrough", await (await client.SendAsync(Get("/maqueta/otra/index.html", user: "tenant"))).Content.ReadAsStringAsync());
        Assert.Equal("fallthrough", await (await client.SendAsync(Get("/maqueta/nube~7/index.html", user: "tenant"))).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await client.SendAsync(Get("/maqueta/nube/index.html", user: null))).StatusCode);
    }

    [Theory]
    [InlineData("/maqueta/tronox/index.html", true, "tronox", null, "/index.html")]
    [InlineData("/maqueta/tronox~3/anotador/anotador.html", true, "tronox", 3, "/anotador/anotador.html")]
    [InlineData("/maqueta/TRONOX/a.css", true, "tronox", null, "/a.css")]
    [InlineData("/maqueta/tronox", false, "", null, "")]
    [InlineData("/maqueta/tronox~x/a.html", false, "", null, "")]
    [InlineData("/maqueta/tronox~0/a.html", false, "", null, "")]
    [InlineData("/maqueta/../x/a.html", false, "", null, "")]
    [InlineData("/otra/tronox/a.html", false, "", null, "")]
    public void RutaDeLaNube_SeInterpreta(string path, bool ok, string slug, int? number, string sub)
    {
        Assert.Equal(ok, MaquetaHub.TryParseCloudPath(new PathString(path), out var s, out var n, out var rest));
        if (!ok) { return; }
        Assert.Equal(slug, s);
        Assert.Equal(number, n);
        Assert.Equal(sub, rest.Value);
    }

    [Fact]
    public async Task HubInactivo_NoSirveNada()
    {
        await using var app = await StartAsync(MaquetaHub.Resolve(Options(enabled: true), isDevelopment: false));
        var res = await app.GetTestClient().SendAsync(Get("/maqueta/demo/index.html", user: "tenant"));
        Assert.Equal("fallthrough", await res.Content.ReadAsStringAsync());
    }

    private MaquetaHubOptions Options(bool enabled) => new()
    {
        Enabled = enabled,
        Projects = { new MaquetaHubProjectOptions { Id = "demo", Nombre = "Demo", RutaFisica = _root } }
    };

    private static HttpRequestMessage Get(string path, string? user)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        if (user is not null) { req.Headers.Add(TestAuthHandler.Header, user); }
        return req;
    }

    private async Task<WebApplication> StartAsync(MaquetaHubRegistry registry, IMockupService? mockups = null, IFileStorageService? files = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration["MaquetaNube:CacheDir"] = Path.Combine(_base, "cache");
        builder.Services.AddSingleton(registry);
        builder.Services.AddSingleton(mockups ?? new FakeMockups(new Dictionary<int, MockupServeTarget>(), 0));
        builder.Services.AddSingleton(files ?? new MockupProjectsTestsBase.MemoryFileStorage());
        builder.Services.AddSingleton<MockupSiteCache>();
        builder.Services.AddAuthentication(TestAuthHandler.Scheme)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
        builder.Services.AddAuthorization();
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMaquetaHub();
        app.Run(ctx => ctx.Response.WriteAsync("fallthrough"));
        await app.StartAsync();
        return app;
    }

    /// <summary>Doble de IMockupService: solo resuelve el slug "nube" (actual o por numero).</summary>
    private sealed class FakeMockups(Dictionary<int, MockupServeTarget> versions, int currentNumber) : IMockupService
    {
        public Task<MockupServeTarget?> ResolveForServingAsync(string slug, int? versionNumber, CancellationToken ct = default)
            => Task.FromResult(slug == "nube" && versions.TryGetValue(versionNumber ?? currentNumber, out var t) ? t : null);

        public Task<IReadOnlyList<MockupProjectDto>> ListProjectsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MockupVersionDto>> ListVersionsAsync(Guid projectId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<MockupProjectDto>> CreateProjectAsync(string name, string? slug, string? description, bool isOwnerOrAdmin, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<MockupVersionDto>> UploadVersionAsync(string projectSlug, byte[] zip, string? notes, string uploaderName, bool isOwnerOrAdmin, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<bool>> SetCurrentVersionAsync(Guid projectId, Guid versionId, bool isOwnerOrAdmin, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<bool>> ArchiveProjectAsync(Guid projectId, bool isOwnerOrAdmin, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MockupProjectDto?> GetProjectAsync(string slug, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<MockupProjectDto>> UpdateProjectAsync(Guid projectId, string name, string? description, string? boardName, bool isOwnerOrAdmin, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MockupSolicitudDto>> ListSolicitudesAsync(string slug, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>Autenticacion de prueba: cabecera X-Test-User = "tenant" (con tenant_id) o "platform" (sin tenant).</summary>
    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Scheme = "Test";
        public const string Header = "X-Test-User";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var who = Request.Headers[Header].ToString();
            if (who.Length == 0) { return Task.FromResult(AuthenticateResult.NoResult()); }
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
            if (who == "tenant") { claims.Add(new Claim("tenant_id", Guid.NewGuid().ToString())); }
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme)));
        }
    }
}
