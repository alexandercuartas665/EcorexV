using Ecorex.Application.Tenancy;

namespace Ecorex.Application.Tests;

/// <summary>
/// El numero de negocio de una linea YCloud se guarda SOLO con digitos, igual que lo entrega el webhook en 'to';
/// con "+57..." la linea no reconocia sus mensajes entrantes (caso SOLDARCO 2026-11-14).
/// </summary>
public sealed class WhatsAppLineYCloudNumberTests
{
    [Theory]
    [InlineData("+573126892065", "573126892065")]
    [InlineData("+57 312 689 2065", "573126892065")]
    [InlineData(" 573171990738 ", "573171990738")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("+", null)]
    public void YCloudNumber_QuedaSoloConDigitos(string? entrada, string? esperado)
        => Assert.Equal(esperado, WhatsAppLineService.YCloudNumber(entrada));
}
