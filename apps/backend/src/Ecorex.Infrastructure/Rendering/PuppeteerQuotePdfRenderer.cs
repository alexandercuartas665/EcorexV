using Ecorex.Application.Common;
using Microsoft.Extensions.Configuration;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace Ecorex.Infrastructure.Rendering;

/// <summary>
/// Render de PDF de cotizaciones con un Chromium headless (PuppeteerSharp). Navega a la pagina publica
/// de la cotizacion e imprime a PDF. El ejecutable de Chrome se resuelve por env (ECOREX_CHROME_PATH),
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
        await using var browser = await LaunchAsync();
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
            // Margen de pagina ("padding" del formato) para que el contenido no quede pegado al borde.
            MarginOptions = new MarginOptions { Top = "12mm", Bottom = "12mm", Left = "10mm", Right = "10mm" }
        });
    }

    public async Task<byte[]> RenderUrlToImageAsync(string url, CancellationToken cancellationToken = default)
    {
        await using var browser = await LaunchAsync();
        await using var page = await browser.NewPageAsync();
        // Ancho fijo acorde al diseno de la cotizacion (tarjeta ~800px) para una imagen consistente y nitida.
        await page.SetViewportAsync(new ViewPortOptions { Width = 820, Height = 1160, DeviceScaleFactor = 2 });
        await page.GoToAsync(url, new NavigationOptions
        {
            WaitUntil = new[] { WaitUntilNavigation.Networkidle0 },
            Timeout = 30000
        });
        return await page.ScreenshotDataAsync(new ScreenshotOptions
        {
            FullPage = true,
            Type = ScreenshotType.Png
        });
    }

    // Lanza un Chromium headless (sistema si esta configurado, o el de BrowserFetcher).
    private async Task<IBrowser> LaunchAsync()
    {
        var options = new LaunchOptions
        {
            Headless = true,
            Args = new[] { "--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage" }
        };

        var exe = ConfiguredChromePath();
        if (exe is not null)
        {
            options.ExecutablePath = exe;
        }
        else
        {
            await new BrowserFetcher().DownloadAsync();
        }

        return await Puppeteer.LaunchAsync(options);
    }

    private string? ConfiguredChromePath()
    {
        var configured = Environment.GetEnvironmentVariable("ECOREX_CHROME_PATH") ?? _config["Chrome:ExecutablePath"];
        return !string.IsNullOrWhiteSpace(configured) && File.Exists(configured) ? configured : null;
    }
}
