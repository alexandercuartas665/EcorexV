using System.Collections.Concurrent;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
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
/// Implementa <see cref="IBrowserSubAgent"/> (ADR-0039). CONCURRENCIA: cada orden (<see cref="ExecuteAsync"/>)
/// levanta su PROPIA instancia efimera de navegador -ventana + WebView2 + carpeta de datos UNICA- que se
/// cierra al terminar. Asi varias tareas simultaneas corren en paralelo en navegadores SEPARADOS (sesiones
/// aisladas: cookies/cache/login independientes) y no se pisan la pagina entre si. No hay estado compartido
/// entre ordenes, por eso no hace falta candado. WebView2 es un control visual y solo vive en el hilo de UI;
/// <see cref="ExecuteAsync"/> marshala al Dispatcher por dentro, asi que sus llamadores (el canal, el MCP) no
/// dependen de WPF ni saben de hilos.
/// </summary>
public sealed class WebView2BrowserSubAgent : IBrowserSubAgent
{
    /// <summary>
    /// Permiso vigente, EMPUJADO por el servicio (ADR-0039): esta clase corre en la colmena, que no
    /// puede abrir la boveda. Arranca denegando todo y se abre solo cuando el servicio publica su
    /// estado; si la colmena se queda sin servicio, vuelve a cerrarse. Cada instancia efimera captura
    /// este permiso al crearse.
    /// </summary>
    private volatile BrowserPolicy _policy = BrowserPolicy.Denied;

    public void ApplyPolicy(BrowserPolicy policy) => _policy = policy;

    public bool IsAllowed(string? host) => _policy.IsAllowed(host);

    // Locks por PERFIL persistente: WebView2 bloquea el userDataDir mientras esta abierto, asi que dos
    // ordenes con el MISMO SessionKey no pueden compartir la carpeta a la vez -> se serializan por clave.
    // Las ordenes EFIMERAS (SessionKey null) NO tocan este dict y siguen 100% en paralelo.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _profileGates =
        new(StringComparer.OrdinalIgnoreCase);

    private static SemaphoreSlim ProfileGate(string sessionKey)
        => _profileGates.GetOrAdd(SanitizeSessionKey(sessionKey), _ => new SemaphoreSlim(1, 1));

    /// <summary>Normaliza una SessionKey a un nombre de carpeta seguro (<c>[a-z0-9_-]</c>); "default" si
    /// queda vacia. Misma normalizacion la usa el "modo login" de la GUI para apuntar al MISMO perfil.</summary>
    public static string SanitizeSessionKey(string sessionKey)
    {
        var chars = (sessionKey ?? string.Empty).Trim().ToLowerInvariant()
            .Select(ch => ((ch is >= 'a' and <= 'z') || (ch is >= '0' and <= '9') || ch == '-' || ch == '_') ? ch : '-')
            .ToArray();
        var s = new string(chars).Trim('-');
        return string.IsNullOrEmpty(s) ? "default" : s;
    }

    /// <summary>Carpeta de datos PERSISTENTE del perfil de una red (cookies/login sobreviven entre ordenes).
    /// La comparten el scraping logueado y el "modo login" de la GUI (mismo directorio = misma sesion).</summary>
    public static string ProfileDirFor(string sessionKey) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ecorex", "Agent", "WebView2", "profiles", SanitizeSessionKey(sessionKey));

