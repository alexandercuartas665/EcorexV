using System.Runtime.ExceptionServices;
using Ecorex.Application.Common;
using Microsoft.Extensions.Configuration;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace Ecorex.Infrastructure.Rendering;

/// <summary>
/// Render de PDF de cotizaciones con un Chromium headless (PuppeteerSharp). Navega a la pagina publica
/// de la cotizacion e imprime a PDF. El ejecutable de Chrome se resuelve por env (ECOREX_CHROME_PATH),
/// rutas comunes del SO, o se descarga con BrowserFetcher como ultimo recurso.
///
/// Robustez (ADR-0162): en prod se observaron fallos intermitentes TargetClosedException
/// ("Target closed" / "Session closed" por "Network.policyUpdated. Timeout of 1000 ms") cuando caen
/// varios PDFs a la vez y Chromium se cuelga en el arranque. Dos defensas aqui:
///   1) Limite de concurrencia (semaforo estatico, compartido entre instancias scoped): no se lanzan
///      mas de N Chromium a la vez, evitando el pico de memoria que mata los targets.
///   2) Reintento de los fallos transitorios de Puppeteer, relanzando un navegador FRESCO en cada intento.
/// Ambos se afinan por env: ECOREX_PDF_MAX_CONCURRENCY (default 2) y ECOREX_PDF_RENDER_RETRIES (default 2).
/// </summary>
public sealed class PuppeteerQuotePdfRenderer : IQuotePdfRenderer
{
    // Tope de renders simultaneos. Estatico porque el servicio es scoped (una instancia por request) y el
    // limite debe ser de proceso, no por request. Se fija una sola vez al cargar el tipo.
    private static readonly int MaxConcurrent =
        int.TryParse(Environment.GetEnvironmentVariable("ECOREX_PDF_MAX_CONCURRENCY"), out var mc) && mc > 0 ? mc : 2;

    private static readonly SemaphoreSlim Gate = new(MaxConcurrent, MaxConcurrent);

    // Reintentos ante fallos transitorios (ademas del primer intento). 2 => hasta 3 intentos.
    private static readonly int Retries =
        int.TryParse(Environment.GetEnvironmentVariable("ECOREX_PDF_RENDER_RETRIES"), out var rt) && rt >= 0 ? rt : 2;

    // Backoff base entre intentos; crece linealmente (400ms, 800ms, ...).
    private const int RetryBaseDelayMs = 400;

    private readonly IConfiguration _config;

    public PuppeteerQuotePdfRenderer(IConfiguration config)
    {
        _config = config;
    }

    public Task<byte[]> RenderUrlToPdfAsync(string url, CancellationToken cancellationToken = default) =>
        RunAsync(async browser =>
        {
            await using var page = await browser.NewPageAsync();
            await page.GoToAsync(url, new NavigationOptions
            {
                WaitUntil = new[] { WaitUntilNavigation.Networkidle0 },
                Timeout = 30000
            });
            return await page.PdfDataAsync(await BuildPdfOptionsAsync(page));
        }, cancellationToken);

    public Task<byte[]> RenderHtmlToPdfAsync(string html, CancellationToken cancellationToken = default) =>
        RunAsync(async browser =>
        {
            await using var page = await browser.NewPageAsync();
            // SetContent en vez de GoTo: el HTML ya viene renderizado (IFormTemplateRenderService), no hay que
            // navegar a un endpoint. Networkidle0 espera a que carguen imagenes/fuentes embebidas por URL.
            await page.SetContentAsync(html, new NavigationOptions
            {
                WaitUntil = new[] { WaitUntilNavigation.Networkidle0 },
                Timeout = 30000
            });
            return await page.PdfDataAsync(await BuildPdfOptionsAsync(page));
        }, cancellationToken);

    public Task<byte[]> RenderUrlToImageAsync(string url, CancellationToken cancellationToken = default) =>
        RunAsync(async browser =>
        {
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
        }, cancellationToken);

