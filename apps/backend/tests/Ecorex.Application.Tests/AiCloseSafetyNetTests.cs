using Ecorex.Application.Tenancy;

namespace Ecorex.Application.Tests;

// ADR-0154 - Red de seguridad de cierre: no re-pinchar hacia crear_lead cuando el agente ya registro
// con crear_actividad/crear_tarea, y NUNCA enviar al cliente texto meta/razonamiento ni nombres de
// herramientas. Estas pruebas blindan la logica de decision que antes dejaba fugar razonamiento interno.
public sealed class AiCloseSafetyNetTests
{
    // Parte 1: las tres herramientas de registro cuentan como cierre (antes solo crear_lead).
    [Theory]
    [InlineData("crear_lead")]
    [InlineData("crear_actividad")]
    [InlineData("crear_tarea")]
    [InlineData("anotar_caso")]
    public void CloseToolNames_incluye_las_tres_herramientas_de_registro(string tool)
    {
        Assert.Contains(tool, AiInferenceService.CloseToolNames);
    }

    [Fact]
    public void CloseToolNames_es_insensible_a_mayusculas()
    {
        Assert.Contains("CREAR_ACTIVIDAD", AiInferenceService.CloseToolNames);
    }

    [Fact]
    public void CloseToolNames_no_cuenta_herramientas_que_no_son_de_cierre()
    {
        Assert.DoesNotContain("consultar_tarea", AiInferenceService.CloseToolNames);
    }

    // Parte 2a: deteccion de fuga: el texto final NUNCA debe nombrar una herramienta del agente.
    private static readonly string[] Tools = { "crear_lead", "crear_actividad", "crear_tarea" };

    [Fact]
    public void ContainsToolName_detecta_el_nombre_de_herramienta_aunque_no_sea_una_llamada()
    {
        // El caso real de prod: justificacion en ingles que menciona la funcion entre backticks.
        const string leak = "I have already registered the request using the `crear_actividad` function; " +
                            "creating a lead now would be redundant.";
        Assert.True(AiInferenceService.ContainsToolName(leak, Tools));
    }

    [Fact]
    public void ContainsToolName_es_insensible_a_mayusculas()
    {
        Assert.True(AiInferenceService.ContainsToolName("uso Crear_Lead internamente", Tools));
    }

    [Fact]
    public void ContainsToolName_no_marca_texto_natural_del_cliente()
    {
        // El cliente ve "crear una actividad" (con espacio), nunca el identificador snake_case.
        const string natural = "Perfecto, muchas gracias por la imagen. Voy a crear una actividad para tu caso.";
        Assert.False(AiInferenceService.ContainsToolName(natural, Tools));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ContainsToolName_vacio_es_falso(string? text)
    {
        Assert.False(AiInferenceService.ContainsToolName(text, Tools));
    }

    // Parte 2b: texto seguro hacia el cliente.
    [Fact]
    public void SafeClientText_devuelve_el_candidato_cuando_es_limpio()
    {
        const string good = "Listo, un asesor se pondra en contacto contigo en breve.";
        Assert.Equal(good, AiInferenceService.SafeClientText(good, Tools));
    }

    [Fact]
    public void SafeClientText_suprime_el_candidato_que_nombra_una_herramienta()
    {
        const string leak = "Ya use crear_actividad, un lead seria redundante.";
        var safe = AiInferenceService.SafeClientText(leak, Tools);
        Assert.False(AiInferenceService.ContainsToolName(safe, Tools));
        Assert.NotEqual(leak, safe);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SafeClientText_usa_acuse_neutro_cuando_no_hay_candidato(string? candidate)
    {
        var safe = AiInferenceService.SafeClientText(candidate, Tools);
        Assert.False(string.IsNullOrWhiteSpace(safe));
        Assert.False(AiInferenceService.ContainsToolName(safe, Tools));
    }

    // La red solo debe dispararse ante una AFIRMACION de cierre; un saludo normal no la activa.
    [Fact]
    public void ClaimsCloseWithoutTool_detecta_afirmacion_de_registro()
    {
        Assert.True(AiInferenceService.ClaimsCloseWithoutTool("He registrado tu solicitud, un asesor te contactara."));
    }

    [Fact]
    public void ClaimsCloseWithoutTool_ignora_un_mensaje_normal()
    {
        Assert.False(AiInferenceService.ClaimsCloseWithoutTool("Hola, con gusto te ayudo. Cuentame que necesitas."));
    }

    // Regresion 2026-11-09 (caso Gustavo): mencionar "tu solicitud/tu pedido/contactar" en una frase de
    // atencion normal NO es una afirmacion de cierre; antes disparaba el nudge y cortaba la conversacion.
    [Theory]
    [InlineData("Con gusto te ayudo con tu solicitud de corte laser y doblez. Para poder cotizar, sera solo el servicio o nosotros suministramos el material?")]
    [InlineData("Claro, reviso tu pedido y te confirmo el valor en un momento.")]
    [InlineData("Con quien tengo el gusto de contactar?")]
    [InlineData("Para registrar tu cotizacion necesito el material y las medidas.")]
    public void ClaimsCloseWithoutTool_NO_dispara_con_frases_de_atencion_normales(string text)
    {
        Assert.False(AiInferenceService.ClaimsCloseWithoutTool(text));
    }

    // Un cierre REAL (afirmar que ya quedo registrado / que un asesor lo contactara) SI debe pinchar.
    [Theory]
    [InlineData("Ya registre tu caso, un asesor te contactara en breve.")]
    [InlineData("Listo, quedaste inscrito; inscripcion confirmada.")]
    [InlineData("Radique tu caso, tu numero de ticket es TASK-123.")]
    [InlineData("Perfecto, nos pondremos en contacto contigo para la cotizacion.")]
    public void ClaimsCloseWithoutTool_SI_dispara_con_un_cierre_real(string text)
    {
        Assert.True(AiInferenceService.ClaimsCloseWithoutTool(text));
    }

    // Parte 2c (regresion): el gate post-nudge distingue texto interno (meta/otro idioma) de una
    // respuesta conversacional limpia en espanol, que debe pasar al cliente.
    [Fact]
    public void LooksLikeInternalMeta_detecta_el_razonamiento_en_ingles_caso_betty()
    {
        const string leak = "I have already registered the customer's request using the crear_actividad " +
                            "function as instructed; creating a lead now would be redundant.";
        Assert.True(AiInferenceService.LooksLikeInternalMeta(leak));
    }

    [Fact]
    public void LooksLikeInternalMeta_detecta_el_razonamiento_en_espanol()
    {
        Assert.True(AiInferenceService.LooksLikeInternalMeta("Ya use la herramienta de registro, un lead seria redundante."));
    }

    [Theory]
    [InlineData("Sera solo el servicio de corte o nosotros suministramos el material?")]
    [InlineData("Perfecto, para cotizar necesito el espesor de la lamina y la cantidad de piezas.")]
    public void LooksLikeInternalMeta_NO_marca_una_respuesta_limpia_en_espanol(string text)
    {
        Assert.False(AiInferenceService.LooksLikeInternalMeta(text));
    }
}
