using System.Security.Cryptography;
using System.Text;

namespace Ecorex.Application.Common;

/// <summary>
/// Utilidades del token opaco de la API de configuracion (FASE 1). El token en claro se genera y se
/// entrega UNA sola vez; el servidor SOLO persiste su hash SHA-256 (hex). Vive en Application para
/// que la logica sea reutilizable y unit-testeable sin acoplar a la capa web.
/// </summary>
public static class ApiTokenHasher
{
    /// <summary>Prefijo del token en claro (ayuda a reconocerlo en logs/errores del cliente).</summary>
    public const string TokenPrefix = "ecx_";

    /// <summary>SHA-256 en hex minusculas (64 chars) del token en claro. Determinista.</summary>
    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>Genera un token opaco nuevo: prefijo + 32 bytes aleatorios en base64url. Alta entropia.</summary>
    public static string Generate()
    {
        var raw = RandomNumberGenerator.GetBytes(32);
        var body = Convert.ToBase64String(raw)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return TokenPrefix + body;
    }
}
