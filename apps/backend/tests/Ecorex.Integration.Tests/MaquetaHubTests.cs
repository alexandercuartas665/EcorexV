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

    private static async Task<WebApplication> StartAsync(MaquetaHubRegistry registry)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(registry);
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