    /// <summary>
    /// Ejecuta la secuencia en una instancia de navegador NUEVA y aislada, y la cierra al terminar.
    /// Seguro desde cualquier hilo: si no estamos en el de UI, salta a el (WebView2 es un control visual).
    /// </summary>
    public Task<BrowserResultMsg> ExecuteAsync(BrowserRequestMsg req)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) { return ExecuteOnUiThreadAsync(req); }
        return dispatcher.InvokeAsync(() => ExecuteOnUiThreadAsync(req)).Task.Unwrap();
    }

    private async Task<BrowserResultMsg> ExecuteOnUiThreadAsync(BrowserRequestMsg req)
    {
        var policy = _policy; // instantanea para esta orden.

        // Consentimiento local (doc 06 s4): sin habilitar por el operador, no se abre el navegador.
        if (!policy.Enabled)
        {
            var blocked = req.Actions
                .Select((a, i) => new BrowserActionResult(i, a.Kind, Ok: false, Error: "Navegador no habilitado por el operador en la colmena."))
                .ToList();
            return new BrowserResultMsg(req.CorrelationId, false, blocked, "Navegador no habilitado por el operador.");
        }

        // Con SessionKey (perfil persistente) se serializa por clave: una orden por perfil a la vez (WebView2
        // bloquea la carpeta). Sin SessionKey (efimero) no hay lock: cada orden tiene su carpeta unica.
        var gate = string.IsNullOrWhiteSpace(req.SessionKey) ? null : ProfileGate(req.SessionKey!);
        if (gate is not null) { await gate.WaitAsync(); }
        try
        {
            // Instancia para esta orden: PERSISTENTE si trae SessionKey (reusa login), EFIMERA si no. Se
            // cierra pase lo que pase (finally); solo la efimera borra su carpeta.
            BrowserInstance instance;
            try { instance = await BrowserInstance.CreateAsync(req.CorrelationId, policy, req.SessionKey); }
            catch (Exception ex)
            {
                var failed = req.Actions
                    .Select((a, i) => new BrowserActionResult(i, a.Kind, Ok: false, Error: $"No se pudo abrir el navegador: {ex.Message}"))
                    .ToList();
                return new BrowserResultMsg(req.CorrelationId, false, failed, "No se pudo abrir el navegador.");
            }

            try
            {
                var results = new List<BrowserActionResult>(req.Actions.Count);
                // Si un Navigate falla (dominio fuera de la allow-list, red, etc.), NO se ejecutan las
                // acciones siguientes: se marcan "omitida". Evita el "CoreWebView2 ... disposed" que salta
                // cuando un Screenshot/Extract corre tras una navegacion que nunca cargo.
                var aborted = false;
                for (var i = 0; i < req.Actions.Count; i++)
                {
                    var action = req.Actions[i];
                    if (aborted)
                    {
                        results.Add(new BrowserActionResult(i, action.Kind, Ok: false, Error: "omitida: el Navigate previo fallo"));
                        continue;
                    }
                    BrowserActionResult r;
                    try { r = await instance.RunActionAsync(i, action); }
                    catch (Exception ex) { r = new BrowserActionResult(i, action.Kind, Ok: false, Error: ex.Message); }
                    results.Add(r);
                    if (action.Kind == BrowserActionKind.Navigate && !r.Ok) { aborted = true; }
                }
                return new BrowserResultMsg(req.CorrelationId, results.All(r => r.Ok), results);
            }
            finally
            {
                instance.Close();
            }
        }
        finally
        {
            gate?.Release();
        }
    }

    /// <summary>Un navegador efimero y aislado para UNA orden: su propia ventana, su propio WebView2 y su
    /// propia carpeta de datos (sesion independiente). Sin estado compartido con otras ordenes.</summary>
    private sealed class BrowserInstance
    {
        // Contador para cascadear las ventanas cuando varias ordenes abren navegador a la vez (solo estetico;
        // se toca solo en el hilo de UI). Asi el operador VE que se abrieron varias instancias.
        private static int _openCount;

        private readonly Window _window;
        private readonly WpfWebView2 _web;
        private readonly BrowserPolicy _policy;
        private readonly string _userDataDir;
        private readonly bool _persistent;
        private readonly List<DownloadRecord> _downloads = new();

        private BrowserInstance(Window window, WpfWebView2 web, BrowserPolicy policy, string userDataDir, bool persistent)
        {
            _window = window;
            _web = web;
            _policy = policy;
            _userDataDir = userDataDir;
            _persistent = persistent;
        }

        private sealed record DownloadRecord(string Uri, string? Path, DateTimeOffset At);

        public static async Task<BrowserInstance> CreateAsync(string correlationId, BrowserPolicy policy, string? sessionKey = null)
        {
            var web = new WpfWebView2();
            var shortId = correlationId.Length > 6 ? correlationId[..6] : correlationId;
            var slot = _openCount++ % 6; // cascada al abrir varias a la vez (solo estetico).
            var window = new Window
            {
                Title = $"ECOREX - Navegador #{shortId}",
                Width = 1100,
                Height = 720,
                Content = web,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 60 + slot * 48,
                Top = 40 + slot * 40,
            };
            window.Show();

            // Carpeta de datos: PERSISTENTE por red si viene SessionKey (cookies/login sobreviven -> scraping
            // logueado), o EFIMERA y unica por orden si no (sesion desechable, comportamiento historico). El
            // perfil persistente guarda cookies = acceso a la cuenta, asi que se endurece su ACL (best-effort).
            var persistent = !string.IsNullOrWhiteSpace(sessionKey);
            string userData;
            if (persistent)
            {
                userData = ProfileDirFor(sessionKey!);
                Directory.CreateDirectory(userData);
                TryHardenProfileAcl(userData);
            }
            else
            {
                userData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Ecorex", "Agent", "WebView2", "sessions", $"s-{shortId}-{Guid.NewGuid():N}");
                Directory.CreateDirectory(userData);
            }

            var instance = new BrowserInstance(window, web, policy, userData, persistent);
            var env = await CoreWebView2Environment.CreateAsync(null, userData, null);
            await web.EnsureCoreWebView2Async(env);

            web.CoreWebView2.DownloadStarting += (_, e) =>
            {
                instance._downloads.Add(new DownloadRecord(e.DownloadOperation.Uri, e.ResultFilePath, DateTimeOffset.UtcNow));
                if (instance._downloads.Count > 50) { instance._downloads.RemoveAt(0); }
            };
            return instance;
        }

        /// <summary>Cierra la ventana y libera el WebView2. Borra la carpeta de datos SOLO si era EFIMERA;
        /// el perfil PERSISTENTE (SessionKey) se conserva para que el login sobreviva a la orden.</summary>
        public void Close()
        {
            try { _web.Dispose(); } catch { /* ya cerrado */ }
            try { _window.Close(); } catch { /* ya cerrada */ }
            if (_persistent) { return; } // perfil logueado: NO se borra.
            try { if (Directory.Exists(_userDataDir)) { Directory.Delete(_userDataDir, recursive: true); } }
            catch { /* el perfil puede quedar bloqueado un instante; se limpia despues */ }
        }

        /// <summary>
        /// Endurece el ACL de la carpeta de perfiles persistentes (best-effort): desactiva la herencia y deja
        /// acceso solo al dueno actual, Administradores y SYSTEM. El perfil guarda cookies de sesion = acceso
        /// a la cuenta; ya vive bajo LocalApplicationData (por-usuario), esto lo refuerza. Si falla (permisos,
        /// FS), se deja el ACL heredado y se sigue -no es un control de correctitud, es defensa en profundidad-.
        /// </summary>
        private static void TryHardenProfileAcl(string dir)
        {
            try
            {
                var di = new DirectoryInfo(dir);
                var sec = di.GetAccessControl();
                sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                void Allow(IdentityReference id) => sec.AddAccessRule(new FileSystemAccessRule(
                    id, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
                var me = WindowsIdentity.GetCurrent().User;
                if (me is not null) { Allow(me); }
                Allow(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
                Allow(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
                di.SetAccessControl(sec);
            }
            catch { /* best-effort */ }
        }

        public async Task<BrowserActionResult> RunActionAsync(int index, BrowserAction a)
        {
            var web = _web;
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

                case BrowserActionKind.ExtractReadable:
                {
                    // Misma reja que Html (JS FIJO del agente, sin firma). Devuelve CONTENIDO LEGIBLE ya
                    // renderizado (texto visible + labels de resultados + enlaces) en vez de outerHTML crudo,
                    // con auto-scroll para feeds perezosos (Maps/SPA). Resuelve la extraccion de contactos.
                    if (!CurrentHostAllowed(out var host))
                    {
                        return Fail(index, a, $"Extraccion no permitida en este dominio: {host}");
                    }

                    // Espera inicial para que el SPA pinte, luego scroll al feed N veces con pausa entre cada uno.
                    await Task.Delay(1500);
                    var rounds = a.ScrollRounds is > 0 ? a.ScrollRounds.Value : 0;
                    var between = a.WaitMs is > 0 ? a.WaitMs.Value : 1200;
                    for (var r = 0; r < rounds; r++)
                    {
                        await web.CoreWebView2.ExecuteScriptAsync(ScrollFeedJs);
                        await Task.Delay(between);
                    }

                    // ExecuteScriptAsync entrega el resultado como cadena JSON-encoded ("...{...}..."): se
                    // decodifica UNA vez para que el backend reciba el JSON {title,url,text,items,links} limpio.
                    var extracted = await web.CoreWebView2.ExecuteScriptAsync(ExtractReadableJs);
                    var decoded = extracted;
                    try { decoded = JsonSerializer.Deserialize<string>(extracted) ?? extracted; }
                    catch { /* si no era literal JSON, se deja crudo */ }
                    return await MaybeShot(index, a, decoded);
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

        private bool CurrentHostAllowed(out string host)
        {
            host = string.Empty;
            var src = _web.CoreWebView2?.Source;
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
                _web.NavigationCompleted -= Handler;
                tcs.TrySetResult(e.IsSuccess);
            }
            _web.NavigationCompleted += Handler;
            _web.CoreWebView2.Navigate(url);
            return tcs.Task;
        }

        private async Task<string> CaptureAsync()
        {
            using var ms = new MemoryStream();
            await _web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
            return Convert.ToBase64String(ms.ToArray());
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

        private static BrowserActionResult Fail(int index, BrowserAction a, string error)
            => new(index, a.Kind, Ok: false, Error: error);

        private static string JsString(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        // ---- JS FIJO del agente para ExtractReadable (no viene del servidor, por eso no requiere firma) ----

        // Scroll al contenedor de resultados (feed) o al documento: hace que los feeds perezosos (Maps/SPA)
        // carguen mas items antes de extraer.
        private const string ScrollFeedJs =
            """
            (function(){var f=document.querySelector('[role="feed"]')||document.scrollingElement||document.body;
             if(f){f.scrollBy(0, f.scrollHeight);} return true;})()
            """;

        // Extractor: texto visible + labels de resultados (aria-label) + enlaces + PERSONAS de LinkedIn,
        // todo acotado. Devuelve JSON {title,url,text,items,links,images,profiles} listo para el modelo.
        // 'profiles' solo se llena en LinkedIn (enlaces /in/): nombre y headline separados (quitando el
        // grado de conexion 1er/2do/3ro). En FB/IG queda vacio (la firmografia sale por text+links).
        private const string ExtractReadableJs =
            """
            (function(){
              function c(s){return (s||'').replace(/\s+/g,' ').trim();}
              var items=[];
              document.querySelectorAll('[role="article"],[role="feed"] [aria-label]').forEach(function(el){
                var l=c(el.getAttribute('aria-label')); if(l && items.indexOf(l)<0 && items.length<80) items.push(l);
              });
              var seen={}, links=[], as=document.querySelectorAll('a[href]');
              for(var i=0;i<as.length && links.length<120;i++){
                var t=c(as[i].innerText), h=as[i].href; if(!t||t.length<2) continue;
                var k=t+'|'+h; if(seen[k]) continue; seen[k]=1; links.push({t:t.slice(0,120),h:h});
              }
              var imgs=[], im=document.querySelectorAll('img');
              for(var j=0;j<im.length && imgs.length<40;j++){
                var s=im[j].src||''; if(!/^https?:/.test(s)) continue;
                if((im[j].naturalWidth||im[j].width||0) < 40) continue;
                imgs.push({src:s, alt:c(im[j].alt).slice(0,120)});
              }
              // PERSONAS (LinkedIn): enlaces a /in/ con nombre + headline separados.
              function degIdx(s){ var m=s.match(/\s(1er|2do|3ro|1st|2nd|3rd|1°|2°|3°)\b/i); return m?m.index:-1; }
              var profs=[], seenP={};
              document.querySelectorAll('a[href*="/in/"]').forEach(function(a){
                var h=(a.href||'').split('?')[0]; if(!/linkedin\.com\/in\//.test(h)) return;
                if(seenP[h]) return; seenP[h]=1;
                var raw=c(a.innerText); if(!raw||raw.length<2) return;
                var i=degIdx(raw);
                var name = i>0 ? raw.slice(0,i).trim() : raw;
                var head = i>0 ? raw.slice(i).replace(/^(\s|1er|2do|3ro|1st|2nd|3rd|·|•|1°|2°|3°)+/gi,'').trim() : '';
                if(profs.length<60) profs.push({name:name.slice(0,80), headline:head.slice(0,120), url:h});
              });
              return JSON.stringify({title:document.title,url:location.href,
                text:c(document.body?document.body.innerText:'').slice(0,9000), items:items, links:links, images:imgs, profiles:profs});
            })()
            """;
    }
}
