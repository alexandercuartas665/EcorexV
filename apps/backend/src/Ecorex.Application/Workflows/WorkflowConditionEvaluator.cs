namespace Ecorex.Application.Workflows;

/// <summary>
/// Evaluador de las condiciones simples de las aristas de compuertas exclusivas.
/// Formato soportado (y suficiente para esta ola; el RulesEngine llegara en otra):
///   "approval == 'Approved'"   -> verdadero si el resultado de aprobacion es Approved
///   "approval != 'Rejected'"   -> verdadero si el resultado NO es Rejected
///   vacio / null               -> arista por defecto (se toma si ninguna condicion aplica)
/// La comparacion es case-insensitive y admite comillas simples o dobles en el literal.
/// Una expresion con formato desconocido NUNCA aplica (falso, fail-closed).
/// </summary>
public static class WorkflowConditionEvaluator
{
    public const string ApprovalVariable = "approval";

    public static bool IsDefault(string? expression) => string.IsNullOrWhiteSpace(expression);

    public static bool Evaluate(string? expression, string? approvalResult)
    {
        if (IsDefault(expression))
        {
            // La arista por defecto no "aplica" por condicion: se elige aparte como fallback.
            return false;
        }

        var text = expression!.Trim();
        bool negated;
        int opIndex;
        if ((opIndex = text.IndexOf("==", StringComparison.Ordinal)) >= 0)
        {
            negated = false;
        }
        else if ((opIndex = text.IndexOf("!=", StringComparison.Ordinal)) >= 0)
        {
            negated = true;
        }
        else
        {
            return false;
        }

        var left = text[..opIndex].Trim();
        if (!string.Equals(left, ApprovalVariable, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var right = text[(opIndex + 2)..].Trim();
        if (right.Length >= 2
            && ((right[0] == '\'' && right[^1] == '\'') || (right[0] == '"' && right[^1] == '"')))
        {
            right = right[1..^1];
        }
        else if (right.Length == 0)
        {
            return false;
        }

        var matches = string.Equals(right, approvalResult ?? "", StringComparison.OrdinalIgnoreCase);
        return negated ? !matches : matches;
    }
}
