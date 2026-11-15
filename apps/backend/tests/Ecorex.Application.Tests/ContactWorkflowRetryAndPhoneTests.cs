using Ecorex.Application.Common;
using Ecorex.Application.Gestor;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Reglas puras del motor de acciones por filtro (2026-11-15): reintento sobre la misma fila,
/// normalizacion de telefonos a E.164 y mapeo de variables de plantilla a campos del contacto.
/// </summary>
public class ContactWorkflowRetryAndPhoneTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1, 16, true)]   // primer fallo, ya paso la espera -> reintenta
    [InlineData(2, 16, true)]   // segundo fallo -> aun cabe el tercero
    [InlineData(3, 16, false)]  // tope alcanzado
    [InlineData(1, 5, false)]   // sin esperar el minimo
    public void Failed_respeta_tope_y_espera(int attempts, int minutesLater, bool expected)
        => Assert.Equal(expected, ContactWorkflowRetryPolicy.ShouldRetry(
            ContactWorkflowRunStatus.Failed, attempts, T0, T0.AddMinutes(minutesLater)));

    [Fact]
    public void Sent_nunca_se_reintenta()
        => Assert.False(ContactWorkflowRetryPolicy.ShouldRetry(ContactWorkflowRunStatus.Sent, 1, T0, T0.AddDays(1)));

    [Theory]
    [InlineData(1, 16, true)]
    [InlineData(9, 16, true)]  // Skipped no tiene tope: no salio ningun mensaje
    [InlineData(1, 10, false)]
    public void Skipped_se_reevalua_tras_la_espera_sin_tope(int attempts, int minutesLater, bool expected)
        => Assert.Equal(expected, ContactWorkflowRetryPolicy.ShouldRetry(
            ContactWorkflowRunStatus.Skipped, attempts, T0, T0.AddMinutes(minutesLater)));

    [Theory]
    [InlineData("3001112233", "+573001112233")]
    [InlineData("300 111 2233", "+573001112233")]
    [InlineData("573001112233", "+573001112233")]
    [InlineData("+57 300-111-2233", "+573001112233")]
    [InlineData("0057 3001112233", "+573001112233")]
    [InlineData("+1 415 555 0100", "+14155550100")]
    [InlineData("6023334444", "+6023334444")]  // fijo de 10 digitos: no se le inventa indicativo
    [InlineData("12345", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ToE164_normaliza_con_pais_por_defecto(string? entrada, string? esperado)
        => Assert.Equal(esperado, PhoneNumber.ToE164(entrada));

    [Fact]
    public void ToE164_usa_el_indicativo_indicado()
        => Assert.Equal("+523001112233", PhoneNumber.ToE164("3001112233", "52"));

    [Theory]
    [InlineData("cliente", "nombre")]
    [InlineData("Nombre", "nombre")]
    [InlineData("empresa", "empresa")]
    [InlineData("codigo", "")]
    public void Variable_de_plantilla_sugiere_campo(string token, string esperado)
        => Assert.Equal(esperado, ContactWorkflowWhatsAppFields.DefaultFor(token));

    [Fact]
    public void Resolve_toma_campo_o_texto_fijo_y_vacio_es_null()
    {
        Assert.Equal("Ana", ContactWorkflowWhatsAppFields.Resolve("nombre", "Ana", null, null, null, null, null));
        Assert.Equal("Promo", ContactWorkflowWhatsAppFields.Resolve("txt:Promo", null, null, null, null, null, null));
        Assert.Null(ContactWorkflowWhatsAppFields.Resolve("cargo", "Ana", null, " ", null, null, null));
        Assert.Null(ContactWorkflowWhatsAppFields.Resolve("txt:", null, null, null, null, null, null));
    }
}
