namespace Ecorex.Application.Forms.Lookups;

/// <summary>
/// Proyeccion PURA de una grilla de dataset externo (columnas + filas) a items de lookup (Ola 6/A2). Separada
/// del adaptador para testear el mapeo sin BD ni conexion externa. Modelo de COPIA: el valor guardado ES el
/// texto mostrado (columna DisplayField, o la primera con valor); los campos pedidos se copian por nombre de
/// columna (para el autollenado). El filtro por texto es substring case-insensitive en la etiqueta o en
/// cualquier campo copiado.
/// </summary>
public static class ExternalDatasetProjection
{
    public static List<FormLookupItem> Project(
        IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string?>> rows,
        string? displayField, IReadOnlyList<string> fields)
    {
        var colIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < columns.Count; i++) { colIndex[columns[i]] = i; }

        string? ValueOf(IReadOnlyList<string?> row, string field)
            => colIndex.TryGetValue(field, out var i) && i < row.Count ? row[i] : null;

        var items = new List<FormLookupItem>(rows.Count);
        foreach (var row in rows)
        {
            var display = !string.IsNullOrEmpty(displayField) ? ValueOf(row, displayField) : null;
            if (string.IsNullOrEmpty(display))
            {
                display = row.FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? string.Empty;
            }
            var picked = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in fields) { picked[key] = ValueOf(row, key); }
            items.Add(new FormLookupItem(display!, display!, picked));
        }
        return items;
    }

    public static List<FormLookupItem> Filter(List<FormLookupItem> items, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) { return items; }
        var q = query.Trim();
        return items
            .Where(it => it.Display.Contains(q, StringComparison.OrdinalIgnoreCase)
                || it.Fields.Values.Any(v => v is not null && v.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}
