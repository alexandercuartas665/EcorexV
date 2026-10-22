using System.Globalization;
using Ecorex.Application.Requerimientos;
using Ecorex.Domain.Enums;

namespace Ecorex.SuperAdmin.Components.Pages.Requerimientos;

/// <summary>
/// Nodo del arbol de filtros del constructor: o un GRUPO (op AND/OR con hijos) o una CONDICION
/// (campo/operador/valor). Se serializa a JSON para guardarlo (SavedFilter) y se evalua en cliente
/// contra cada necesidad. Grupos anidables a cualquier profundidad. (Calcado de Conciliacion DIAN.)
/// </summary>
public sealed class FilterNode
{
    /// <summary>"group" o "cond".</summary>
    public string Kind { get; set; } = "cond";

    // ---- group ----
    public string Op { get; set; } = "and"; // "and" | "or"
    public List<FilterNode> Children { get; set; } = new();

    // ---- cond ----
    public string Field { get; set; } = "";
    public string Operator { get; set; } = "";
    public string? Value { get; set; }
    public string? Value2 { get; set; } // para "entre"

    public static FilterNode NewGroup(string op = "and") => new() { Kind = "group", Op = op, Children = new() };
    public static FilterNode NewCond() => new() { Kind = "cond", Field = "", Operator = "", Value = null };
}

public enum FilterFieldType { Text, Number, Date, Estado }

/// <summary>Metadatos de un campo filtrable (etiqueta + tipo). El tipo define los operadores disponibles.</summary>
public sealed record FilterField(string Key, string Label, FilterFieldType Type);

/// <summary>
/// Catalogo de campos y operadores del constructor para las necesidades de compra, mas el evaluador
/// (cliente) del arbol contra una fila (RequerimientoDto). Las condiciones incompletas se IGNORAN.
/// </summary>
public static class RequerimientoFilter
{
    public static readonly IReadOnlyList<FilterField> Fields = new List<FilterField>
    {
        new("producto", "Producto", FilterFieldType.Text),
        new("area", "Area solicitante", FilterFieldType.Text),
        new("origen", "Origen", FilterFieldType.Text),
        new("unidad", "Unidad de medida", FilterFieldType.Text),
        new("proveedor", "Proveedor (en distribuciones)", FilterFieldType.Text),
        new("estado", "Estado", FilterFieldType.Estado),
        new("cantidad", "Cantidad", FilterFieldType.Number),
        new("monto", "Monto total", FilterFieldType.Number),
        new("hijos", "N.o de distribuciones", FilterFieldType.Number),
        new("fecha", "Fecha de necesidad", FilterFieldType.Date),
    };

    public static FilterField? FieldByKey(string? key) => Fields.FirstOrDefault(f => f.Key == key);

    /// <summary>Busca el campo en el catalogo base MAS los campos dinamicos (a medida) ofrecidos como filtro.</summary>
    public static FilterField? FieldByKey(string? key, IReadOnlyList<FilterField>? extra)
        => Fields.FirstOrDefault(f => f.Key == key) ?? extra?.FirstOrDefault(f => f.Key == key);

    /// <summary>Mapea el tipo de un campo dinamico a un tipo de filtro.</summary>
    public static FilterFieldType MapDynFilterType(string? t) => t switch
    {
        "Number" => FilterFieldType.Number,
        "Date" => FilterFieldType.Date,
        _ => FilterFieldType.Text, // Text | Bool | Select | Url
    };

    public static readonly IReadOnlyList<RequerimientoEstado> Estados = Enum.GetValues<RequerimientoEstado>();

    public static string EstadoLabel(RequerimientoEstado e) => e switch
    {
        RequerimientoEstado.Recibido => "Recibido",
        RequerimientoEstado.Agrupado => "Agrupado",
        RequerimientoEstado.Planificado => "Planificado",
        RequerimientoEstado.ConProveedor => "Con proveedor",
        RequerimientoEstado.Ejecutado => "Ejecutado",
        RequerimientoEstado.Cerrado => "Cerrado",
        _ => e.ToString(),
    };

