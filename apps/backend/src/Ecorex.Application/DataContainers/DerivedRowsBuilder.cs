using System.Globalization;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.DataContainers;

/// <summary>
/// Motor PURO de derivacion (ADR-0127): sin EF, testeable sin UI. Recibe las columnas del origen y sus
/// filas (celdas por ColumnId, valor SIEMPRE string por el modelo EAV), NORMALIZA cada valor por tipo
/// antes de armar la clave de grupo (para que "10" y "10.0" caigan en el mismo grupo), agrupa, calcula
/// los agregados y devuelve filas por NOMBRE de columna destino, listas para <see cref="IRowIngestService"/>.
///
/// Tipos de resultado: Count -> Number; Sum/Avg -> Decimal; Min/Max heredan el tipo de la columna
/// origen. La columna clave (Text) es la concatenacion de los valores normalizados de agrupacion.
/// </summary>
public static class DerivedRowsBuilder
{
    /// <summary>Separador de la clave de grupo (visible al usuario en la columna "Clave").</summary>
    public const string KeySeparator = " | ";

    /// <summary>Tope de longitud de un nombre de columna (espeja el MaxLength de DataContainerColumn.Name).</summary>
    private const int MaxColumnName = 150;

    /// <summary>Resultado de validar una spec contra las columnas del origen: el plan de columnas o el error.</summary>
    public readonly record struct PlanResult(IReadOnlyList<DerivedColumnPlan>? Columns, string? Error)
    {
        public bool Ok => Columns is not null && Error is null;
    }

    /// <summary>
    /// Valida la spec contra el origen y devuelve las columnas que producira la derivada, en orden:
    /// agrupacion (heredan tipo), clave (Text) y agregados. Es la UNICA fuente de verdad del esquema de
    /// la hija: el wizard la usa para crear columnas y el refresco para casarlas por nombre.
    /// </summary>
    public static PlanResult Plan(DerivationSpec spec, IReadOnlyList<SourceColumnInfo> sourceColumns)
    {
        var byId = sourceColumns.ToDictionary(c => c.Id, c => c);
        var keyName = string.IsNullOrWhiteSpace(spec.KeyColumnName) ? DerivationSpec.DefaultKeyColumnName : spec.KeyColumnName.Trim();

        if (spec.GroupBy is null || spec.GroupBy.Count == 0)
        {
            return new PlanResult(null, "Marca al menos una columna de agrupacion.");
        }

        var plan = new List<DerivedColumnPlan>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var gid in spec.GroupBy.Distinct())
        {
            if (!byId.TryGetValue(gid, out var col))
            {
                return new PlanResult(null, "Una columna de agrupacion ya no existe en la tabla origen.");
            }
            if (!IsScalar(col.Type))
            {
                return new PlanResult(null, $"La columna '{col.Name}' no se puede usar para agrupar (no es escalar).");
            }
            if (!names.Add(col.Name))
            {
                return new PlanResult(null, $"La columna '{col.Name}' esta repetida.");
            }
            plan.Add(new DerivedColumnPlan(col.Name, col.Type, DerivedColumnRole.GroupBy));
        }

        if (!names.Add(keyName))
        {
            return new PlanResult(null, $"El nombre de la columna clave '{keyName}' choca con una columna de agrupacion.");
        }
        plan.Add(new DerivedColumnPlan(keyName, DataContainerColumnType.Text, DerivedColumnRole.Key));

