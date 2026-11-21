using System.Globalization;
using System.Text;
using System.Text.Json;
using Ecorex.Application.Forms;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Voice;

/// <summary>Pregunta de un formulario vista desde una llamada de voz (solo lo que hace falta para preguntar y volcar).</summary>
public sealed record VoiceFormQuestion(
    string FieldCode,
    string Label,
    string? HelpText,
    FormControlType ControlType,
    string? OptionsJson,
    FormSourceKind SourceKind = FormSourceKind.Options,
    bool IsHidden = false);

/// <summary>
/// Puente formulario &lt;-&gt; llamada de voz del motor de acciones de contactos (ADR-0190, fase B). Funciones PURAS:
/// - <see cref="Askable"/>: que preguntas se le pueden hacer por voz a un contacto.
/// - <see cref="BuildPromptBlock"/>: bloque de "preguntas a hacer" que se anexa a la instruccion de la llamada.
/// - <see cref="BuildAnalysisFields"/>: campos que Retell extrae al colgar (custom_analysis_data), uno por pregunta.
/// - <see cref="MapCaptured"/>: lo que Retell devolvio -&gt; valores del formulario (etiqueta de opcion -&gt; id, etc.).
/// </summary>
public static class ContactCallFormQuestions
{
    /// <summary>Tope de preguntas por llamada: una llamada larga se cae; el resto se ignora.</summary>
    public const int MaxQuestions = 15;

    private static readonly HashSet<FormControlType> AskableTypes = new()
    {
        FormControlType.Text, FormControlType.TextArea, FormControlType.Select, FormControlType.MultiCheck,
        FormControlType.Radio, FormControlType.Toggle, FormControlType.Number, FormControlType.Date,
        FormControlType.Time, FormControlType.DateTime
    };

    /// <summary>Preguntas que se pueden hacer por voz: tipos simples, visibles y con opciones propias (no lookups).</summary>
    public static IReadOnlyList<VoiceFormQuestion> Askable(IEnumerable<VoiceFormQuestion> questions)
        => questions
            .Where(q => AskableTypes.Contains(q.ControlType) && !q.IsHidden
                && !string.IsNullOrWhiteSpace(q.FieldCode) && !string.IsNullOrWhiteSpace(q.Label)
                && (!IsChoice(q.ControlType) || (q.SourceKind == FormSourceKind.Options && Options(q).Count > 0)))
            .GroupBy(q => q.FieldCode, StringComparer.Ordinal)
            .Select(g => g.First())
            .Take(MaxQuestions)
            .ToList();

    /// <summary>Bloque de texto con las preguntas a hacer, en orden. Vacio si no hay preguntas.</summary>
    public static string BuildPromptBlock(IReadOnlyList<VoiceFormQuestion> questions)
    {
        if (questions.Count == 0) { return string.Empty; }
        var sb = new StringBuilder();
        sb.Append("Preguntas que debes hacerle al contacto durante la llamada (en este orden, de forma natural y una a la vez; ")
          .Append("si no sabe o no quiere responder, no insistas y sigue con la siguiente; confirma las respuestas importantes antes de despedirte):");
        var i = 1;
        foreach (var q in questions)
        {
            sb.Append('\n').Append(i++).Append(". ").Append(q.Label.Trim());
            var opts = Options(q);
            if (IsChoice(q.ControlType) && opts.Count > 0)
            {
                sb.Append(q.ControlType == FormControlType.MultiCheck ? " (puede elegir varias: " : " (opciones: ")
                  .Append(string.Join(", ", opts.Select(o => o.Label))).Append(')');
            }
            else if (q.ControlType == FormControlType.Toggle)
            {
                sb.Append(" (si / no)");
            }
            if (!string.IsNullOrWhiteSpace(q.HelpText)) { sb.Append(" - ").Append(q.HelpText!.Trim()); }
        }
        return sb.ToString();
    }

