using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;

namespace Ecorex.Application.Rules;

/// <summary>
/// Generador/parseador PARAM_XML &lt;-&gt; ParamsJson (ADR-0023). El XML es SOLO una
/// REPRESENTACION editable del ParamsJson tipado del descriptor del verbo (homenaje al
/// contrato PARAM_XML del legacy 000802): NUNCA se interpreta por reflexion ni ejecuta
/// nada (ADR-0016). Contrato:
/// &lt;REGLA&gt;&lt;PROCESO&gt;VERBO&lt;/PROCESO&gt;&lt;PARAMETROS&gt;
///   &lt;PARAM name=".." tipo=".." obligatorio=".." valor=".." /&gt;
/// &lt;/PARAMETROS&gt;&lt;/REGLA&gt;
/// Generate emite TODOS los parametros del descriptor (los sin valor van sin atributo
/// valor); claves del ParamsJson fuera del descriptor se descartan (el guardado ya las
/// rechaza). Parse valida proceso, nombres, tipos y obligatorios con errores claros.
/// Clase PURA: sin BD, sin IO.
/// </summary>
public static class RuleParamXml
{
    private const string RootName = "REGLA";
    private const string ProcessName = "PROCESO";
    private const string ParamsName = "PARAMETROS";
    private const string ParamName = "PARAM";

    /// <summary>Comentario cabecera del XML generado (se ignora al parsear).</summary>
    private const string HeaderComment =
        " Contrato PARAM_XML: representacion editable del ParamsJson tipado (ADR-0016/0023) ";

    // ---- Generacion (ParamsJson -> XML) ----

    /// <summary>
    /// Genera el PARAM_XML de una regla desde su ParamsJson y el descriptor del verbo.
    /// ParamsJson corrupto o no-objeto se trata como vacio (el editor lo reescribe).
    /// </summary>
    public static string Generate(RuleVerbDescriptor descriptor, string? paramsJson)
    {
        var values = ReadJsonObject(paramsJson);

        var parametros = new XElement(ParamsName);
        foreach (var p in descriptor.Params)
        {
            var param = new XElement(ParamName,
                new XAttribute("name", p.Name),
                new XAttribute("tipo", TipoOf(p.Type)),
                new XAttribute("obligatorio", p.Required ? "true" : "false"));
            if (values.TryGetValue(p.Name, out var node))
            {
                param.Add(new XAttribute("valor", ValueText(node, p.Type)));
            }
            parametros.Add(param);
        }

        var root = new XElement(RootName,
            new XElement(ProcessName, descriptor.VerbName),
            parametros);
        var document = new XDocument(new XComment(HeaderComment), root);
        return document.ToString(SaveOptions.None);
    }

    // ---- Parseo (XML -> ParamsJson) ----

