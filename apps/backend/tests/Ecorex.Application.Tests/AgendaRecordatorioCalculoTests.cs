using Ecorex.Application.Agendas;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>ADR-0167: calculo puro de los recordatorios de turnos (momento, horario permitido, decision, tokens).</summary>
public class AgendaRecordatorioCalculoTests
{
    private static readonly TimeZoneInfo Cot = TimeZoneInfo.CreateCustomTimeZone("COT", TimeSpan.FromHours(-5), "COT", "COT");

    // Turno el miercoles 18-nov-2026 a las 7:00 en Bogota = 12:00 UTC.
    private static readonly DateTimeOffset Turno7am = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Momento_SinHorario_EsInicioMenosAnticipacion()
    {
        var m = AgendaRecordatorioCalculo.Momento(Turno7am, 60, null, null, AgendaRecordatorioFueraHorario.Adelantar, Cot);
        Assert.Equal(Turno7am.AddHours(-1), m.Programado);
        Assert.Null(m.MotivoOmision);
    }

    [Fact]
    public void Momento_FueraDeHorario_AdelantaUOmite()
    {
        // 1 hora antes de las 7:00 = 6:00, antes de abrir (7:00-20:00): se adelanta al cierre del dia anterior (20:00).
        var adelantado = AgendaRecordatorioCalculo.Momento(Turno7am, 60, new TimeOnly(7, 0), new TimeOnly(20, 0), AgendaRecordatorioFueraHorario.Adelantar, Cot);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 1, 0, 0, TimeSpan.Zero), adelantado.Programado); // martes 20:00 COT

        // Turno a las 22:30: 1 hora antes = 21:30, despues de cerrar: al cierre de ese mismo dia (20:00).
        var nocturno = new DateTimeOffset(2026, 11, 19, 3, 30, 0, TimeSpan.Zero); // miercoles 22:30 COT
        var mismoDia = AgendaRecordatorioCalculo.Momento(nocturno, 60, new TimeOnly(7, 0), new TimeOnly(20, 0), AgendaRecordatorioFueraHorario.Adelantar, Cot);
        Assert.Equal(new DateTimeOffset(2026, 11, 19, 1, 0, 0, TimeSpan.Zero), mismoDia.Programado); // miercoles 20:00 COT

        var omitido = AgendaRecordatorioCalculo.Momento(Turno7am, 60, new TimeOnly(7, 0), new TimeOnly(20, 0), AgendaRecordatorioFueraHorario.Omitir, Cot);
        Assert.Null(omitido.Programado);
        Assert.Contains("fuera del horario", omitido.MotivoOmision);

        // Un dia antes (6:59... no: 7:00 del martes) cae dentro: no se ajusta.
        var dentro = AgendaRecordatorioCalculo.Momento(Turno7am, 1440, new TimeOnly(7, 0), new TimeOnly(20, 0), AgendaRecordatorioFueraHorario.Adelantar, Cot);
        Assert.Equal(Turno7am.AddDays(-1), dentro.Programado);
    }

    [Fact]
    public void Evaluar_EsperaEnviaUOmitePorCadaMotivo()
    {
        var m = AgendaRecordatorioCalculo.Momento(Turno7am, 60, null, null, AgendaRecordatorioFueraHorario.Adelantar, Cot);
        var due = Turno7am.AddHours(-1);
        var reservado = Turno7am.AddDays(-3);

        Assert.Equal(AgendaRecordatorioAccion.Esperar,
            AgendaRecordatorioCalculo.Evaluar(m, Turno7am, reservado, AgendaTurnoEstado.Reservado, false, due, due.AddMinutes(-1)).Accion);
        Assert.Equal(AgendaRecordatorioAccion.Enviar,
            AgendaRecordatorioCalculo.Evaluar(m, Turno7am, reservado, AgendaTurnoEstado.Reservado, false, due, due.AddMinutes(2)).Accion);

        var tarde = AgendaRecordatorioCalculo.Evaluar(m, Turno7am, due.AddMinutes(10), AgendaTurnoEstado.Reservado, false, due, due.AddMinutes(20));
        Assert.Equal(AgendaRecordatorioAccion.Omitir, tarde.Accion);
        Assert.Contains("despues del momento", tarde.Motivo);

        var confirmado = AgendaRecordatorioCalculo.Evaluar(m, Turno7am, reservado, AgendaTurnoEstado.Confirmado, true, due, due.AddMinutes(1));
        Assert.Equal(AgendaRecordatorioAccion.Omitir, confirmado.Accion);
        Assert.Contains("confirmado", confirmado.Motivo);
        // Sin "solo si no confirmo", un confirmado SI recibe el recordatorio.
        Assert.Equal(AgendaRecordatorioAccion.Enviar,
            AgendaRecordatorioCalculo.Evaluar(m, Turno7am, reservado, AgendaTurnoEstado.Confirmado, false, due, due.AddMinutes(1)).Accion);

        Assert.Equal(AgendaRecordatorioAccion.Omitir,
            AgendaRecordatorioCalculo.Evaluar(m, Turno7am, reservado, AgendaTurnoEstado.Reservado, false, due, Turno7am.AddMinutes(5)).Accion);

        var fueraHorario = AgendaRecordatorioCalculo.Momento(Turno7am, 60, new TimeOnly(7, 0), new TimeOnly(20, 0), AgendaRecordatorioFueraHorario.Omitir, Cot);
        Assert.Equal(AgendaRecordatorioAccion.Esperar,
            AgendaRecordatorioCalculo.Evaluar(fueraHorario, Turno7am, reservado, AgendaTurnoEstado.Reservado, false, due, due.AddMinutes(-5)).Accion);
        Assert.Equal(AgendaRecordatorioAccion.Omitir,
            AgendaRecordatorioCalculo.Evaluar(fueraHorario, Turno7am, reservado, AgendaTurnoEstado.Reservado, false, due, due.AddMinutes(1)).Accion);
    }

    [Fact]
    public void Tokens_SugerenciasYRender()
    {
        Assert.Equal("{turno.cliente}", AgendaRecordatorioCalculo.Sugerida("cliente"));
        Assert.Equal("{turno.fecha}", AgendaRecordatorioCalculo.Sugerida("Fecha"));
        Assert.Equal("{turno.hora}", AgendaRecordatorioCalculo.Sugerida("hora_cita"));
        Assert.Equal("{turno.recurso}", AgendaRecordatorioCalculo.Sugerida("medico"));
        Assert.Equal(string.Empty, AgendaRecordatorioCalculo.Sugerida("xyz"));

        var valores = AgendaRecordatorioCalculo.Valores("Ana", "Consulta", new DateOnly(2026, 11, 18), new TimeOnly(15, 0), new TimeOnly(15, 20),
            "Dra. Gomez", "T00101", "3001234567", "Clinica San Rafael");
        Assert.Equal("Ana: miercoles 18 de noviembre 3:00 p. m. con Dra. Gomez",
            AgendaRecordatorioCalculo.Render("{turno.cliente}: {TURNO.FECHA} {turno.hora} con {turno.recurso}", valores)
                .Replace(((char)0xE9).ToString(), "e"));
        Assert.Equal("7:00 a. m.", AgendaRecordatorioCalculo.Valores("x", "y", new DateOnly(2026, 1, 1), new TimeOnly(7, 0), new TimeOnly(8, 0),
            null, null, null, null)["{turno.hora}"]);
    }
}