        foreach (var agg in spec.Aggregates ?? Array.Empty<DerivationAggregate>())
        {
            var target = (agg.TargetColumnName ?? "").Trim();
            if (target.Length == 0) { return new PlanResult(null, "Un agregado no tiene nombre de columna destino."); }
            if (target.Length > MaxColumnName) { return new PlanResult(null, $"El nombre '{target}' supera {MaxColumnName} caracteres."); }
            if (!names.Add(target))
            {
                return new PlanResult(null, $"El nombre de columna '{target}' esta repetido en la derivada.");
            }

            if (agg.Fn == DerivationFn.Count)
            {
                plan.Add(new DerivedColumnPlan(target, DataContainerColumnType.Number, DerivedColumnRole.Aggregate));
                continue;
            }

            if (agg.SourceColumnId is not Guid sid || !byId.TryGetValue(sid, out var src))
            {
                return new PlanResult(null, $"El agregado '{target}' apunta a una columna que ya no existe en el origen.");
            }
            if (!IsScalar(src.Type))
            {
                return new PlanResult(null, $"La columna '{src.Name}' no se puede agregar (no es escalar).");
            }
            if (agg.Fn is DerivationFn.Sum or DerivationFn.Avg && !IsNumeric(src.Type))
            {
                return new PlanResult(null, $"'{agg.Fn}' solo aplica a columnas numericas; '{src.Name}' es {src.Type}.");
            }

            var type = agg.Fn switch
            {
                DerivationFn.Sum or DerivationFn.Avg => DataContainerColumnType.Decimal,
                _ => src.Type // Min/Max heredan el tipo.
            };
            plan.Add(new DerivedColumnPlan(target, type, DerivedColumnRole.Aggregate));
        }

        foreach (var f in spec.Filter ?? Array.Empty<DerivationFilter>())
        {
            if (!byId.TryGetValue(f.ColumnId, out var fc) || !IsScalar(fc.Type))
            {
                return new PlanResult(null, "Una columna del filtro ya no existe en la tabla origen.");
            }
        }

