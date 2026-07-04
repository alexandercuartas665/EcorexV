namespace Ecorex.Application.Rules.Verbs;

/// <summary>
/// Verbo BLOQUEAR_CAMPO_XCONDICION (port del legacy): evalua una condicion simple sobre un
/// campo del FormData (equals, notEquals, empty, notEmpty) y devuelve la accion de UI sobre
/// el campo objetivo (hide/show/require/optional). Si la condicion NO se cumple devuelve la
/// accion INVERSA, para que el renderer pueda alternar el estado al vaiven del valor.
/// </summary>
public sealed class BloquearCampoPorCondicionVerb : IRuleVerb
{
    public const string VerbName = "BLOQUEAR_CAMPO_XCONDICION";

    public string Name => VerbName;

    public RuleVerbDescriptor Descriptor { get; } = new(
        VerbName,
        "Bloquear campo por condicion",
        "Oculta/muestra o cambia la obligatoriedad de un campo objetivo segun una condicion simple sobre otro campo.",
        [
            new RuleVerbParamDescriptor("sourceField", "Campo evaluado", RuleParamType.FieldCode, Required: true,
                "FieldCode del campo cuyo valor se evalua."),
            new RuleVerbParamDescriptor("operator", "Operador", RuleParamType.Text, Required: true,
                "equals | notEquals | empty | notEmpty"),
            new RuleVerbParamDescriptor("value", "Valor comparado", RuleParamType.Text, Required: false,
                "Valor contra el que se compara (solo equals/notEquals)."),
            new RuleVerbParamDescriptor("targetField", "Campo objetivo", RuleParamType.FieldCode, Required: true,
                "FieldCode del campo sobre el que se aplica la accion."),
            new RuleVerbParamDescriptor("effect", "Efecto si se cumple", RuleParamType.Text, Required: false,
                "hide | show | require | optional (default hide). Si no se cumple, se aplica el efecto inverso.")
        ]);

    public Task<RuleVerbResult> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        var sourceField = context.GetStringParam("sourceField");
        var op = context.GetStringParam("operator");
        var targetField = context.GetStringParam("targetField");
        var comparedValue = context.GetStringParam("value");
        var effect = (context.GetStringParam("effect") ?? "hide").Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(sourceField) || string.IsNullOrWhiteSpace(targetField))
        {
            return Task.FromResult(RuleVerbResult.Fail(
                "Parametros 'sourceField' y 'targetField' son obligatorios."));
        }

        var current = context.FormData.GetValueOrDefault(sourceField);
        bool? condition = (op ?? "").Trim().ToLowerInvariant() switch
        {
            "equals" => string.Equals(current ?? "", comparedValue ?? "", StringComparison.OrdinalIgnoreCase),
            "notequals" => !string.Equals(current ?? "", comparedValue ?? "", StringComparison.OrdinalIgnoreCase),
            "empty" => string.IsNullOrWhiteSpace(current),
            "notempty" => !string.IsNullOrWhiteSpace(current),
            _ => null
        };
        if (condition is null)
        {
            return Task.FromResult(RuleVerbResult.Fail(
                "Parametro 'operator' invalido: usa equals, notEquals, empty o notEmpty."));
        }

        RuleAction? action = (effect, condition.Value) switch
        {
            ("hide", true) => RuleAction.Hide(targetField),
            ("hide", false) => RuleAction.Show(targetField),
            ("show", true) => RuleAction.Show(targetField),
            ("show", false) => RuleAction.Hide(targetField),
            ("require", true) => RuleAction.SetRequired(targetField, true),
            ("require", false) => RuleAction.SetRequired(targetField, false),
            ("optional", true) => RuleAction.SetRequired(targetField, false),
            ("optional", false) => RuleAction.SetRequired(targetField, true),
            _ => null
        };
        if (action is null)
        {
            return Task.FromResult(RuleVerbResult.Fail(
                "Parametro 'effect' invalido: usa hide, show, require u optional."));
        }

        return Task.FromResult(RuleVerbResult.Ok(
            $"Condicion {(condition.Value ? "cumplida" : "no cumplida")}: {action.Kind} sobre '{targetField}'.",
            1, [action], context.GetBoolParam("autoComplete")));
    }
}
