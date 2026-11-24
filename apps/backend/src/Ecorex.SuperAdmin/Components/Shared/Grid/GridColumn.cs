using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace Ecorex.SuperAdmin.Components.Shared.Grid;

/// <summary>Tipo logico de una columna de la grilla comun (ADR-0207). Define operadores de filtro,
/// orden, alineacion, sumas y el formato al exportar a Excel.</summary>
public enum GridColType { Text, Number, Date, Bool }

/// <summary>
/// Definicion de una columna de <c>EcxGrid</c>. La pagina declara QUE muestra (valor, texto, celda a
/// medida); el componente resuelve filtro, orden, agrupacion, sumas, columnas fijas y exportacion.
/// </summary>
public sealed class GridColumn<TItem>
{
    /// <summary>Clave estable (se guarda en las preferencias del usuario y en los filtros guardados).</summary>
    public required string Key { get; init; }

    public required string Label { get; init; }

    public GridColType Type { get; init; } = GridColType.Text;

    /// <summary>Valor crudo: decimal/int para Number, DateTime/DateTimeOffset/DateOnly para Date, bool para
    /// Bool, texto para Text. Se usa para ordenar, filtrar, sumar y exportar.</summary>
    public required Func<TItem, object?> Value { get; init; }

    /// <summary>Texto visible (y de agrupacion/exportacion). Si es null se formatea <see cref="Value"/>.</summary>
    public Func<TItem, string?>? Text { get; init; }

    /// <summary>Celda a medida (avatar, chips, botones). Si es null se pinta el texto.</summary>
    public RenderFragment<TItem>? Cell { get; init; }

    public bool Groupable { get; init; } = true;
    public bool Sortable { get; init; } = true;
    public bool Filterable { get; init; } = true;

    /// <summary>Solo Number: suma en subtotales y total general.</summary>
    public bool Summable { get; init; }

    /// <summary>Columna oculta hasta que el usuario la muestre (p.ej. campos de ficha).</summary>
    public bool DefaultHidden { get; init; }

    public bool Exportable { get; init; } = true;

    /// <summary>Seccion en los menus de columnas y filtros (p.ej. "Campos de ficha").</summary>
    public string? Section { get; init; }

    /// <summary>Clase CSS extra de la celda de datos.</summary>
    public string? CellClass { get; init; }

    /// <summary>Texto de la celda para mostrar, agrupar y exportar.</summary>
    public string TextOf(TItem item) => Text is not null ? (Text(item) ?? "") : GridFormat.Format(Value(item), Type);
}

/// <summary>Conversiones y formato comunes de la grilla (cultura del producto: es-CO).</summary>
public static class GridFormat
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-CO");

    public static string Format(object? value, GridColType type)
    {
        switch (value)
        {
            case null: return "";
            case string s: return s;
            case bool b: return b ? "Si" : "No";
            case DateTimeOffset dto: return dto.ToString("dd/MM/yyyy", Culture);
            case DateTime dt: return dt.ToString("dd/MM/yyyy", Culture);
            case DateOnly d: return d.ToString("dd/MM/yyyy", Culture);
        }
        if (type == GridColType.Number && ToDecimal(value) is decimal n) { return n.ToString("#,##0.##", Culture); }
        return Convert.ToString(value, Culture) ?? "";
    }

    public static decimal? ToDecimal(object? value) => value switch
    {
        null => null,
        decimal d => d,
        int i => i,
        long l => l,
        double db => (decimal)db,
        float f => (decimal)f,
        string s => ParseDecimal(s),
        _ => ParseDecimal(Convert.ToString(value, CultureInfo.InvariantCulture)),
    };

    public static decimal? ParseDecimal(string? s)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0) { return null; }
        if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)) { return n; }
        if (decimal.TryParse(s, NumberStyles.Number, Culture, out n)) { return n; }
        // Montos con simbolo o separadores mezclados ("$ 1.234.567"): se dejan solo digitos, signo y coma.
        var clean = new string(s.Where(ch => char.IsDigit(ch) || ch is ',' or '-').ToArray()).Replace(',', '.');
        return decimal.TryParse(clean, NumberStyles.Number, CultureInfo.InvariantCulture, out n) ? n : null;
    }

    public static DateTime? ToDate(object? value) => value switch
    {
        null => null,
        DateTime dt => dt,
        DateTimeOffset dto => dto.DateTime,
        DateOnly d => d.ToDateTime(TimeOnly.MinValue),
        string s => ParseDate(s),
        _ => null,
    };

    public static DateTime? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) { return null; }
        s = s.Trim();
        string[] formats = { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm" };
        if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) { return d; }
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) { return d; }
        return DateTime.TryParse(s, Culture, DateTimeStyles.None, out d) ? d : null;
    }

    public static bool ToBool(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => s.Trim().ToLowerInvariant() is "1" or "true" or "si" or "s\u00ed" or "x" or "on" or "yes",
        _ => ToDecimal(value) is decimal n && n != 0,
    };

    public static bool IsEmpty(object? value) => value is null || (value is string s && string.IsNullOrWhiteSpace(s));
}
