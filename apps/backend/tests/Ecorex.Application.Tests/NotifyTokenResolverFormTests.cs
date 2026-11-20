using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// {form.*} en avisos (bug 2026-11-16, SOLDARCO T00084): el resolver filtraba por FormResponse.IsActive, que
/// NO es "vigente" sino la marca opcional de "formulario principal" (ADR-0065), casi nunca puesta. Ahora toma
/// todas las respuestas ancladas a la tarea; la marcada gana y, si no hay, la mas antigua.
/// </summary>
public class NotifyTokenResolverFormTests
{
    private static FormResponse Resp(string? reference, bool isActive, int minute, string data) => new()
    {
        Reference = reference,
        IsActive = isActive,
        CreatedAt = new DateTimeOffset(2026, 11, 16, 10, minute, 0, TimeSpan.Zero),
        Data = data
    };

    private static Dictionary<string, string> Build(params FormResponse[] responses)
    {
        var datas = NotifyTokenResolver.AnchoredToTask(responses.AsQueryable(), "T00001").Select(r => r.Data).ToList();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        NotifyTokenResolver.AddFormTokens(map, datas);
        return map;
    }

    [Fact]
    public void Respuesta_no_marcada_principal_si_expone_sus_campos()
    {
        var map = Build(Resp("T00001-1", isActive: false, 0, """{"ubicacion":{"value":"Medellin, Colombia","type":"text"}}"""));

        Assert.Equal("Medellin, Colombia", map["form.ubicacion"]);
        Assert.Equal("Medellin, Colombia", map["ubicacion"]);
    }

    [Fact]
    public void Con_dos_respuestas_gana_la_marcada_principal()
    {
        var map = Build(
            Resp("T00001-1", isActive: false, 0, """{"ciudad":{"value":"Cali"}}"""),
            Resp("T00001-2", isActive: true, 5, """{"ciudad":{"value":"Bogota"}}"""));

        Assert.Equal("Bogota", map["form.ciudad"]);
    }

    [Fact]
    public void Sin_marcada_gana_la_mas_antigua_y_los_vacios_no_tapan()
    {
        var map = Build(
            Resp("T00001-2", isActive: false, 5, """{"ciudad":{"value":"Bogota"},"nombre":{"value":"Ana"}}"""),
            Resp("T00001", isActive: false, 0, """{"ciudad":{"value":"Cali"},"nombre":{"value":""}}"""));

        Assert.Equal("Cali", map["form.ciudad"]);
        Assert.Equal("Ana", map["form.nombre"]);
    }

    [Fact]
    public void Solo_toma_respuestas_ancladas_a_la_tarea()
    {
        var map = Build(
            Resp("T000011", isActive: true, 0, """{"ciudad":{"value":"Otra tarea"}}"""),
            Resp(null, isActive: true, 1, """{"ciudad":{"value":"Sin ancla"}}"""),
            Resp("T00001-3", isActive: false, 2, """{"ciudad":{"value":"Pasto"}}"""));

        Assert.Equal("Pasto", map["form.ciudad"]);
    }
}
