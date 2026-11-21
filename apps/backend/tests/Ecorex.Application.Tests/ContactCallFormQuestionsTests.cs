using Ecorex.Application.Voice;
using Ecorex.Domain.Enums;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Puente formulario &lt;-&gt; llamada de voz del motor de contactos (ADR-0190 fase B): que se pregunta, como se le
/// pide a Retell y como vuelve lo capturado al formulario (etiqueta -&gt; id de opcion, sin pisar con vacios).
/// </summary>
public class ContactCallFormQuestionsTests
{
    private const string CanalOpts = "[{\"id\":\"wa\",\"label\":\"WhatsApp\"},{\"id\":\"mail\",\"label\":\"Correo electr\u00f3nico\"},{\"id\":\"tel\",\"label\":\"Llamada\"}]";

    private static VoiceFormQuestion Q(string code, string label, FormControlType type, string? opts = null,
        FormSourceKind source = FormSourceKind.Options, bool hidden = false)
        => new(code, label, null, type, opts, source, hidden);

    private static readonly IReadOnlyList<VoiceFormQuestion> Perfil = new[]
    {
        Q("titulo", "Datos del contacto", FormControlType.Heading),
        Q("canal", "Canal preferido", FormControlType.Select, CanalOpts),
        Q("intereses", "Servicios de interes", FormControlType.MultiCheck, CanalOpts),
        Q("opinion", "Que opina de nuestro servicio", FormControlType.TextArea),
        Q("recomienda", "Nos recomendaria", FormControlType.Toggle),
        Q("empleados", "Numero de empleados", FormControlType.Number),
        Q("oculto", "Campo oculto", FormControlType.Text, hidden: true),
        Q("ciudad_lookup", "Ciudad", FormControlType.Select, null, FormSourceKind.DataContainer),
        Q("firma", "Firma", FormControlType.Signature),
    };

    [Fact]
    public void Solo_se_preguntan_campos_simples_visibles_y_con_opciones_propias()
    {
        var codes = ContactCallFormQuestions.Askable(Perfil).Select(q => q.FieldCode).ToList();
        Assert.Equal(new[] { "canal", "intereses", "opinion", "recomienda", "empleados" }, codes);
    }

    [Fact]
    public void El_bloque_del_prompt_lista_las_preguntas_en_orden_con_sus_opciones()
    {
        var block = ContactCallFormQuestions.BuildPromptBlock(ContactCallFormQuestions.Askable(Perfil));
        Assert.Contains("1. Canal preferido (opciones: WhatsApp, Correo electr\u00f3nico, Llamada)", block);
        Assert.Contains("2. Servicios de interes (puede elegir varias:", block);
        Assert.Contains("4. Nos recomendaria (si / no)", block);
        Assert.Equal(string.Empty, ContactCallFormQuestions.BuildPromptBlock(Array.Empty<VoiceFormQuestion>()));
    }

    [Fact]
    public void Los_campos_de_analisis_usan_el_tipo_de_Retell_por_control()
    {
        var fields = ContactCallFormQuestions.BuildAnalysisFields(ContactCallFormQuestions.Askable(Perfil))
            .ToDictionary(f => f.Name);
        Assert.Equal("enum", fields["canal"].Type);
        Assert.Equal(new[] { "WhatsApp", "Correo electr\u00f3nico", "Llamada" }, fields["canal"].Choices);
        Assert.Equal("string", fields["intereses"].Type);
        Assert.Equal("boolean", fields["recomienda"].Type);
        Assert.Equal("number", fields["empleados"].Type);
        Assert.Equal("string", fields["opinion"].Type);
    }

    [Fact]
    public void Lo_capturado_se_traduce_a_valores_del_formulario()
    {
        var captured = new Dictionary<string, string>
        {
            ["canal"] = "  correo ELECTRONICO ",     // sin tildes ni mayusculas -> id de la opcion
            ["intereses"] = "WhatsApp, Llamada, Fax",  // la que no existe se descarta
            ["opinion"] = "Muy bueno",
            ["recomienda"] = "Si",
            ["empleados"] = "45,5",
        };
        var mapped = ContactCallFormQuestions.MapCaptured(ContactCallFormQuestions.Askable(Perfil), captured);
        Assert.Equal("mail", mapped["canal"]);
        Assert.Equal("""["wa","tel"]""", mapped["intereses"]);
        Assert.Equal("Muy bueno", mapped["opinion"]);
        Assert.Equal("true", mapped["recomienda"]);
        Assert.Equal("45.5", mapped["empleados"]);
    }

    [Fact]
    public void Vacios_y_opciones_que_no_casan_se_omiten()
    {
        var captured = new Dictionary<string, string>
        {
            ["canal"] = "Telegram",
            ["opinion"] = "   ",
            ["recomienda"] = "tal vez",
            ["empleados"] = "null",
            ["desconocido"] = "x",
        };
        Assert.Empty(ContactCallFormQuestions.MapCaptured(ContactCallFormQuestions.Askable(Perfil), captured));
    }
}
