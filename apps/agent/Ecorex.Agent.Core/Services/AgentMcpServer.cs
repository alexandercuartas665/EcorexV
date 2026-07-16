using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Ecorex.Contracts.Agent;

namespace Ecorex.Agent.Core.Services;

/// <summary>
/// Servidor MCP embebido del agente (prior-art doc 07: `consumoweb_webserver`). Expone las
/// herramientas `browser.*` (7) y `file.*` (6) por JSON-RPC 2.0 sobre HTTP, escuchando SOLO en
/// loopback (127.0.0.1) -defensa presente en el legacy-. Un cliente MCP local (o una IA local) puede
/// llamar `initialize` / `tools/list` / `tools/call`. Habla con el navegador por el seam
/// <see cref="IBrowserSubAgent"/> (ADR-0039): no sabe que hay WebView2 detras ni marshala hilos.
/// Ambas capacidades respetan su allow-list local.
/// </summary>
public sealed class AgentMcpServer
{
    private readonly IBrowserSubAgent _browser;
    private readonly FileSubAgent _files = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public AgentMcpServer(IBrowserSubAgent browser, int port = 8765)
    {
        _browser = browser;
        Port = port;
    }

    public int Port { get; }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, Port); // loopback-only
        _listener.Start();
        _ = AcceptLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        try { _cts?.Cancel(); _listener?.Stop(); } catch { /* best-effort */ }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener!.AcceptTcpClientAsync(ct); }
            catch { break; }
            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var (method, _, body) = await ReadRequestAsync(stream, ct);
                if (method == "POST" && !string.IsNullOrEmpty(body))
                {
                    string json;
                    bool isNotification;
                    try { (json, isNotification) = await HandleRpcAsync(body); }
                    catch (Exception ex) { await WriteAsync(stream, 200, $"{{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":{{\"code\":-32000,\"message\":{JsonEncode(ex.Message)}}}}}", ct); return; }
                    if (isNotification) { await WriteAsync(stream, 202, null, ct); }
                    else { await WriteAsync(stream, 200, json, ct); }
                }
                else
                {
                    await WriteAsync(stream, 200, "{\"server\":\"ecorex-agent-mcp\",\"transport\":\"jsonrpc/http\"}", ct);
                }
            }
            catch { /* conexion caida: ignora */ }
        }
    }

    private async Task<(string Json, bool IsNotification)> HandleRpcAsync(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
        var hasId = root.TryGetProperty("id", out var idEl) && idEl.ValueKind != JsonValueKind.Null;
        var id = hasId ? idEl.GetRawText() : "null";

        // Notificaciones (sin id): no llevan respuesta JSON-RPC.
        if (!hasId || method is null || method.StartsWith("notifications/", StringComparison.Ordinal))
        {
            return (string.Empty, IsNotification: true);
        }

        switch (method)
        {
            case "initialize":
                return (Result(id, """
                    {"protocolVersion":"2024-11-05","capabilities":{"tools":{}},
                     "serverInfo":{"name":"ecorex-browser","version":"1.0"}}
                    """), false);

            case "tools/list":
                return (Result(id, ToolsCatalog()), false);

            case "tools/call":
            {
                var prms = root.GetProperty("params");
                var name = prms.GetProperty("name").GetString() ?? string.Empty;
                var args = prms.TryGetProperty("arguments", out var a) ? a : default;
                return (await CallToolAsync(id, name, args), false);
            }

            default:
                return (Error(id, -32601, $"Metodo no soportado: {method}"), false);
        }
    }

    private async Task<string> CallToolAsync(string id, string name, JsonElement args)
    {
        try
        {
            if (name.StartsWith("browser.", StringComparison.Ordinal))
            {
                var action = MapBrowserTool(name, args);
                if (action is null) { return ToolContent(id, true, $"Herramienta desconocida: {name}"); }
                var req = new BrowserRequestMsg(Guid.NewGuid().ToString("N")[..8], "mcp", new[] { action });
                var result = await _browser.ExecuteAsync(req);
                var r = result.Results.Count > 0 ? result.Results[0] : new BrowserActionResult(0, action.Kind, false, Error: "sin resultado");
                return r.Ok
                    ? ToolContent(id, false, r.Value ?? "ok", r.ScreenshotBase64)
                    : ToolContent(id, true, r.Error ?? "error");
            }

            if (name.StartsWith("file.", StringComparison.Ordinal))
            {
                var action = MapFileTool(name, args);
                if (action is null) { return ToolContent(id, true, $"Herramienta desconocida: {name}"); }
                var result = await _files.ExecuteAsync(new FileRequestMsg(Guid.NewGuid().ToString("N")[..8], "mcp", new[] { action }));
                var r = result.Results[0];
                if (!r.Ok) { return ToolContent(id, true, r.Error ?? "error"); }
                var text = r.Value ?? (r.Entries is not null ? JsonSerializer.Serialize(r.Entries) : "ok");
                return ToolContent(id, false, text);
            }

            return ToolContent(id, true, $"Herramienta desconocida: {name}");
        }
        catch (Exception ex)
        {
            return ToolContent(id, true, ex.Message);
        }
    }

    private static string? Str(JsonElement args, string k)
        => args.ValueKind == JsonValueKind.Object && args.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static BrowserAction? MapBrowserTool(string name, JsonElement args)
    {
        int? Int(string k) => args.ValueKind == JsonValueKind.Object && args.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
        bool Shot() => args.ValueKind == JsonValueKind.Object && args.TryGetProperty("screenshot", out var v) && v.ValueKind == JsonValueKind.True;

        return name switch
        {
            "browser.navigate" => new BrowserAction(BrowserActionKind.Navigate, Url: Str(args, "url"), Screenshot: Shot()),
            "browser.eval" => new BrowserAction(BrowserActionKind.Eval, Script: Str(args, "script"), Screenshot: Shot()),
            "browser.wait" => new BrowserAction(BrowserActionKind.Wait, WaitMs: Int("waitMs"), ConditionScript: Str(args, "conditionScript"), Screenshot: Shot()),
            "browser.screenshot" => new BrowserAction(BrowserActionKind.Screenshot),
            "browser.html" => new BrowserAction(BrowserActionKind.Html, Selector: Str(args, "selector"), Screenshot: Shot()),
            "browser.mouse" => new BrowserAction(BrowserActionKind.Mouse, ScriptJson: Str(args, "scriptJson"), Screenshot: Shot()),
            "browser.downloads" => new BrowserAction(BrowserActionKind.Downloads),
            _ => null,
        };
    }

    private static FileAction? MapFileTool(string name, JsonElement args) => name switch
    {
        "file.list" => new FileAction(FileActionKind.List, Path: Str(args, "path")),
        "file.read" => new FileAction(FileActionKind.Read, Path: Str(args, "path")),
        "file.readBytes" => new FileAction(FileActionKind.ReadBytes, Path: Str(args, "path")),
        "file.write" => new FileAction(FileActionKind.Write, Path: Str(args, "path"), Content: Str(args, "content")),
        "file.delete" => new FileAction(FileActionKind.Delete, Path: Str(args, "path")),
        "file.exists" => new FileAction(FileActionKind.Exists, Path: Str(args, "path")),
        "file.mkdir" => new FileAction(FileActionKind.MakeDir, Path: Str(args, "path")),
        _ => null,
    };

    // ---- JSON-RPC helpers ----

    private static string Result(string id, string resultJson)
        => $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{resultJson}}}";

    private static string Error(string id, int code, string message)
        => $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"error\":{{\"code\":{code},\"message\":{JsonEncode(message)}}}}}";

    private static string ToolContent(string id, bool isError, string text, string? screenshotB64 = null)
    {
        var content = new StringBuilder();
        content.Append('[');
        content.Append($"{{\"type\":\"text\",\"text\":{JsonEncode(text)}}}");
        if (!string.IsNullOrEmpty(screenshotB64))
        {
            content.Append($",{{\"type\":\"image\",\"data\":\"{screenshotB64}\",\"mimeType\":\"image/png\"}}");
        }
        content.Append(']');
        return Result(id, $"{{\"content\":{content},\"isError\":{(isError ? "true" : "false")}}}");
    }

    private static string ToolsCatalog()
    {
        // 7 herramientas browser.* (doc 07) con su input schema (JSON Schema minimo).
        string Tool(string name, string desc, string props, string required)
            => $"{{\"name\":\"{name}\",\"description\":{JsonEncode(desc)},\"inputSchema\":{{\"type\":\"object\",\"properties\":{props},\"required\":[{required}]}}}}";

        var tools = string.Join(",", new[]
        {
            Tool("browser.navigate", "Navega a una URL http/https (sujeta a la allow-list local).",
                "{\"url\":{\"type\":\"string\"},\"screenshot\":{\"type\":\"boolean\"}}", "\"url\""),
            Tool("browser.eval", "Ejecuta JavaScript en la pagina actual y devuelve el resultado.",
                "{\"script\":{\"type\":\"string\"},\"screenshot\":{\"type\":\"boolean\"}}", "\"script\""),
            Tool("browser.wait", "Espera ms o hasta que una condicion JS sea truthy.",
                "{\"waitMs\":{\"type\":\"integer\"},\"conditionScript\":{\"type\":\"string\"},\"screenshot\":{\"type\":\"boolean\"}}", ""),
            Tool("browser.screenshot", "Captura el navegador (PNG base64).", "{}", ""),
            Tool("browser.html", "HTML de la pagina o de un selector CSS.",
                "{\"selector\":{\"type\":\"string\"},\"screenshot\":{\"type\":\"boolean\"}}", ""),
            Tool("browser.mouse", "Ejecuta un guion JSON de pasos (click/type por selector).",
                "{\"scriptJson\":{\"type\":\"string\"},\"screenshot\":{\"type\":\"boolean\"}}", "\"scriptJson\""),
            Tool("browser.downloads", "Historial reciente de descargas.", "{}", ""),
            // Sub-agente Archivos (acotado a la allow-list de rutas local).
            Tool("file.list", "Lista un directorio (dentro de la allow-list de rutas).",
                "{\"path\":{\"type\":\"string\"}}", "\"path\""),
            Tool("file.read", "Lee un archivo de texto UTF-8 (tope 1 MB).",
                "{\"path\":{\"type\":\"string\"}}", "\"path\""),
            Tool("file.readBytes", "Lee un archivo binario y lo devuelve en base64 (tope 5 MB).",
                "{\"path\":{\"type\":\"string\"}}", "\"path\""),
            Tool("file.write", "Escribe (crea/reemplaza) un archivo.",
                "{\"path\":{\"type\":\"string\"},\"content\":{\"type\":\"string\"}}", "\"path\""),
            Tool("file.delete", "Borra un archivo.", "{\"path\":{\"type\":\"string\"}}", "\"path\""),
            Tool("file.exists", "Informa si una ruta existe (file/dir/none).",
                "{\"path\":{\"type\":\"string\"}}", "\"path\""),
            Tool("file.mkdir", "Crea un directorio.", "{\"path\":{\"type\":\"string\"}}", "\"path\""),
        });
        return $"{{\"tools\":[{tools}]}}";
    }

    private static string JsonEncode(string s) => JsonSerializer.Serialize(s);

    // ---- HTTP minimo (loopback) ----

    private static async Task<(string Method, string Path, string Body)> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var ms = new MemoryStream();
        var headerEnd = -1;
        while (headerEnd < 0)
        {
            var n = await stream.ReadAsync(buffer, ct);
            if (n == 0) { break; }
            ms.Write(buffer, 0, n);
            headerEnd = IndexOfHeaderEnd(ms.GetBuffer(), (int)ms.Length);
            if (ms.Length > 1_048_576) { break; } // 1 MB tope de request
        }
        if (headerEnd < 0) { return (string.Empty, string.Empty, string.Empty); }

        var all = ms.GetBuffer();
        var headerText = Encoding.ASCII.GetString(all, 0, headerEnd);
        var lines = headerText.Split("\r\n");
        var reqLine = lines[0].Split(' ');
        var method = reqLine.Length > 0 ? reqLine[0] : string.Empty;
        var path = reqLine.Length > 1 ? reqLine[1] : "/";

        var contentLength = 0;
        foreach (var line in lines.Skip(1))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(line[15..].Trim(), out contentLength);
            }
        }

        var bodyStart = headerEnd + 4;
        var bodyMs = new MemoryStream();
        var have = (int)ms.Length - bodyStart;
        if (have > 0) { bodyMs.Write(all, bodyStart, have); }
        while (bodyMs.Length < contentLength)
        {
            var n = await stream.ReadAsync(buffer, ct);
            if (n == 0) { break; }
            bodyMs.Write(buffer, 0, n);
        }
        var body = Encoding.UTF8.GetString(bodyMs.GetBuffer(), 0, (int)Math.Min(bodyMs.Length, contentLength));
        return (method, path, body);
    }

    private static int IndexOfHeaderEnd(byte[] buf, int len)
    {
        for (var i = 0; i + 3 < len; i++)
        {
            if (buf[i] == '\r' && buf[i + 1] == '\n' && buf[i + 2] == '\r' && buf[i + 3] == '\n') { return i; }
        }
        return -1;
    }

    private static async Task WriteAsync(NetworkStream stream, int status, string? json, CancellationToken ct)
    {
        var bytes = json is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(json);
        var reason = status == 202 ? "Accepted" : "OK";
        var header = $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct);
        if (bytes.Length > 0) { await stream.WriteAsync(bytes, ct); }
    }
}
