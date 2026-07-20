using System.Text.Json;
using Ecorex.Application.Tenancy;
using Ecorex.Contracts.Agent;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>Insumos de un paso de IA (lo que el operador configuro + a que agente/tabla va).</summary>
public sealed record AiStepContext(
    string ClientId, Guid TenantId, string Instruction, Guid TargetContainerId,
    IReadOnlyList<string> ToolAllowList, int MaxSteps, int MaxSeconds, Guid? AiProviderId, string? Secret);

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
    private const int MaxHtmlChars = 6000; // tope de lo que un leer_html/evaluar_js le devuelve al modelo.
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
                    toolResult = await ExecuteBrowserToolAsync(ctx, call.Name, call.ArgumentsJson, ct);
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
            "objetos (una fila por registro; las claves son los nombres de las columnas). Se conciso: " +
            $"tienes un tope de {(ctx.MaxSteps is > 0 ? ctx.MaxSteps : 8)} pasos. No inventes datos: extrae " +
            "solo lo que veas en la pagina. Cuando termines de guardar, no llames mas herramientas.";
    }

    private async Task<string> ExecuteBrowserToolAsync(AiStepContext ctx, string tool, string argsJson, CancellationToken ct)
    {
        JsonElement args;
        try { using var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson); args = d.RootElement.Clone(); }
        catch { return "error: argumentos JSON invalidos."; }

        var corr = NewCorr();
        BrowserAction action;
        switch (tool)
        {
            case "navegar":
                action = new BrowserAction(BrowserActionKind.Navigate, Url: Str(args, "url"));
                break;
            case "leer_html":
                action = new BrowserAction(BrowserActionKind.Html, Selector: Str(args, "selector"));
                break;
            case "esperar":
                action = new BrowserAction(BrowserActionKind.Wait, WaitMs: Int(args, "ms") ?? 500);
                break;
            case "captura":
                action = new BrowserAction(BrowserActionKind.Screenshot, Screenshot: true);
                break;
            case "evaluar_js":
                {
                    var js = Str(args, "script");
                    if (string.IsNullOrWhiteSpace(js)) { return "error: falta el script."; }
                    if (string.IsNullOrEmpty(ctx.Secret)) { return "error: el agente no tiene secreto para firmar JS."; }
                    action = new BrowserAction(BrowserActionKind.Eval, Script: js, Signature: AgentSign.SignJs(ctx.Secret!, corr, js!));
                    break;
                }
            case "clic":
                {
                    var selector = Str(args, "selector");
                    if (string.IsNullOrWhiteSpace(selector)) { return "error: falta el selector."; }
                    if (string.IsNullOrEmpty(ctx.Secret)) { return "error: el agente no tiene secreto para firmar la accion."; }
                    var scriptJson = JsonSerializer.Serialize(new[] { new { action = "click", selector } });
                    action = new BrowserAction(BrowserActionKind.Mouse, ScriptJson: scriptJson, Signature: AgentSign.SignJs(ctx.Secret!, corr, scriptJson));
                    break;
                }
            default:
                return $"error: herramienta '{tool}' no disponible en este paso.";
        }

        try
        {
            var req = new BrowserRequestMsg(corr, ctx.TenantId.ToString(), new List<BrowserAction> { action });
            var result = await channel.ExecuteAsync(ctx.ClientId, req, PerActionTimeout, ct);
            var res = result.Results.Count > 0 ? result.Results[0] : null;
            if (res is null || !res.Ok)
            {
                return "error: " + (res?.Error ?? result.Error ?? "sin resultado");
            }
            var value = res.Value ?? "ok";
            return value.Length > MaxHtmlChars ? value[..MaxHtmlChars] + "...[recortado]" : value;
        }
        catch (TimeoutException)
        {
            return "error: el navegador no respondio a tiempo.";
        }
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
            var (i, u, d) = await rowSink.IngestAsync(ctx.TargetContainerId, ctx.TenantId, null, rows, ct);
            return (i, u, d, true, $"guardadas {i} filas.");
        }
        catch (Exception ex)
        {
            return (0, 0, 0, false, "error al guardar: " + ex.Message);
        }
    }

    private static AiStepOutcome Fail(string msg) => new(false, 0, 0, 0, msg, 0);
    private static string NewCorr() => Guid.NewGuid().ToString("N")[..8];
    private static string? Str(JsonElement o, string k) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int? Int(JsonElement o, string k) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(k, out var v) && v.TryGetInt32(out var n) ? n : null;
}
