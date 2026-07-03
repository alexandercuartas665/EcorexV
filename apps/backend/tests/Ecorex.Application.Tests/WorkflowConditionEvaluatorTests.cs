using Ecorex.Application.Workflows;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests unitarios del evaluador de condiciones de compuertas exclusivas (FASE 4,
/// ADR-0014): formato simple "approval == 'X'" / "approval != 'X'", arista default
/// (expresion vacia) y fail-closed ante formatos desconocidos.
/// </summary>
public class WorkflowConditionEvaluatorTests
{
    [Theory]
    [InlineData("approval == 'Approved'", "Approved", true)]
    [InlineData("approval == 'Approved'", "Rejected", false)]
    [InlineData("approval == 'Approved'", null, false)]
    [InlineData("approval == 'Rejected'", "Rejected", true)]
    // Case-insensitive en la variable y en el literal; comillas dobles tambien valen.
    [InlineData("APPROVAL == 'approved'", "Approved", true)]
    [InlineData("approval == \"Approved\"", "Approved", true)]
    // Espacios flexibles.
    [InlineData("  approval   ==   'Approved'  ", "Approved", true)]
    public void Evaluate_EqualityExpressions(string expression, string? approval, bool expected)
        => Assert.Equal(expected, WorkflowConditionEvaluator.Evaluate(expression, approval));

    [Theory]
    [InlineData("approval != 'Rejected'", "Approved", true)]
    [InlineData("approval != 'Rejected'", "Rejected", false)]
    [InlineData("approval != 'Rejected'", null, true)]
    public void Evaluate_InequalityExpressions(string expression, string? approval, bool expected)
        => Assert.Equal(expected, WorkflowConditionEvaluator.Evaluate(expression, approval));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyExpression_IsDefaultEdge_AndNeverMatchesByCondition(string? expression)
    {
        Assert.True(WorkflowConditionEvaluator.IsDefault(expression));
        // La default no aplica "por condicion": se elige como fallback en el motor.
        Assert.False(WorkflowConditionEvaluator.Evaluate(expression, "Approved"));
    }

    [Theory]
    // Formatos desconocidos: fail-closed (nunca aplican).
    [InlineData("resultado == 'Approved'")]
    [InlineData("approval > 5")]
    [InlineData("approval ==")]
    [InlineData("1 == 1; DROP TABLE x")]
    public void UnknownFormats_NeverMatch(string expression)
    {
        Assert.False(WorkflowConditionEvaluator.IsDefault(expression));
        Assert.False(WorkflowConditionEvaluator.Evaluate(expression, "Approved"));
    }
}