    /// <summary>Campos de analisis post-llamada de Retell, uno por pregunta (nombre = codigo del campo).</summary>
    public static IReadOnlyList<RetellAnalysisField> BuildAnalysisFields(IReadOnlyList<VoiceFormQuestion> questions)
    {
        var fields = new List<RetellAnalysisField>(questions.Count);
        foreach (var q in questions)
        {
            var desc = $"Respuesta del contacto a: \"{q.Label.Trim()}\". Dejalo vacio si no respondio.";
            var opts = Options(q);
            fields.Add(q.ControlType switch
            {
                FormControlType.Select or FormControlType.Radio =>
                    new RetellAnalysisField(q.FieldCode, "enum", desc, opts.Select(o => o.Label).ToList()),
                FormControlType.Toggle => new RetellAnalysisField(q.FieldCode, "boolean", desc),
                FormControlType.Number => new RetellAnalysisField(q.FieldCode, "number", desc),
                FormControlType.MultiCheck => new RetellAnalysisField(q.FieldCode, "string",
                    desc + " Si eligio varias, separalas con coma. Opciones validas: " + string.Join(", ", opts.Select(o => o.Label)) + "."),
                FormControlType.Date => new RetellAnalysisField(q.FieldCode, "string", desc + " Formato AAAA-MM-DD."),
                FormControlType.Time => new RetellAnalysisField(q.FieldCode, "string", desc + " Formato HH:mm (24 h)."),
                FormControlType.DateTime => new RetellAnalysisField(q.FieldCode, "string", desc + " Formato AAAA-MM-DD HH:mm."),
                _ => new RetellAnalysisField(q.FieldCode, "string", desc)
            });
        }
        return fields;
    }

    /// <summary>
    /// Traduce lo capturado por Retell (codigo -&gt; texto) a valores del formulario. Solo devuelve respuestas
    /// REALES: vacios, "null" o valores que no encajan con las opciones se descartan (no pisan lo que ya habia).
    /// </summary>
    public static IReadOnlyDictionary<string, string> MapCaptured(
        IReadOnlyList<VoiceFormQuestion> questions, IReadOnlyDictionary<string, string> captured)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var q in questions)
        {
            if (!captured.TryGetValue(q.FieldCode, out var raw)) { continue; }
            var value = (raw ?? string.Empty).Trim();
            if (value.Length == 0 || value.Equals("null", StringComparison.OrdinalIgnoreCase)) { continue; }

            var mapped = q.ControlType switch
            {
                FormControlType.Select or FormControlType.Radio => MatchOption(q, value)?.Id,
                FormControlType.MultiCheck => MapMulti(q, value),
                FormControlType.Toggle => MapBool(value),
                FormControlType.Number => decimal.TryParse(value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var n)
                    ? n.ToString(CultureInfo.InvariantCulture) : null,
                _ => value
            };
            if (!string.IsNullOrWhiteSpace(mapped)) { result[q.FieldCode] = mapped!; }
        }
        return result;
    }

    private static bool IsChoice(FormControlType t)
        => t is FormControlType.Select or FormControlType.Radio or FormControlType.MultiCheck;

    private static IReadOnlyList<FormOption> Options(VoiceFormQuestion q)
        => FormFieldValidator.ParseOptions(q.OptionsJson).Where(o => !string.IsNullOrWhiteSpace(o.Label) && !string.IsNullOrWhiteSpace(o.Id)).ToList();

    private static FormOption? MatchOption(VoiceFormQuestion q, string value)
    {
        var opts = Options(q);
        var v = Normalize(value);
        return opts.FirstOrDefault(o => Normalize(o.Label) == v)
            ?? opts.FirstOrDefault(o => Normalize(o.Id) == v || (o.Value is not null && Normalize(o.Value) == v));
    }

    private static string? MapMulti(VoiceFormQuestion q, string value)
    {
        var ids = value.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => MatchOption(q, part)?.Id)
            .Where(id => id is not null)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return ids.Count == 0 ? null : JsonSerializer.Serialize(ids);
    }

    private static string? MapBool(string value) => Normalize(value) switch
    {
        "true" or "si" or "yes" or "1" => "true",
        "false" or "no" or "0" => "false",
        _ => null
    };

    /// <summary>Minusculas sin tildes ni espacios sobrantes, para comparar etiquetas dichas por voz.</summary>
    private static string Normalize(string s)
    {
        var d = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) { sb.Append(c); }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
