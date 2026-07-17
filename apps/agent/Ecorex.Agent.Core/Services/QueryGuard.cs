using System.Text.RegularExpressions;

namespace Ecorex.Agent.Core.Services;

/// <summary>
/// Whitelist de seguridad para las consultas que ejecuta el Gateway (Ola C / doc 05 Ola 2): SOLO
/// lectura. Rechaza cualquier cosa que no sea un unico SELECT (o CTE WITH ... SELECT), y bloquea
/// verbos de escritura/DDL/ejecucion de procedimientos. Defensa en profundidad demas de un usuario
/// de BD de solo-lectura.
/// </summary>
public static class QueryGuard
{
    // Verbos prohibidos (como palabra completa, sin distinguir mayusculas).
    private static readonly string[] Forbidden =
    {
        "insert", "update", "delete", "merge", "drop", "alter", "create", "truncate",
        "grant", "revoke", "exec", "execute", "sp_", "xp_", "into", "shutdown", "backup",
        "restore", "waitfor", "openrowset", "openquery", "bulk",
    };

    /// <summary>Devuelve (ok, error). ok=false con motivo si la consulta no es un SELECT seguro.</summary>
    public static (bool Ok, string? Error) Validate(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return (false, "Consulta vacia.");
        }

        var stripped = StripComments(sql).Trim();
        if (stripped.Length == 0)
        {
            return (false, "Consulta vacia.");
        }

        // Un solo statement: no se permiten ';' internos (solo un ';' final opcional).
        var withoutTrailing = stripped.TrimEnd(';', ' ', '\t', '\r', '\n');
        if (withoutTrailing.Contains(';'))
        {
            return (false, "Solo se permite una sentencia.");
        }

        // Debe empezar por SELECT o WITH (CTE que termina en SELECT).
        var head = withoutTrailing.TrimStart('(', ' ', '\t', '\r', '\n');
        if (!StartsWithKeyword(head, "select") && !StartsWithKeyword(head, "with"))
        {
            return (false, "Solo se permiten consultas SELECT.");
        }

        foreach (var verb in Forbidden)
        {
            if (ContainsWord(withoutTrailing, verb))
            {
                return (false, $"Palabra no permitida en una consulta de solo-lectura: '{verb}'.");
            }
        }

        return (true, null);
    }

    private static string StripComments(string sql)
    {
        // Comentarios de linea (-- ...) y de bloque (/* ... */).
        sql = Regex.Replace(sql, "--.*?$", " ", RegexOptions.Multiline);
        sql = Regex.Replace(sql, "/\\*.*?\\*/", " ", RegexOptions.Singleline);
        return sql;
    }

    private static bool StartsWithKeyword(string text, string keyword) =>
        text.Length >= keyword.Length
        && text[..keyword.Length].Equals(keyword, StringComparison.OrdinalIgnoreCase)
        && (text.Length == keyword.Length || !char.IsLetterOrDigit(text[keyword.Length]));

    private static bool ContainsWord(string text, string word)
    {
        // sp_/xp_ son prefijos: basta con encontrarlos como token. El resto, palabra completa.
        if (word.EndsWith('_'))
        {
            return Regex.IsMatch(text, "\\b" + Regex.Escape(word), RegexOptions.IgnoreCase);
        }
        return Regex.IsMatch(text, "\\b" + Regex.Escape(word) + "\\b", RegexOptions.IgnoreCase);
    }
}
