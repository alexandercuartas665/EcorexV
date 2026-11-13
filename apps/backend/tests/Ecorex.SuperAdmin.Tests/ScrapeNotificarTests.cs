using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.SuperAdmin.Agents;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// Paso "Notificar" del dron (ADR-0168): partes puras (plantilla con rutas del dron + tokens de la tarea,
/// variable TAREA_NUEVA, config JSON con enums como texto, cuerpo HTML del correo, variables de la plantilla de
/// WhatsApp), mas las variables que deja "Crear actividad" y el tope de filas del bucle.
/// </summary>
public class ScrapeNotificarTests
{
    // Render de tokens no usa la BD: basta el resolvedor real sin contexto.
    private static readonly INotifyTokenResolver Tokens = new NotifyTokenResolver(null!, TimeProvider.System);

    private static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["tarea.numero"] = "T00099",
        ["numero"] = "T00099",
        ["tarea.titulo"] = "Pedido INCAUCA 1501",
        ["form.petof"] = "1501",
        ["tarea.enlace"] = "https://x/t/1",
    };

    private static ScrapeRunContext Ctx()
    {
        var c = new ScrapeRunContext();
        c.Set("DETALLE", """{"cabecera":{"solicitud":"SOL-7"}}""");
        c.Set("TAREA", "T00099");
        c.Set("TAREA_NUEVA", "1");
        return c;
    }

    [Fact]
    public void Render_combina_rutas_del_dron_y_tokens_de_la_tarea()
    {
        var s = ScrapeNotificar.Render(Ctx(), Tokens, Map, "Nueva {tarea.numero} ({form.petof}) sol {{DETALLE.cabecera.solicitud}} {{NO.EXISTE}}");
        Assert.Equal("Nueva T00099 (1501) sol SOL-7", s);
    }

    [Fact]
    public void Render_token_desconocido_queda_vacio()
    {
        Assert.Equal("Hola", ScrapeNotificar.Render(Ctx(), Tokens, Map, "Hola {otro.cosa}"));
    }

    [Theory]
    [InlineData("TAREA", "TAREA_NUEVA")]
    [InlineData("{{PEDIDO}}", "PEDIDO_NUEVA")]
    [InlineData(" X ", "X_NUEVA")]
    public void NuevaVar_deriva_la_variable_de_nueva(string tarea, string esperado)
    {
        Assert.Equal(esperado, ScrapeNotificar.NuevaVar(tarea));
    }

    [Fact]
    public void Config_round_trip_con_enums_como_texto()
    {
        var cfg = new ScrapeNotificar.Config
        {
            Canal = ScrapeNotificar.Canal.WhatsApp,
            Destino = ScrapeNotificar.Destino.Usuarios,
            UsuarioIds = [Guid.NewGuid()],
            LineaId = Guid.NewGuid(),
            Plantilla = "aviso_tarea",
            Idioma = "es",
            Variables = new() { ["numero"] = "{tarea.numero}" },
            SoloSiNueva = false,
        };
        var json = ScrapeNotificar.Serialize(cfg);
        Assert.Contains("\"WhatsApp\"", json);
        var back = ScrapeNotificar.Parse(json)!;
        Assert.Equal(ScrapeNotificar.Canal.WhatsApp, back.Canal);
        Assert.Equal(ScrapeNotificar.Destino.Usuarios, back.Destino);
        Assert.False(back.SoloSiNueva);
        Assert.Equal("aviso_tarea", back.Plantilla);
        Assert.Null(ScrapeNotificar.Parse("{no es json"));
    }

    [Fact]
    public void Config_por_defecto_es_campana_al_responsable_solo_si_nueva()
    {
        var cfg = ScrapeNotificar.Parse("{}")!;
        Assert.Equal(ScrapeNotificar.Canal.Campana, cfg.Canal);
        Assert.Equal(ScrapeNotificar.Destino.Responsable, cfg.Destino);
        Assert.True(cfg.SoloSiNueva);
        Assert.True(cfg.IncluirEnlace);
    }

    [Fact]
    public void ToHtml_escapa_y_agrega_boton_del_enlace()
    {
        var html = ScrapeNotificar.ToHtml("Linea 1 <b>\nLinea 2", "https://x/t/1");
        Assert.Contains("Linea 1 &lt;b&gt;<br>Linea 2", html);
        Assert.Contains("href=\"https://x/t/1\"", html);
        Assert.DoesNotContain("<a ", ScrapeNotificar.ToHtml("sin enlace", null));
    }

    [Fact]
    public void BindVariables_llena_las_variables_de_la_plantilla()
    {
        var vars = new Dictionary<string, string> { ["Numero_Pedido"] = "{form.petof}", ["Solicitud"] = "{{DETALLE.cabecera.solicitud}}" };
        var bound = ScrapeNotificar.BindVariables(vars, Map, Ctx(), Tokens);
        Assert.Equal("1501", bound["numero_pedido"]);
        Assert.Equal("SOL-7", bound["solicitud"]);
        Assert.Equal("T00099", bound["numero"]); // los tokens siguen disponibles por nombre
    }

    [Fact]
    public void CrearActividad_deja_TAREA_y_TAREA_NUEVA()
    {
        var step = new ScrapeStep { Name = "c", Kind = ScrapeStepKind.CrearActividad };
        var vars = ScrapeCrearActividad.OutputVars(step, new ScrapeCrearActividad.Result(true, true, "ok", "T00099"));
        Assert.Contains(("TAREA", "T00099"), vars);
        Assert.Contains(("TAREA_NUEVA", "1"), vars);

        var existente = ScrapeCrearActividad.OutputVars(new ScrapeStep { Name = "c", OutputVar = "PED" },
            new ScrapeCrearActividad.Result(true, false, "ya", "T00010"));
        Assert.Contains(("PED", "T00010"), existente);
        Assert.Contains(("PED_NUEVA", "0"), existente);
    }

    [Theory]
    [InlineData("2", 2)]
    [InlineData(" 5 ", 5)]
    [InlineData("0", 0)]
    [InlineData("-1", 0)]
    [InlineData("abc", 0)]
    public void LoopRowCap_lee_la_variable_del_flujo(string valor, int esperado)
    {
        var vars = new Dictionary<string, string> { [BrowserRunService.LoopRowCapVar] = valor };
        Assert.Equal(esperado, BrowserRunService.LoopRowCap(vars));
        Assert.Equal(0, BrowserRunService.LoopRowCap(new Dictionary<string, string>()));
    }

    [Fact]
    public void NeedsStepwise_incluye_Notificar()
    {
        var f = new ScrapeFlow { Name = "a", StartUrl = "x" };
        f.Steps.Add(new ScrapeStep { Name = "n", Kind = ScrapeStepKind.Notificar });
        Assert.True(BrowserRunService.NeedsStepwise(f));
    }
}
