using Ecorex.Domain.Enums;

namespace Ecorex.Application.Agendas;

/// <summary>Tramo ocupado por un turno ya tomado (horas locales).</summary>
public readonly record struct AgendaOcupacion(TimeOnly Inicio, TimeOnly Fin);

/// <summary>
/// Un recurso en UNA fecha, listo para calcular huecos: las franjas de su vigencia para ese dia de la semana, las
/// novedades que lo tocan, si el calendario de la entidad cierra el dia y los turnos que ya lo ocupan.
/// </summary>
public sealed record AgendaRecursoDia(
    Guid RecursoId,
    string Nombre,
    IReadOnlyList<AgendaFranjaDto> Franjas,
    IReadOnlyList<AgendaBloqueo> Bloqueos,
    bool Cerrado,
    IReadOnlyList<AgendaOcupacion> Ocupaciones);

/// <summary>Requisito de un servicio reducido al calculo: nombre + recursos que lo pueden cumplir.</summary>
public sealed record AgendaRequisitoCalc(string Nombre, IReadOnlyList<Guid> RecursoIds);

/// <summary>Recurso asignado a un requisito en un hueco.</summary>
public sealed record AgendaAsignacion(string Requisito, Guid RecursoId, string RecursoNombre);

/// <summary>Hueco libre para un servicio: fecha, horas y los recursos que lo cubririan.</summary>
public sealed record AgendaHueco(DateOnly Fecha, TimeOnly Inicio, TimeOnly Fin, IReadOnlyList<AgendaAsignacion> Asignacion);

/// <summary>
/// Calculo PURO de huecos y asignacion de recursos para turnos (ADR-0165 olas 3 y 4). Un turno empieza en el
/// inicio de un bloque de una franja, debe terminar dentro de esa misma franja y ocupa todos los bloques que toca;
/// en cada bloque caben tantos turnos como el cupo de la franja. Cada requisito del servicio se cumple con un
/// recurso distinto que este libre todo el turno (se prefiere el menos ocupado del dia, para repartir).
/// </summary>
public static class AgendaTurnosCalculo
{
    /// <summary>
    /// True si el recurso puede tomar un turno de <paramref name="inicio"/> a <paramref name="fin"/> en su fecha:
    /// dia abierto, inicio alineado a un bloque de una franja que contiene todo el turno, ningun bloque cerrado por
    /// una novedad y ningun bloque lleno.
    /// </summary>
    public static bool Libre(AgendaRecursoDia r, DateOnly fecha, TimeOnly inicio, TimeOnly fin)
    {
        if (r.Cerrado || fin <= inicio)
        {
            return false;
        }
        var franja = r.Franjas.FirstOrDefault(f =>
            f.Inicio <= inicio && fin <= f.Fin && f.IntervaloMinutos > 0
            && ((int)(inicio - f.Inicio).TotalMinutes) % f.IntervaloMinutos == 0);
        if (franja is null)
        {
            return false;
        }
        var bloques = AgendaCalculo.Bloques(franja);
        var delDia = r.Bloqueos.Where(b => b.Desde <= fecha && b.Hasta >= fecha).ToList();
        for (var i = 0; i < bloques; i++)
        {
            var bIni = franja.Inicio.AddMinutes(i * franja.IntervaloMinutos);
            var bFin = bIni.AddMinutes(franja.IntervaloMinutos);
            if (bFin <= inicio)
            {
                continue;
            }
            if (bIni >= fin)
            {
                break;
            }
            if (delDia.Any(b => b.HoraDesde is null || b.HoraHasta is null || (b.HoraDesde < bFin && b.HoraHasta > bIni)))
            {
                return false;
            }
            var ocupados = r.Ocupaciones.Count(o => o.Inicio < bFin && o.Fin > bIni);
            if (ocupados >= franja.Cupo)
            {
                return false;
            }
        }
        // El turno no puede pasar del ultimo bloque completo (el sobrante de la franja no se ofrece).
        var finUltimoBloque = franja.Inicio.AddMinutes(bloques * franja.IntervaloMinutos);
        return fin <= finUltimoBloque;
    }

