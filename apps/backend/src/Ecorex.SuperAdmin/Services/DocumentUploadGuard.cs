namespace Ecorex.SuperAdmin.Services;

/// <summary>
/// Reglas para aceptar los archivos del Gestor Documental. Hermano de
/// <see cref="ImageUploadGuard"/> y con el mismo razonamiento de fondo:
///
///  - La extension y el ContentType los pone el NAVEGADOR, o sea el cliente. Por si solos no
///    prueban nada; hay que mirar los BYTES.
///  - El nombre del archivo tambien viene del cliente y NUNCA se usa para construir la ruta de
///    escritura (path traversal): el nombre en disco lo genera el servidor con un Guid y la
///    extension sale de esta lista blanca.
///  - .svg y .html quedan FUERA: son documentos activos (script, on*, iframe) y servidos desde el
///    mismo origen se convierten en XSS almacenado contra quien los abra.
///
/// LIMITE CONOCIDO: .txt y .csv no tienen bytes magicos, asi que para ellos la comprobacion de
/// contenido no puede existir; se acepta cualquier binario renombrado a .txt. El riesgo real es
/// bajo porque se sirven como descarga, no se ejecutan. Se documenta para no dar por hecho que
/// TODO lo que pasa por aqui esta verificado byte a byte.
/// </summary>
public static class DocumentUploadGuard
{
    /// <summary>Tamano maximo por archivo (25 MB). Es tambien el tope que se pasa a OpenReadStream.</summary>
    public const long MaxBytes = 25L * 1024 * 1024;

    /// <summary>Extensiones aceptadas. Sin .svg ni .html (ver nota de la clase).</summary>
    public static readonly string[] AllowedExtensions =
    [
        ".pdf",
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".txt", ".csv",
        ".jpg", ".jpeg", ".png", ".gif", ".webp",
        ".zip"
    ];

    /// <summary>Valor del atributo accept del InputFile (solo filtra el dialogo, no es control de seguridad).</summary>
    public const string Accept =
        ".pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.txt,.csv,.jpg,.jpeg,.png,.gif,.webp,.zip";

    public const string FormatosTexto = "PDF, Word, Excel, PowerPoint, texto, CSV, imagenes o ZIP";

    /// <summary>
    /// Extension canonica de la lista blanca correspondiente al nombre recibido, o null si no se
    /// admite. Se devuelve la CONSTANTE interna, no el texto del cliente.
    /// </summary>
    public static string? ResolveExtension(string? clientFileName)
    {
        if (string.IsNullOrWhiteSpace(clientFileName)) { return null; }

        string ext;
        try
        {
            ext = Path.GetExtension(clientFileName).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return null;
        }

        foreach (var allowed in AllowedExtensions)
        {
            if (string.Equals(ext, allowed, StringComparison.Ordinal)) { return allowed; }
        }
        return null;
    }

    /// <summary>
    /// Comprueba que los primeros bytes correspondan de verdad al formato que anuncia la
    /// extension. Devuelve true para .txt y .csv sin mirar nada: no tienen firma (ver nota).
    /// </summary>
    public static bool MatchesSignature(ReadOnlySpan<byte> bytes, string extension) => extension switch
    {
        ".pdf" => IsPdf(bytes),
        // Los formatos Office modernos (OOXML) son ZIP; los antiguos son contenedores OLE.
        ".docx" or ".xlsx" or ".pptx" or ".zip" => IsZip(bytes),
        ".doc" or ".xls" or ".ppt" => IsOle(bytes) || IsZip(bytes),
        ".txt" or ".csv" => true,
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" => ImageUploadGuard.MatchesSignature(bytes, extension),
        _ => false
    };

    /// <summary>Nombre a escribir en disco: prefijo + Guid del SERVIDOR + extension de la lista blanca.</summary>
    public static string BuildStoredFileName(string prefix, string extension)
        => $"{prefix}-{Guid.NewGuid():N}{extension}";

    /// <summary>MIME razonable a partir de la extension, para que la descarga abra en la app correcta.</summary>
    public static string MimeFor(string extension) => extension switch
    {
        ".pdf" => "application/pdf",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xls" => "application/vnd.ms-excel",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".ppt" => "application/vnd.ms-powerpoint",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".txt" => "text/plain",
        ".csv" => "text/csv",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".zip" => "application/zip",
        _ => "application/octet-stream"
    };

    // "%PDF".
    private static bool IsPdf(ReadOnlySpan<byte> b)
        => b.Length >= 4 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46;

    // "PK" + (03 04 normal | 05 06 vacio | 07 08 dividido).
    private static bool IsZip(ReadOnlySpan<byte> b)
        => b.Length >= 4 && b[0] == 0x50 && b[1] == 0x4B
           && ((b[2] == 0x03 && b[3] == 0x04) || (b[2] == 0x05 && b[3] == 0x06) || (b[2] == 0x07 && b[3] == 0x08));

    // Contenedor OLE / Compound File Binary (Office 97-2003): D0 CF 11 E0 A1 B1 1A E1.
    private static bool IsOle(ReadOnlySpan<byte> b)
        => b.Length >= 8 && b[0] == 0xD0 && b[1] == 0xCF && b[2] == 0x11 && b[3] == 0xE0
           && b[4] == 0xA1 && b[5] == 0xB1 && b[6] == 0x1A && b[7] == 0xE1;
}
