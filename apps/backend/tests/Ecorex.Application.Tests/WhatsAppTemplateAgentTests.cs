using System.Text.Json;
using Ecorex.Application.Gestor.TemplateAgent;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Agente editor de plantillas de WhatsApp (ADR-0174, fase 2): verificador determinista de las reglas de Meta
/// (dorados) y lectura del borrador que propone el agente.
/// </summary>
public class WhatsAppTemplateAgentTests
{
    private static WhatsAppDraft Draft(string body, params (string Token, string Example)[] vars) => new(
        "aviso_pedido_listo", "es", WhatsAppTemplateCategory.Utility, null, null, null, body, "SOLDARCO",
        vars.Select(v => new WhatsAppTemplateVariable(v.Token, v.Example)).ToList());

    [Fact]
    public void Plantilla_correcta_sin_errores_ni_avisos()
    {
        var r = WhatsAppTemplateVerifier.Verify(Draft(
            "Hola {{cliente}}, tu pedido {{pedido}} ya esta listo para recoger en nuestra bodega de Cali. Te esperamos de lunes a sabado.",
            ("cliente", "Juan Perez"), ("pedido", "PED-1024")));
        Assert.Equal(0, r.Errors);
        Assert.Equal(0, r.Warnings);
    }

    [Fact]
    public void Variable_al_inicio_o_al_final_es_error()
    {
        Assert.Contains(WhatsAppTemplateVerifier.Verify(Draft("{{cliente}}, tu pedido esta listo para recoger en la bodega hoy mismo.", ("cliente", "Juan"))).Issues,
            i => i.Severity == "error" && i.Problem.Contains("empieza o termina"));
        Assert.Contains(WhatsAppTemplateVerifier.Verify(Draft("Tu pedido esta listo para recoger en la bodega hoy mismo, {{cliente}}.", ("cliente", "Juan"))).Issues,
            i => i.Severity == "error" && i.Problem.Contains("empieza o termina"));
    }

    [Fact]
    public void Variable_sin_ejemplo_y_variables_pegadas_son_error()
    {
        var r = WhatsAppTemplateVerifier.Verify(Draft("Hola, el pedido {{pedido}}{{fecha}} esta listo para recoger en la bodega principal.", ("pedido", "PED-1"), ("fecha", "")));
        Assert.Contains(r.Issues, i => i.Severity == "error" && i.Problem.Contains("no tiene ejemplo"));
        Assert.Contains(r.Issues, i => i.Severity == "error" && i.Problem.Contains("seguidas"));
    }

    [Fact]
    public void Demasiadas_variables_para_el_texto_es_error()
    {
        var r = WhatsAppTemplateVerifier.Verify(Draft("Hola {{a}} y {{b}} con {{c}} ok.", ("a", "1"), ("b", "2"), ("c", "3")));
        Assert.Contains(r.Issues, i => i.Severity == "error" && i.Problem.Contains("Params Words Ratio"));
    }

    [Fact]
    public void Nombre_idioma_largos_y_authentication_son_error()
    {
        var d = Draft("Hola, tu pedido esta listo para recoger en la bodega principal de Cali hoy.") with
        {
            Nombre = "Aviso Pedido",
            Idioma = "espanol",
            Categoria = WhatsAppTemplateCategory.Authentication,
            TipoEncabezado = WhatsAppTemplateHeaderType.Text,
            Encabezado = new string('x', 61) + "{{1}}",
            Pie = new string('y', 61),
        };
        var r = WhatsAppTemplateVerifier.Verify(d);
        Assert.Contains(r.Issues, i => i.Where == "nombre" && i.Fix.Contains("aviso_pedido"));
        Assert.Contains(r.Issues, i => i.Where == "idioma");
        Assert.Contains(r.Issues, i => i.Where == "categoria" && i.Severity == "error");
        Assert.Equal(2, r.Issues.Count(i => i.Where == "encabezado"));
        Assert.Contains(r.Issues, i => i.Where == "pie");
    }

    [Fact]
    public void Promocion_en_Utility_es_aviso_y_variable_sin_usar_tambien()
    {
        var r = WhatsAppTemplateVerifier.Verify(Draft("Hola, aprovecha el descuento del 15% en soldaduras esta semana en todas nuestras sedes.", ("cliente", "Juan")));
        Assert.Equal(0, r.Errors);
        Assert.Contains(r.Issues, i => i.Severity == "warning" && i.Where == "categoria");
        Assert.Contains(r.Issues, i => i.Severity == "warning" && i.Problem.Contains("no se usa"));
    }

    [Fact]
    public void Borrador_del_agente_se_lee_y_la_vista_previa_aplica_ejemplos()
    {
        using var doc = JsonDocument.Parse("""
            {"nombre":"aviso_pedido","categoria":"marketing","cuerpo":"Hola {{cliente}}, tu pedido esta listo.","pie":"SOLDARCO",
             "variables":[{"token":"{{cliente}}","ejemplo":"Juan Perez"}]}
            """);
        var d = WhatsAppTemplateAgentToolset.DraftFromArgs(doc.RootElement);
        Assert.Equal(WhatsAppTemplateCategory.Marketing, d.Categoria);
        Assert.Equal("es", d.Idioma);
        Assert.Equal("cliente", Assert.Single(d.Variables).Token); // quita las llaves si el modelo las manda
        Assert.Equal("Hola Juan Perez, tu pedido esta listo.\n\n_SOLDARCO_", WhatsAppTemplateAgentToolset.Preview(d));
    }
}
