using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>Indicadores del inicio: activas/vencidas, cierres por mes y por dia en la zona del tenant,
/// alcance mio vs equipo, agrupado por tablero y tableros restringidos.</summary>
public class InicioStatsCalculatorTests
{
    private static readonly TimeZoneInfo Bogota = TimeZoneInfo.CreateCustomTimeZone("bog", TimeSpan.FromHours(-5), "bog", "bog");

    // Viernes 2026-11-27 15:00 en Bogota = 20:00 UTC.
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 20, 0, 0, TimeSpan.Zero);

    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Otro = Guid.NewGuid();
    private static readonly Guid Ventas = Guid.NewGuid();
    private static readonly Guid Soporte = Guid.NewGuid();
    private static readonly Guid Gerencia = Guid.NewGuid();

    private static readonly Dictionary<Guid, InicioBoardInfo> Boards = new()
    {
        [Ventas] = new("Ventas", true),
        [Soporte] = new("Soporte", true),
        [Gerencia] = new("Gerencia", false), // restringido a otros
    };

    // Hora local de Bogota -> UTC.
    private static DateTimeOffset L(int month, int day, int hour = 10) => new(2026, month, day, hour, 0, 0, TimeSpan.FromHours(-5));

    private static InicioTaskRow Activa(Guid? board, Guid? who, DateTimeOffset? due = null, DateTimeOffset? created = null)
        => new(board, who, TaskItemStatus.Active, false, due, created ?? L(1, 10), null, null, false);

    private static InicioTaskRow CerradaEnColumna(Guid? board, Guid? who, DateTimeOffset when, DateTimeOffset? created = null)
        => new(board, who, TaskItemStatus.Done, false, null, created ?? L(1, 10), null, when, true);

    [Fact]
    public void Activas_Vencidas_Y_Alcance_Mio()
    {
        var rows = new List<InicioTaskRow>
        {
            Activa(Ventas, Me, due: L(11, 20)),     // vencida
            Activa(Ventas, Me, due: L(12, 5)),
            Activa(Soporte, Otro),
            new(Ventas, Me, TaskItemStatus.Active, true, null, L(1, 1), null, null, false), // archivada: no cuenta
            new(Ventas, Me, TaskItemStatus.Active, false, null, L(1, 1), null, L(11, 1), true), // en columna de cierre: cerrada
        };

        var r = InicioStatsCalculator.Compute(rows, Boards, Me, Bogota, Now);

        Assert.Equal(3, r.Equipo.Activas);
        Assert.Equal(1, r.Equipo.Vencidas);
        Assert.NotNull(r.Mio);
        Assert.Equal(2, r.Mio!.Activas);
        Assert.Equal(new[] { new InicioBoardCountDto("Ventas", 2) }, r.Mio.ActivasPorTablero);
    }

    [Fact]
    public void Cierres_Por_Mes_Dia_Y_Tendencia_En_Hora_Local()
    {
        var rows = new List<InicioTaskRow>
        {
            CerradaEnColumna(Ventas, Me, L(11, 27, 9)),            // hoy
            CerradaEnColumna(Ventas, Me, L(11, 21)),               // hace 6 dias: entra en 7 dias
            CerradaEnColumna(Soporte, Me, L(11, 20)),              // hace 7 dias: fuera de 7 dias, dentro del mes
            CerradaEnColumna(Soporte, Me, L(10, 15)),              // mes anterior
            CerradaEnColumna(Soporte, Me, L(10, 31, 22)),          // 31-oct 22:00 local = 1-nov UTC: cuenta octubre
            new(Ventas, Me, TaskItemStatus.Closed, false, null, L(1, 1), L(11, 2), null, false), // Closed con ClosedAt
            CerradaEnColumna(Ventas, Me, L(4, 10)),                // fuera de la ventana de 6 meses
        };

        var mio = InicioStatsCalculator.Compute(rows, Boards, Me, Bogota, Now).Mio!;

        Assert.Equal(4, mio.CerradasMes);
        Assert.Equal(2, mio.CerradasMesAnterior);
        Assert.Equal(2, mio.Cerradas7Dias);
        Assert.Equal(6, mio.Mensual.Labels.Count);
        Assert.Equal("Nov", mio.Mensual.Labels[^1]);
        Assert.Equal("Jun", mio.Mensual.Labels[0]);
        Assert.Equal(new[] { 0, 0, 0, 0, 2, 4 }, mio.Mensual.Cerradas);
        Assert.Equal(7, mio.UltimosDias.Labels.Count);
        Assert.Equal(1, mio.UltimosDias.Cerradas[0]); // 21-nov
        Assert.Equal(1, mio.UltimosDias.Cerradas[^1]); // hoy
        Assert.EndsWith("27", mio.UltimosDias.Labels[^1]);
        Assert.Equal(new[] { new InicioBoardCountDto("Ventas", 3), new InicioBoardCountDto("Soporte", 3) }.OrderBy(x => x.Board),
            mio.CerradasPorTablero.OrderBy(x => x.Board));
    }

    [Fact]
    public void Nuevas_Por_Fecha_De_Creacion()
    {
        var rows = new List<InicioTaskRow>
        {
            Activa(Ventas, Me, created: L(11, 26)),
            Activa(Ventas, Otro, created: L(11, 26)),
            Activa(Ventas, Otro, created: L(9, 3)),
        };

        var r = InicioStatsCalculator.Compute(rows, Boards, Me, Bogota, Now);

        Assert.Equal(new[] { 0, 0, 0, 1, 0, 2 }, r.Equipo.Mensual.Nuevas);
        Assert.Equal(2, r.Equipo.UltimosDias.Nuevas[5]); // 26-nov
        Assert.Equal(1, r.Mio!.UltimosDias.Nuevas[5]);
    }

    [Fact]
    public void Restringido_Sale_Como_Otros_En_Equipo_Pero_Con_Nombre_En_Lo_Mio()
    {
        var rows = new List<InicioTaskRow>
        {
            Activa(Gerencia, Me),
            Activa(Gerencia, Otro),
            Activa(null, Otro),
        };

        var r = InicioStatsCalculator.Compute(rows, Boards, Me, Bogota, Now);

        Assert.Contains(new InicioBoardCountDto(InicioStatsCalculator.OtrosTableros, 2), r.Equipo.ActivasPorTablero);
        Assert.Contains(new InicioBoardCountDto(InicioStatsCalculator.SinTablero, 1), r.Equipo.ActivasPorTablero);
        Assert.DoesNotContain(r.Equipo.ActivasPorTablero, x => x.Board == "Gerencia");
        Assert.Equal(new[] { new InicioBoardCountDto("Gerencia", 1) }, r.Mio!.ActivasPorTablero);
    }

    [Fact]
    public void Mas_De_Ocho_Tableros_Agrupa_El_Resto_Y_Sin_Usuario_No_Hay_Mio()
    {
        var boards = new Dictionary<Guid, InicioBoardInfo>();
        var rows = new List<InicioTaskRow>();
        for (var i = 0; i < 11; i++)
        {
            var id = Guid.NewGuid();
            boards[id] = new InicioBoardInfo($"T{i:00}", true);
            for (var k = 0; k <= i; k++) { rows.Add(Activa(id, Otro)); }
        }

        var r = InicioStatsCalculator.Compute(rows, boards, null, Bogota, Now);

        Assert.Null(r.Mio);
        Assert.Equal(InicioStatsCalculator.TopBoards, r.Equipo.ActivasPorTablero.Count);
        Assert.Equal("T10", r.Equipo.ActivasPorTablero[0].Board);
        var otros = r.Equipo.ActivasPorTablero[^1];
        Assert.Equal(InicioStatsCalculator.OtrosTableros, otros.Board);
        Assert.Equal(1 + 2 + 3 + 4, otros.Count); // T00..T03
        Assert.Equal(rows.Count, r.Equipo.ActivasPorTablero.Sum(x => x.Count));
    }

    [Fact]
    public void Inicio_De_Ventana_Es_Medianoche_Local_Del_Primer_Mes()
    {
        var from = InicioStatsCalculator.WindowStartUtc(Bogota, Now);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 5, 0, 0, TimeSpan.Zero), from);
        Assert.Equal(TimeSpan.Zero, from.Offset);
    }
}
