namespace Ecorex.SuperAdmin.Components.Shared.Grid;

/// <summary>
/// Nodo del arbol de filtros de la grilla comun (ADR-0207): GRUPO (Y/O con hijos, anidable) o CONDICION
/// (campo/operador/valor). Mismo formato JSON que el constructor del Conciliador, para que un filtro
/// guardado (SavedFilter) sea portable.
/// </summary>
public sealed class GridFilterNode
{
    /// <summary>"group" o "cond".</summary>
    public string Kind { get; set; } = "cond";

    public string Op { get; set; } = "and";
    public List<GridFilterNode> Children { get; set; } = new();

    public string Field { get; set; } = "";
    public string Operator { get; set; } = "";
    public string? Value { get; set; }
    public string? Value2 { get; set; }

    public static GridFilterNode NewGroup(string op = "and") => new() { Kind = "group", Op = op, Children = new() };
    public static GridFilterNode NewCond() => new() { Kind = "cond" };
}

/// <summary>Campo filtrable (sale de las columnas de la grilla).</summary>
public sealed record GridFilterField(string Key, string Label, GridColType Type, string? Section);

/// <summary>Operadores por tipo y evaluador del arbol sobre una fila. Las condiciones incompletas se
/// ignoran (cuentan como verdaderas) para que la edicion en vivo no vacie la grilla.</summary>
public static class GridFilter
{
    public static IReadOnlyList<(string Key, string Label)> OperatorsFor(GridColType type) => type switch
    {
        GridColType.Number => new (string, string)[]
        {
            ("eq", "="), ("neq", "\u2260"), ("gt", ">"), ("gte", "\u2265"), ("lt", "<"), ("lte", "\u2264"),
            ("between", "entre"), ("empty", "est\u00e1 vac\u00edo"), ("nempty", "tiene contenido"),
        },
        GridColType.Date => new (string, string)[]
        {
            ("eq", "es el d\u00eda"), ("after", "despu\u00e9s de"), ("before", "antes de"), ("between", "entre"),
            ("empty", "est\u00e1 vac\u00edo"), ("nempty", "tiene contenido"),
        },
        GridColType.Bool => new (string, string)[] { ("istrue", "s\u00ed"), ("isfalse", "no") },
        _ => new (string, string)[]
        {
            ("contains", "contiene"), ("ncontains", "no contiene"), ("eq", "es igual a"), ("neq", "es distinto de"),
            ("in", "es uno de (separados por coma)"), ("empty", "est\u00e1 vac\u00edo"), ("nempty", "tiene contenido"),
        },
    };

    public static bool NeedsValue(string op) => op is "contains" or "ncontains" or "eq" or "neq" or "in"
        or "gt" or "gte" or "lt" or "lte" or "between" or "after" or "before";

    public static bool NeedsValue2(string op) => op == "between";

    public static bool IsConditionComplete(GridFilterNode n, Func<string, GridFilterField?> fieldOf)
    {
        if (fieldOf(n.Field) is null || string.IsNullOrEmpty(n.Operator)) { return false; }
        if (NeedsValue(n.Operator) && string.IsNullOrWhiteSpace(n.Value)) { return false; }
        if (NeedsValue2(n.Operator) && string.IsNullOrWhiteSpace(n.Value2)) { return false; }
        return true;
    }

    public static bool IsActive(GridFilterNode n, Func<string, GridFilterField?> fieldOf)
        => n.Kind == "group" ? n.Children.Any(c => IsActive(c, fieldOf)) : IsConditionComplete(n, fieldOf);

    public static int CountConditions(GridFilterNode? node, Func<string, GridFilterField?> fieldOf)
    {
        if (node is null) { return 0; }
        if (node.Kind == "cond") { return IsConditionComplete(node, fieldOf) ? 1 : 0; }
        return node.Children.Sum(c => CountConditions(c, fieldOf));
    }

    /// <summary>Evalua el arbol. <paramref name="raw"/>(clave) da el valor crudo de la fila y
    /// <paramref name="text"/>(clave) su texto visible (para los campos de texto).</summary>
    public static bool Evaluate(GridFilterNode? node, Func<string, GridFilterField?> fieldOf,
        Func<string, object?> raw, Func<string, string> text)
    {
        if (node is null) { return true; }
        if (node.Kind == "group")
        {
            var active = node.Children.Where(c => IsActive(c, fieldOf)).ToList();
            if (active.Count == 0) { return true; }
            return node.Op == "or"
                ? active.Any(c => Evaluate(c, fieldOf, raw, text))
                : active.All(c => Evaluate(c, fieldOf, raw, text));
        }
        var f = fieldOf(node.Field);
        if (f is null || string.IsNullOrEmpty(node.Operator)) { return true; }

        if (node.Operator is "empty" or "nempty")
        {
            var empty = f.Type == GridColType.Text ? string.IsNullOrWhiteSpace(text(f.Key)) : GridFormat.IsEmpty(raw(f.Key));
            return node.Operator == "empty" ? empty : !empty;
        }
        return f.Type switch
        {
            GridColType.Number => EvalNumber(GridFormat.ToDecimal(raw(f.Key)), node.Operator, node.Value, node.Value2),
            GridColType.Date => EvalDate(GridFormat.ToDate(raw(f.Key)), node.Operator, node.Value, node.Value2),
            GridColType.Bool => node.Operator == "istrue" ? GridFormat.ToBool(raw(f.Key)) : !GridFormat.ToBool(raw(f.Key)),
            _ => EvalText(text(f.Key), node.Operator, node.Value),
        };
    }

    private static bool EvalText(string value, string op, string? arg)
    {
        value = (value ?? "").Trim();
        var a = (arg ?? "").Trim();
        return op switch
        {
            "contains" => value.Contains(a, StringComparison.OrdinalIgnoreCase),
            "ncontains" => !value.Contains(a, StringComparison.OrdinalIgnoreCase),
            "eq" => string.Equals(value, a, StringComparison.OrdinalIgnoreCase),
            "neq" => !string.Equals(value, a, StringComparison.OrdinalIgnoreCase),
            "in" => a.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(x => string.Equals(value, x, StringComparison.OrdinalIgnoreCase)),
            _ => true,
        };
    }

    private static bool EvalNumber(decimal? value, string op, string? arg, string? arg2)
    {
        if (GridFormat.ParseDecimal(arg) is not decimal a) { return true; }
        if (value is not decimal v) { return op == "neq"; }
        return op switch
        {
            "eq" => v == a,
            "neq" => v != a,
            "gt" => v > a,
            "gte" => v >= a,
            "lt" => v < a,
            "lte" => v <= a,
            "between" => GridFormat.ParseDecimal(arg2) is decimal b && v >= Math.Min(a, b) && v <= Math.Max(a, b),
            _ => true,
        };
    }

    private static bool EvalDate(DateTime? value, string op, string? arg, string? arg2)
    {
        if (GridFormat.ParseDate(arg) is not DateTime a) { return true; }
        if (value is not DateTime v) { return false; }
        var d = v.Date;
        return op switch
        {
            "eq" => d == a.Date,
            "after" => d > a.Date,
            "before" => d < a.Date,
            "between" => GridFormat.ParseDate(arg2) is DateTime b && d >= a.Date && d <= b.Date,
            _ => true,
        };
    }
}
