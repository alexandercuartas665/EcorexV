namespace Ecorex.SuperAdmin.Services;

/// <summary>
/// Reglas comunes para aceptar imagenes subidas por el usuario y guardarlas en wwwroot.
///
/// El PORQUE de que esto exista y no se valide "a ojo" en cada pagina:
///  - La extension y el ContentType que llegan del navegador los controla el CLIENTE. Un atacante
///    envia lo que quiera en ambos, asi que por si solos no prueban nada: hay que mirar los BYTES.
///  - El nombre del archivo tambien viene del cliente. Nunca se usa para construir la ruta de
///    escritura (path traversal); el nombre en disco lo genera el servidor con un Guid y la
///    extension SALE DE ESTA LISTA BLANCA, no del texto que mando el navegador.
///  - SVG queda FUERA a proposito: es XML, admite &lt;script&gt; y manejadores on*, y al servirse
///    desde el mismo origen de la aplicacion se convierte en XSS almacenado (roba la sesion de
///    quien abra la imagen). Para fotos de producto o un logo no aporta nada frente a PNG.
/// </summary>
public static class ImageUploadGuard
{
    /// <summary>Extensiones aceptadas. Sin .svg (ver nota de la clase).</summary>
    public static readonly string[] AllowedExtensions =
        [".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp"];

    /// <summary>Valor para el atributo accept del InputFile (solo filtra el dialogo, no es control de seguridad).</summary>
    public const string Accept = ".jpg,.jpeg,.png,.gif,.webp,.bmp";

    /// <summary>Texto de ayuda/error unico para no describir en cada pagina formatos distintos.</summary>
    public const string FormatosTexto = "JPG, PNG, GIF, WEBP o BMP";

    /// <summary>
    /// Devuelve la extension canonica de la lista blanca que corresponde al nombre recibido del
    /// cliente, o null si no esta permitida. Se retorna la constante interna (no el texto del
    /// cliente) para que quien la concatene a la ruta nunca escriba datos sin sanear.
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
            // Nombre con caracteres invalidos: se descarta en vez de propagar la excepcion.
            return null;
        }

        foreach (var allowed in AllowedExtensions)
        {
            if (string.Equals(ext, allowed, StringComparison.Ordinal)) { return allowed; }
        }
        return null;
    }

    /// <summary>
    /// Comprueba que los primeros bytes del archivo correspondan de verdad a un mapa de bits de la
    /// familia que anuncia la extension. Es la unica de las validaciones que el cliente no puede
    /// falsear con solo renombrar el archivo: para pasar tiene que enviar una imagen real.
    /// </summary>
    public static bool MatchesSignature(ReadOnlySpan<byte> bytes, string extension) => extension switch
    {
        ".jpg" or ".jpeg" => IsJpeg(bytes),
        ".png" => IsPng(bytes),
        ".gif" => IsGif(bytes),
        ".webp" => IsWebp(bytes),
        ".bmp" => IsBmp(bytes),
        _ => false
    };

    /// <summary>Cualquiera de las firmas admitidas (util cuando no importa la familia exacta).</summary>
    public static bool HasImageSignature(ReadOnlySpan<byte> bytes)
        => IsJpeg(bytes) || IsPng(bytes) || IsGif(bytes) || IsWebp(bytes) || IsBmp(bytes);

    /// <summary>Nombre a escribir en disco: prefijo del modulo + Guid del SERVIDOR + extension de la lista blanca.</summary>
    public static string BuildStoredFileName(string prefix, string extension)
        => $"{prefix}-{Guid.NewGuid():N}{extension}";

    // SOI + marcador: FF D8 FF.
    private static bool IsJpeg(ReadOnlySpan<byte> b)
        => b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;

    // 89 'P' 'N' 'G' 0D 0A 1A 0A.
    private static bool IsPng(ReadOnlySpan<byte> b)
        => b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
           && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A;

    // "GIF8" (cubre 87a y 89a).
    private static bool IsGif(ReadOnlySpan<byte> b)
        => b.Length >= 4 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x38;

    // Contenedor RIFF: "RIFF" en 0..3 y "WEBP" en 8..11 (4..7 es el tamano).
    private static bool IsWebp(ReadOnlySpan<byte> b)
        => b.Length >= 12
           && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46
           && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50;

    // "BM".
    private static bool IsBmp(ReadOnlySpan<byte> b)
        => b.Length >= 2 && b[0] == 0x42 && b[1] == 0x4D;
}
