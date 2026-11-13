using Ecorex.Application.Agendas;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>ADR-0165 olas 3 y 4: calculo puro de huecos y asignacion de recursos para turnos.</summary>
public class AgendaTurnosCalculoTests
{
    private static readonly DateOnly Martes = new(2026, 11, 17);

    private static AgendaRecursoDia R(string nombre, int hi, int hf, int iv, int cupo,
        IEnumerable<AgendaOcupacion>? ocup = null, IEnumerable<AgendaBloqueo>? bloq = null, bool cerrado = false) =>
        new(Guid.CreateVersion7(), nombre,
            new[] { new AgendaFranjaDto(DayOfWeek.Tuesday, new TimeOnly(hi, 0), new TimeOnly(hf, 0), iv, cupo) },
            (bloq ?? Array.Empty<AgendaBloqueo>()).ToList(), cerrado, (ocup ?? Array.Empty<AgendaOcupacion>()).ToList());

    private static TimeOnly H(int h, int m = 0) => new(h, m);

    [Fact]
    public void Libre_RespetaAlineacionFranjaCupoYNovedad()
    {
        var linea = R("Linea", 7, 17, 30, 2, new[] { new AgendaOcupacion(H(7), H(8)) });
        Assert.True(AgendaTurnosCalculo.Libre(linea, Martes, H(7), H(8)));         // cupo 2: queda uno
        Assert.False(AgendaTurnosCalculo.Libre(linea, Martes, H(7, 10), H(8, 10))); // no alineado al bloque
        Assert.False(AgendaTurnosCalculo.Libre(linea, Martes, H(16, 30), H(17, 30))); // se sale de la franja

        var llena = R("Linea", 7, 17, 30, 2, new[] { new AgendaOcupacion(H(7), H(8)), new AgendaOcupacion(H(7, 30), H(8)) });
        Assert.False(AgendaTurnosCalculo.Libre(llena, Martes, H(7, 30), H(8, 30))); // el bloque 7:30 ya tiene 2
        Assert.True(AgendaTurnosCalculo.Libre(llena, Martes, H(8), H(9)));

        var mantenimiento = R("Linea", 7, 17, 30, 2, bloq: new[] { new AgendaBloqueo(Martes, Martes, H(7), H(9), "Mantenimiento") });
        Assert.False(AgendaTurnosCalculo.Libre(mantenimiento, Martes, H(8, 30), H(9, 30)));
        Assert.True(AgendaTurnosCalculo.Libre(mantenimiento, Martes, H(9), H(10)));

        Assert.False(AgendaTurnosCalculo.Libre(R("Cerrado", 7, 17, 30, 2, cerrado: true), Martes, H(9), H(10)));
    }

    [Fact]
    public void Duracion_QueNoCalzaConElBloque_OcupaLosBloquesQueToca()
    {
        // Consulta de 30 min sobre bloques de 20: ocupa 2 bloques; no cabe al final si el ultimo bloque completo termina antes.
        var medico = R("Medica", 7, 12, 20, 1, new[] { new AgendaOcupacion(H(7, 20), H(7, 40)) });
        Assert.False(AgendaTurnosCalculo.Libre(medico, Martes, H(7), H(7, 30)));   // toca el bloque 7:20 ocupado
        Assert.True(AgendaTurnosCalculo.Libre(medico, Martes, H(7, 40), H(8, 10)));
        Assert.False(AgendaTurnosCalculo.Libre(medico, Martes, H(11, 40), H(12, 10))); // pasaria de las 12
    }

    [Fact]
    public void Asignar_UsaRecursosDistintosYPrefiereElMenosOcupado()
    {
        var linea = R("Linea 1", 7, 17, 30, 2);
        var inspA = R("Inspector A", 7, 17, 30, 1, new[] { new AgendaOcupacion(H(10), H(11)) });
        var inspB = R("Inspector B", 7, 17, 30, 1);
        var dict = new[] { linea, inspA, inspB }.ToDictionary(r => r.RecursoId);
        var reqs = new[]
        {
            new AgendaRequisitoCalc("Linea", new[] { linea.RecursoId }),
            new AgendaRequisitoCalc("Inspector", new[] { inspA.RecursoId, inspB.RecursoId })
        };

        var a = AgendaTurnosCalculo.Asignar(reqs, dict, Martes, H(7), H(8))!;
        Assert.Equal(new[] { "Linea 1", "Inspector B" }, a.Select(x => x.RecursoNombre)); // B tiene menos ocupacion

        // A las 10 el inspector A esta ocupado: va B.
        Assert.Equal("Inspector B", AgendaTurnosCalculo.Asignar(reqs, dict, Martes, H(10), H(11))![1].RecursoNombre);

        // Un mismo recurso no cumple dos requisitos a la vez.
        var dobles = new[] { new AgendaRequisitoCalc("X", new[] { inspB.RecursoId }), new AgendaRequisitoCalc("Y", new[] { inspB.RecursoId }) };
        Assert.Null(AgendaTurnosCalculo.Asignar(dobles, dict, Martes, H(7), H(8)));
    }

    [Fact]
    public void HuecosDia_RespetaNoAntesDeYMedianoche()
    {
        var sala = R("Sala", 7, 10, 60, 1);
        var dict = new Dictionary<Guid, AgendaRecursoDia> { [sala.RecursoId] = sala };
        var reqs = new[] { new AgendaRequisitoCalc("Sala", new[] { sala.RecursoId }) };
        Assert.Equal(new[] { H(7), H(8), H(9) }, AgendaTurnosCalculo.HuecosDia(Martes, 60, reqs, dict).Select(h => h.Inicio));
        Assert.Equal(new[] { H(9) }, AgendaTurnosCalculo.HuecosDia(Martes, 60, reqs, dict, H(8, 30)).Select(h => h.Inicio));
        Assert.Empty(AgendaTurnosCalculo.HuecosDia(Martes, 240, reqs, dict)); // no cabe en la franja
    }

    [Fact]
    public void Estados_Transiciones()
    {
        Assert.True(AgendaTurnosCalculo.PuedeCambiar(AgendaTurnoEstado.Reservado, AgendaTurnoEstado.Confirmado));
        Assert.True(AgendaTurnosCalculo.PuedeCambiar(AgendaTurnoEstado.Confirmado, AgendaTurnoEstado.Cancelado));
        Assert.False(AgendaTurnosCalculo.PuedeCambiar(AgendaTurnoEstado.Cancelado, AgendaTurnoEstado.Reservado));
        Assert.False(AgendaTurnosCalculo.PuedeCambiar(AgendaTurnoEstado.Atendido, AgendaTurnoEstado.Cancelado));
        Assert.True(AgendaTurnosCalculo.Reprogramable(AgendaTurnoEstado.Confirmado));
        Assert.False(AgendaTurnosCalculo.Reprogramable(AgendaTurnoEstado.NoAsistio));
    }
}
