using System.Globalization;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Agendas;

/// <summary>Momento en que toca recordar un turno, ya ajustado al horario permitido; o el motivo para no hacerlo.</summary>
public sealed record AgendaRecordatorioMomento(DateTimeOffset? Programado, string? MotivoOmision);

/// <summary>Que hacer con un turno frente a una regla en este instante.</summary>
public enum AgendaRecordatorioAccion
{
    /// <summary>Aun no es momento.</summary>
    Esperar,
    /// <summary>Enviar ahora.</summary>
    Enviar,
    /// <summary>No se enviara: se registra omitido con su motivo.</summary>
    Omitir
}

/// <summary>
/// Calculo PURO de los recordatorios de turnos (ADR-0167): el momento del aviso con el horario permitido, la
/// decision esperar/enviar/omitir y el armado de los valores de la plantilla a partir de tokens del turno.
/// </summary>
public static class AgendaRecordatorioCalculo
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-CO");

    /// <summary>Tokens que se pueden usar en las variables de la plantilla, con su descripcion.</summary>
    public static readonly (string Token, string Descripcion)[] Tokens =
    {
        ("{turno.cliente}", "Nombre de quien toma el turno"),
        ("{turno.servicio}", "Servicio (p.ej. Consulta medicina general)"),
        ("{turno.fecha}", "Fecha larga: martes 18 de noviembre"),
        ("{turno.fecha_corta}", "Fecha corta: 18/11/2026"),
        ("{turno.hora}", "Hora de inicio: 3:00 p. m."),
        ("{turno.hora_fin}", "Hora de fin"),
        ("{turno.recurso}", "Con quien / donde (recursos asignados)"),
        ("{turno.ticket}", "Numero de la tarea del turno"),
        ("{turno.telefono}", "Telefono del cliente"),
        ("{empresa.nombre}", "Nombre de la empresa")
    };

    /// <summary>
    /// Momento del aviso: inicio - anticipacion. Si cae fuera del horario permitido [desde, hasta] (hora local del
    /// tenant): Adelantar lo mueve al cierre del ultimo horario permitido anterior; Omitir lo descarta con motivo.
    /// Sin horario (o con un horario invalido) no se ajusta.
    /// </summary>
    public static AgendaRecordatorioMomento Momento(
        DateTimeOffset inicioUtc, int anticipacionMinutos, TimeOnly? desde, TimeOnly? hasta,
        AgendaRecordatorioFueraHorario modo, TimeZoneInfo zona)
    {
        var due = inicioUtc.AddMinutes(-anticipacionMinutos);
        if (desde is not TimeOnly d || hasta is not TimeOnly h || h <= d)
        {
            return new AgendaRecordatorioMomento(due, null);
        }
        var local = TimeZoneInfo.ConvertTime(due, zona).DateTime;
        var hora = TimeOnly.FromDateTime(local);
        if (hora >= d && hora <= h)
        {
            return new AgendaRecordatorioMomento(due, null);
        }
        if (modo == AgendaRecordatorioFueraHorario.Omitir)
        {
            return new AgendaRecordatorioMomento(null,
                $"El aviso caia a las {hora:HH\\:mm}, fuera del horario permitido ({d:HH\\:mm} a {h:HH\\:mm}).");
        }
        // Adelantar: si se paso del cierre, al cierre de ese mismo dia; si es antes de abrir, al cierre del dia anterior.
        var fecha = DateOnly.FromDateTime(local);
        var objetivo = hora > h ? fecha : fecha.AddDays(-1);
        var localObjetivo = objetivo.ToDateTime(h, DateTimeKind.Unspecified);
        return new AgendaRecordatorioMomento(new DateTimeOffset(localObjetivo, zona.GetUtcOffset(localObjetivo)).ToUniversalTime(), null);
    }

    /// <summary>
    /// Decision para un turno activo (Reservado/Confirmado) frente a una regla en <paramref name="ahoraUtc"/>.
    /// Omite si el horario no lo permite, si el turno ya empezo, si quedo en su horario despues del momento del aviso
    /// (se reservo o reprogramo tarde) o si la regla es "solo si no confirmo" y ya confirmo.
    /// </summary>
    public static (AgendaRecordatorioAccion Accion, DateTimeOffset? Programado, string? Motivo) Evaluar(
        AgendaRecordatorioMomento momento, DateTimeOffset inicioUtc, DateTimeOffset? horarioDesdeUtc,
        AgendaTurnoEstado estado, bool soloSiNoConfirmado, DateTimeOffset dueSinAjuste, DateTimeOffset ahoraUtc)
    {
        if (momento.Programado is not DateTimeOffset programado)
        {
            // Horario no permitido: se registra cuando llega la hora original del aviso.
            return ahoraUtc >= dueSinAjuste
                ? (AgendaRecordatorioAccion.Omitir, dueSinAjuste, momento.MotivoOmision)
                : (AgendaRecordatorioAccion.Esperar, null, null);
        }
        if (ahoraUtc < programado)
        {
            return (AgendaRecordatorioAccion.Esperar, programado, null);
        }
        if (ahoraUtc >= inicioUtc)
        {
            return (AgendaRecordatorioAccion.Omitir, programado, "El turno ya habia empezado cuando tocaba avisar.");
        }
        if (horarioDesdeUtc is DateTimeOffset desde && desde > programado)
        {
            return (AgendaRecordatorioAccion.Omitir, programado, "El turno se reservo o reprogramo despues del momento del aviso.");
        }
        if (soloSiNoConfirmado && estado == AgendaTurnoEstado.Confirmado)
        {
            return (AgendaRecordatorioAccion.Omitir, programado, "El cliente ya habia confirmado.");
        }
        return (AgendaRecordatorioAccion.Enviar, programado, null);
    }

    /// <summary>Valores de los tokens para un turno.</summary>
    public static Dictionary<string, string> Valores(
        string cliente, string servicio, DateOnly fecha, TimeOnly inicio, TimeOnly fin, string? recursos,
        string? ticket, string? telefono, string? empresa)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{turno.cliente}"] = cliente,
            ["{turno.servicio}"] = servicio,
            ["{turno.fecha}"] = fecha.ToString("dddd d 'de' MMMM", Es),
            ["{turno.fecha_corta}"] = fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            ["{turno.hora}"] = HoraLegible(inicio),
            ["{turno.hora_fin}"] = HoraLegible(fin),
            ["{turno.recurso}"] = recursos ?? string.Empty,
            ["{turno.ticket}"] = ticket ?? string.Empty,
            ["{turno.telefono}"] = telefono ?? string.Empty,
            ["{empresa.nombre}"] = empresa ?? string.Empty
        };
    }

    /// <summary>Reemplaza los tokens de <paramref name="expresion"/> por sus valores (sin distinguir mayusculas).</summary>
    public static string Render(string? expresion, IReadOnlyDictionary<string, string> valores)
    {
        if (string.IsNullOrEmpty(expresion))
        {
            return string.Empty;
        }
        var texto = expresion;
        foreach (var (token, valor) in valores)
        {
            texto = texto.Replace(token, valor, StringComparison.OrdinalIgnoreCase);
        }
        return texto.Trim();
    }

    /// <summary>
    /// Expresion sugerida para una variable de plantilla segun su nombre (cliente, fecha, hora, servicio...). Vacio si
    /// no se reconoce: el usuario la asigna a mano.
    /// </summary>
    public static string Sugerida(string variable)
    {
        var v = variable.Trim().ToLowerInvariant();
        if (v.Contains("cliente") || v.Contains("nombre") || v.Contains("paciente")) { return "{turno.cliente}"; }
        if (v.Contains("fecha") || v.Contains("dia")) { return "{turno.fecha}"; }
        if (v.Contains("hora")) { return "{turno.hora}"; }
        if (v.Contains("servicio") || v.Contains("cita")) { return "{turno.servicio}"; }
        if (v.Contains("recurso") || v.Contains("medico") || v.Contains("profesional") || v.Contains("asesor") || v.Contains("lugar")) { return "{turno.recurso}"; }
        if (v.Contains("empresa")) { return "{empresa.nombre}"; }
        if (v.Contains("ticket") || v.Contains("numero")) { return "{turno.ticket}"; }
        return string.Empty;
    }

    private static string HoraLegible(TimeOnly h)
    {
        var hora12 = h.Hour % 12 == 0 ? 12 : h.Hour % 12;
        return $"{hora12}:{h.Minute:00} {(h.Hour < 12 ? "a. m." : "p. m.")}";
    }
}
