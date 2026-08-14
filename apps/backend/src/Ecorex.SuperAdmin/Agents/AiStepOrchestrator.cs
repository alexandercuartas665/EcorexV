using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ecorex.Application.Tenancy;
using Ecorex.Contracts.Agent;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>Insumos de un paso de IA (lo que el operador configuro + a que agente/tabla va).</summary>
public sealed record AiStepContext(
    string ClientId, Guid TenantId, string Instruction, Guid TargetContainerId,
    IReadOnlyList<string> ToolAllowList, int MaxSteps, int MaxSeconds, Guid? AiProviderId, string? Secret,
    // Sumidero alternativo para las filas de 'guardar_filas'. Null = el DataContainer por defecto
    // (flujos de extraccion). Las busquedas de contactos pasan un sink propio -> ProspectoScrapeado.
    IScrapeRowSink? SinkOverride = null,
    // Clave de PERFIL persistente del navegador para scraping LOGUEADO (ej. "linkedin"). Null = sesion
    // efimera. Se propaga a cada BrowserRequestMsg que arma el orquestador.
    string? SessionKey = null);

/// <summary>Como quedo el paso de IA.</summary>
public sealed record AiStepOutcome(bool Ok, int Inserted, int Updated, int Deleted, string? Error, int RoundsUsed);

/// <summary>
/// Orquesta un paso de IA (modulo 000730, Ola 4, doc 03 s2): un agente de IA maneja el navegador para
/// cumplir una instruccion en lenguaje natural. No se manda un JS fijo; se corre un bucle de
/// function-calling contra el AI Provider Gateway (`IAiProviderClient.CompleteWithToolsAsync`) donde las
/// TOOLS son acciones de navegador (navegar, leer html, esperar, y -si el operador lo permitio- evaluar
/// JS o hacer clic), ejecutadas en el agente por el canal request/response (`IBrowserActionChannel`).
///
/// La contencion (doc s2) es triple: la ALLOW-LIST de tools (que puede usar el agente), el TOPE de
/// pasos y el TOPE de tiempo. Ademas, como el JS de la IA viaja por el hub (no por el MCP loopback), el
/// servidor lo FIRMA (el agente lo rechazaria sin firma, fail-closed), y la allow-list de DOMINIOS del
/// agente sigue aplicando. El resultado se estructura llamando la tool `guardar_filas`, que ingiere en
/// la tabla destino. Todo consumo se registra en el modulo de tokens (`IAiUsageService`).
/// </summary>
public interface IAiStepOrchestrator
{
    Task<AiStepOutcome> RunAsync(AiStepContext ctx, CancellationToken ct = default);
}

