using Ecorex.Domain.Enums;

namespace Ecorex.Application.Agendas;

/// <summary>Novedad reducida a lo que necesita el calculo (rango de fechas + tramo horario opcional).</summary>
public sealed record AgendaBloqueo(DateOnly Desde, DateOnly Hasta, TimeOnly? HoraDesde, TimeOnly? HoraHasta, string Etiqueta);

/// <summary>Vigencia reducida a lo que necesita el calculo.</summary>
public sealed record AgendaVigenciaCalc(string Nombre, DateOnly Desde, DateOnly? Hasta, IReadOnlyList<AgendaFranjaDto> Franjas);

/// <summary>
/// Calculo PURO del modulo de Agendas (ADR-0165): validacion de franjas, cupos por franja/semana, que vigencia
/// aplica a una fecha y la disponibilidad de un dia descontando cierres del calendario y novedades. Sin BD:
/// lo usan el servicio y las pruebas unitarias. La futura ola de turnos restara ademas los turnos tomados.
/// </summary>
public static class AgendaCalculo
{
    public const int IntervaloMin = 5;
    public const int IntervaloMax = 720;
    public const int CupoMax = 999;

    /// <summary>Lunes primero (como se muestra la semana en Colombia).</summary>
    public static readonly DayOfWeek[] SemanaLunesPrimero =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    };

    public static string NombreDia(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "Lunes",
        DayOfWeek.Tuesday => "Martes",
        DayOfWeek.Wednesday => "Miercoles",
        DayOfWeek.Thursday => "Jueves",
        DayOfWeek.Friday => "Viernes",
        DayOfWeek.Saturday => "Sabado",
        _ => "Domingo"
    };

    public static string NombreNovedad(AgendaNovedadTipo t) => t switch
    {
        AgendaNovedadTipo.Vacaciones => "Vacaciones",
        AgendaNovedadTipo.Incapacidad => "Incapacidad",
        AgendaNovedadTipo.Permiso => "Permiso",
        AgendaNovedadTipo.Capacitacion => "Capacitacion",
        AgendaNovedadTipo.Mantenimiento => "Mantenimiento",
        _ => "Otro"
    };

    /// <summary>
    /// Valida un juego de franjas. Devuelve el primer error legible o null si todo esta bien: fin despues del
    /// inicio, bloque entre 5 y 720 minutos que quepa al menos una vez, cupo entre 1 y 999, y sin franjas que se
    /// crucen en el mismo dia.
    /// </summary>
    public static string? ValidarFranjas(IEnumerable<AgendaFranjaDto> franjas)
    {
        var lista = franjas.ToList();
        foreach (var f in lista)
        {
            var dia = NombreDia(f.Dia);
            if (f.Fin <= f.Inicio)
            {
                return $"{dia}: la hora de fin ({f.Fin:HH\\:mm}) debe ser posterior a la de inicio ({f.Inicio:HH\\:mm}).";
            }
            if (f.IntervaloMinutos < IntervaloMin || f.IntervaloMinutos > IntervaloMax)
            {
                return $"{dia}: el bloque debe estar entre {IntervaloMin} y {IntervaloMax} minutos.";
            }
            if (f.Cupo < 1 || f.Cupo > CupoMax)
            {
                return $"{dia}: el cupo debe estar entre 1 y {CupoMax}.";
            }
            if (Minutos(f) < f.IntervaloMinutos)
            {
                return $"{dia} {f.Inicio:HH\\:mm}-{f.Fin:HH\\:mm}: el tramo es mas corto que un bloque de {f.IntervaloMinutos} minutos.";
            }
        }
        foreach (var grupo in lista.GroupBy(f => f.Dia))
        {
            var orden = grupo.OrderBy(f => f.Inicio).ToList();
            for (var i = 1; i < orden.Count; i++)
            {
                if (orden[i].Inicio < orden[i - 1].Fin)
                {
                    return $"{NombreDia(grupo.Key)}: las franjas {orden[i - 1].Inicio:HH\\:mm}-{orden[i - 1].Fin:HH\\:mm} y "
                        + $"{orden[i].Inicio:HH\\:mm}-{orden[i].Fin:HH\\:mm} se cruzan.";
                }
            }
        }
        return null;
    }

    private static int Minutos(AgendaFranjaDto f) => (int)(f.Fin - f.Inicio).TotalMinutes;

    /// <summary>Bloques completos de la franja (el sobrante que no completa un bloque no cuenta).</summary>
    public static int Bloques(AgendaFranjaDto f) => f.IntervaloMinutos <= 0 ? 0 : Minutos(f) / f.IntervaloMinutos;

    /// <summary>Cupos de la franja = bloques completos * cupo simultaneo.</summary>
    public static int CuposFranja(AgendaFranjaDto f) => Bloques(f) * Math.Max(0, f.Cupo);

    public static int CuposSemana(IEnumerable<AgendaFranjaDto> franjas) => franjas.Sum(CuposFranja);

    /// <summary>
    /// La vigencia que aplica a <paramref name="fecha"/>: entre las que la cubren (desde &lt;= fecha y sin fin o
    /// fin &gt;= fecha), la de inicio mas reciente. Asi un horario de temporada se monta encima del normal.
    /// </summary>
    public static AgendaVigenciaCalc? VigenciaPara(IEnumerable<AgendaVigenciaCalc> vigencias, DateOnly fecha) =>
        vigencias
            .Where(v => v.Desde <= fecha && (v.Hasta is null || v.Hasta >= fecha))
            .OrderByDescending(v => v.Desde)
            .FirstOrDefault();

    /// <summary>
    /// Disponibilidad de un dia. <paramref name="cierreCalendario"/> (festivo / dia no operativo del tenant)
    /// cierra el dia entero. Cada novedad que cubra la fecha bloquea los bloques que toca (todos si es de dia
    /// completo, o los que se cruzan con su tramo horario).
    /// </summary>
    public static AgendaDiaDto Dia(
        DateOnly fecha,
        IEnumerable<AgendaVigenciaCalc> vigencias,
        IEnumerable<AgendaBloqueo> bloqueos,
        string? cierreCalendario)
    {
        var vigencia = VigenciaPara(vigencias, fecha);
        var franjas = vigencia?.Franjas.Where(f => f.Dia == fecha.DayOfWeek).OrderBy(f => f.Inicio).ToList()
            ?? new List<AgendaFranjaDto>();
        var total = franjas.Sum(CuposFranja);
        var delDia = bloqueos.Where(b => b.Desde <= fecha && b.Hasta >= fecha).ToList();

        if (vigencia is null)
        {
            return new AgendaDiaDto(fecha, null, franjas, 0, 0, "Sin agenda vigente", delDia.Select(b => b.Etiqueta).ToList());
        }
        if (!string.IsNullOrWhiteSpace(cierreCalendario))
        {
            return new AgendaDiaDto(fecha, vigencia.Nombre, franjas, total, total, cierreCalendario, delDia.Select(b => b.Etiqueta).ToList());
        }

        var bloqueados = 0;
        foreach (var f in franjas)
        {
            var bloques = Bloques(f);
            for (var i = 0; i < bloques; i++)
            {
                var ini = f.Inicio.AddMinutes(i * f.IntervaloMinutos);
                var fin = ini.AddMinutes(f.IntervaloMinutos);
                if (delDia.Any(b => b.HoraDesde is null || b.HoraHasta is null || (b.HoraDesde < fin && b.HoraHasta > ini)))
                {
                    bloqueados += f.Cupo;
                }
            }
        }
        return new AgendaDiaDto(fecha, vigencia.Nombre, franjas, total, bloqueados, null, delDia.Select(b => b.Etiqueta).ToList());
    }

    /// <summary>Lunes de la semana de <paramref name="fecha"/>.</summary>
    public static DateOnly LunesDe(DateOnly fecha)
    {
        var delta = ((int)fecha.DayOfWeek + 6) % 7;
        return fecha.AddDays(-delta);
    }

    /// <summary>True si dos juegos de franjas son iguales (mismo dia, tramo, bloque y cupo), sin importar el orden.</summary>
    public static bool MismasFranjas(IEnumerable<AgendaFranjaDto> a, IEnumerable<AgendaFranjaDto> b)
    {
        static string Clave(AgendaFranjaDto f) => $"{(int)f.Dia}|{f.Inicio:HHmm}|{f.Fin:HHmm}|{f.IntervaloMinutos}|{f.Cupo}";
        var sa = a.Select(Clave).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var sb = b.Select(Clave).OrderBy(x => x, StringComparer.Ordinal).ToList();
        return sa.SequenceEqual(sb, StringComparer.Ordinal);
    }
}
