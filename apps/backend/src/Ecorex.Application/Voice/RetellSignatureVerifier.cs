using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Ecorex.Application.Voice;

/// <summary>
/// Verificacion de la firma del webhook de Retell (funcion PURA, sin I/O). Retell firma con HMAC-SHA256
/// usando la API key del tenant como secreto. El header <c>x-retell-signature</c> tiene el formato
/// <c>v={timestamp_ms},d={hex_digest}</c>. Se compara en tiempo constante y se rechaza si el timestamp cae
/// fuera de la ventana anti-replay.
///
/// NOTA: la composicion exacta del mensaje HMAC (cuerpo, o cuerpo+timestamp) se toma del SDK oficial de
/// Retell; aqui se implementa <c>HMAC(key, body)</c> y, si Retell usa <c>body+timestamp</c>, basta cambiar
/// <see cref="BuildMessage"/> (un solo punto). SIEMPRE se usa el RAW body (no un JSON re-serializado).
/// </summary>
public static class RetellSignatureVerifier
{
    /// <summary>Ventana anti-replay por defecto.</summary>
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    private static readonly Regex HeaderPattern =
        new(@"^\s*v=(?<v>\d+)\s*,\s*d=(?<d>[0-9a-fA-F]+)\s*$", RegexOptions.Compiled);

    /// <summary>Resultado tipado de la verificacion (para diagnostico sin filtrar el secreto).</summary>
    public enum Result { Valid, MissingOrMalformedHeader, EmptySecret, Replay, Mismatch }

    public static Result Verify(string? rawBody, string? apiKey, string? signatureHeader, DateTimeOffset nowUtc, TimeSpan? tolerance = null)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return Result.EmptySecret;
        }
        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return Result.MissingOrMalformedHeader;
        }

        var m = HeaderPattern.Match(signatureHeader);
        if (!m.Success)
        {
            return Result.MissingOrMalformedHeader;
        }

        if (!long.TryParse(m.Groups["v"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tsMs))
        {
            return Result.MissingOrMalformedHeader;
        }

        var tol = tolerance ?? DefaultTolerance;
        var ts = DateTimeOffset.FromUnixTimeMilliseconds(tsMs);
        if ((nowUtc - ts).Duration() > tol)
        {
            return Result.Replay;
        }

        var expectedHex = m.Groups["d"].Value;
        var computedHex = ComputeHex(apiKey, BuildMessage(rawBody ?? string.Empty, tsMs));

        return FixedTimeEqualsHex(expectedHex, computedHex) ? Result.Valid : Result.Mismatch;
    }

    /// <summary>Conveniencia booleana.</summary>
    public static bool IsValid(string? rawBody, string? apiKey, string? signatureHeader, DateTimeOffset nowUtc, TimeSpan? tolerance = null)
        => Verify(rawBody, apiKey, signatureHeader, nowUtc, tolerance) == Result.Valid;

    // Mensaje a firmar. Retell documenta el RAW body; si el SDK concatena el timestamp, se ajusta aqui.
    private static string BuildMessage(string rawBody, long timestampMs) => rawBody;

    private static string ComputeHex(string key, string message)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private static bool FixedTimeEqualsHex(string a, string b)
    {
        // Comparacion en tiempo constante sobre los bytes del hex (case-insensitive).
        if (a.Length != b.Length)
        {
            return false;
        }
        var ba = Encoding.ASCII.GetBytes(a.ToLowerInvariant());
        var bb = Encoding.ASCII.GetBytes(b.ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
