using System.Text.RegularExpressions;

namespace Ecorex.Application.Reporting.External;

/// <summary>
/// Guarda de SOLO LECTURA para el conector externo (ADR-0064), defensa en profundidad ADEMAS del
/// usuario de BD de solo lectura y la transaccion read-only del motor. Pieza PURA (sin BD), por eso
/// testable: rechaza cualquier comando que no sea una lectura (un unico SELECT/WITH) o que contenga
/// verbos de escritura/DDL. No sustituye al usuario de solo lectura; lo refuerza.
/// </summary>
public static partial class ExternalReadOnlyGuard
{
    // Verbos de escritura / DDL / ejecucion prohibidos como palabra completa (incluye SELECT ... INTO).
    [GeneratedRegex(
        @"\b(insert|update|delete|drop|alter|truncate|merge|create|grant|revoke|exec|execute|call|into|nextval|setval|copy)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ForbiddenVerbs();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex BlockComments();

    [GeneratedRegex(@"--[^\r\n]*", RegexOptions.CultureInvariant)]
    private static partial Regex LineComments();

    /// <summary>True si el texto es una lectura pura. No lanza.</summary>
    public static bool IsReadOnly(string? commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return false;
        }

        // Quita comentarios para que no escondan verbos ni statements.
        var sql = BlockComments().Replace(commandText, " ");
        sql = LineComments().Replace(sql, " ");
        sql = sql.Trim();

        // Un solo statement: se permite un unico ';' final opcional, nada de multi-statement.
        var withoutTrailing = sql.TrimEnd().TrimEnd(';').TrimEnd();
        if (withoutTrailing.Contains(';'))
        {
            return false;
        }

        // Debe empezar por SELECT o WITH (CTE que termina en SELECT).
        var startsRead = withoutTrailing.StartsWith("select", StringComparison.OrdinalIgnoreCase)
            || withoutTrailing.StartsWith("with", StringComparison.OrdinalIgnoreCase);
        if (!startsRead)
        {
            return false;
        }

        // Ningun verbo de escritura/DDL como palabra completa.
        return !ForbiddenVerbs().IsMatch(withoutTrailing);
    }

    /// <summary>Lanza <see cref="ReportValidationException"/> si el comando no es de solo lectura.</summary>
    public static void EnsureReadOnly(string? commandText)
    {
        if (!IsReadOnly(commandText))
        {
            throw new ReportValidationException(
                "El comando del dataset externo no es de solo lectura: solo se admite un unico SELECT/WITH sin verbos de escritura.");
        }
    }

    /// <summary>
    /// Aplica el guard SALVO que haya un opt-in explicito: <paramref name="allowWrite"/> (conexion propia del
    /// tenant con escritura) o <paramref name="allowBatch"/> (dataset con batch multi-statement habilitado por
    /// el dueño, ADR-0064). Centraliza la decision del bypass para que sea unica y testeable. El default
    /// (ambos false) mantiene el comportamiento estricto: un unico SELECT/WITH sin verbos de escritura.
    /// </summary>
    public static void EnsureReadOnly(string? commandText, bool allowWrite, bool allowBatch)
    {
        if (allowWrite || allowBatch)
        {
            return;
        }

        EnsureReadOnly(commandText);
    }
}
