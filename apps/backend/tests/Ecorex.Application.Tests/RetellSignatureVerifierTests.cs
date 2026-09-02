using System.Security.Cryptography;
using System.Text;
using Ecorex.Application.Voice;
using Xunit;

namespace Ecorex.Application.Tests;

// Verificacion de la firma del webhook de Retell (funcion PURA, sin llamadas reales). Cubre: firma valida,
// cuerpo alterado, key equivocada, header malformado, replay (timestamp viejo) y secreto vacio.
public class RetellSignatureVerifierTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddYears(56); // fecha fija arbitraria

    private static string Sign(string body, string key, long tsMs)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hex = Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        return $"v={tsMs},d={hex}";
    }

    [Fact]
    public void Valid_Signature_Passes()
    {
        const string body = "{\"event\":\"call_ended\",\"call\":{\"call_id\":\"abc\"}}";
        const string key = "sk_test_key";
        var header = Sign(body, key, Now.ToUnixTimeMilliseconds());

        Assert.Equal(RetellSignatureVerifier.Result.Valid, RetellSignatureVerifier.Verify(body, key, header, Now));
    }

    [Fact]
    public void Tampered_Body_Fails()
    {
        const string key = "sk_test_key";
        var header = Sign("cuerpo-original", key, Now.ToUnixTimeMilliseconds());

        Assert.Equal(RetellSignatureVerifier.Result.Mismatch, RetellSignatureVerifier.Verify("cuerpo-alterado", key, header, Now));
    }

    [Fact]
    public void Wrong_Key_Fails()
    {
        const string body = "payload";
        var header = Sign(body, "key-A", Now.ToUnixTimeMilliseconds());

        Assert.Equal(RetellSignatureVerifier.Result.Mismatch, RetellSignatureVerifier.Verify(body, "key-B", header, Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("basura")]
    [InlineData("v=123")]
    [InlineData("d=deadbeef")]
    [InlineData("v=abc,d=deadbeef")]
    public void Malformed_Header_Fails(string header)
    {
        Assert.Equal(RetellSignatureVerifier.Result.MissingOrMalformedHeader,
            RetellSignatureVerifier.Verify("payload", "key", header, Now));
    }

    [Fact]
    public void Replay_OldTimestamp_Fails()
    {
        const string body = "payload";
        const string key = "k";
        // Firma correcta pero con timestamp de hace 10 minutos (fuera de la ventana de 5 min).
        var oldTs = Now.AddMinutes(-10).ToUnixTimeMilliseconds();
        var header = Sign(body, key, oldTs);

        Assert.Equal(RetellSignatureVerifier.Result.Replay, RetellSignatureVerifier.Verify(body, key, header, Now));
    }

    [Fact]
    public void Empty_Secret_Fails()
    {
        Assert.Equal(RetellSignatureVerifier.Result.EmptySecret,
            RetellSignatureVerifier.Verify("payload", "", "v=1,d=ff", Now));
    }
}
