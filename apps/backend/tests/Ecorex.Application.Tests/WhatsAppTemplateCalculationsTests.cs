using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Pruebas unitarias de la logica pura de plantillas HSM (ADR-0029): normalizacion del nombre
/// tecnico, extraccion de variables {{token}}, validacion de la solicitud y reglas de transicion
/// de estado. Sin BD (mismo enfoque que InventoryCalculationsTests).
/// </summary>
public class WhatsAppTemplateCalculationsTests
{
    [Theory]
    [InlineData("Bienvenida Cliente", "bienvenida_cliente")]
    [InlineData("  Promo-Mensual  ", "promo_mensual")]
    [InlineData("Aviso   con   espacios", "aviso_con_espacios")]
    [InlineData("Acentos y Enie", "acentos_y_enie")]
    [InlineData("", "plantilla")]
    [InlineData("---", "plantilla")]
    public void NormalizeName_ProducesMetaTechnicalName(string raw, string expected)
    {
        Assert.Equal(expected, WhatsAppTemplateCalculations.NormalizeName(raw));
    }

    [Fact]
    public void ExtractTokens_ReturnsDistinctTokensInOrder()
    {
        var body = "Hola {{cliente}}, {{empresa}} te saluda. Gracias {{cliente}}.";
        var tokens = WhatsAppTemplateCalculations.ExtractTokens(body);
        Assert.Equal(new[] { "cliente", "empresa" }, tokens);
    }

    [Fact]
    public void ExtractTokens_EmptyBody_IsEmpty()
    {
        Assert.Empty(WhatsAppTemplateCalculations.ExtractTokens(""));
        Assert.Empty(WhatsAppTemplateCalculations.ExtractTokens(null));
        Assert.Empty(WhatsAppTemplateCalculations.ExtractTokens("Sin variables."));
    }

    [Fact]
    public void ValidateSave_Valid_ReturnsNull()
    {
        var req = new SaveWhatsAppTemplateRequest(
            "promo", "es", WhatsAppTemplateCategory.Utility, "Hola {{cliente}}.", Guid.NewGuid());
        Assert.Null(WhatsAppTemplateCalculations.ValidateSave(req));
    }

    [Theory]
    [InlineData("", "es", "cuerpo", true, "El nombre es obligatorio.")]
    [InlineData("promo", "", "cuerpo", true, "El idioma es obligatorio.")]
    [InlineData("promo", "es", "", true, "El cuerpo es obligatorio.")]
    public void ValidateSave_Required(string name, string lang, string body, bool withLine, string expected)
    {
        var req = new SaveWhatsAppTemplateRequest(
            name, lang, WhatsAppTemplateCategory.Utility, body, withLine ? Guid.NewGuid() : Guid.Empty);
        Assert.Equal(expected, WhatsAppTemplateCalculations.ValidateSave(req));
    }

    [Fact]
    public void ValidateSave_RequiresLine()
    {
        var req = new SaveWhatsAppTemplateRequest(
            "promo", "es", WhatsAppTemplateCategory.Utility, "Hola.", Guid.Empty);
        Assert.NotNull(WhatsAppTemplateCalculations.ValidateSave(req));
    }

    [Fact]
    public void ValidateSave_RejectsTooLongBody()
    {
        var req = new SaveWhatsAppTemplateRequest(
            "promo", "es", WhatsAppTemplateCategory.Utility, new string('x', 1025), Guid.NewGuid());
        Assert.NotNull(WhatsAppTemplateCalculations.ValidateSave(req));
    }

    [Theory]
    [InlineData(WhatsAppTemplateStatus.Draft, true)]
    [InlineData(WhatsAppTemplateStatus.Rejected, true)]
    [InlineData(WhatsAppTemplateStatus.Submitted, false)]
    [InlineData(WhatsAppTemplateStatus.Approved, false)]
    [InlineData(WhatsAppTemplateStatus.Paused, false)]
    [InlineData(WhatsAppTemplateStatus.Disabled, false)]
    public void CanEdit_And_CanSubmit_OnlyFromDraftOrRejected(WhatsAppTemplateStatus status, bool expected)
    {
        Assert.Equal(expected, WhatsAppTemplateCalculations.CanEdit(status));
        Assert.Equal(expected, WhatsAppTemplateCalculations.CanSubmit(status));
    }
}
