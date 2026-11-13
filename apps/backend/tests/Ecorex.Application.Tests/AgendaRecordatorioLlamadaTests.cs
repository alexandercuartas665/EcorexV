using Ecorex.Application.Agendas;
using Ecorex.Application.Voice;

namespace Ecorex.Application.Tests;

/// <summary>ADR-0167 etapa 2: partes puras del recordatorio por llamada (telefono E.164, variables, guion, resultado).</summary>
public class AgendaRecordatorioLlamadaTests
{
    [Theory]
    [InlineData("3104567890", "+573104567890")]
    [InlineData("310 456 7890", "+573104567890")]
    [InlineData("573104567890", "+573104567890")]
    [InlineData("+57 310-456-7890", "+573104567890")]
    [InlineData("+1 (305) 555-1234", "+13055551234")]
    [InlineData("03104567890", "+573104567890")]
    [InlineData("12345", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void TelefonoE164(string? entrada, string? esperado) =>
        Assert.Equal(esperado, AgendaRecordatorioLlamada.TelefonoE164(entrada));

    [Fact]
    public void VariablesDinamicas_NuncaVacias()
    {
        var valores = AgendaRecordatorioCalculo.Valores("Ana", "Consulta", new DateOnly(2026, 11, 17), new TimeOnly(9, 0), new TimeOnly(9, 20),
            null, null, "3104567890", null);
        var v = AgendaRecordatorioLlamada.VariablesDinamicas(valores);
        Assert.Equal("Ana", v["cliente"]);
        Assert.Equal("9:00 a. m.", v["hora"]);
        Assert.Equal("no indicado", v["recurso"]);
        Assert.Equal("la empresa", v["empresa"]);
        Assert.Equal("martes 17 de noviembre", v["fecha"]);
    }

    [Fact]
    public void Guion_EsConstantePorRegla_YLlevaLaInstruccion()
    {
        var g = AgendaRecordatorioLlamada.Guion("Trae la orden medica.");
        Assert.Contains("{{cliente}}", g);
        Assert.Contains("{{fecha}}", g);
        Assert.EndsWith("Trae la orden medica.", g);
        Assert.Equal(g, AgendaRecordatorioLlamada.Guion("Trae la orden medica."));
        Assert.DoesNotContain("Instruccion de la empresa", AgendaRecordatorioLlamada.Guion(null));
    }

    [Theory]
    [InlineData("Analyzed", "confirma", AgendaLlamadaResultado.Confirma)]
    [InlineData("Analyzed", "Confirmado", AgendaLlamadaResultado.Confirma)]
    [InlineData("Analyzed", "si", AgendaLlamadaResultado.Confirma)]
    [InlineData("Analyzed", "CANCELA", AgendaLlamadaResultado.Cancela)]
    [InlineData("Analyzed", "reprogramar", AgendaLlamadaResultado.Reprogramar)]
    [InlineData("Analyzed", "cambiar fecha", AgendaLlamadaResultado.Reprogramar)]
    [InlineData("Analyzed", "no_contesto", AgendaLlamadaResultado.NoContesto)]
    [InlineData("Analyzed", "buzon de voz", AgendaLlamadaResultado.NoContesto)]
    [InlineData("Analyzed", "quizas", AgendaLlamadaResultado.SinDefinir)]
    [InlineData("Failed", "confirma", AgendaLlamadaResultado.NoContesto)]
    [InlineData("Error", null, AgendaLlamadaResultado.NoContesto)]
    [InlineData("Analyzed", null, AgendaLlamadaResultado.SinDefinir)]
    public void Interpretar(string estado, string? valor, AgendaLlamadaResultado esperado)
    {
        var datos = valor is null ? null : new Dictionary<string, string> { [AgendaRecordatorioLlamada.CampoResultado] = valor };
        Assert.Equal(esperado, AgendaRecordatorioLlamada.Interpretar(estado, datos).Resultado);
    }

    [Fact]
    public void Interpretar_TraeLaNota_YDescribirLaUsa()
    {
        var (r, nota) = AgendaRecordatorioLlamada.Interpretar("Analyzed", new Dictionary<string, string>
        {
            [AgendaRecordatorioLlamada.CampoResultado] = "cancela",
            [AgendaRecordatorioLlamada.CampoNota] = "  tiene un viaje "
        });
        Assert.Equal(AgendaLlamadaResultado.Cancela, r);
        Assert.Equal("tiene un viaje", nota);
        Assert.Equal("el cliente cancelo el turno (tiene un viaje)", AgendaRecordatorioLlamada.Describir(r, nota));
    }

    [Fact]
    public void FirmaDeAnalisis_CambiaElHashSoloSiHayCampos()
    {
        Assert.Equal(string.Empty, RetellVoiceService.AnalysisSignature(null));
        Assert.Equal(string.Empty, RetellVoiceService.AnalysisSignature(Array.Empty<RetellAnalysisField>()));
        var firma = RetellVoiceService.AnalysisSignature(AgendaRecordatorioLlamada.CamposAnalisis);
        Assert.Contains("resultado_turno", firma);
        Assert.NotEqual(RetellVoiceService.PromptHash("p", "v", "es"), RetellVoiceService.PromptHash("p" + firma, "v", "es"));
    }
}
