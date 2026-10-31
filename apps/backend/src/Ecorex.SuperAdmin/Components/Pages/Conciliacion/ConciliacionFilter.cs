using System.Globalization;
using Ecorex.Application.Automatizaciones.ConciliacionDian;

namespace Ecorex.SuperAdmin.Components.Pages.Conciliacion;

/// <summary>
/// Nodo del arbol de filtros del constructor: o un GRUPO (op AND/OR con hijos) o una CONDICION
/// (campo/operador/valor). Se serializa a JSON para guardarlo (SavedFilter) y se evalua en cliente
/// contra cada renglon. Grupos anidables a cualquier profundidad.
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

public enum FilterFieldType { Text, Number, Date, Bool, Estado }

/// <summary>Metadatos de un campo filtrable (etiqueta + tipo). El tipo define los operadores disponibles.</summary>
public sealed record FilterField(string Key, string Label, FilterFieldType Type);

/// <summary>
/// Catalogo de campos y operadores del constructor, mas el evaluador (cliente) del arbol contra un renglon.
/// Las condiciones incompletas se IGNORAN (cuentan como verdaderas) para que la edicion en vivo no "rompa".
/// </summary>
public static class ConciliacionFilter
{
    public static readonly IReadOnlyList<FilterField> Fields = new List<FilterField>
    {
        new("proveedor", "Nombre proveedor", FilterFieldType.Text),
        new("nit", "NIT proveedor", FilterFieldType.Text),
        new("numprov", "N° factura proveedor", FilterFieldType.Text),
        new("numsol", "N° factura SISTEMA", FilterFieldType.Text),
        new("oc", "Orden de compra", FilterFieldType.Text),
        new("tipodoc", "Tipo doc DIAN", FilterFieldType.Text),
        new("tipopago", "Tipo de pago", FilterFieldType.Text),
        new("cufe", "CUFE", FilterFieldType.Text),
        new("fecha", "Fecha de emisión", FilterFieldType.Date),
        new("total", "Total factura", FilterFieldType.Number),
        new("subtotalneto", "Subtotal neto", FilterFieldType.Number),
        new("iva", "IVA descontable", FilterFieldType.Number),
        new("totalantesret", "Total antes de retenciones", FilterFieldType.Number),
        new("retefuente", "Retefuente", FilterFieldType.Number),
        new("retica", "ReteICA", FilterFieldType.Number),
        new("estado_radian", "Estado RADIAN", FilterFieldType.Estado),
        new("plataforma", "En Newton (plataforma)", FilterFieldType.Bool),
        new("archivos", "Tiene archivos", FilterFieldType.Bool),
        new("aprobada", "Factura aprobada", FilterFieldType.Bool),
        new("rechazado", "Documento rechazado", FilterFieldType.Bool),
        // Usuario asignado (texto = nombre). El valor lo resuelve la pagina via el resolver (UserNameById);
        // con el operador "está vacío" se filtran los renglones SIN usuario asignado.
        new("usuario", "Usuario", FilterFieldType.Text),
    };

    public static FilterField? FieldByKey(string? key) => Fields.FirstOrDefault(f => f.Key == key);

    /// <summary>Busca en el catalogo estatico + los campos dinamicos (EAV) que pasa la pagina.</summary>
    public static FilterField? FieldByKey(string? key, IReadOnlyList<FilterField>? extra)
    {
        var f = Fields.FirstOrDefault(x => x.Key == key);
        if (f is not null || extra is null) { return f; }
        return extra.FirstOrDefault(x => x.Key == key);
    }

