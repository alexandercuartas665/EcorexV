using System.IO;
using System.Text.Json;
using System.Windows;
using Ecorex.Agent.Core.Services;
using Ecorex.Contracts.Agent;
using Microsoft.Web.WebView2.Core;
using WpfWebView2 = Microsoft.Web.WebView2.Wpf.WebView2;

namespace Ecorex.Agent.Gui.Services;

/// <summary>
/// Sub-agente Navegador (doc 06 s3.2 / prior-art doc 07): WebView2 (Edge/Chromium embebido) que
/// ejecuta una secuencia de acciones TIPADAS (navigate/eval/wait/screenshot/html). Seguridad (doc 06
/// s4): la <see cref="BrowserAllowList"/> LOCAL gobierna a que dominios se puede navegar y en cuales
/// se permite inyectar JS; nada fuera de la lista, aunque la nube lo pida.
///
/// Implementa <see cref="IBrowserSubAgent"/> (ADR-0039). WebView2 es un control visual y solo vive en
/// el hilo de UI, pero eso es asunto SUYO: <see cref="ExecuteAsync"/> marshala al Dispatcher por
/// dentro, asi que sus llamadores (el canal, el MCP) no dependen de WPF ni saben de hilos.
/// </summary>
public sealed class WebView2BrowserSubAgent : IBrowserSubAgent
{
    private readonly List<DownloadRecord> _downloads = new();
    private Window? _window;
    private WpfWebView2? _web;

    private sealed record DownloadRecord(string Uri, string? Path, DateTimeOffset At);

    /// <summary>
    /// Permiso vigente, EMPUJADO por el servicio (ADR-0039): esta clase corre en la colmena, que no
    /// puede abrir la boveda. Arranca denegando todo y se abre solo cuando el servicio publica su
    /// estado; si la colmena se queda sin servicio, vuelve a cerrarse.
    /// </summary>
    private volatile BrowserPolicy _policy = BrowserPolicy.Denied;

    public void ApplyPolicy(BrowserPolicy policy) => _policy = policy;

    public bool IsAllowed(string? host) => _policy.IsAllowed(host);

