using System.Text;

namespace Ecorex.SuperAdmin.Endpoints;

/// <summary>
/// Configuraciones listas para copiar de los clientes MCP (Claude Code, Gemini CLI/app, Codex, Claude Desktop) para un
/// servidor MCP de ECOREX. Las comparten /conectar-ia (tableros, ADR-0159) y la configuracion MCP de cada linea en
/// /lineas (WhatsApp, ADR-0164): solo cambian el nombre del servidor y la URL.
/// </summary>
public static class McpClientSnippets
{
    public static IEnumerable<(string Title, string Snippet)> Build(string serverName, string url, string token)
    {
        yield return ("Claude Code (terminal)",
            $"claude mcp add --transport http {serverName} {url} --header \"Authorization: Bearer {token}\"");
        yield return ("Gemini CLI (~/.gemini/settings.json)",
            "{\n  \"mcpServers\": {\n    \"" + serverName + "\": {\n      \"httpUrl\": \"" + url + "\",\n" +
            "      \"headers\": { \"Authorization\": \"Bearer " + token + "\" }\n    }\n  }\n}");
        yield return ("Gemini (aplicacion / Code Assist, si el CLI falla; requiere Node) ~/.gemini/settings.json",
            "{\n  \"mcpServers\": {\n    \"" + serverName + "\": {\n      \"command\": \"npx\",\n" +
            "      \"args\": [\"-y\", \"mcp-remote\", \"" + url + "\", \"--header\", \"Authorization:Bearer " + token + "\"]\n    }\n  }\n}");
        yield return ("Codex (~/.codex/config.toml, requiere Node)",
            "[mcp_servers." + serverName + "]\ncommand = \"npx\"\n" +
            "args = [\"-y\", \"mcp-remote\", \"" + url + "\", \"--header\", \"Authorization:Bearer " + token + "\"]");
        yield return ("Claude Desktop (claude_desktop_config.json, requiere Node)",
            "{\n  \"mcpServers\": {\n    \"" + serverName + "\": {\n      \"command\": \"npx\",\n" +
            "      \"args\": [\"-y\", \"mcp-remote\", \"" + url + "\", \"--header\", \"Authorization:Bearer " + token + "\"]\n    }\n  }\n}");
    }

    /// <summary>Nombre de servidor para el cliente MCP a partir de un texto (ASCII, minusculas, guiones).</summary>
    public static string Slug(string prefix, string? text)
    {
        var sb = new StringBuilder();
        foreach (var ch in (text ?? string.Empty).Normalize(NormalizationForm.FormD))
        {
            var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == System.Globalization.UnicodeCategory.NonSpacingMark) { continue; }
            var c = char.ToLowerInvariant(ch);
            sb.Append(c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '-');
        }
        var core = string.Join('-', sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        var name = core.Length == 0 ? prefix : $"{prefix}-{core}";
        return name.Length <= 40 ? name : name[..40].TrimEnd('-');
    }
}
