using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// Fixture de coleccion de la suite E2E (ADR-0019). Resuelve la URL base de la consola:
/// 1) Si ECOREX_E2E_BASEURL esta definida, usa esa app ya corriendo (modo CI / manual).
/// 2) Si no, ARRANCA la consola como conveniencia local: dotnet run --no-build del
///    proyecto Ecorex.SuperAdmin en el primer puerto libre 5250+, con
///    ASPNETCORE_ENVIRONMENT=Development y ECOREX_DB_CONNECTION al Postgres dev (5442),
///    y espera /login 200 (el arranque aplica migraciones + seed demo, tarda unos segundos).
/// Si nada de eso es posible (sin build previo, sin Postgres, sin binarios de Playwright),
/// la suite completa se SALTA con un motivo claro en vez de fallar (Skip.If en cada test).
/// Al terminar mata el proceso de la app (solo si lo arranco este fixture).
/// </summary>
public sealed class E2eAppFixture : IAsyncLifetime
{
    /// <summary>Cadena por defecto al Postgres dev del repo (deploy/docker, puerto 5442).</summary>
    public const string DefaultConnectionString =
        "Host=localhost;Port=5442;Database=ecorex_dev;Username=ecorex;Password=EcorexDev2026pg";

    public string? BaseUrl { get; private set; }
    public string? SkipReason { get; private set; }

    /// <summary>Cadena usada por el backdoor de datos (misma BD que usa la app arrancada).</summary>
    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("ECOREX_DB_CONNECTION") ?? DefaultConnectionString;

    public IBrowser Browser => _browser
        ?? throw new InvalidOperationException("Browser no inicializado (revisa SkipReason).");

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private Process? _app;
    private readonly StringBuilder _appLog = new();

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("ECOREX_E2E_BASEURL");
        if (!string.IsNullOrWhiteSpace(external))
        {
            var url = external.TrimEnd('/');
            if (await IsLoginUpAsync(url, TimeSpan.FromSeconds(15)))
            {
                BaseUrl = url;
            }
            else
            {
                SkipReason = $"ECOREX_E2E_BASEURL={url} esta definida pero GET /login no respondio 200. " +
                             "Arranca la consola en esa URL o quita la variable para el arranque automatico.";
                return;
            }
        }
        else
        {
            await TryStartAppAsync();
            if (BaseUrl is null)
            {
                return; // SkipReason ya quedo explicado.
            }
        }

        try
        {
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            // El timeout de contexto NO aplica a Assertions.Expect (default 5s): sobre un
            // circuito Blazor Server con guardados reales se queda corto.
            Assertions.SetDefaultExpectTimeout(15_000);
        }
        catch (Exception ex)
        {
            SkipReason = "No se pudo lanzar Chromium de Playwright. Instala los binarios con: " +
                         "pwsh bin/Debug/net10.0/playwright.ps1 install chromium (ver README del proyecto). " +
                         $"Detalle: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }
        _playwright?.Dispose();
        StopApp();
    }

    // ---- Arranque automatico local ----

    private async Task TryStartAppAsync()
    {
        var backendDir = FindBackendDir();
        if (backendDir is null)
        {
            SkipReason = "No se encontro apps/backend/Ecorex.sln subiendo desde el directorio del test.";
            return;
        }
        var superAdminProj = Path.Combine(backendDir, "src", "Ecorex.SuperAdmin");
        if (!Directory.Exists(superAdminProj))
        {
            SkipReason = $"No existe el proyecto de la consola en {superAdminProj}.";
            return;
        }

        var port = FindFreePort(5250, 5299);
        if (port is null)
        {
            SkipReason = "No hay puertos libres en el rango 5250-5299 para arrancar la consola.";
            return;
        }
        var url = $"http://localhost:{port}";

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = backendDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        // --no-build: exige `dotnet build Ecorex.sln` previo (la suite E2E ya compila la
        // solucion al compilarse a si misma via ProjectReference, asi que en la practica
        // el binario existe cuando corren los tests).
        foreach (var arg in new[] { "run", "--project", superAdminProj, "--no-build", "--no-launch-profile", "--urls", url })
        {
            psi.ArgumentList.Add(arg);
        }
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        psi.Environment["ECOREX_DB_CONNECTION"] = ConnectionString;

        try
        {
            _app = Process.Start(psi);
        }
        catch (Exception ex)
        {
            SkipReason = $"No se pudo lanzar 'dotnet run' de la consola: {ex.Message}";
            return;
        }
        if (_app is null)
        {
            SkipReason = "Process.Start devolvio null al arrancar la consola.";
            return;
        }
        _app.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (_appLog) { _appLog.AppendLine(e.Data); } } };
        _app.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (_appLog) { _appLog.AppendLine(e.Data); } } };
        _app.BeginOutputReadLine();
        _app.BeginErrorReadLine();

        // El primer arranque aplica migraciones + seeders demo: margen generoso.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            if (_app.HasExited)
            {
                SkipReason = "La consola termino antes de responder /login (probablemente falta " +
                             "`dotnet build Ecorex.sln` o el Postgres dev 5442 no esta arriba). " +
                             $"Ultimas lineas: {TailLog()}";
                return;
            }
            if (await IsLoginUpAsync(url, TimeSpan.FromSeconds(3)))
            {
                BaseUrl = url;
                return;
            }
            await Task.Delay(1000);
        }

        SkipReason = $"La consola no respondio /login en 120s en {url}. Ultimas lineas: {TailLog()}";
        StopApp();
    }

    private void StopApp()
    {
        if (_app is null) { return; }
        try
        {
            if (!_app.HasExited)
            {
                _app.Kill(entireProcessTree: true);
                _app.WaitForExit(10_000);
            }
        }
        catch
        {
            // Mejor esfuerzo: el proceso pudo terminar entre el chequeo y el Kill.
        }
        finally
        {
            _app.Dispose();
            _app = null;
        }
    }

    private string TailLog()
    {
        lock (_appLog)
        {
            var text = _appLog.ToString();
            return text.Length <= 600 ? text : text[^600..];
        }
    }

    private static async Task<bool> IsLoginUpAsync(string baseUrl, TimeSpan timeout)
    {
        using var http = new HttpClient { Timeout = timeout };
        try
        {
            var response = await http.GetAsync($"{baseUrl}/login");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindBackendDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Ecorex.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return null;
    }

    private static int? FindFreePort(int from, int to)
    {
        for (var port = from; port <= to; port++)
        {
            try
            {
                var listener = new TcpListener(System.Net.IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return port;
            }
            catch (SocketException)
            {
                // Puerto ocupado: probar el siguiente.
            }
        }
        return null;
    }
}

[CollectionDefinition("e2e")]
public sealed class E2eCollection : ICollectionFixture<E2eAppFixture>
{
    // Todas las clases de test comparten la app y el browser, y corren en secuencia
    // (misma coleccion xunit): evita carreras entre wizards/toasts de tests distintos.
}
