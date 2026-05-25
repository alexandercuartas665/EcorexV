using CubotTravels.Application.Common;
using Microsoft.Extensions.Configuration;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace CubotTravels.Infrastructure.Rendering;

/// <summary>
/// Render de PDF de cotizaciones con un Chromium headless (PuppeteerSharp). Navega a la pagina publica
/// de la cotizacion e imprime a PDF. El ejecutable de Chrome se resuelve por env (CUBOT_CHROME_PATH),
/// rutas comunes del SO, o se descarga con BrowserFetcher como ultimo recurso.
/// </summary>
public sealed class PuppeteerQuotePdfRenderer : IQuotePdfRenderer
{
    private readonly IConfiguration _config;

    public PuppeteerQuotePdfRenderer(IConfiguration config)
    {
        _config = config;
    }

    public async Task<byte[]> RenderUrlToPdfAsync(string url, CancellationToken cancellationToken = default)
    {
        var options = new LaunchOptions
        {
            Headless = true,
            Args = new[] { "--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage" }
        };

        // Solo se usa un Chrome del sistema si se indica explicitamente (CUBOT_CHROME_PATH / Chrome:ExecutablePath);
        // de lo contrario se descarga el Chromium que corresponde a esta version de PuppeteerSharp (evita
        // incompatibilidades de protocolo CDP con un Chrome demasiado nuevo).
        var exe = ConfiguredChromePath();
        if (exe is not null)
        {
            options.ExecutablePath = exe;
        }
        else
        {
            await new BrowserFetcher().DownloadAsync();
        }

        await using var browser = await Puppeteer.LaunchAsync(options);
        await using var page = await browser.NewPageAsync();
        await page.GoToAsync(url, new NavigationOptions
        {
            WaitUntil = new[] { WaitUntilNavigation.Networkidle0 },
            Timeout = 30000
        });
        return await page.PdfDataAsync(new PdfOptions
        {
            Format = PaperFormat.A4,
            PrintBackground = true,
            MarginOptions = new MarginOptions { Top = "0", Bottom = "0", Left = "0", Right = "0" }
        });
    }

    private string? ConfiguredChromePath()
    {
        var configured = Environment.GetEnvironmentVariable("CUBOT_CHROME_PATH") ?? _config["Chrome:ExecutablePath"];
        return !string.IsNullOrWhiteSpace(configured) && File.Exists(configured) ? configured : null;
    }
}