    /// <summary>
    /// Asigna un recurso distinto a cada requisito para el turno, o null si algun requisito no tiene recurso libre.
    /// </summary>
    public static IReadOnlyList<AgendaAsignacion>? Asignar(
        IReadOnlyList<AgendaRequisitoCalc> requisitos,
        IReadOnlyDictionary<Guid, AgendaRecursoDia> recursos,
        DateOnly fecha,
        TimeOnly inicio,
        TimeOnly fin)
    {
        if (requisitos.Count == 0)
        {
            return null;
        }
        var usados = new HashSet<Guid>();
        var asignacion = new List<AgendaAsignacion>();
        foreach (var req in requisitos)
        {
            var elegido = req.RecursoIds
                .Where(id => !usados.Contains(id) && recursos.ContainsKey(id))
                .Select(id => recursos[id])
                .Where(r => Libre(r, fecha, inicio, fin))
                .OrderBy(r => r.Ocupaciones.Count)
                .ThenBy(r => r.Nombre, StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault();
            if (elegido is null)
            {
                return null;
            }
            usados.Add(elegido.RecursoId);
            asignacion.Add(new AgendaAsignacion(req.Nombre, elegido.RecursoId, elegido.Nombre));
        }
        return asignacion;
    }

    /// <summary>
    /// Huecos de un dia para un servicio de <paramref name="duracionMinutos"/>. Los inicios candidatos son los inicios
    /// de bloque de las franjas de los recursos elegibles; <paramref name="noAntesDe"/> descarta los que ya pasaron o
    /// no cumplen la anticipacion minima.
    /// </summary>
    public static IReadOnlyList<AgendaHueco> HuecosDia(
        DateOnly fecha,
        int duracionMinutos,
        IReadOnlyList<AgendaRequisitoCalc> requisitos,
        IReadOnlyDictionary<Guid, AgendaRecursoDia> recursos,
        TimeOnly? noAntesDe = null)
    {
        var elegibles = requisitos.SelectMany(r => r.RecursoIds).Distinct()
            .Where(recursos.ContainsKey).Select(id => recursos[id]).ToList();
        var inicios = elegibles
            .Where(r => !r.Cerrado)
            .SelectMany(r => r.Franjas.SelectMany(f =>
                Enumerable.Range(0, AgendaCalculo.Bloques(f)).Select(i => f.Inicio.AddMinutes(i * f.IntervaloMinutos))))
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        var huecos = new List<AgendaHueco>();
        foreach (var ini in inicios)
        {
            if (noAntesDe is TimeOnly limite && ini < limite)
            {
                continue;
            }
            var minutosDisponibles = (int)(new TimeOnly(23, 59) - ini).TotalMinutes + 1;
            if (duracionMinutos > minutosDisponibles)
            {
                continue; // el turno cruzaria la medianoche
            }
            var fin = ini.AddMinutes(duracionMinutos);
            var asignacion = Asignar(requisitos, recursos, fecha, ini, fin);
            if (asignacion is not null)
            {
                huecos.Add(new AgendaHueco(fecha, ini, fin, asignacion));
            }
        }
        return huecos;
    }

    /// <summary>
    /// Transiciones validas de estado: un turno Reservado puede pasar a cualquier otro; uno Confirmado puede
    /// atenderse, marcarse como no asistido, cancelarse o volver a Reservado. Atendido, No asistio y Cancelado
    /// son finales.
    /// </summary>
    public static bool PuedeCambiar(AgendaTurnoEstado de, AgendaTurnoEstado a) => (de, a) switch
    {
        (AgendaTurnoEstado.Reservado, not AgendaTurnoEstado.Reservado) => true,
        (AgendaTurnoEstado.Confirmado, AgendaTurnoEstado.Atendido or AgendaTurnoEstado.NoAsistio
            or AgendaTurnoEstado.Cancelado or AgendaTurnoEstado.Reservado) => true,
        _ => false
    };

    /// <summary>Un turno en estos estados aun se puede mover de horario.</summary>
    public static bool Reprogramable(AgendaTurnoEstado e) => e is AgendaTurnoEstado.Reservado or AgendaTurnoEstado.Confirmado;
}
