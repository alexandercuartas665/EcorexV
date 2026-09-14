using System.Text.Json;
using System.Text.RegularExpressions;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Compila una <see cref="WhatsAppTemplate"/> guardada (cuerpo con tokens AMIGABLES {{cliente}}) al formato
/// de COMPONENTES que exige Meta/YCloud al crear la plantilla (ADR-0029): placeholders POSICIONALES
/// {{1}},{{2}}... y un arreglo de ejemplos por variable. En este corte las variables se compilan en el
/// BODY (caso comun); HEADER TEXT y FOOTER viajan como texto plano. La categoria va en MAYUSCULAS
/// (MARKETING/UTILITY/AUTHENTICATION).
/// </summary>
public static class WhatsAppTemplateComponents
{
    /// <summary>Resultado de compilar el cuerpo: texto con {{1}}.. y ejemplos en el mismo orden posicional.</summary>
    public sealed record CompiledBody(string Text, IReadOnlyList<string> Examples);

    /// <summary>Categoria de Meta en mayusculas.</summary>
    public static string MetaCategory(WhatsAppTemplateCategory category) => category.ToString().ToUpperInvariant();

    /// <summary>Arreglo de componentes (HEADER? + BODY + FOOTER?) listo para enviar como 'components'.</summary>
    public static IReadOnlyList<object> Build(WhatsAppTemplate t)
    {
        var comps = new List<object>();

        // HEADER de texto (sin variables en este corte): viaja tal cual.
        if (t.HeaderType == WhatsAppTemplateHeaderType.Text && !string.IsNullOrWhiteSpace(t.HeaderText))
        {
            comps.Add(new { type = "HEADER", format = "TEXT", text = t.HeaderText!.Trim() });
        }

        // BODY: compila tokens amigables -> posicionales + ejemplos.
        var body = CompileBody(t.BodyText, ParseVariables(t.VariablesJson));
        comps.Add(body.Examples.Count > 0
            ? new { type = "BODY", text = body.Text, example = new { body_text = new[] { body.Examples.ToArray() } } }
            : (object)new { type = "BODY", text = body.Text });

        // FOOTER de texto.
        if (!string.IsNullOrWhiteSpace(t.FooterText))
        {
            comps.Add(new { type = "FOOTER", text = t.FooterText!.Trim() });
        }

        return comps;
    }

    /// <summary>Reemplaza cada {{token}} presente en el cuerpo por {{1}},{{2}}... en el orden de las variables
    /// declaradas, y arma el arreglo de ejemplos SOLO de las variables usadas (posiciones consecutivas).</summary>
    public static CompiledBody CompileBody(string? bodyText, IReadOnlyList<(string Token, string? Example)> variables)
    {
        var text = bodyText ?? string.Empty;
        var examples = new List<string>();
        var pos = 0;
        foreach (var (token, example) in variables)
        {
            if (string.IsNullOrWhiteSpace(token)) { continue; }
            var rx = new Regex(@"\{\{\s*" + Regex.Escape(token.Trim()) + @"\s*\}\}", RegexOptions.IgnoreCase);
            if (!rx.IsMatch(text)) { continue; }   // variable declarada pero no usada: no ocupa posicion
            pos++;
            text = rx.Replace(text, "{{" + pos + "}}");
            examples.Add(string.IsNullOrWhiteSpace(example) ? token.Trim() : example!.Trim());
        }
        return new CompiledBody(text.Trim(), examples);
    }

    /// <summary>Lee VariablesJson (<c>[{ "token": "cliente", "example": "Juan" }]</c>) en orden.</summary>
    public static IReadOnlyList<(string Token, string? Example)> ParseVariables(string? variablesJson)
    {
        var result = new List<(string, string?)>();
        if (string.IsNullOrWhiteSpace(variablesJson)) { return result; }
        try
        {
            using var doc = JsonDocument.Parse(variablesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) { return result; }
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) { continue; }
                var token = el.TryGetProperty("token", out var t) ? t.GetString() : null;
                if (string.IsNullOrWhiteSpace(token)) { continue; }
                var example = el.TryGetProperty("example", out var e) ? e.GetString() : null;
                result.Add((token!, example));
            }
        }
        catch (JsonException) { /* variables mal formadas: sin ejemplos */ }
        return result;
    }
}