    /// <summary>
    /// Ejecuta la secuencia. Seguro desde cualquier hilo: si no estamos en el de UI, salta a el
    /// (WebView2 es un control visual). Antes esto lo hacia cada llamador; ahora es asunto interno.
    /// </summary>
    public Task<BrowserResultMsg> ExecuteAsync(BrowserRequestMsg req)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) { return ExecuteOnUiThreadAsync(req); }
        return dispatcher.InvokeAsync(() => ExecuteOnUiThreadAsync(req)).Task.Unwrap();
    }

    private async Task<BrowserResultMsg> ExecuteOnUiThreadAsync(BrowserRequestMsg req)
    {
        // Consentimiento local (doc 06 s4): sin habilitar por el operador, no se abre el navegador.
        if (!_policy.Enabled)
        {
            var blocked = req.Actions
                .Select((a, i) => new BrowserActionResult(i, a.Kind, Ok: false, Error: "Navegador no habilitado por el operador en la colmena."))
                .ToList();
            return new BrowserResultMsg(req.CorrelationId, false, blocked, "Navegador no habilitado por el operador.");
        }

        await EnsureReadyAsync();
        var results = new List<BrowserActionResult>(req.Actions.Count);
        for (var i = 0; i < req.Actions.Count; i++)
        {
            var action = req.Actions[i];
            try
            {
                results.Add(await RunActionAsync(i, action));
            }
            catch (Exception ex)
            {
                results.Add(new BrowserActionResult(i, action.Kind, Ok: false, Error: ex.Message));
            }
        }
        return new BrowserResultMsg(req.CorrelationId, results.All(r => r.Ok), results);
    }

    private async Task<BrowserActionResult> RunActionAsync(int index, BrowserAction a)
    {
        var web = _web!;
        switch (a.Kind)
        {
            case BrowserActionKind.Navigate:
            {
                if (string.IsNullOrWhiteSpace(a.Url) || !Uri.TryCreate(a.Url, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    return Fail(index, a, "URL invalida (solo http/https).");
                }
                if (!_policy.IsAllowed(uri.Host))
                {
                    return Fail(index, a, $"Dominio no permitido por la allow-list local: {uri.Host}");
                }
                await NavigateAsync(uri.AbsoluteUri);
                return await MaybeShot(index, a, uri.AbsoluteUri);
            }

            case BrowserActionKind.Eval:
            {
                if (!CurrentHostAllowed(out var host))
                {
                    return Fail(index, a, $"Inyeccion de JS no permitida en este dominio: {host}");
                }
                var result = await web.CoreWebView2.ExecuteScriptAsync(a.Script ?? "null");
                return await MaybeShot(index, a, result);
            }

            case BrowserActionKind.Html:
            {
                if (!CurrentHostAllowed(out var host))
                {
                    return Fail(index, a, $"Lectura de HTML no permitida en este dominio: {host}");
                }
                var script = string.IsNullOrWhiteSpace(a.Selector)
                    ? "document.documentElement.outerHTML"
                    : $"(function(){{var e=document.querySelector({JsString(a.Selector!)});return e?e.outerHTML:''}})()";
                var html = await web.CoreWebView2.ExecuteScriptAsync(script);
                return await MaybeShot(index, a, html);
            }

            case BrowserActionKind.Wait:
            {
                var timeout = a.WaitMs is > 0 ? a.WaitMs.Value : 5000;
                if (!string.IsNullOrWhiteSpace(a.ConditionScript))
                {
                    var deadline = timeout;
                    var elapsed = 0;
                    while (elapsed < deadline)
                    {
                        var r = await web.CoreWebView2.ExecuteScriptAsync(a.ConditionScript!);
                        if (r == "true") { return await MaybeShot(index, a, "true"); }
                        await Task.Delay(100);
                        elapsed += 100;
                    }
                    return Fail(index, a, "La condicion no se cumplio dentro del timeout.");
                }
                await Task.Delay(timeout);
                return await MaybeShot(index, a, "ok");
            }

            case BrowserActionKind.Screenshot:
            {
                var shot = await CaptureAsync();
                return new BrowserActionResult(index, a.Kind, Ok: true, ScreenshotBase64: shot);
            }

            case BrowserActionKind.Mouse:
            {
                if (!CurrentHostAllowed(out var host))
                {
                    return Fail(index, a, $"Acciones de mouse no permitidas en este dominio: {host}");
                }
                var steps = ParseMouseSteps(a.ScriptJson);
                var done = 0;
                foreach (var (mAction, selector, text) in steps)
                {
                    var js = mAction switch
                    {
                        "click" => $"(function(){{var e=document.querySelector({JsString(selector)});if(e){{e.click();return true}}return false}})()",
                        "type" => $"(function(){{var e=document.querySelector({JsString(selector)});if(e){{e.focus();e.value={JsString(text ?? string.Empty)};e.dispatchEvent(new Event('input',{{bubbles:true}}));return true}}return false}})()",
                        _ => "false",
                    };
                    if (await web.CoreWebView2.ExecuteScriptAsync(js) == "true") { done++; }
                    await Task.Delay(120);
                }
                return await MaybeShot(index, a, $"{done}/{steps.Count} pasos");
            }

            case BrowserActionKind.Downloads:
            {
                var json = JsonSerializer.Serialize(_downloads);
                return await MaybeShot(index, a, json);
            }

            default:
                return Fail(index, a, "Accion no soportada.");
        }
    }

    private async Task<BrowserActionResult> MaybeShot(int index, BrowserAction a, string? value)
    {
        string? shot = a.Screenshot ? await CaptureAsync() : null;
        return new BrowserActionResult(index, a.Kind, Ok: true, Value: value, ScreenshotBase64: shot);
    }

    private static BrowserActionResult Fail(int index, BrowserAction a, string error)
        => new(index, a.Kind, Ok: false, Error: error);

    private bool CurrentHostAllowed(out string host)
    {
        host = string.Empty;
        var src = _web?.CoreWebView2?.Source;
        if (!string.IsNullOrEmpty(src) && Uri.TryCreate(src, UriKind.Absolute, out var uri))
        {
            host = uri.Host;
            return _policy.IsAllowed(uri.Host);
        }
        return false;
    }

    private Task NavigateAsync(string url)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? s, CoreWebView2NavigationCompletedEventArgs e)
        {
            _web!.NavigationCompleted -= Handler;
            tcs.TrySetResult(e.IsSuccess);
        }
        _web!.NavigationCompleted += Handler;
        _web.CoreWebView2.Navigate(url);
        return tcs.Task;
    }

    private async Task<string> CaptureAsync()
    {
        using var ms = new MemoryStream();
        await _web!.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
        return Convert.ToBase64String(ms.ToArray());
    }

    private async Task EnsureReadyAsync()
    {
        if (_web is not null) { return; }

        _web = new WpfWebView2();
        _window = new Window
        {
            Title = "ECOREX - Sub-agente Navegador",
            Width = 1200,
            Height = 800,
            Content = _web,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        _window.Show();

        // Carpeta de datos propia (no escribe junto al exe).
        var userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ecorex", "Agent", "WebView2");
        Directory.CreateDirectory(userData);
        var env = await CoreWebView2Environment.CreateAsync(null, userData, null);
        await _web.EnsureCoreWebView2Async(env);

        // Historial de descargas (browser.downloads).
        _web.CoreWebView2.DownloadStarting += (_, e) =>
        {
            _downloads.Add(new DownloadRecord(e.DownloadOperation.Uri, e.ResultFilePath, DateTimeOffset.UtcNow));
            if (_downloads.Count > 50) { _downloads.RemoveAt(0); }
        };
    }

    private static List<(string Action, string Selector, string? Text)> ParseMouseSteps(string? json)
    {
        var steps = new List<(string, string, string?)>();
        if (string.IsNullOrWhiteSpace(json)) { return steps; }
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) { return steps; }
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var action = el.TryGetProperty("action", out var a) ? a.GetString() ?? string.Empty : string.Empty;
                var selector = el.TryGetProperty("selector", out var s) ? s.GetString() ?? string.Empty : string.Empty;
                var text = el.TryGetProperty("text", out var t) ? t.GetString() : null;
                steps.Add((action, selector, text));
            }
        }
        catch { /* JSON invalido -> sin pasos */ }
        return steps;
    }

    private static string JsString(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
