using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.SuperAdmin.Agents;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// Decision del resolutor de IA del paso de Extraccion: dado el proveedor que ELIGIO el operador (por Id de
/// su config del Super Admin) y esa config, produce el choice o un error legible. Fail-closed: exige una IA
/// elegida, habilitada y con llave descifrable. Se prueba la logica pura (sin EF ni DataProtection).
/// </summary>
public class AiProviderResolverTests
{
    private static readonly Guid Id = Guid.NewGuid();
    private static string Plain(string enc) => enc; // "descifrado" identidad para la prueba.

    private static AiProviderConfig Cfg(bool enabled = true, string? key = "enc-key", string? model = "claude-sonnet-5")
        => new() { Id = Id, Provider = AiProvider.Claude, IsEnabled = enabled, ApiKeyEncrypted = key, Model = model, BaseUrl = "https://x" };

    [Fact]
    public void Fails_clearly_when_no_provider_was_chosen()
    {
        var (choice, error) = AiProviderResolver.Decide(null, null, Plain);
        Assert.Null(choice);
        Assert.Contains("no tiene un proveedor elegido", error);
    }

    [Fact]
    public void Fails_clearly_when_the_chosen_provider_no_longer_exists()
    {
        var (choice, error) = AiProviderResolver.Decide(Id, null, Plain);
        Assert.Null(choice);
        Assert.Contains("ya no esta habilitado", error);
    }

    [Fact]
    public void Fails_clearly_when_the_chosen_provider_is_disabled()
    {
        var (choice, error) = AiProviderResolver.Decide(Id, Cfg(enabled: false), Plain);
        Assert.Null(choice);
        Assert.Contains("ya no esta habilitado", error);
    }

    [Fact]
    public void Fails_clearly_when_the_chosen_provider_has_no_key()
    {
        var (choice, error) = AiProviderResolver.Decide(Id, Cfg(key: null), Plain);
        Assert.Null(choice);
        Assert.Contains("ya no esta habilitado", error);
    }

    [Fact]
    public void Fails_clearly_when_the_key_cannot_be_decrypted()
    {
        var (choice, error) = AiProviderResolver.Decide(Id, Cfg(), _ => throw new InvalidOperationException("bad"));
        Assert.Null(choice);
        Assert.Contains("no se pudo descifrar", error);
    }

    [Fact]
    public void Resolves_the_chosen_provider_with_the_admin_model()
    {
        var (choice, error) = AiProviderResolver.Decide(Id, Cfg(model: "claude-sonnet-5"), Plain);
        Assert.Null(error);
        Assert.NotNull(choice);
        Assert.Equal(AiProvider.Claude, choice!.Provider);
        Assert.Equal("enc-key", choice.ApiKey);
        Assert.Equal("https://x", choice.BaseUrl);
        Assert.Equal("claude-sonnet-5", choice.Model); // el modelo lo fija el Super Admin en el proveedor.
    }

    [Fact]
    public void Falls_back_to_the_catalog_default_model_when_admin_left_it_empty()
    {
        var (choice, error) = AiProviderResolver.Decide(Id, Cfg(model: null), Plain);
        Assert.Null(error);
        Assert.Equal("claude-opus-4-8", choice!.Model); // DefaultModel del catalogo para Claude.
    }
}