    // Nucleo comun: cap de concurrencia + reintento con navegador fresco por intento. Cada intento abre y
    // CIERRA su propio Chromium (await using), asi un target muerto no contamina el siguiente intento.
    private async Task<byte[]> RunAsync(Func<IBrowser, Task<byte[]>> work, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            PuppeteerException? last = null;
            for (var attempt = 0; attempt <= Retries; attempt++)
            {
                try
                {
                    await using var browser = await LaunchAsync();
                    return await work(browser);
                }
                catch (PuppeteerException ex)
                {
                    // Transitorio tipico: TargetClosedException / NavigationException por un Chromium que
                    // se cayo en el arranque. Reintentamos con uno nuevo; si es el ultimo intento, relanzamos.
                    last = ex;
                    if (attempt < Retries)
                    {
                        await Task.Delay(RetryBaseDelayMs * (attempt + 1), cancellationToken);
                    }
                }
            }

            // Se agotaron los intentos: relanzar la ultima excepcion preservando su stack original.
            ExceptionDispatchInfo.Capture(last!).Throw();
            return default!; // inalcanzable; satisface al compilador.
        }
        finally
        {
            Gate.Release();
        }
    }

    // Arma las PdfOptions leyendo el opt-in de paginador de la plantilla. Chromium al imprimir a PDF IGNORA
    // los contadores CSS @page (counter(page)/counter(pages)); la unica via fiable es el footerTemplate de
    // Puppeteer con <span class="pageNumber"></span> / <span class="totalPages">.
    //
    // Contrato (opt-in por plantilla, self-serve): si el <head> trae
    //   <meta name="ecorex-pdf-footer" content="Pagina {pageNumber} de {totalPages}">
    // se activa el pie con esa etiqueta (los tokens {pageNumber}/{totalPages} se sustituyen por los spans de
    // Puppeteer). Si la plantilla NO trae el meta, el PDF sale igual que hoy (sin footer). No afecta a otras
    // plantillas ni tenants.
    private static async Task<PdfOptions> BuildPdfOptionsAsync(IPage page)
    {
        var footerRaw = await page.EvaluateFunctionAsync<string?>(
            "() => document.querySelector('meta[name=\"ecorex-pdf-footer\"]')?.content ?? null");

        // Sin meta -> PdfOptions como hoy (Bottom=12mm, sin header/footer).
        if (string.IsNullOrWhiteSpace(footerRaw))
        {
            return new PdfOptions
            {
                Format = PaperFormat.A4,
                PrintBackground = true,
                // Margen de pagina ("padding" del formato) para que el contenido no quede pegado al borde.
                MarginOptions = new MarginOptions { Top = "12mm", Bottom = "12mm", Left = "10mm", Right = "10mm" }
            };
        }

        // Escapamos la etiqueta (es texto de la plantilla) y luego insertamos los spans de Puppeteer, que son
        // los que el motor reemplaza por el numero de pagina / total al imprimir.
        var footerHtml = System.Net.WebUtility.HtmlEncode(footerRaw)
            .Replace("{pageNumber}", "<span class=\"pageNumber\"></span>")
            .Replace("{totalPages}", "<span class=\"totalPages\"></span>");

        return new PdfOptions
        {
            Format = PaperFormat.A4,
            PrintBackground = true,
            DisplayHeaderFooter = true,
            // HeaderTemplate vacio OBLIGATORIO: con DisplayHeaderFooter=true y sin el, Chromium pinta su header
            // por defecto (titulo + fecha). El font-size va inline: Chromium ignora CSS externo en el pie.
            HeaderTemplate = "<span></span>",
            FooterTemplate = $"<div style=\"width:100%;font-size:8px;color:#444;text-align:center;font-family:Arial,Helvetica,sans-serif;padding:0 10mm\">{footerHtml}</div>",
            // El pie se dibuja DENTRO del margen: Bottom sube a 16mm solo con footer, para que no pise el contenido.
            MarginOptions = new MarginOptions { Top = "12mm", Bottom = "16mm", Left = "10mm", Right = "10mm" }
        };
    }

    // Lanza un Chromium headless (sistema si esta configurado, o el de BrowserFetcher).
    private async Task<IBrowser> LaunchAsync()
    {
        var options = new LaunchOptions
        {
            Headless = true,
            // --disable-gpu / --no-zygote endurecen el headless en contenedor (menos procesos hijos que se
            // cuelguen). --disable-dev-shm-usage evita el /dev/shm de 64MB por defecto de Docker.
            Args = new[]
            {
                "--no-sandbox",
                "--disable-setuid-sandbox",
                "--disable-dev-shm-usage",
                "--disable-gpu",
                "--no-zygote"
            }
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