public sealed class AiStepOrchestrator(
    IAiProviderClient ai,
    IAiUsageService usage,
    IAiProviderResolver providerResolver,
    IBrowserActionChannel channel,
    IScrapeRowSink rowSink,
    ILogger<AiStepOrchestrator> log) : IAiStepOrchestrator
{
    private const int MaxHtmlChars = 14000; // tope de lo que un leer_html/evaluar_js le devuelve al modelo.
    private static readonly TimeSpan PerActionTimeout = TimeSpan.FromSeconds(45);

    public async Task<AiStepOutcome> RunAsync(AiStepContext ctx, CancellationToken ct = default)
    {
        // Proveedor/modelo: entre los que habilito el Super Admin (config global, key cifrada). Si no
        // hay ninguno, el paso no puede correr y se dice claro.
        var (choice, providerError) = await providerResolver.ResolveAsync(ctx.AiProviderId, ct);
        if (choice is null) { return Fail(providerError ?? "No hay proveedor de IA."); }

        // Cupo del plan: si es duro y ya se agoto, no se ejecuta (mismo criterio que el chat de agentes).
        var quota = await usage.GetQuotaAsync(ct);
        if (quota.Exceeded && quota.Hard)
        {
            return Fail($"Alcanzaste el limite de tokens de IA de tu plan este mes ({quota.MonthlyLimitTokens:N0}).");
        }

        var model = choice.Model;
        var tools = BuildTools(ctx.ToolAllowList);
        var system = BuildSystemPrompt(ctx, tools);
        var messages = new List<AiToolMessage> { new("user", "Empieza a trabajar.") };

        var maxSteps = ctx.MaxSteps is > 0 ? ctx.MaxSteps : 8;
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(ctx.MaxSeconds is > 0 ? ctx.MaxSeconds : 90);
        int ins = 0, upd = 0, del = 0, round = 0;
        var saved = false;

        // El navegador del agente es EFIMERO por orden (abre-ejecuta-cierra, sin estado entre ordenes),
        // asi que la "pagina actual" no sobrevive entre tool calls. La sesion recuerda la ultima URL para
        // RE-NAVEGAR dentro de la misma orden al leer/clicar (ver ExecuteBrowserToolAsync).
        var session = new BrowserToolSession();

        for (; round < maxSteps; round++)
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                log.LogInformation("[IA-PASO] tope de tiempo alcanzado en la ronda {Round}", round);
                break;
            }

            var completion = await ai.CompleteWithToolsAsync(
                choice.Provider, choice.ApiKey, choice.BaseUrl, model, system, messages, tools, ct);

            await usage.RecordAsync(null, choice.Provider, model,
                completion.InputTokens, completion.OutputTokens, "automation", completion.Ok, ct);

            if (!completion.Ok)
            {
                return new AiStepOutcome(false, ins, upd, del, completion.Error ?? "El proveedor de IA fallo.", round);
            }

            // Sin tool calls: el modelo dio una respuesta final (terminado, aunque no haya guardado).
            if (completion.ToolCalls.Count == 0)
            {
                messages.Add(new AiToolMessage("assistant", completion.Text));
                break;
            }

            // Se re-inyecta el turno del asistente con sus tool calls, y luego un mensaje "tool" por cada
            // resultado (el contrato que espera el bucle de function-calling).
            messages.Add(new AiToolMessage("assistant", completion.Text, completion.ToolCalls));
            foreach (var call in completion.ToolCalls)
            {
                if (DateTimeOffset.UtcNow >= deadline) { break; }

                string toolResult;
                if (call.Name == "guardar_filas")
                {
                    var (i, u, d, ok, msg) = await SaveRowsAsync(ctx, call.ArgumentsJson, ct);
                    if (ok) { ins += i; upd += u; del += d; saved = true; }
                    toolResult = msg;
                }
                else
                {
                    toolResult = await ExecuteBrowserToolAsync(ctx, session, call.Name, call.ArgumentsJson, ct);
                }
                messages.Add(new AiToolMessage("tool", toolResult, ToolCallId: call.Id, ToolName: call.Name));
            }
        }

        if (!saved)
        {
            return new AiStepOutcome(false, ins, upd, del,
                "El paso de IA termino sin guardar filas (tope de pasos/tiempo, o no encontro datos).", round);
        }
        return new AiStepOutcome(true, ins, upd, del, null, round);
    }

    // ---- Tools ----

    private static readonly Dictionary<string, string> ToolByAllow = new(StringComparer.OrdinalIgnoreCase)
    {
        ["navigate"] = "navegar",
        ["html"] = "leer_html",
        ["wait"] = "esperar",
        ["screenshot"] = "captura",
        ["eval"] = "evaluar_js",
        ["mouse"] = "clic",
    };

    private static IReadOnlyList<AiToolSpec> BuildTools(IReadOnlyList<string> allow)
    {
        // Vacio = default SEGURO (leer, no ejecutar JS ni clicar): navegar + leer_html + esperar. Las
        // tools potentes (evaluar_js, clic) solo se ofrecen si el operador las puso en la allow-list.
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (allow.Count == 0)
        {
            names.Add("navegar"); names.Add("leer_html"); names.Add("esperar");
        }
        else
        {
            foreach (var a in allow)
            {
                if (ToolByAllow.TryGetValue(a.Trim(), out var t)) { names.Add(t); }
            }
        }

        var specs = new List<AiToolSpec>();
        if (names.Contains("navegar"))
            specs.Add(new("navegar", "Abre una URL en el navegador.",
                "{\"type\":\"object\",\"properties\":{\"url\":{\"type\":\"string\"}},\"required\":[\"url\"]}"));
        if (names.Contains("leer_html"))
            specs.Add(new("leer_html", "Devuelve el HTML de la pagina o de un selector CSS.",
                "{\"type\":\"object\",\"properties\":{\"selector\":{\"type\":\"string\"}}}"));
        if (names.Contains("esperar"))
            specs.Add(new("esperar", "Espera unos milisegundos a que la pagina cargue.",
                "{\"type\":\"object\",\"properties\":{\"ms\":{\"type\":\"integer\"}},\"required\":[\"ms\"]}"));
        if (names.Contains("captura"))
            specs.Add(new("captura", "Toma una captura de pantalla (para dejar evidencia).",
                "{\"type\":\"object\",\"properties\":{}}"));
        if (names.Contains("evaluar_js"))
            specs.Add(new("evaluar_js", "Ejecuta JavaScript en la pagina y devuelve su resultado.",
                "{\"type\":\"object\",\"properties\":{\"script\":{\"type\":\"string\"}},\"required\":[\"script\"]}"));
        if (names.Contains("clic"))
            specs.Add(new("clic", "Hace clic sobre un selector CSS.",
                "{\"type\":\"object\",\"properties\":{\"selector\":{\"type\":\"string\"}},\"required\":[\"selector\"]}"));

        // La tool de salida SIEMPRE esta: es como el agente entrega lo extraido.
        specs.Add(new("guardar_filas",
            "Guarda las filas extraidas en la tabla destino. Cada fila es un objeto con las columnas como claves.",
            "{\"type\":\"object\",\"properties\":{\"filas\":{\"type\":\"array\",\"items\":{\"type\":\"object\"}}},\"required\":[\"filas\"]}"));
        return specs;
    }

    private static string BuildSystemPrompt(AiStepContext ctx, IReadOnlyList<AiToolSpec> tools)
    {
        var toolList = string.Join(", ", tools.Select(t => t.Name));
        return
            "Eres un agente que maneja un navegador web para EXTRAER DATOS. Tu objetivo es cumplir esta " +
            $"instruccion del operador:\n\n\"{ctx.Instruction}\"\n\n" +
            $"Herramientas disponibles: {toolList}. Usa 'navegar' y 'leer_html' para llegar a los datos, " +
            "razona sobre el HTML, y cuando tengas las filas llama 'guardar_filas' con un arreglo de " +
            "objetos (una fila por registro; las claves son los nombres de las columnas). " +
            "IMPORTANTE sobre el navegador: cada 'leer_html' CARGA DE CERO la ultima URL que fijaste con " +
            "'navegar' (no hay sesion persistente ni estado entre pasos), asi que navega DIRECTO a una URL " +
            "de resultados (p.ej. en Google Maps usa https://www.google.com/maps/search/<terminos+ubicacion>) " +
            "y luego 'leer_html'. 'leer_html' devuelve el CONTENIDO YA RENDERIZADO como JSON " +
            "{title,url,text,items,links,images}: 'items' son los nombres de cada resultado (los negocios en " +
            "Maps), 'text' es el texto visible, 'links' los enlaces e 'images' las URLs de fotos/logos de la " +
            "pagina; hace auto-scroll para cargar mas resultados. NO es HTML crudo: extrae los datos de " +
            "items/text/links/images. Cuando corresponda, pon en cada fila 'imagen_url' (foto o logo del " +
            "negocio, de 'images') y 'url' (la URL de origen donde encontraste el contacto). Los clics/JS no " +
            "persisten entre lecturas. Se conciso: " +
            $"tienes un tope de {(ctx.MaxSteps is > 0 ? ctx.MaxSteps : 8)} pasos. No inventes datos: extrae " +
            "solo lo que veas en la pagina. Cuando termines de guardar, no llames mas herramientas.";
    }

    /// <summary>Estado minimo del navegador ENTRE tool calls de una misma corrida: la ultima URL a la
    /// que el modelo pidio navegar. El navegador del agente es efimero por orden, por eso cada lectura
    /// re-navega a esta URL en la MISMA orden (si no, caeria en una pestana en blanco).</summary>
    private sealed class BrowserToolSession
    {
        public string? CurrentUrl;
    }

    private async Task<string> ExecuteBrowserToolAsync(
        AiStepContext ctx, BrowserToolSession session, string tool, string argsJson, CancellationToken ct)
    {
        JsonElement args;
        try { using var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson); args = d.RootElement.Clone(); }
        catch { return "error: argumentos JSON invalidos."; }

        var corr = NewCorr();

        // CLAVE: el navegador del agente abre-ejecuta-cierra un WebView2 por ORDEN, sin estado entre
        // ordenes. Por eso 'navegar' solo FIJA la URL de la sesion, y toda accion que opere sobre "la
        // pagina actual" (leer_html/captura/evaluar_js/clic) RE-NAVEGA a esa URL dentro de la MISMA orden
        // (Navigate + Wait + accion). Sin esto la lectura caeria en una pestana en blanco (host vacio) y
        // el agente la rechaza ("Lectura de HTML no permitida en este dominio").
        var actions = new List<BrowserAction>();
        switch (tool)
        {
            case "navegar":
            {
                var url = Str(args, "url");
                if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
                {
                    return "error: url invalida (usa una URL http/https completa).";
                }
                session.CurrentUrl = url;
                // No se abre navegador aqui (se perderia al cerrar la celda): la pagina se carga al leer.
                return "ok: URL fijada. Llama 'leer_html' para obtener el contenido de la pagina.";
            }
            case "esperar":
                // La espera se pliega dentro de la siguiente lectura; sola no aporta (celda efimera).
                return "ok";
            case "leer_html":
            {
                if (string.IsNullOrWhiteSpace(session.CurrentUrl)) { return "error: navega a una URL antes de leer_html."; }
                actions.Add(new BrowserAction(BrowserActionKind.Navigate, Url: session.CurrentUrl));
                // ExtractReadable: contenido legible ya renderizado (texto + labels de resultados + enlaces)
                // con auto-scroll para feeds perezosos (Maps/SPA). Reemplaza al outerHTML crudo. Si el agente
                // es viejo (<1.6.0) esta accion cae en su 'default' (no soportada) y abajo se hace fallback a Html.
                actions.Add(new BrowserAction(BrowserActionKind.ExtractReadable, ScrollRounds: 4, WaitMs: 1200));
                break;
            }
            case "captura":
            {
                if (string.IsNullOrWhiteSpace(session.CurrentUrl)) { return "error: navega a una URL antes de captura."; }
                actions.Add(new BrowserAction(BrowserActionKind.Navigate, Url: session.CurrentUrl));
                actions.Add(new BrowserAction(BrowserActionKind.Wait, WaitMs: 2000));
                actions.Add(new BrowserAction(BrowserActionKind.Screenshot, Screenshot: true));
                break;
            }
            case "evaluar_js":
            {
                var js = Str(args, "script");
                if (string.IsNullOrWhiteSpace(js)) { return "error: falta el script."; }
                if (string.IsNullOrEmpty(ctx.Secret)) { return "error: el agente no tiene secreto para firmar JS."; }
                if (string.IsNullOrWhiteSpace(session.CurrentUrl)) { return "error: navega a una URL antes de evaluar_js."; }
                actions.Add(new BrowserAction(BrowserActionKind.Navigate, Url: session.CurrentUrl));
                actions.Add(new BrowserAction(BrowserActionKind.Wait, WaitMs: 2000));
                actions.Add(new BrowserAction(BrowserActionKind.Eval, Script: js, Signature: AgentSign.SignJs(ctx.Secret!, corr, js!)));
                break;
            }
            case "clic":
            {
                var selector = Str(args, "selector");
                if (string.IsNullOrWhiteSpace(selector)) { return "error: falta el selector."; }
                if (string.IsNullOrEmpty(ctx.Secret)) { return "error: el agente no tiene secreto para firmar la accion."; }
                if (string.IsNullOrWhiteSpace(session.CurrentUrl)) { return "error: navega a una URL antes de clic."; }
                var scriptJson = JsonSerializer.Serialize(new[] { new { action = "click", selector } });
                actions.Add(new BrowserAction(BrowserActionKind.Navigate, Url: session.CurrentUrl));
                actions.Add(new BrowserAction(BrowserActionKind.Wait, WaitMs: 2000));
                actions.Add(new BrowserAction(BrowserActionKind.Mouse, ScriptJson: scriptJson, Signature: AgentSign.SignJs(ctx.Secret!, corr, scriptJson)));
                break;
            }
            default:
                return $"error: herramienta '{tool}' no disponible en este paso.";
        }

        try
        {
            var req = new BrowserRequestMsg(corr, ctx.TenantId.ToString(), actions, SessionKey: ctx.SessionKey);
            var result = await channel.ExecuteAsync(ctx.ClientId, req, PerActionTimeout, ct);

            if (tool == "leer_html")
            {
                // Navigate rechazado por la allow-list (u otro fallo del navigate): reportarlo tal cual.
                var nav = result.Results.FirstOrDefault(r => r.Kind == BrowserActionKind.Navigate);
                if (nav is not null && !nav.Ok) { return "error: " + (nav.Error ?? "navegacion no permitida"); }

                var ext = result.Results.FirstOrDefault(r => r.Kind == BrowserActionKind.ExtractReadable);
                if (ext is not null && ext.Ok) { return FormatReadable(ext.Value); }

                // Agente viejo (<1.6.0): ExtractReadable cayo en su 'default' (no soportada). Fallback en la
                // MISMA llamada al camino previo: Html crudo + limpieza (CleanHtmlForModel). Asi funciona
                // igual antes y despues de que el usuario instale el MSI 1.6.0.
                return await ReadHtmlFallbackAsync(ctx, session.CurrentUrl!, Str(args, "selector"), ct);
            }

            // captura / evaluar_js / clic: el resultado UTIL es el de la ULTIMA accion.
            var failed = result.Results.FirstOrDefault(r => !r.Ok);
            if (failed is not null) { return "error: " + (failed.Error ?? result.Error ?? "sin resultado"); }
            var res = result.Results.Count > 0 ? result.Results[^1] : null;
            if (res is null) { return "error: " + (result.Error ?? "sin resultado"); }
            var value = res.Value ?? "ok";
            return value.Length > MaxHtmlChars ? value[..MaxHtmlChars] + "...[recortado]" : value;
        }
        catch (TimeoutException)
        {
            return "error: el navegador no respondio a tiempo.";
        }
    }

    /// <summary>Fallback de leer_html para agentes &lt;1.6.0 (sin ExtractReadable): re-navega y lee el
    /// outerHTML crudo, luego lo des-escapa/limpia (CleanHtmlForModel). Mismo comportamiento que antes.</summary>
    private async Task<string> ReadHtmlFallbackAsync(AiStepContext ctx, string url, string? selector, CancellationToken ct)
    {
        try
        {
            var actions = new List<BrowserAction>
            {
                new(BrowserActionKind.Navigate, Url: url),
                new(BrowserActionKind.Wait, WaitMs: 3800),
                new(BrowserActionKind.Html, Selector: selector),
            };
            var result = await channel.ExecuteAsync(
                ctx.ClientId, new BrowserRequestMsg(NewCorr(), ctx.TenantId.ToString(), actions, SessionKey: ctx.SessionKey), PerActionTimeout, ct);
            var failed = result.Results.FirstOrDefault(r => !r.Ok);
            if (failed is not null) { return "error: " + (failed.Error ?? result.Error ?? "sin resultado"); }
            var last = result.Results.Count > 0 ? result.Results[^1] : null;
            if (last is null) { return "error: " + (result.Error ?? "sin resultado"); }
            var cleaned = CleanHtmlForModel(last.Value ?? "");
            return cleaned.Length > MaxHtmlChars ? cleaned[..MaxHtmlChars] + "...[recortado]" : cleaned;
        }
        catch (TimeoutException)
        {
            return "error: el navegador no respondio a tiempo.";
        }
    }

    /// <summary>Formatea la salida de ExtractReadable (JSON {title,url,text,items,links,images,profiles})
    /// para el modelo: antepone las PERSONAS de LinkedIn ('profiles') y los 'items' (nombres de cada
    /// resultado, p.ej. negocios en Maps) y luego el JSON completo, respetando el tope. Si no es JSON
    /// valido, devuelve el valor tal cual (acotado).</summary>
    private static string FormatReadable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) { return "(la pagina no devolvio contenido legible)"; }
        try
        {
            using var doc = JsonDocument.Parse(value);
            var root = doc.RootElement;
            var sb = new StringBuilder();

            // PERSONAS (LinkedIn): una por linea con nombre | cargo(headline) | url del perfil.
            if (root.TryGetProperty("profiles", out var profiles)
                && profiles.ValueKind == JsonValueKind.Array && profiles.GetArrayLength() > 0)
            {
                sb.Append("PERSONAS DETECTADAS (LinkedIn):\n");
                foreach (var p in profiles.EnumerateArray())
                {
                    var name = p.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (string.IsNullOrWhiteSpace(name)) { continue; }
                    var head = p.TryGetProperty("headline", out var hh) ? hh.GetString() : null;
                    var url = p.TryGetProperty("url", out var u) ? u.GetString() : null;
                    sb.Append("- ").Append(name);
                    if (!string.IsNullOrWhiteSpace(head)) { sb.Append(" | ").Append(head); }
                    if (!string.IsNullOrWhiteSpace(url)) { sb.Append(" | ").Append(url); }
                    sb.Append('\n');
                }
                sb.Append('\n');
            }

            // RESULTADOS (Maps u otros): nombre de cada resultado (aria-label).
            if (root.TryGetProperty("items", out var items)
                && items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0)
            {
                sb.Append("RESULTADOS DETECTADOS:\n");
                foreach (var it in items.EnumerateArray())
                {
                    var s = it.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) { sb.Append("- ").Append(s).Append('\n'); }
                }
                sb.Append('\n');
            }

            if (sb.Length > 0)
            {
                sb.Append(value);
                var outStr = sb.ToString();
                return outStr.Length > MaxHtmlChars ? outStr[..MaxHtmlChars] + "...[recortado]" : outStr;
            }
        }
        catch { /* no era JSON valido: se devuelve tal cual, acotado */ }
        return value.Length > MaxHtmlChars ? value[..MaxHtmlChars] + "...[recortado]" : value;
    }

    private async Task<(int Ins, int Upd, int Del, bool Ok, string Msg)> SaveRowsAsync(
        AiStepContext ctx, string argsJson, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson);
            if (!doc.RootElement.TryGetProperty("filas", out var filas) || filas.ValueKind != JsonValueKind.Array)
            {
                return (0, 0, 0, false, "error: 'filas' debe ser un arreglo de objetos.");
            }
            var rows = ScrapeRowIngest.ParseRows(filas.GetRawText());
            if (rows.Count == 0) { return (0, 0, 0, false, "error: no llegaron filas validas."); }
            var sink = ctx.SinkOverride ?? rowSink;
            var (i, u, d) = await sink.IngestAsync(ctx.TargetContainerId, ctx.TenantId, null, rows, ct);
            return (i, u, d, true, $"guardadas {i} filas.");
        }
        catch (Exception ex)
        {
            return (0, 0, 0, false, "error al guardar: " + ex.Message);
        }
    }

    /// <summary>
    /// Prepara el HTML para el modelo: (1) des-escapa el resultado de WebView2 (ExecuteScriptAsync lo
    /// devuelve como cadena JSON, p.ej. "<html>..."), (2) quita head/scripts/estilos/comentarios
    /// -puro ruido sin datos que agota el presupuesto- y (3) colapsa espacios. Deja el markup del body
    /// (aria-labels, enlaces, direcciones, telefonos) que es donde estan los contactos.
    /// </summary>
    private static string CleanHtmlForModel(string raw)
    {
        if (string.IsNullOrEmpty(raw)) { return raw; }
        var s = raw;
        // WebView2 entrega el string como literal JSON (con comillas y \uXXXX). Se decodifica a HTML real.
        if (s.Length >= 2 && s[0] == '"')
        {
            try { s = JsonSerializer.Deserialize<string>(s) ?? s; } catch { /* se usa tal cual */ }
        }
        s = Regex.Replace(s, "<head[\\s\\S]*?</head>", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<script[\\s\\S]*?</script>", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<style[\\s\\S]*?</style>", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<!--[\\s\\S]*?-->", " ");
        s = Regex.Replace(s, "\\s+", " ");
        return s.Trim();
    }

    private static AiStepOutcome Fail(string msg) => new(false, 0, 0, 0, msg, 0);
    private static string NewCorr() => Guid.NewGuid().ToString("N")[..8];
    private static string? Str(JsonElement o, string k) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int? Int(JsonElement o, string k) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(k, out var v) && v.TryGetInt32(out var n) ? n : null;
}