        return new PlanResult(plan, null);
    }

    /// <summary>
    /// Agrupa y agrega. <paramref name="rows"/> trae cada fila del origen como celdas por ColumnId. Las
    /// filas de salida estan ordenadas por clave (determinista) e indexadas por NOMBRE de columna destino.
    /// Lanza <see cref="InvalidOperationException"/> si la spec no valida (usar <see cref="Plan"/> antes).
    /// </summary>
    public static DerivedResult Build(
        DerivationSpec spec,
        IReadOnlyList<SourceColumnInfo> sourceColumns,
        IEnumerable<IReadOnlyDictionary<Guid, string?>> rows)
    {
        var planned = Plan(spec, sourceColumns);
        if (!planned.Ok) { throw new InvalidOperationException(planned.Error); }
        var columns = planned.Columns!;

        var byId = sourceColumns.ToDictionary(c => c.Id, c => c);
        var groupCols = spec.GroupBy.Distinct().Select(id => byId[id]).ToList();
        var keyName = columns.First(c => c.Role == DerivedColumnRole.Key).Name;
        var filters = (spec.Filter ?? Array.Empty<DerivationFilter>())
            .Select(f => (Filter: f, Column: byId[f.ColumnId])).ToList();
        var aggs = (spec.Aggregates ?? Array.Empty<DerivationAggregate>())
            .Select(a => (Agg: a, Column: a.SourceColumnId is Guid sid && byId.TryGetValue(sid, out var c) ? c : null))
            .ToList();

        var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
        var read = 0;
        var kept = 0;
        foreach (var row in rows)
        {
            read++;
            if (!PassesFilters(row, filters)) { continue; }
            kept++;

            var parts = new string[groupCols.Count];
            for (var i = 0; i < groupCols.Count; i++)
            {
                parts[i] = Normalize(Get(row, groupCols[i].Id), groupCols[i].Type);
            }
            var key = string.Join(KeySeparator, parts);
            if (!groups.TryGetValue(key, out var g))
            {
                g = new Group(parts);
                groups[key] = g;
            }
            g.Count++;
            foreach (var (agg, col) in aggs)
            {
                if (col is null) { continue; }
                g.Observe(agg.TargetColumnName.Trim(), col.Type, Get(row, col.Id));
            }
        }

        var outRows = new List<IReadOnlyDictionary<string, string?>>(groups.Count);
        foreach (var (key, g) in groups.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var dict = new Dictionary<string, string?>(StringComparer.Ordinal);
            for (var i = 0; i < groupCols.Count; i++)
            {
                dict[groupCols[i].Name] = g.Parts[i].Length == 0 ? null : g.Parts[i];
            }
            dict[keyName] = key;
            foreach (var (agg, col) in aggs)
            {
                var target = agg.TargetColumnName.Trim();
                dict[target] = agg.Fn switch
                {
                    DerivationFn.Count => g.Count.ToString(CultureInfo.InvariantCulture),
                    _ => g.Result(target, agg.Fn, col!.Type)
                };
            }
            outRows.Add(dict);
        }

        return new DerivedResult(columns, outRows, read, kept);
    }

    // ---- Normalizacion por tipo ----

    /// <summary>
    /// Forma CANONICA de un valor segun el tipo de su columna, para que la clave de grupo no dependa de
    /// como se tecleo: Number/Decimal con InvariantCulture y sin ceros a la derecha ("10.0" -> "10",
    /// "1.500" -> "1.5"), Date a ISO (yyyy-MM-dd, o con hora si la trae), Boolean a true/false, Text con
    /// trim. Lo que no se pueda interpretar se deja tal cual (trim). Null -> cadena vacia.
    /// </summary>
    public static string Normalize(string? value, DataContainerColumnType type)
    {
        var s = (value ?? "").Trim();
        if (s.Length == 0) { return ""; }
        switch (type)
        {
            case DataContainerColumnType.Number:
            case DataContainerColumnType.Decimal:
                return TryDecimal(s, out var d) ? FormatDecimal(d) : s;
            case DataContainerColumnType.Date:
                return TryDate(s, out var dt) ? FormatDate(dt) : s;
            case DataContainerColumnType.Boolean:
                return TryBool(s, out var b) ? (b ? "true" : "false") : s.ToLowerInvariant();
            default:
                return s;
        }
    }

    /// <summary>Parseo numerico tolerante: punto decimal (InvariantCulture) primero; si no hay punto,
    /// la coma se toma como DECIMAL (1,5 -> 1.5). No se aceptan separadores de miles: en un EAV que
    /// guarda texto, 1,500 es ambiguo y se prefiere leerlo como 1.5 antes que como 1500.</summary>
    public static bool TryDecimal(string? s, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(s)) { return false; }
        s = s.Trim();
        const NumberStyles styles = NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign;
        if (decimal.TryParse(s, styles, CultureInfo.InvariantCulture, out value)) { return true; }
        if (!s.Contains('.') && s.Contains(','))
        {
            return decimal.TryParse(s.Replace(',', '.'), styles, CultureInfo.InvariantCulture, out value);
        }
        return false;
    }

    public static bool TryDate(string? s, out DateTime value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(s)) { return false; }
        return DateTime.TryParse(s.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind, out value);
    }

    public static bool TryBool(string? s, out bool value)
    {
        value = false;
        if (string.IsNullOrWhiteSpace(s)) { return false; }
        switch (s.Trim().ToLowerInvariant())
        {
            case "true" or "1" or "si" or "yes" or "verdadero" or "on" or "x":
                value = true;
                return true;
            case "false" or "0" or "no" or "falso" or "off":
                value = false;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Decimal canonico: sin exponente, sin ceros a la derecha ni punto colgante.</summary>
    public static string FormatDecimal(decimal d)
    {
        // "/ 1.0000...m" normaliza la escala (1.500m -> 1.5m) sin perder precision.
        var normalized = d / 1.000000000000000000000000000000000m;
        return normalized.ToString(CultureInfo.InvariantCulture);
    }

    public static string FormatDate(DateTime dt)
        => dt.TimeOfDay == TimeSpan.Zero
            ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : dt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    public static bool IsScalar(DataContainerColumnType t)
        => t is DataContainerColumnType.Text or DataContainerColumnType.Number or DataContainerColumnType.Decimal
            or DataContainerColumnType.Date or DataContainerColumnType.Boolean;

    public static bool IsNumeric(DataContainerColumnType t)
        => t is DataContainerColumnType.Number or DataContainerColumnType.Decimal;

    // ---- Filtro previo ----

    private static bool PassesFilters(
        IReadOnlyDictionary<Guid, string?> row,
        List<(DerivationFilter Filter, SourceColumnInfo Column)> filters)
    {
        foreach (var (f, col) in filters)
        {
            var raw = Get(row, col.Id);
            var left = Normalize(raw, col.Type);
            var right = Normalize(f.Value, col.Type);
            var ok = f.Op switch
            {
                DerivationFilterOp.IsEmpty => left.Length == 0,
                DerivationFilterOp.NotEmpty => left.Length > 0,
                DerivationFilterOp.Equals => string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
                DerivationFilterOp.NotEquals => !string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
                DerivationFilterOp.Contains => left.Contains(right, StringComparison.OrdinalIgnoreCase),
                DerivationFilterOp.GreaterThan => Compare(left, right, col.Type) > 0,
                DerivationFilterOp.LessThan => left.Length > 0 && Compare(left, right, col.Type) < 0,
                _ => true
            };
            if (!ok) { return false; }
        }
        return true;
    }

    /// <summary>Comparacion tipada de dos valores YA normalizados. Vacio es menor que todo.</summary>
    public static int Compare(string a, string b, DataContainerColumnType type)
    {
        if (a.Length == 0 || b.Length == 0) { return a.Length.CompareTo(b.Length); }
        switch (type)
        {
            case DataContainerColumnType.Number:
            case DataContainerColumnType.Decimal:
                if (TryDecimal(a, out var da) && TryDecimal(b, out var db)) { return da.CompareTo(db); }
                break;
            case DataContainerColumnType.Date:
                if (TryDate(a, out var ta) && TryDate(b, out var tb)) { return ta.CompareTo(tb); }
                break;
            case DataContainerColumnType.Boolean:
                if (TryBool(a, out var ba) && TryBool(b, out var bb)) { return ba.CompareTo(bb); }
                break;
        }
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static string? Get(IReadOnlyDictionary<Guid, string?> row, Guid colId)
        => row.TryGetValue(colId, out var v) ? v : null;

    // ---- Acumulador por grupo ----

    private sealed class Group
    {
        public string[] Parts { get; }
        public int Count { get; set; }

        // Por columna destino: suma + cantidad de numericos, y min/max normalizados.
        private readonly Dictionary<string, (decimal Sum, int N)> _num = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _min = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _max = new(StringComparer.Ordinal);

        public Group(string[] parts) => Parts = parts;

        public void Observe(string target, DataContainerColumnType type, string? raw)
        {
            var norm = Normalize(raw, type);
            if (norm.Length == 0) { return; } // los vacios no cuentan en ningun agregado (como SQL NULL).

            if (IsNumeric(type))
            {
                // En una columna numerica, lo que no es numero no participa en NINGUN agregado (igual
                // que un NULL en SQL): ni en la suma ni en el min/max, para que un texto colado no gane.
                if (!TryDecimal(norm, out var d)) { return; }
                var cur = _num.TryGetValue(target, out var acc) ? acc : (0m, 0);
                _num[target] = (cur.Item1 + d, cur.Item2 + 1);
            }
            else if (type == DataContainerColumnType.Date && !TryDate(norm, out _)) { return; }
            else if (type == DataContainerColumnType.Boolean && !TryBool(norm, out _)) { return; }

            if (!_min.TryGetValue(target, out var mn) || Compare(norm, mn, type) < 0) { _min[target] = norm; }
            if (!_max.TryGetValue(target, out var mx) || Compare(norm, mx, type) > 0) { _max[target] = norm; }
        }

        public string? Result(string target, DerivationFn fn, DataContainerColumnType type)
        {
            switch (fn)
            {
                case DerivationFn.Sum:
                    return _num.TryGetValue(target, out var s) && s.N > 0 ? FormatDecimal(s.Sum) : null;
                case DerivationFn.Avg:
                    // Division por cero: un grupo sin valores numericos no tiene promedio (null), no 0.
                    return _num.TryGetValue(target, out var a) && a.N > 0 ? FormatDecimal(Math.Round(a.Sum / a.N, 6)) : null;
                case DerivationFn.Min:
                    return _min.TryGetValue(target, out var mn) ? mn : null;
                case DerivationFn.Max:
                    return _max.TryGetValue(target, out var mx) ? mx : null;
                default:
                    return Count.ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
