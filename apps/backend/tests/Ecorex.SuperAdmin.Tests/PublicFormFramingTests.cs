using Ecorex.SuperAdmin.Endpoints;
using Microsoft.AspNetCore.Http;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// Formulario publico /f/{token} embebible en cualquier dominio: solo esa ruta pierde X-Frame-Options y abre
/// frame-ancestors; el resto del app conserva el anti-framing del framework.
/// </summary>
public sealed class PublicFormFramingTests
{
    [Theory]
    [InlineData("/f/abc123", true)]
    [InlineData("/F/abc123", true)]
    [InlineData("/f/abc123/extra", true)]
    [InlineData("/f", false)]
    [InlineData("/f/", false)]
    [InlineData("/formularios", false)]
    [InlineData("/fx/abc", false)]
    [InlineData("/admin/usuarios", false)]
    [InlineData("/", false)]
    public void IsPublicFormPath_SoloLaRutaDelFormularioPublico(string path, bool esperado)
        => Assert.Equal(esperado, PublicFormFraming.IsPublicFormPath(new PathString(path)));

    [Fact]
    public void AllowAnyAncestor_QuitaXFrameOptions_Y_AbreFrameAncestors()
    {
        var headers = new HeaderDictionary
        {
            ["X-Frame-Options"] = "SAMEORIGIN",
            ["Content-Security-Policy"] = "frame-ancestors 'self'"
        };

        PublicFormFraming.AllowAnyAncestor(headers);

        Assert.False(headers.ContainsKey("X-Frame-Options"));
        Assert.Equal("frame-ancestors *", headers["Content-Security-Policy"].ToString());
    }

    [Fact]
    public void AllowAnyAncestor_ConservaOtrasDirectivas_Y_SinCspPoneSoloFrameAncestors()
    {
        var conOtras = new HeaderDictionary { ["Content-Security-Policy"] = "default-src 'self'; frame-ancestors 'self'" };
        PublicFormFraming.AllowAnyAncestor(conOtras);
        Assert.Equal("default-src 'self'; frame-ancestors *", conOtras["Content-Security-Policy"].ToString());

        var vacio = new HeaderDictionary();
        PublicFormFraming.AllowAnyAncestor(vacio);
        Assert.Equal("frame-ancestors *", vacio["Content-Security-Policy"].ToString());
    }
}
