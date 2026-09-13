using Ecorex.Application.Workflows;

namespace Ecorex.Application.Tests;

/// <summary>
/// Render de plantillas de notificacion (ADR-0100): sustituye {ns.clave} desde el mapa de tokens; token
/// desconocido -> vacio. (BuildAsync lee la BD y se cubre en integracion; aqui solo la sustitucion pura.)
/// </summary>
public class NotifyTokenResolverTests
{
    // Render no toca la BD; se puede construir con un contexto nulo.
    private static readonly INotifyTokenResolver Resolver = new NotifyTokenResolver(db: null!);

    [Fact]
    public void Render_SustituyeTokensConocidos()
    {
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tarea.numero"] = "T00042",
            ["tarea.contacto"] = "Diego",
            ["form.total"] = "1500"
        };
        var result = Resolver.Render("Tarea {tarea.numero} de {tarea.contacto} por {form.total}", tokens);
        Assert.Equal("Tarea T00042 de Diego por 1500", result);
    }

    [Fact]
    public void Render_TokenDesconocido_QuedaVacio()
    {
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["tarea.numero"] = "T1" };
        Assert.Equal("T1 -", Resolver.Render("{tarea.numero} -{tarea.nada}", tokens));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Render_PlantillaVacia_DevuelveVacio(string? tpl)
    {
        Assert.Equal("", Resolver.Render(tpl, new Dictionary<string, string>()));
    }
}