    /// <summary>Operadores (clave -> etiqueta) validos para un tipo de campo.</summary>
    public static IReadOnlyList<(string Key, string Label)> OperatorsFor(FilterFieldType type) => type switch
    {
        FilterFieldType.Text => new (string, string)[]
        {
            ("contains", "contiene"), ("ncontains", "no contiene"),
            ("eq", "es igual a"), ("neq", "es distinto de"),
            ("empty", "esta vacio"), ("nempty", "tiene contenido"),
        },
        FilterFieldType.Number => new (string, string)[]
        {
            ("eq", "="), ("neq", "≠"), ("gt", ">"), ("gte", "≥"),
            ("lt", "<"), ("lte", "≤"), ("between", "entre"),
        },
        FilterFieldType.Date => new (string, string)[]
        {
            ("eq", "es el dia"), ("after", "despues de"), ("before", "antes de"), ("between", "entre"),
        },
        FilterFieldType.Estado => new (string, string)[]
        {
            ("eq", "es"), ("neq", "no es"),
        },
        _ => Array.Empty<(string, string)>(),
    };

    public static bool OperatorNeedsValue(string op) => op is "contains" or "ncontains" or "eq" or "neq"
        or "gt" or "gte" or "lt" or "lte" or "between" or "after" or "before";

    public static bool OperatorNeedsValue2(string op) => op == "between";

    /// <summary>Resolver del valor (texto) de un campo DINAMICO para una fila, por su clave ("dyn:{id}").</summary>
    public delegate string? DynValueResolver(RequerimientoDto r, string key);

    public static bool Evaluate(FilterNode? node, RequerimientoDto r,
        IReadOnlyList<FilterField>? extra = null, DynValueResolver? dyn = null)
    {
        if (node is null) { return true; }
        if (node.Kind == "group")
        {
            var active = node.Children.Where(c => IsActive(c, extra)).ToList();
            if (active.Count == 0) { return true; }
            return node.Op == "or"
                ? active.Any(c => Evaluate(c, r, extra, dyn))
                : active.All(c => Evaluate(c, r, extra, dyn));
        }
        return EvalCond(node, r, extra, dyn);
    }

    public static bool IsActive(FilterNode n, IReadOnlyList<FilterField>? extra = null)
    {
        if (n.Kind == "group") { return n.Children.Any(c => IsActive(c, extra)); }
        return IsConditionComplete(n, extra);
    }

    public static bool IsConditionComplete(FilterNode n, IReadOnlyList<FilterField>? extra = null)
    {
        var f = FieldByKey(n.Field, extra);
        if (f is null || string.IsNullOrEmpty(n.Operator)) { return false; }
        if (OperatorNeedsValue(n.Operator) && string.IsNullOrWhiteSpace(n.Value)) { return false; }
        if (OperatorNeedsValue2(n.Operator) && string.IsNullOrWhiteSpace(n.Value2)) { return false; }
        return true;
    }

    public static int CountConditions(FilterNode? node, IReadOnlyList<FilterField>? extra = null)
    {
        if (node is null) { return 0; }
        if (node.Kind == "cond") { return IsConditionComplete(node, extra) ? 1 : 0; }
        return node.Children.Sum(c => CountConditions(c, extra));
    }

    private static bool EvalCond(FilterNode n, RequerimientoDto r, IReadOnlyList<FilterField>? extra, DynValueResolver? dyn)
    {
        var f = FieldByKey(n.Field, extra);
        if (f is null || string.IsNullOrEmpty(n.Operator)) { return true; } // incompleta -> se ignora
        // Campo dinamico (a medida): el valor no esta en el DTO, se resuelve por el delegate.
        if (extra is not null && extra.Any(x => x.Key == n.Field))
        {
            return EvalDyn(f.Type, dyn?.Invoke(r, n.Field), n.Operator, n.Value, n.Value2);
        }
        switch (f.Type)
        {
            case FilterFieldType.Text: return EvalText(TextOf(r, n.Field), n.Operator, n.Value);
            case FilterFieldType.Number: return EvalNumber(NumberOf(r, n.Field), n.Operator, n.Value, n.Value2);
            case FilterFieldType.Date: return EvalDate(r.FechaNecesidad, n.Operator, n.Value, n.Value2);
            case FilterFieldType.Estado: return EvalEstado(r, n.Operator, n.Value);
            default: return true;
        }
    }

