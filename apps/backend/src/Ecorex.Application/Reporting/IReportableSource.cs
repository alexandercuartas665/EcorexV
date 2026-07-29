namespace Ecorex.Application.Reporting;

/// <summary>
/// Una fuente reportable NATIVA (entidad EF curada por el dev). El patron es "registrar muchas
/// implementaciones + un despachador" (igual que IFormLookupSource): cada entidad reportable es una
/// clase pequena que declara sus campos logicos y sabe consultarse a si misma con LINQ tipado sobre
/// su DbSet. Como el DbSet lleva el filtro global tenant, la consulta es tenant-safe por construccion
/// (no hay forma de pedir cross-tenant). El despachador <see cref="IReportDataSource"/> valida el
/// spec contra el catalogo antes de delegar aqui.
/// </summary>
public interface IReportableSource
{
    /// <summary>Metadatos de catalogo de esta fuente (clave, campos, capacidades).</summary>
    ReportSourceDescriptor Describe();

    /// <summary>Ejecuta el spec (ya validado contra el catalogo) y devuelve un dataset neutro.</summary>
    Task<ReportDataSet> QueryAsync(ReportQuerySpec spec, ReportContext ctx, CancellationToken ct = default);
}

/// <summary>
/// Conversion de un valor de celda/parametro (texto) al tipo logico del campo. Espeja la regla del
/// EAV de contenedores: el valor SIEMPRE se persiste como string y se convierte al leer segun el tipo
/// de la columna. Cultura invariante para que PG y SQL Server coincidan.
/// </summary>
public static class ReportValueConverter
{
    public static object? Convert(string? raw, ReportFieldType type)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var s = raw.Trim();
        return type switch
        {
            ReportFieldType.Number =>
                long.TryParse(s, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var l) ? l : (object?)null,
            ReportFieldType.Decimal =>
                decimal.TryParse(s, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : (object?)null,
            ReportFieldType.Date =>
                DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var dt) ? dt : (object?)null,
            ReportFieldType.Boolean =>
                bool.TryParse(s, out var b) ? b
                    : (s == "1" ? true : s == "0" ? false : (object?)null),
            _ => s
        };
    }

    /// <summary>Interpreta un valor como decimal para agregaciones numericas. Null si no parsea.</summary>
    public static decimal? AsDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return decimal.TryParse(raw.Trim(), System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
    }
}
