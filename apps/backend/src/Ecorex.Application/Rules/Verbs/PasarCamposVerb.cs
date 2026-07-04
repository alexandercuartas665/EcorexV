using System.Text.Json;

namespace Ecorex.Application.Rules.Verbs;

/// <summary>
/// Verbo PASAR_CAMPOS (port del verbo homonimo del legacy): copia valores entre campos del
/// FormData segun el mapeo declarado en params. Devuelve acciones SetFieldValue para que el
/// renderer pinte los destinos, y ademas actualiza el FormData del contexto (los verbos
/// encadenados ven los valores copiados).
/// </summary>
public sealed class PasarCamposVerb : IRuleVerb
{
    public const string VerbName = "PASAR_CAMPOS";

    public string Name => VerbName;

    public RuleVerbDescriptor Descriptor { get; } = new(
        VerbName,
        "Pasar campos",
        "Copia el valor de un campo origen a un campo destino del formulario (uno o varios mapeos).",
        [
            new RuleVerbParamDescriptor("mappings", "Mapeos (JSON)", RuleParamType.Json, Required: true,
                "Arreglo de mapeos source -> target. Ej: [{\"source\":\"nombre\",\"target\":\"nombre_copia\"}]")
        ]);

    public Task<RuleVerbResult> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        if (!context.Params.TryGetValue("mappings", out var mappingsElement)
            || mappingsElement.ValueKind != JsonValueKind.Array)
        {
            return Task.FromResult(RuleVerbResult.Fail(
                "Parametro 'mappings' obligatorio: arreglo JSON de objetos {source,target}."));
        }

        var actions = new List<RuleAction>();
        foreach (var mapping in mappingsElement.EnumerateArray())
        {
            if (mapping.ValueKind != JsonValueKind.Object
                || !mapping.TryGetProperty("source", out var sourceElement)
                || !mapping.TryGetProperty("target", out var targetElement)
                || sourceElement.ValueKind != JsonValueKind.String
                || targetElement.ValueKind != JsonValueKind.String)
            {
                return Task.FromResult(RuleVerbResult.Fail(
                    "Cada mapeo debe ser un objeto {\"source\":\"campo\",\"target\":\"campo\"}."));
            }
            var source = sourceElement.GetString()!;
            var target = targetElement.GetString()!;
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
            {
                return Task.FromResult(RuleVerbResult.Fail("Los mapeos no admiten source/target vacios."));
            }

            var value = context.FormData.GetValueOrDefault(source);
            context.FormData[target] = value;
            actions.Add(RuleAction.SetValue(target, value));
        }

        if (actions.Count == 0)
        {
            return Task.FromResult(RuleVerbResult.Fail("El parametro 'mappings' no tiene mapeos."));
        }

        return Task.FromResult(RuleVerbResult.Ok(
            $"{actions.Count} campo(s) copiado(s).", actions.Count, actions,
            context.GetBoolParam("autoComplete")));
    }
}