    private static bool EvalDyn(FilterFieldType type, string? value, string op, string? arg, string? arg2)
    {
        switch (type)
        {
            case FilterFieldType.Number:
                return TryNum(value, out var nv) && EvalNumber(nv, op, arg, arg2);
            case FilterFieldType.Date:
                return TryDate(value, out var dv) && EvalDate(new DateTimeOffset(dv.Date, TimeSpan.Zero), op, arg, arg2);
            default:
                return EvalText(value ?? "", op, arg);
        }
    }

    private static string TextOf(RequerimientoDto r, string key) => key switch
    {
        "producto" => r.ItemNombre ?? r.Descripcion,
        "area" => r.Area,
        "origen" => r.OrigenNombre ?? "",
        "unidad" => r.UnidadMedida,
        "proveedor" => string.Join(" ", r.Hijos.Select(h => h.ProveedorNombre).Where(p => !string.IsNullOrWhiteSpace(p))),
        _ => "",
    } ?? "";

    private static decimal NumberOf(RequerimientoDto r, string key) => key switch
    {
        "cantidad" => r.Cantidad,
        "monto" => r.MontoTotal,
        "hijos" => r.CantidadHijos,
        _ => 0m,
    };

    private static bool EvalText(string value, string op, string? arg)
    {
        value ??= "";
        var a = (arg ?? "").Trim();
        return op switch
        {
            "contains" => value.Contains(a, StringComparison.OrdinalIgnoreCase),
            "ncontains" => !value.Contains(a, StringComparison.OrdinalIgnoreCase),
            "eq" => string.Equals(value.Trim(), a, StringComparison.OrdinalIgnoreCase),
            "neq" => !string.Equals(value.Trim(), a, StringComparison.OrdinalIgnoreCase),
            "empty" => string.IsNullOrWhiteSpace(value),
            "nempty" => !string.IsNullOrWhiteSpace(value),
            _ => true,
        };
    }

    private static bool EvalNumber(decimal value, string op, string? arg, string? arg2)
    {
        if (!TryNum(arg, out var a)) { return true; }
        return op switch
        {
            "eq" => value == a,
            "neq" => value != a,
            "gt" => value > a,
            "gte" => value >= a,
            "lt" => value < a,
            "lte" => value <= a,
            "between" => TryNum(arg2, out var b) && value >= Math.Min(a, b) && value <= Math.Max(a, b),
            _ => true,
        };
    }

    private static bool EvalDate(DateTimeOffset? value, string op, string? arg, string? arg2)
    {
        if (value is not DateTimeOffset dv) { return false; }
        if (!TryDate(arg, out var a)) { return true; }
        var d = dv.Date;
        return op switch
        {
            "eq" => d == a.Date,
            "after" => d > a.Date,
            "before" => d < a.Date,
            "between" => TryDate(arg2, out var b) && d >= a.Date && d <= b.Date,
            _ => true,
        };
    }

    private static bool EvalEstado(RequerimientoDto r, string op, string? arg)
    {
        var a = (arg ?? "").Trim();
        if (a.Length == 0) { return true; }
        var match = string.Equals(r.Estado.ToString(), a, StringComparison.OrdinalIgnoreCase);
        return op switch
        {
            "eq" => match,
            "neq" => !match,
            _ => true,
        };
    }

    private static bool TryNum(string? s, out decimal n)
    {
        s = (s ?? "").Trim().Replace(",", ".");
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out n);
    }

    private static bool TryDate(string? s, out DateTime d)
    {
        d = default;
        if (string.IsNullOrWhiteSpace(s)) { return false; }
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)
            || DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out d);
    }
}
