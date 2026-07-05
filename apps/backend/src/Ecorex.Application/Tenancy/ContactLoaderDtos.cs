namespace Ecorex.Application.Tenancy;

// ============================================================================
// DTOs del Cargador de contactos (modulo 000873): parseo CSV -> mapeo de
// columnas -> validacion por fila -> carga transaccional al embudo (Lead).
// ============================================================================

/// <summary>Tabla parseada de un CSV: encabezados + filas de datos utilizables.</summary>
public sealed record CsvTable(IReadOnlyList<string> Headers, IReadOnlyList<CsvRow> Rows);

/// <summary>Fila de datos con su numero de linea fisico en el archivo (el encabezado es la linea 1).</summary>
public sealed record CsvRow(int LineNumber, IReadOnlyList<string> Fields);

/// <summary>Fila que no se pudo usar (rota): numero de linea y motivo.</summary>
public sealed record CsvRowError(int LineNumber, string Message);

/// <summary>Resultado del parseo: la tabla utilizable + las filas rotas reportadas.</summary>
public sealed record CsvParseResult(CsvTable Table, IReadOnlyList<CsvRowError> Errors);

/// <summary>
/// Mapeo de columnas del CSV (indice 0-based) a los campos reales del Lead.
/// Name -> Lead.ContactName (obligatorio); Phone -> Lead.ContactPhone;
/// Email y Company -> Lead.FieldValuesJson (claves "email" / "empresa");
/// Destination -> Lead.Destination; EstimatedValue -> Lead.EstimatedValue.
/// Null = esa columna no se carga.
/// </summary>
public sealed record ContactColumnMapping(
    int? Name,
    int? Phone = null,
    int? Email = null,
    int? Company = null,
    int? Destination = null,
    int? EstimatedValue = null)
{
    private static readonly string[] NameHints = ["nombre", "nombre_completo", "name", "contacto", "contact", "cliente"];
    private static readonly string[] PhoneHints = ["telefono", "phone", "celular", "movil", "whatsapp", "tel"];
    private static readonly string[] EmailHints = ["email", "correo", "e-mail", "mail"];
    private static readonly string[] CompanyHints = ["empresa", "company", "compania", "organizacion", "negocio"];
    private static readonly string[] DestinationHints = ["destino", "destination", "interes"];
    private static readonly string[] ValueHints = ["valor", "valor_estimado", "monto", "presupuesto", "importe", "estimated_value"];

    /// <summary>Propone un mapeo a partir de los nombres de los encabezados (sinonimos usuales ES/EN).</summary>
    public static ContactColumnMapping AutoMap(IReadOnlyList<string> headers)
    {
        return new ContactColumnMapping(
            Name: Find(headers, NameHints),
            Phone: Find(headers, PhoneHints),
            Email: Find(headers, EmailHints),
            Company: Find(headers, CompanyHints),
            Destination: Find(headers, DestinationHints),
            EstimatedValue: Find(headers, ValueHints));
    }

    private static int? Find(IReadOnlyList<string> headers, string[] hints)
    {
        // Primero coincidencia exacta (mas confiable), luego por contenido.
        for (var i = 0; i < headers.Count; i++)
        {
            var h = Normalize(headers[i]);
            if (hints.Contains(h))
            {
                return i;
            }
        }

        for (var i = 0; i < headers.Count; i++)
        {
            var h = Normalize(headers[i]);
            if (hints.Any(hint => h.Contains(hint, StringComparison.Ordinal)))
            {
                return i;
            }
        }

        return null;
    }

    private static string Normalize(string header) =>
        header.Trim().ToLowerInvariant().Replace(' ', '_');
}

/// <summary>Veredicto de una fila tras la validacion / carga.</summary>
public enum ContactRowStatus
{
    Valid = 0,
    Duplicate = 1,
    Invalid = 2
}

/// <summary>Resultado por fila: datos proyectados + veredicto + motivo si no es valida.</summary>
public sealed record ContactRowResult(
    int LineNumber,
    string? Name,
    string? Phone,
    string? Email,
    ContactRowStatus Status,
    string? Message);

/// <summary>Previsualizacion de la carga: conteos + veredicto fila a fila (nada se persiste).</summary>
public sealed record ContactImportPreview(
    int Total,
    int Valid,
    int Duplicates,
    int Invalid,
    IReadOnlyList<ContactRowResult> Rows);

/// <summary>Resultado de la carga transaccional: conteos finales + veredicto fila a fila.</summary>
public sealed record ContactImportResult(
    Guid BatchId,
    int Total,
    int Inserted,
    int Duplicates,
    int Invalid,
    IReadOnlyList<ContactRowResult> Rows);

/// <summary>Entrada del historial de cargas del tenant.</summary>
public sealed record ContactImportBatchDto(
    Guid Id,
    string FileName,
    int TotalRows,
    int Inserted,
    int Duplicates,
    int Invalid,
    DateTimeOffset CreatedAt,
    string? CreatedByName);
