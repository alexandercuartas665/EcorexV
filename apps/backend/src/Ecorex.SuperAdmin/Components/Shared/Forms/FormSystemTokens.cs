using System.Text.RegularExpressions;

namespace Ecorex.SuperAdmin.Components.Shared.Forms;

/// <summary>
/// Tokens de SISTEMA (un segmento, no dependen de la tarea) para el default_value de un campo de formulario:
///  - {hoy} / {hoy+N}: fecha de hoy (mas N dias) en formato yyyy-MM-dd (valor de un input date).
///  - {ahora}: hora local actual HH:mm (valor de un input time).
///  - {numero}: RecordNumber del registro (ej. numero de OT); vacio si aun no tiene numero.
/// La "hora local" es la zona del tenant (se pasa el <c>now</c> ya convertido). Complementa a los tokens
/// {tareas.campo} de DynamicFormRenderer (que son de dos segmentos y no colisionan con estos).
/// </summary>
public static class FormSystemTokens
{
    private static readonly Regex SystemTokenRegex =
        new(@"\{\s*(hoy|ahora|numero)\s*(?:\+\s*(\d+))?\s*\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Sustituye los tokens de sistema en <paramref name="input"/>. <paramref name="now"/> ya viene en
    /// la zona local del tenant. Tokens no reconocidos no se tocan (los resuelve otro pase).</summary>
    public static string Resolve(string input, string? recordNumber, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(input) || !input.Contains('{')) { return input; }
        return SystemTokenRegex.Replace(input, m =>
        {
            switch (m.Groups[1].Value.ToLowerInvariant())
            {
                case "hoy":
                    var dias = m.Groups[2].Success && int.TryParse(m.Groups[2].Value, out var n) ? n : 0;
                    return now.Date.AddDays(dias).ToString("yyyy-MM-dd");
                case "ahora":
                    return now.ToString("HH:mm");
                case "numero":
                    return recordNumber ?? string.Empty;
                default:
                    return string.Empty;
            }
        });
    }
}