    /// <summary>
    /// Parsea el PARAM_XML de vuelta a ParamsJson validando contra el descriptor.
    /// Devuelve el ParamsJson (null si ningun parametro trae valor) o Invalid con el
    /// error claro (XML malformado, proceso distinto, parametro desconocido, tipo o
    /// obligatorio incumplido).
    /// </summary>
    public static RuleResult<string?> Parse(string xml, RuleVerbDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return RuleResult<string?>.Invalid("El XML esta vacio.");
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (XmlException ex)
        {
            return RuleResult<string?>.Invalid($"XML malformado: {ex.Message}");
        }

        var root = document.Root;
        if (root is null || !NameIs(root.Name, RootName))
        {
            return RuleResult<string?>.Invalid($"La raiz debe ser <{RootName}>.");
        }

        var proceso = root.Elements().FirstOrDefault(e => NameIs(e.Name, ProcessName));
        if (proceso is null || string.IsNullOrWhiteSpace(proceso.Value))
        {
            return RuleResult<string?>.Invalid($"Falta el elemento <{ProcessName}> con el verbo.");
        }
        if (!string.Equals(proceso.Value.Trim(), descriptor.VerbName, StringComparison.OrdinalIgnoreCase))
        {
            return RuleResult<string?>.Invalid(
                $"El PROCESO '{proceso.Value.Trim()}' no coincide con el verbo de la regla ({descriptor.VerbName}).");
        }

        var parametros = root.Elements().FirstOrDefault(e => NameIs(e.Name, ParamsName));
        var byName = descriptor.Params.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var param in parametros?.Elements() ?? [])
        {
            if (!NameIs(param.Name, ParamName))
            {
                return RuleResult<string?>.Invalid(
                    $"Elemento inesperado <{param.Name.LocalName}> dentro de <{ParamsName}> (solo se admite <{ParamName}>).");
            }
            var name = param.Attribute("name")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return RuleResult<string?>.Invalid($"Hay un <{ParamName}> sin atributo name.");
            }
            if (!byName.TryGetValue(name, out var paramDescriptor))
            {
                var valid = string.Join(", ", descriptor.Params.Select(p => p.Name));
                return RuleResult<string?>.Invalid(
                    $"El parametro '{name}' no existe en el verbo {descriptor.VerbName}. Validos: {valid}.");
            }
            if (values.ContainsKey(paramDescriptor.Name))
            {
                return RuleResult<string?>.Invalid($"El parametro '{paramDescriptor.Name}' esta repetido.");
            }
            var valor = param.Attribute("valor")?.Value;
            if (valor is null)
            {
                continue; // Sin valor: el parametro queda sin configurar.
            }
            var (node, error) = ConvertValue(valor, paramDescriptor);
            if (error is not null)
            {
                return RuleResult<string?>.Invalid(error);
            }
            values[paramDescriptor.Name] = node;
        }

        var missing = descriptor.Params
            .Where(p => p.Required && !values.ContainsKey(p.Name))
            .Select(p => p.Name)
            .ToList();
        if (missing.Count > 0)
        {
            return RuleResult<string?>.Invalid(
                $"Faltan parametros obligatorios del verbo {descriptor.VerbName}: {string.Join(", ", missing)}.");
        }

        if (values.Count == 0)
        {
            return RuleResult<string?>.Ok(null);
        }
        // En orden del descriptor: la salida es determinista (round-trip estable).
        var result = new JsonObject();
        foreach (var p in descriptor.Params)
        {
            if (values.TryGetValue(p.Name, out var node))
            {
                result[p.Name] = node;
            }
        }
        return RuleResult<string?>.Ok(result.ToJsonString());
    }

    // ---- Formateo ----

    /// <summary>Re-indenta el XML (2 espacios) preservando comentarios; Invalid si esta roto.</summary>
    public static RuleResult<string> Format(string xml)
    {
        try
        {
            var document = XDocument.Parse(xml, LoadOptions.None);
            return RuleResult<string>.Ok(document.ToString(SaveOptions.None));
        }
        catch (XmlException ex)
        {
            return RuleResult<string>.Invalid($"XML malformado: {ex.Message}");
        }
    }

    // ---- Helpers ----

    /// <summary>Etiqueta de tipo del atributo tipo="" por RuleParamType.</summary>
    public static string TipoOf(RuleParamType type) => type switch
    {
        RuleParamType.Number => "numeric",
        RuleParamType.Boolean => "boolean",
        RuleParamType.Json => "json",
        RuleParamType.FieldCode => "fieldcode",
        _ => "string"
    };

    private static bool NameIs(XName name, string expected)
        => string.Equals(name.LocalName, expected, StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, JsonNode?> ReadJsonObject(string? paramsJson)
    {
        var values = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(paramsJson))
        {
            return values;
        }
        try
        {
            if (JsonNode.Parse(paramsJson) is JsonObject json)
            {
                foreach (var (key, node) in json)
                {
                    values[key] = node?.DeepClone();
                }
            }
        }
        catch (JsonException)
        {
            // ParamsJson corrupto: el XML se genera vacio y el guardado lo reescribe.
        }
        return values;
    }

    /// <summary>Texto del atributo valor="" segun el tipo (JSON crudo para tipo json).</summary>
    private static string ValueText(JsonNode? node, RuleParamType type)
    {
        if (node is null)
        {
            return "";
        }
        return type switch
        {
            RuleParamType.Json => node.ToJsonString(),
            _ => node is JsonValue value && value.TryGetValue<string>(out var text)
                ? text
                : node.ToJsonString().Trim('"')
        };
    }

    /// <summary>Convierte el texto del atributo valor al JsonNode tipado del descriptor.</summary>
    private static (JsonNode? Node, string? Error) ConvertValue(string valor, RuleVerbParamDescriptor descriptor)
    {
        switch (descriptor.Type)
        {
            case RuleParamType.Number:
                if (!decimal.TryParse(valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                {
                    return (null, $"El parametro '{descriptor.Name}' debe ser numerico (valor: '{valor}').");
                }
                return (JsonValue.Create(number), null);
            case RuleParamType.Boolean:
                if (!bool.TryParse(valor, out var flag))
                {
                    return (null, $"El parametro '{descriptor.Name}' debe ser true o false (valor: '{valor}').");
                }
                return (JsonValue.Create(flag), null);
            case RuleParamType.Json:
                try
                {
                    return (JsonNode.Parse(valor), null);
                }
                catch (JsonException ex)
                {
                    return (null, $"El parametro '{descriptor.Name}' no es JSON valido: {ex.Message}");
                }
            default:
                return (JsonValue.Create(valor), null);
        }
    }
}
