using Ecorex.Domain.Enums;

namespace Ecorex.Application.Voice;

/// <summary>Resultado visible de una llamada de voz IA (ADR-0190), derivado del estado + disconnection_reason.</summary>
public enum VoiceCallOutcome
{
    /// <summary>Colocada en Retell, aun timbrando / sin eventos.</summary>
    Llamando = 0,

    /// <summary>En curso (call_started).</summary>
    EnCurso = 1,

    /// <summary>La persona contesto y hubo conversacion (colgo el usuario o el agente, transferencia...).</summary>
    Contesto = 2,

    /// <summary>Timbro y nadie contesto (dial_no_answer).</summary>
    NoContesto = 3,

    /// <summary>Linea ocupada (dial_busy).</summary>
    Ocupado = 4,

    /// <summary>Contesto el buzon de voz (voicemail_reached).</summary>
    Buzon = 5,

    /// <summary>La persona rechazo la llamada (user_declined).</summary>
    Rechazada = 6,

    /// <summary>No se pudo completar: numero invalido, error del proveedor, sin saldo, etc.</summary>
    Fallo = 7
}

/// <summary>
/// Clasificacion PURA del resultado de una llamada de Retell y regla de reintento del motor de acciones
/// (ADR-0190). El disconnection_reason manda sobre el estado: Retell lo envia en call_ended / call_analyzed
/// y en GET get-call.
/// </summary>
public static class VoiceCallOutcomes
{
    /// <summary>Reintentos extra maximos que acepta un paso Llamada (ademas del primer intento).</summary>
    public const int MaxRetries = 5;

    /// <summary>Espera minima entre dos intentos a un mismo contacto.</summary>
    public const int MinRetryMinutes = 5;

    /// <summary>Espera por defecto entre intentos cuando el paso no la define.</summary>
    public const int DefaultRetryMinutes = 30;

    public static VoiceCallOutcome Classify(VoiceCallStatus status, string? disconnectionReason)
    {
        var reason = (disconnectionReason ?? "").Trim().ToLowerInvariant();
        if (reason.Length > 0)
        {
            switch (reason)
            {
                case "dial_no_answer":
                case "registered_call_timeout":
                case "error_user_not_joined":
                    return VoiceCallOutcome.NoContesto;
                case "dial_busy":
                    return VoiceCallOutcome.Ocupado;
                case "voicemail_reached":
                    return VoiceCallOutcome.Buzon;
                case "user_declined":
                    return VoiceCallOutcome.Rechazada;
                case "user_hangup":
                case "agent_hangup":
                case "call_transfer":
                case "inactivity":
                case "max_duration_reached":
                case "ivr_reached":
                    return VoiceCallOutcome.Contesto;
                default:
                    // dial_failed, invalid_destination, telephony_provider_*, sip_routing_error, marked_as_spam,
                    // no_valid_payment, concurrency_limit_reached, scam_detected, error_*: no se completo.
                    return VoiceCallOutcome.Fallo;
            }
        }

        return status switch
        {
            VoiceCallStatus.Registered => VoiceCallOutcome.Llamando,
            VoiceCallStatus.Ongoing => VoiceCallOutcome.EnCurso,
            VoiceCallStatus.Ended or VoiceCallStatus.Analyzed => VoiceCallOutcome.Contesto,
            _ => VoiceCallOutcome.Fallo
        };
    }

    /// <summary>Resultados que justifican volver a llamar mas tarde (la persona no atendio).</summary>
    public static bool IsRetryable(VoiceCallOutcome outcome)
        => outcome is VoiceCallOutcome.NoContesto or VoiceCallOutcome.Ocupado
            or VoiceCallOutcome.Buzon or VoiceCallOutcome.Rechazada;

    /// <summary>Sigue en curso: aun no se sabe el resultado (no se reintenta ni se marca fallo).</summary>
    public static bool IsPending(VoiceCallOutcome outcome)
        => outcome is VoiceCallOutcome.Llamando or VoiceCallOutcome.EnCurso;

    /// <summary>Reintentos efectivos del paso: 0..MaxRetries.</summary>
    public static int ClampRetries(int? retries) => Math.Clamp(retries ?? 0, 0, MaxRetries);

    /// <summary>Minutos efectivos entre intentos: MinRetryMinutes..240 (por defecto DefaultRetryMinutes).</summary>
    public static int ClampRetryMinutes(int? minutes)
        => minutes is int m && m > 0 ? Math.Clamp(m, MinRetryMinutes, 240) : DefaultRetryMinutes;

    /// <summary>
    /// Regla de reintento por "no contesto" sobre la MISMA fila del run. <paramref name="attempts"/> = llamadas ya
    /// hechas (la primera incluida). Se vuelve a llamar si el ultimo resultado lo justifica, quedan intentos y ya
    /// paso la espera configurada. La ventana horaria del paso la valida el motor antes de llegar aqui.
    /// </summary>
    public static bool ShouldRetry(VoiceCallOutcome lastOutcome, int attempts, int? retries, int? retryMinutes,
        DateTimeOffset lastAttemptUtc, DateTimeOffset nowUtc)
    {
        if (!IsRetryable(lastOutcome)) { return false; }
        if (attempts >= 1 + ClampRetries(retries)) { return false; }
        return nowUtc - lastAttemptUtc >= TimeSpan.FromMinutes(ClampRetryMinutes(retryMinutes));
    }

    public static string Label(VoiceCallOutcome outcome) => outcome switch
    {
        VoiceCallOutcome.Llamando => "Llamando",
        VoiceCallOutcome.EnCurso => "En curso",
        VoiceCallOutcome.Contesto => "Contesto",
        VoiceCallOutcome.NoContesto => "No contesto",
        VoiceCallOutcome.Ocupado => "Ocupado",
        VoiceCallOutcome.Buzon => "Buzon de voz",
        VoiceCallOutcome.Rechazada => "Rechazada",
        _ => "Fallo"
    };

    /// <summary>Texto corto para el motivo tecnico de Retell (se muestra como detalle).</summary>
    public static string? ReasonText(string? disconnectionReason)
    {
        var reason = (disconnectionReason ?? "").Trim().ToLowerInvariant();
        return reason switch
        {
            "" => null,
            "user_hangup" => "Colgo el contacto",
            "agent_hangup" => "Colgo el agente",
            "call_transfer" => "Transferida",
            "inactivity" => "Sin respuesta en la conversacion",
            "max_duration_reached" => "Duracion maxima alcanzada",
            "dial_no_answer" => "Nadie contesto",
            "dial_busy" => "Linea ocupada",
            "voicemail_reached" => "Buzon de voz",
            "user_declined" => "El contacto rechazo la llamada",
            "invalid_destination" => "Numero invalido",
            "dial_failed" => "No se pudo marcar",
            "no_valid_payment" => "Cuenta de Retell sin saldo",
            "concurrency_limit_reached" => "Limite de llamadas simultaneas",
            _ => reason
        };
    }
}