    /// <summary>Operadores (clave -> etiqueta) validos para un tipo de campo.</summary>
    public static IReadOnlyList<(string Key, string Label)> OperatorsFor(FilterFieldType type) => type switch
    {
        FilterFieldType.Text => new (string, string)[]
        {
            ("contains", "contiene"), ("ncontains", "no contiene"),
            ("eq", "es igual a"), ("neq", "es distinto de"),
            ("empty", "está vacío"), ("nempty", "tiene contenido"),
        },
        FilterFieldType.Number => new (string, string)[]
        {
            ("eq", "="), ("neq", "≠"), ("gt", ">"), ("gte", "≥"),
            ("lt", "<"), ("lte", "≤"), ("between", "entre"),
        },
        FilterFieldType.Date => new (string, string)[]
        {
            ("eq", "es el día"), ("after", "después de"), ("before", "antes de"), ("between", "entre"),
        },
        FilterFieldType.Bool => new (string, string)[]
        {
            ("istrue", "sí"), ("isfalse", "no"),
        },
        FilterFieldType.Estado => new (string, string)[]
        {
            ("sin", "sin ningún estado"), ("con", "con algún estado"),
            ("ev30", "acuse de recibo (030)"), ("ev31", "reclamo (031)"),
            ("ev32", "recibo del bien/servicio (032)"), ("ev33", "aceptación expresa (033)"), ("ev34", "(034)"),
        },
        _ => Array.Empty<(string, string)>(),
    };

    /// <summary>true si el operador necesita un valor de texto/numero/fecha para evaluarse.</summary>
    public static bool OperatorNeedsValue(string op) => op is "contains" or "ncontains" or "eq" or "neq"
        or "gt" or "gte" or "lt" or "lte" or "between" or "after" or "before";

    public static bool OperatorNeedsValue2(string op) => op == "between";

    /// <summary>Evalua el arbol contra un renglon. hasFiles indica si ese CUFE tiene archivos (se calcula fuera).</summary>
    public static bool Evaluate(FilterNode? node, ConciliacionDianRenglonDto r, Func<string, bool> hasFiles)
        => Evaluate(node, r, hasFiles, null, null);

    /// <summary>
    /// Variante que admite campos DINAMICOS (EAV): <paramref name="extra"/> amplia el catalogo y
    /// <paramref name="dynResolver"/>(renglon, key) devuelve el valor string del campo dinamico para ese renglon.
    /// </summary>
    public static bool Evaluate(FilterNode? node, ConciliacionDianRenglonDto r, Func<string, bool> hasFiles,
        IReadOnlyList<FilterField>? extra, Func<ConciliacionDianRenglonDto, string, string?>? dynResolver)
    {
        if (node is null) { return true; }
        if (node.Kind == "group")
        {
            // Solo cuentan los hijos "evaluables" (condiciones completas o subgrupos no vacios).
            var active = node.Children.Where(c => IsActive(c, extra)).ToList();
            if (active.Count == 0) { return true; }
            return node.Op == "or"
                ? active.Any(c => Evaluate(c, r, hasFiles, extra, dynResolver))
                : active.All(c => Evaluate(c, r, hasFiles, extra, dynResolver));
        }
        return EvalCond(node, r, hasFiles, extra, dynResolver);
    }

    /// <summary>Un nodo "cuenta" si es una condicion completa o un grupo con al menos un hijo activo.</summary>
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

    /// <summary>Cuenta condiciones completas en todo el arbol (para el badge "Filtros (N)").</summary>
    public static int CountConditions(FilterNode? node, IReadOnlyList<FilterField>? extra = null)
    {
        if (node is null) { return 0; }
        if (node.Kind == "cond") { return IsConditionComplete(node, extra) ? 1 : 0; }
        return node.Children.Sum(c => CountConditions(c, extra));
    }

