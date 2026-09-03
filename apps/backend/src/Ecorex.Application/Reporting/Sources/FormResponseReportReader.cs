using Ecorex.Application.Common;
using Ecorex.Application.Forms;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Reporting.Sources;

/// <summary>
/// Lee una fuente reportable de tipo RESPUESTAS DE FORMULARIO (modulos), analogo al
/// <see cref="ContainerReportReader"/> pero sobre <c>_db.FormResponses</c> (ADR-0068). Una fuente por
/// MODULO (form_definitions.is_module = true). La clave es "form:{code}" (no el id): el code es estable
/// entre entornos (dev/prod), asi un PanelSpec autorizado por la sesion de reportes funciona igual en
/// ambos, mientras que el definitionId difiere por tenant/entorno.
///
/// Tenant-safe por construccion: <c>FormResponses</c>, <c>FormDefinitions</c> y <c>FormQuestions</c>
/// llevan el filtro global de tenant, asi que un tenant solo ve SUS modulos y SUS respuestas; pedir el
/// modulo de otro devuelve vacio. Los campos salen de las <c>FormQuestions</c> escalares del modulo
/// (<see cref="FormFieldValidator.IsCapture"/>); mas campos sinteticos (Reference, RecordNumber, Status,
/// IsActive, TransactionDate, SubmittedAt, CreatedAt). El valor se lee del jsonb <c>data</c> con la forma
/// {"value":...,"type":...} via <see cref="FormResponseService.ParseDocument"/> y se convierte al tipo
/// declarado. Solo se excluyen por defecto las respuestas anuladas (record_status = Voided); el estado se
/// expone ademas como campo para filtrar. Soporta modo tabular y agregacion numerica (Sum/Avg/Min/Max/Count).
/// </summary>
public sealed class FormResponseReportReader
{
    public const string KeyPrefix = "form:";
    private const int MaterializeCap = 50_000;

    // Campos sinteticos SIEMPRE presentes (no salen del jsonb, sino de columnas de la respuesta).
    public const string FieldReference = "Reference";
    public const string FieldRecordNumber = "RecordNumber";
    public const string FieldStatus = "Status";
    public const string FieldIsActive = "IsActive";
    public const string FieldTransactionDate = "TransactionDate";
    public const string FieldSubmittedAt = "SubmittedAt";
    public const string FieldCreatedAt = "CreatedAt";

