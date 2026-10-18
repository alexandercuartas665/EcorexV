using System.Globalization;
using System.Text;

namespace Ecorex.Application.Automatizaciones.ConciliacionDian;

// ===================== Especificacion de una regla (lo que va en DefinitionJson) =====================

/// <summary>Normalizacion aplicada a AMBOS lados antes de comparar en un VLOOKUP (match tolerante).</summary>
public sealed class RuleNorm
{
    public bool Trim { get; set; } = true;
    public bool Upper { get; set; }
    public bool StripLeadingZeros { get; set; }
    public bool DigitsOnly { get; set; }
}

/// <summary>Una condicion de match del VLOOKUP: columna del contenedor == valor del campo (renglon o scratch).</summary>
public sealed class RuleLookupMatch
{
    public string Column { get; set; } = "";       // columna del contenedor (p. ej. REF1)
    public string SourceField { get; set; } = "";  // campo de origen (p. ej. numprov) o $scratch
}

/// <summary>Regla de BUSQUEDA (VLOOKUP) contra una tabla de un contenedor de datos.</summary>
public sealed class RuleLookupSpec
{
    public Guid ContainerId { get; set; }
    public string ContainerLabel { get; set; } = ""; // "DIAN_CONCILIADOR / COMPRAS_SISTEMA" (solo display)
    public List<RuleLookupMatch> Matches { get; set; } = new();
    public string ReturnColumn { get; set; } = "";   // columna a devolver (p. ej. DOC_PEDIDO)
    public string TargetField { get; set; } = "";    // campo destino (p. ej. numsol) o $scratch
    public string OnNotFound { get; set; } = "blank"; // blank | keep
    public string OnMultiple { get; set; } = "first"; // first | blank
    public RuleNorm Norm { get; set; } = new();
}

/// <summary>Nodo del arbol de condiciones (mismo shape que el constructor de filtros para reuso en la UI).</summary>
public sealed class RuleCond
{
    public string Kind { get; set; } = "cond";  // "group" | "cond"
    public string Op { get; set; } = "and";      // group: "and" | "or"
    public List<RuleCond> Children { get; set; } = new();
    public string Field { get; set; } = "";       // cond: campo
    public string Operator { get; set; } = "";    // contains|ncontains|eq|neq|empty|nempty|gt|gte|lt|lte|istrue|isfalse
    public string? Value { get; set; }
    public string? Value2 { get; set; }
}

