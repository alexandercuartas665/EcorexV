using Cronos;
using Ecorex.Application.Scheduling;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.DataContainers;

/// <summary>Por que una programacion no tiene proxima ejecucion. El motivo importa: "no se programa"
/// (Manual) y "el cron esta roto" exigen reacciones opuestas.</summary>
public enum ImportScheduleProblem
{
    /// <summary>Sin problema.</summary>
    None,
    /// <summary>No es automatica (Manual) o esta inactiva. Normal.</summary>
    NotScheduled,
    /// <summary>La configuracion no permite calcular nada (intervalo vacio, cron invalido...).</summary>
    Invalid
}

public sealed record NextRunResult(DateTimeOffset? NextRunAt, ImportScheduleProblem Problem, string? Reason);

/// <summary>
/// Cuando toca la proxima corrida de un <see cref="ImportProcess"/>.
///
/// NO reusa <see cref="ScheduledJobRecurrence"/> a proposito (ADR-0041): aquel recibe un
/// `ScheduledJobRule` concreto y razona en frecuencias de calendario (Daily/Weekly/Monthly), que no
/// contemplan ni "cada N minutos" ni cron. Lo que SI se reusa es su
/// <see cref="ScheduledJobRecurrence.ResolveTimeZone"/>, porque la zona del tenant es la misma idea.
/// </summary>
public static class ImportRecurrence
{
    /// <summary>Piso del intervalo. Por debajo de un minuto no tiene sentido: el worker barre cada
    /// minuto, asi que pedir "cada 10 segundos" solo crearia la ilusion de que corre mas seguido.</summary>
    public const int MinIntervalMinutes = 1;

    public static NextRunResult ComputeNextRun(ImportProcess process, DateTimeOffset afterUtc, TimeZoneInfo timeZone)
    {
        if (!process.IsActive)
        {
            return new(null, ImportScheduleProblem.NotScheduled, null);
        }

        switch (process.ScheduleKind)
        {
            case ImportScheduleKind.Manual:
                return new(null, ImportScheduleProblem.NotScheduled, null);

            case ImportScheduleKind.Interval:
                {
                    if (process.IntervalMinutes is not int minutes || minutes < MinIntervalMinutes)
                    {
                        return new(null, ImportScheduleProblem.Invalid,
                            $"El intervalo debe ser de al menos {MinIntervalMinutes} minuto(s).");
                    }

                    // Se ancla a la ULTIMA corrida, no a "ahora": asi "cada 15 minutos" no se va
                    // corriendo hacia adelante segun lo que tarde cada refresco.
                    var anchor = process.LastRunAt ?? afterUtc;
                    var next = anchor.AddMinutes(minutes);

                    // Si el servidor estuvo apagado, el ancla puede quedar muy atras y next seria pasado.
                    // Se avanza hasta el futuro en vez de disparar una rafaga de corridas atrasadas: a
                    // nadie le sirve recibir los 40 refrescos que no ocurrieron anoche.
                    if (next <= afterUtc)
                    {
                        var missed = (afterUtc - anchor).TotalMinutes / minutes;
                        next = anchor.AddMinutes(minutes * (Math.Floor(missed) + 1));
                    }
                    return new(next.ToUniversalTime(), ImportScheduleProblem.None, null);
                }

            case ImportScheduleKind.Cron:
                {
                    if (string.IsNullOrWhiteSpace(process.CronExpression))
                    {
                        return new(null, ImportScheduleProblem.Invalid, "Falta la expresion cron.");
                    }

                    CronExpression parsed;
                    try
                    {
                        parsed = CronExpression.Parse(process.CronExpression.Trim());
                    }
                    catch (CronFormatException ex)
                    {
                        return new(null, ImportScheduleProblem.Invalid, $"Cron invalido: {ex.Message}");
                    }

                    // El cron se interpreta en hora DEL TENANT (un "0 3 * * *" es "a las 3 de la
                    // madrugada aqui", no en UTC). Cronos resuelve el horario de verano: en el salto de
                    // primavera la hora que no existe se corre, y en el de otono la que ocurre dos veces
                    // no dispara dos veces.
                    var next = parsed.GetNextOccurrence(afterUtc.ToUniversalTime(), timeZone, inclusive: false);

                    // ToUniversalTime() NO es adorno: Cronos devuelve el instante con el desfase de la
                    // ZONA PEDIDA (-05:00 en Bogota), y Npgsql rechaza guardar un `timestamptz` que no
                    // venga con desfase 0. Sin esto, guardar una programacion cron revienta al escribir.
                    // Ojo al probarlo: DateTimeOffset compara INSTANTES, asi que un test de igualdad pasa
                    // igual con -05:00 que con Z; hay que afirmar sobre .Offset (ver ImportRecurrenceTests).
                    return next is null
                        ? new(null, ImportScheduleProblem.Invalid, "El cron no vuelve a ocurrir.")
                        : new(next.Value.ToUniversalTime(), ImportScheduleProblem.None, null);
                }

            default:
                return new(null, ImportScheduleProblem.NotScheduled, null);
        }
    }
}