    private static bool EvalCond(FilterNode n, ConciliacionDianRenglonDto r, Func<string, bool> hasFiles,
        IReadOnlyList<FilterField>? extra, Func<ConciliacionDianRenglonDto, string, string?>? dynResolver)
    {
        var f = FieldByKey(n.Field, extra);
        if (f is null || string.IsNullOrEmpty(n.Operator)) { return true; } // incompleta -> se ignora

        // Campo dinamico (EAV) o "usuario": el valor lo resuelve la pagina (el nombre del usuario no se puede
        // sacar del renglon aqui). Se evalua por tipo contra el string (texto vacio => sin usuario).
        if (dynResolver is not null && (n.Field.StartsWith("dyn:", StringComparison.Ordinal) || n.Field == "usuario"))
        {
            var dv = dynResolver(r, n.Field) ?? "";
            return f.Type switch
            {
                FilterFieldType.Number => EvalNumber(TryNum(dv, out var dn) ? dn : 0m, n.Operator, n.Value, n.Value2),
                FilterFieldType.Date => TryDate(dv, out var dd) ? EvalDate(new DateTimeOffset(dd, TimeSpan.Zero), n.Operator, n.Value, n.Value2) : true,
                FilterFieldType.Bool => EvalBool(IsTruthy(dv), n.Operator),
                _ => EvalText(dv, n.Operator, n.Value),
            };
        }

        switch (f.Type)
        {
            case FilterFieldType.Text: return EvalText(TextOf(r, n.Field), n.Operator, n.Value);
            case FilterFieldType.Number: return EvalNumber(NumberOf(r, n.Field), n.Operator, n.Value, n.Value2);
            case FilterFieldType.Date: return EvalDate(r.FechaEmision, n.Operator, n.Value, n.Value2);
            case FilterFieldType.Bool: return EvalBool(BoolOf(r, n.Field, hasFiles), n.Operator);
            case FilterFieldType.Estado: return EvalEstado(r, n.Operator);
            default: return true;
        }
    }

    private static bool IsTruthy(string? v)
    {
        var s = (v ?? "").Trim();
        return s is "1" or "true" or "True" or "TRUE" or "si" or "Si" or "SI" or "Sí" or "SÍ" or "on";
    }

    private static string TextOf(ConciliacionDianRenglonDto r, string key) => key switch
    {
        "proveedor" => r.NombreProveedor,
        "nit" => r.NitProveedor,
        "numprov" => r.NumFacturaProveedor,
        "numsol" => r.NumFacturaSoldarco,
        "oc" => r.OrdenCompraSoldarco,
        "tipodoc" => r.TipoDocDian,
        "tipopago" => r.TipoPago,
        "cufe" => r.Cufe,
        _ => "",
    } ?? "";

    private static decimal NumberOf(ConciliacionDianRenglonDto r, string key) => key switch
    {
        "total" => r.TotalFactura,
        "subtotalneto" => r.SubtotalNeto,
        "iva" => r.IvaDescontable,
        "totalantesret" => r.TotalAntesRetenciones,
        "retefuente" => r.RetRetefuente,
        "retica" => r.RetIca,
        _ => 0m,
    };

    private static bool BoolOf(ConciliacionDianRenglonDto r, string key, Func<string, bool> hasFiles) => key switch
    {
        "plataforma" => r.PlataformaProveedor,
        "aprobada" => r.FacturaAprobada,
        "archivos" => hasFiles(r.Cufe),
        "rechazado" => r.Rechazado,
        _ => false,
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

    private static bool EvalDate(DateTimeOffset value, string op, string? arg, string? arg2)
    {
        if (!TryDate(arg, out var a)) { return true; }
        var d = value.Date;
        return op switch
        {
            "eq" => d == a.Date,
            "after" => d > a.Date,
            "before" => d < a.Date,
            "between" => TryDate(arg2, out var b) && d >= a.Date && d <= b.Date,
            _ => true,
        };
    }

    private static bool EvalBool(bool value, string op) => op switch
    {
        "istrue" => value,
        "isfalse" => !value,
        _ => true,
    };

    private static bool EvalEstado(ConciliacionDianRenglonDto r, string op)
    {
        var any = r.Evento30 || r.Evento31 || r.Evento32 || r.Evento33 || r.Evento34;
        return op switch
        {
            "sin" => !any,
            "con" => any,
            "ev30" => r.Evento30,
            "ev31" => r.Evento31,
            "ev32" => r.Evento32,
            "ev33" => r.Evento33,
            "ev34" => r.Evento34,
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