/// <summary>Asignacion de un valor a un campo (parte "entonces"/"si no" de una condicion). El valor admite
/// tokens {otroCampo} que se resuelven contra el contexto.</summary>
public sealed class RuleAssign
{
    public string Field { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>Regla de CONDICION: cuando (arbol Y/O) -> entonces (asignaciones); opcional si-no.</summary>
public sealed class RuleConditionSpec
{
    public RuleCond When { get; set; } = new() { Kind = "group", Op = "and" };
    public List<RuleAssign> Then { get; set; } = new();
    public List<RuleAssign> Else { get; set; } = new();
}

// ===================== Catalogo de campos del renglon (para los dropdowns de la UI) =====================

public enum RuleFieldType { Text, Number, Date, Bool }

public sealed record RuleField(string Key, string Label, RuleFieldType Type, bool Writable);

/// <summary>Contenedor + sus columnas, para elegir en el editor de VLOOKUP.</summary>
public sealed record RuleContenedorDto(Guid ContainerId, string Label, IReadOnlyList<string> Columns);

public static class ConciliacionRuleFields
{
    /// <summary>Campos del renglon disponibles como origen/destino/condicion. Writable = se puede asignar.</summary>
    public static readonly IReadOnlyList<RuleField> All = new List<RuleField>
    {
        new("numprov", "N° factura proveedor", RuleFieldType.Text, false),
        new("numsol", "N° factura SOLDARCO", RuleFieldType.Text, true),
        new("oc", "Orden de compra", RuleFieldType.Text, true),
        new("nit", "NIT proveedor", RuleFieldType.Text, false),
        new("proveedor", "Nombre proveedor", RuleFieldType.Text, false),
        new("tipodoc", "Tipo doc DIAN", RuleFieldType.Text, false),
        new("tipopago", "Tipo de pago", RuleFieldType.Text, false),
        new("cufe", "CUFE", RuleFieldType.Text, false),
        new("fecha", "Fecha de emisión", RuleFieldType.Date, false),
        new("sub_bruto", "Subtotal bruto", RuleFieldType.Number, false),
        new("descuento", "Descuento", RuleFieldType.Number, false),
        new("sub_neto", "Subtotal neto", RuleFieldType.Number, false),
        new("iva", "IVA descontable", RuleFieldType.Number, false),
        new("total_antes_ret", "Total antes de retenciones", RuleFieldType.Number, false),
        new("retefuente", "Retefuente", RuleFieldType.Number, false),
        new("ica", "ReteICA", RuleFieldType.Number, false),
        new("total", "Total factura", RuleFieldType.Number, false),
        new("plataforma", "En Newton (plataforma)", RuleFieldType.Bool, false),
        new("aprobada", "Factura aprobada", RuleFieldType.Bool, true),
        new("ev30", "Evento 30 (acuse)", RuleFieldType.Bool, false),
        new("ev31", "Evento 31 (reclamo)", RuleFieldType.Bool, false),
        new("ev32", "Evento 32 (recibo bien/serv)", RuleFieldType.Bool, false),
        new("ev33", "Evento 33 (aceptación)", RuleFieldType.Bool, false),
        new("ev34", "Evento 34", RuleFieldType.Bool, false),
        new("estado", "Estado", RuleFieldType.Text, false),
    };

    public static RuleField? ByKey(string? key) => All.FirstOrDefault(f => f.Key == key);

    public static IReadOnlyList<(string Key, string Label)> OperatorsFor(RuleFieldType t) => t switch
    {
        RuleFieldType.Text => new (string, string)[]
        {
            ("contains", "contiene"), ("ncontains", "no contiene"),
            ("eq", "es igual a"), ("neq", "es distinto de"),
            ("empty", "está vacío"), ("nempty", "tiene contenido"),
        },
        RuleFieldType.Number => new (string, string)[]
        {
            ("eq", "="), ("neq", "≠"), ("gt", ">"), ("gte", "≥"), ("lt", "<"), ("lte", "≤"),
        },
        RuleFieldType.Date => new (string, string)[]
        {
            ("eq", "es el día"), ("empty", "vacío"), ("nempty", "con fecha"),
        },
        RuleFieldType.Bool => new (string, string)[] { ("istrue", "sí"), ("isfalse", "no") },
        _ => Array.Empty<(string, string)>(),
    };

    public static bool OperatorNeedsValue(string op) => op is "contains" or "ncontains" or "eq" or "neq" or "gt" or "gte" or "lt" or "lte";
}

// ===================== Evaluador / helpers del motor (puros, testeables) =====================

public static class ConciliacionRuleEngine
{
    /// <summary>Normaliza un valor para el match del VLOOKUP segun las banderas de la regla.</summary>
    public static string Normalize(string? v, RuleNorm n)
    {
        var s = v ?? "";
        if (n.Trim) { s = s.Trim(); }
        if (n.DigitsOnly) { s = new string(s.Where(char.IsDigit).ToArray()); }
        if (n.StripLeadingZeros) { s = s.TrimStart('0'); if (s.Length == 0 && (v ?? "").Any(char.IsDigit)) { s = "0"; } }
        if (n.Upper) { s = s.ToUpperInvariant(); }
        return s;
    }

    /// <summary>Clave compuesta (una o varias columnas de match) normalizada.</summary>
    public static string MatchKey(IEnumerable<string?> values, RuleNorm n) =>
        string.Join("\u0001", values.Select(x => Normalize(x, n)));

    /// <summary>Reemplaza tokens {campo} del valor de una asignacion con el contexto. Sin token = literal.</summary>
    public static string ResolveValue(string raw, Func<string, string?> ctx)
    {
        if (string.IsNullOrEmpty(raw) || raw.IndexOf('{') < 0) { return raw; }
        var sb = new StringBuilder();
        for (var i = 0; i < raw.Length;)
        {
            if (raw[i] == '{')
            {
                var end = raw.IndexOf('}', i + 1);
                if (end > i)
                {
                    var key = raw.Substring(i + 1, end - i - 1).Trim();
                    sb.Append(ctx(key) ?? "");
                    i = end + 1;
                    continue;
                }
            }
            sb.Append(raw[i]);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>Evalua el arbol de condiciones contra el contexto (campo -> valor string). Nodos incompletos se ignoran.</summary>
    public static bool Eval(RuleCond? node, Func<string, string?> ctx)
    {
        if (node is null) { return true; }
        if (node.Kind == "group")
        {
            var active = node.Children.Where(IsActive).ToList();
            if (active.Count == 0) { return true; }
            return node.Op == "or" ? active.Any(c => Eval(c, ctx)) : active.All(c => Eval(c, ctx));
        }
        return EvalCond(node, ctx);
    }

    public static bool IsActive(RuleCond n) =>
        n.Kind == "group" ? n.Children.Any(IsActive)
        : (!string.IsNullOrEmpty(n.Field) && !string.IsNullOrEmpty(n.Operator));

    private static bool EvalCond(RuleCond n, Func<string, string?> ctx)
    {
        if (string.IsNullOrEmpty(n.Field) || string.IsNullOrEmpty(n.Operator)) { return true; }
        var value = ctx(n.Field) ?? "";
        var arg = n.Value ?? "";
        switch (n.Operator)
        {
            case "contains": return value.Contains(arg, StringComparison.OrdinalIgnoreCase);
            case "ncontains": return !value.Contains(arg, StringComparison.OrdinalIgnoreCase);
            case "eq": return string.Equals(value.Trim(), arg.Trim(), StringComparison.OrdinalIgnoreCase);
            case "neq": return !string.Equals(value.Trim(), arg.Trim(), StringComparison.OrdinalIgnoreCase);
            case "empty": return string.IsNullOrWhiteSpace(value);
            case "nempty": return !string.IsNullOrWhiteSpace(value);
            case "istrue": return IsTrue(value);
            case "isfalse": return !IsTrue(value);
            case "gt": return Num(value, out var a1) && Num(arg, out var b1) && a1 > b1;
            case "gte": return Num(value, out var a2) && Num(arg, out var b2) && a2 >= b2;
            case "lt": return Num(value, out var a3) && Num(arg, out var b3) && a3 < b3;
            case "lte": return Num(value, out var a4) && Num(arg, out var b4) && a4 <= b4;
            default: return true;
        }
    }

    public static bool IsTrue(string? v)
    {
        var s = (v ?? "").Trim();
        return s is "true" or "True" or "1" or "Si" or "SI" or "si" or "Sí" or "SÍ";
    }

    private static bool Num(string? s, out decimal n)
    {
        s = (s ?? "").Trim().Replace(",", ".");
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out n);
    }
}