    private static readonly HashSet<string> SyntheticKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        FieldReference, FieldRecordNumber, FieldStatus, FieldIsActive,
        FieldTransactionDate, FieldSubmittedAt, FieldCreatedAt
    };

    private readonly IApplicationDbContext _db;

    public FormResponseReportReader(IApplicationDbContext db) => _db = db;

    public static bool Handles(string sourceKey) => sourceKey.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase);

    public static string KeyForCode(string code) => KeyPrefix + code;

    /// <summary>Extrae el CODE del modulo desde la clave "form:{code}". Null si la clave no es de formulario.</summary>
    public static string? ParseCode(string sourceKey) =>
        Handles(sourceKey) ? sourceKey[KeyPrefix.Length..].Trim() : null;

    /// <summary>Mapea el tipo de control del formulario al tipo logico reportable. El control Number del
    /// formulario admite decimales (ej. montos "381466.40"), por eso se mapea a Decimal: asi el valor se
    /// preserva al convertir (Number logico usa parseo entero) y queda agregable (Sum/Avg).</summary>
    public static ReportFieldType MapType(FormControlType type) => type switch
    {
        FormControlType.Number => ReportFieldType.Decimal,
        FormControlType.Date or FormControlType.DateTime or FormControlType.Time => ReportFieldType.Date,
        FormControlType.Toggle => ReportFieldType.Boolean,
        _ => ReportFieldType.Text
    };

    // ---- Catalogo ----

    /// <summary>Modulos del tenant activo como descriptores reportables (uno por form_definition is_module).</summary>
    public async Task<IReadOnlyList<ReportSourceDescriptor>> ListModulesAsync(CancellationToken ct = default)
    {
        var modules = await _db.FormDefinitions.AsNoTracking()
            .Where(d => d.IsModule && !d.IsArchived)
            .OrderBy(d => d.Title)
            .Select(d => new { d.Id, d.Code, d.Title })
            .ToListAsync(ct);

        var result = new List<ReportSourceDescriptor>(modules.Count);
        foreach (var m in modules)
        {
            var fields = await BuildFieldsAsync(m.Id, ct);
            result.Add(new ReportSourceDescriptor(KeyForCode(m.Code), m.Title, ReportSourceKind.Native, fields));
        }

        return result;
    }

    /// <summary>Descriptor del modulo por su code (tenant activo). Null si no existe o no es modulo.</summary>
    public async Task<ReportSourceDescriptor?> DescribeAsync(string code, CancellationToken ct = default)
    {
        var module = await _db.FormDefinitions.AsNoTracking()
            .Where(d => d.IsModule && !d.IsArchived && d.Code == code)
            .Select(d => new { d.Id, d.Code, d.Title })
            .FirstOrDefaultAsync(ct);
        if (module is null)
        {
            return null;
        }

        var fields = await BuildFieldsAsync(module.Id, ct);
        return new ReportSourceDescriptor(KeyForCode(module.Code), module.Title, ReportSourceKind.Native, fields);
    }

    private async Task<IReadOnlyList<ReportField>> BuildFieldsAsync(Guid definitionId, CancellationToken ct)
    {
        var questions = await _db.FormQuestions.AsNoTracking()
            .Where(q => q.DefinitionId == definitionId)
            .OrderBy(q => q.SortOrder)
            .Select(q => new { q.FieldCode, q.Label, q.ControlType })
            .ToListAsync(ct);

        var fields = new List<ReportField>();
        foreach (var q in questions)
        {
            // Solo campos que capturan un valor ESCALAR (ni estructura, ni multimedia, ni GridDetail/Subform).
            if (!FormFieldValidator.IsCapture(q.ControlType) || SyntheticKeys.Contains(q.FieldCode))
            {
                continue;
            }

            var type = MapType(q.ControlType);
            var isNumeric = type is ReportFieldType.Number or ReportFieldType.Decimal;
            var label = string.IsNullOrWhiteSpace(q.Label) ? q.FieldCode : q.Label;
            fields.Add(new ReportField(q.FieldCode, label, type, CanFilter: true, CanGroup: true, CanAggregate: isNumeric));
        }

        // Campos sinteticos (columnas de la respuesta): siempre presentes.
        fields.Add(new ReportField(FieldReference, "Referencia", ReportFieldType.Text));
        fields.Add(new ReportField(FieldRecordNumber, "Numero de registro", ReportFieldType.Text));
        fields.Add(new ReportField(FieldStatus, "Estado del registro", ReportFieldType.Text));
        fields.Add(new ReportField(FieldIsActive, "Activa", ReportFieldType.Boolean));
        fields.Add(new ReportField(FieldTransactionDate, "Fecha de transaccion", ReportFieldType.Date));
        fields.Add(new ReportField(FieldSubmittedAt, "Enviada", ReportFieldType.Date));
        fields.Add(new ReportField(FieldCreatedAt, "Creada", ReportFieldType.Date));
        return fields;
    }

    // ---- Consulta ----

    public async Task<ReportDataSet> QueryAsync(ReportSourceDescriptor descriptor, ReportQuerySpec spec, ReportContext ctx, CancellationToken ct = default)
    {
        var code = ParseCode(descriptor.Key)
            ?? throw new ReportValidationException($"Clave de modulo invalida: '{descriptor.Key}'.");

        var definitionId = await _db.FormDefinitions.AsNoTracking()
            .Where(d => d.IsModule && d.Code == code)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(ct);
        if (definitionId is null)
        {
            // El tenant no tiene ese modulo (o no es modulo): conjunto vacio, no error de datos.
            return new ReportDataSet(descriptor.Fields.Select(f => new ReportColumn(f.Key, f.DisplayName, f.Type)).ToList(),
                new List<IReadOnlyList<object?>>());
        }

        var fieldTypes = descriptor.Fields.ToDictionary(f => f.Key, f => f.Type, StringComparer.OrdinalIgnoreCase);

        // Solo respuestas VIGENTES: se excluyen las anuladas (Voided). El resto se expone; el estado del
        // registro (Status) y IsActive quedan como campos para que el panel filtre a voluntad.
        var raw = await _db.FormResponses.AsNoTracking()
            .Where(r => r.DefinitionId == definitionId && r.RecordStatus != FormRecordStatus.Voided)
            .Select(r => new
            {
                r.Data,
                r.Reference,
                r.RecordNumber,
                r.RecordStatus,
                r.IsActive,
                r.TransactionDate,
                r.SubmittedAt,
                r.CreatedAt
            })
            .Take(MaterializeCap)
            .ToListAsync(ct);

        // Pivotea cada respuesta a un diccionario campoKey -> valor (string) leyendo el jsonb + sinteticos.
        var rows = new List<Dictionary<string, string?>>(raw.Count);
        foreach (var r in raw)
        {
            var doc = FormResponseService.ParseDocument(r.Data);
            var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                [FieldReference] = r.Reference,
                [FieldRecordNumber] = r.RecordNumber,
                [FieldStatus] = r.RecordStatus.ToString(),
                [FieldIsActive] = r.IsActive ? "true" : "false",
                [FieldTransactionDate] = r.TransactionDate?.ToString("o"),
                [FieldSubmittedAt] = r.SubmittedAt?.ToString("o"),
                [FieldCreatedAt] = r.CreatedAt.ToString("o")
            };

            foreach (var f in descriptor.Fields)
            {
                if (SyntheticKeys.Contains(f.Key))
                {
                    continue;
                }

                row[f.Key] = doc.TryGetValue(f.Key, out var fv) ? fv.Value : null;
            }

            rows.Add(row);
        }

        // Filtros del spec en memoria (el conjunto ya viene acotado a tenant + modulo).
        foreach (var f in spec.Filters)
        {
            var type = fieldTypes.TryGetValue(f.FieldKey, out var t) ? t : ReportFieldType.Text;
            rows = rows.Where(row => MatchesInMemory(row.GetValueOrDefault(f.FieldKey), f, type)).ToList();
        }

        return spec.IsAggregated
            ? Aggregate(descriptor, spec, rows, fieldTypes)
            : Tabular(descriptor, spec, rows);
    }

    private static ReportDataSet Tabular(ReportSourceDescriptor descriptor, ReportQuerySpec spec, List<Dictionary<string, string?>> rows)
    {
        var fields = spec.Fields.Count > 0
            ? spec.Fields.Select(k => descriptor.FindField(k)!).ToList()
            : descriptor.Fields.ToList();

        var columns = fields.Select(f => new ReportColumn(f.Key, f.DisplayName, f.Type)).ToList();

        IEnumerable<Dictionary<string, string?>> ordered = rows;
        foreach (var s in spec.Sort)
        {
            ordered = s.Descending
                ? ordered.OrderByDescending(r => r.GetValueOrDefault(s.FieldKey), StringComparer.OrdinalIgnoreCase)
                : ordered.OrderBy(r => r.GetValueOrDefault(s.FieldKey), StringComparer.OrdinalIgnoreCase);
        }

        if (spec.Top is int top && top >= 0)
        {
            ordered = ordered.Take(top);
        }

        var data = ordered
            .Select(row => (IReadOnlyList<object?>)fields
                .Select(f => ReportValueConverter.Convert(row.GetValueOrDefault(f.Key), f.Type))
                .ToList())
            .ToList();

        return new ReportDataSet(columns, data);
    }

    private static ReportDataSet Aggregate(
        ReportSourceDescriptor descriptor, ReportQuerySpec spec,
        List<Dictionary<string, string?>> rows, Dictionary<string, ReportFieldType> fieldTypes)
    {
        if (spec.GroupBy.Count != 1)
        {
            throw new ReportValidationException("Una fuente de respuestas de formulario soporta exactamente un campo de agrupacion.");
        }

        var groupField = descriptor.FindField(spec.GroupBy[0])
            ?? throw new ReportValidationException($"Campo de agrupacion desconocido: '{spec.GroupBy[0]}'.");

        var groups = rows.GroupBy(r => r.GetValueOrDefault(groupField.Key)).ToList();

        var columns = new List<ReportColumn> { new(groupField.Key, groupField.DisplayName, groupField.Type) };
        var aggregates = spec.Aggregates.Count > 0
            ? spec.Aggregates.ToList()
            : new List<ReportAggregate> { new(groupField.Key, ReportAggregateFunction.Count) };

        foreach (var agg in aggregates)
        {
            var label = agg.Function switch
            {
                ReportAggregateFunction.Count => "Conteo",
                ReportAggregateFunction.Sum => "Suma",
                ReportAggregateFunction.Avg => "Promedio",
                ReportAggregateFunction.Min => "Minimo",
                ReportAggregateFunction.Max => "Maximo",
                _ => agg.Function.ToString()
            };
            var type = agg.Function == ReportAggregateFunction.Count ? ReportFieldType.Number : ReportFieldType.Decimal;
            columns.Add(new ReportColumn($"{agg.Function}_{agg.FieldKey}", label, type));
        }

        var outRows = new List<IReadOnlyList<object?>>();
        foreach (var g in groups)
        {
            var cells = new List<object?> { g.Key };
            foreach (var agg in aggregates)
            {
                cells.Add(ComputeAggregate(agg, g, descriptor));
            }

            outRows.Add(cells);
        }

        foreach (var s in spec.Sort)
        {
            var idx = columns.FindIndex(c => c.Key.Equals(s.FieldKey, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                outRows = (s.Descending
                    ? outRows.OrderByDescending(r => r[idx], NullSafeComparer.Instance)
                    : outRows.OrderBy(r => r[idx], NullSafeComparer.Instance)).ToList();
            }
        }

        return new ReportDataSet(columns, outRows);
    }

    private static object? ComputeAggregate(ReportAggregate agg, IGrouping<string?, Dictionary<string, string?>> group, ReportSourceDescriptor descriptor)
    {
        if (agg.Function == ReportAggregateFunction.Count)
        {
            return (long)group.Count();
        }

        var field = descriptor.FindField(agg.FieldKey)
            ?? throw new ReportValidationException($"Campo de agregacion desconocido: '{agg.FieldKey}'.");
        if (!field.CanAggregate)
        {
            throw new ReportValidationException($"El campo '{field.DisplayName}' no es numerico: no admite {agg.Function}.");
        }

        var values = group
            .Select(row => ReportValueConverter.AsDecimal(row.GetValueOrDefault(field.Key)))
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();
        if (values.Count == 0)
        {
            return null;
        }

        return agg.Function switch
        {
            ReportAggregateFunction.Sum => values.Sum(),
            ReportAggregateFunction.Avg => Math.Round(values.Average(), 4),
            ReportAggregateFunction.Min => values.Min(),
            ReportAggregateFunction.Max => values.Max(),
            _ => null
        };
    }

    private static bool MatchesInMemory(string? raw, ReportFilter f, ReportFieldType type)
    {
        if (type is ReportFieldType.Number or ReportFieldType.Decimal)
        {
            var v = ReportValueConverter.AsDecimal(raw);
            var a = f.Values.Count > 0 ? ReportValueConverter.AsDecimal(f.Values[0]) : null;
            var b = f.Values.Count > 1 ? ReportValueConverter.AsDecimal(f.Values[1]) : null;
            if (v is null)
            {
                return false;
            }

            return f.Operator switch
            {
                ReportFilterOperator.Equals => a.HasValue && v == a,
                ReportFilterOperator.NotEquals => !a.HasValue || v != a,
                ReportFilterOperator.GreaterThan => a.HasValue && v > a,
                ReportFilterOperator.GreaterThanOrEqual => a.HasValue && v >= a,
                ReportFilterOperator.LessThan => a.HasValue && v < a,
                ReportFilterOperator.LessThanOrEqual => a.HasValue && v <= a,
                ReportFilterOperator.Between => a.HasValue && b.HasValue && v >= a && v <= b,
                _ => true
            };
        }

        if (type == ReportFieldType.Date)
        {
            var v = ReportValueConverter.Convert(raw, ReportFieldType.Date) as DateTimeOffset?;
            var a = f.Values.Count > 0 ? ReportValueConverter.Convert(f.Values[0], ReportFieldType.Date) as DateTimeOffset? : null;
            var b = f.Values.Count > 1 ? ReportValueConverter.Convert(f.Values[1], ReportFieldType.Date) as DateTimeOffset? : null;
            if (v is null)
            {
                return false;
            }

            return f.Operator switch
            {
                ReportFilterOperator.GreaterThan => a.HasValue && v > a,
                ReportFilterOperator.GreaterThanOrEqual => a.HasValue && v >= a,
                ReportFilterOperator.LessThan => a.HasValue && v < a,
                ReportFilterOperator.LessThanOrEqual => a.HasValue && v <= a,
                ReportFilterOperator.Between => a.HasValue && b.HasValue && v >= a && v <= b,
                _ => true
            };
        }

        var s = raw?.ToLowerInvariant();
        var target = (f.Values.Count > 0 ? f.Values[0] : null)?.ToLowerInvariant();
        return f.Operator == ReportFilterOperator.NotEquals ? s != target : s == target;
    }

    private sealed class NullSafeComparer : IComparer<object?>
    {
        public static readonly NullSafeComparer Instance = new();

        public int Compare(object? x, object? y)
        {
            if (x is null && y is null) { return 0; }
            if (x is null) { return -1; }
            if (y is null) { return 1; }
            if (x is IComparable cx && x.GetType() == y.GetType()) { return cx.CompareTo(y); }
            return string.CompareOrdinal(x.ToString(), y.ToString());
        }
    }
}
