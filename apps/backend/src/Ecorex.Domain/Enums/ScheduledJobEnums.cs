namespace Ecorex.Domain.Enums;

// Motor de programaciones (modulo 000889 "Programar actividad"). Enums persistidos como
// TEXTO (HaveConversion<string>) para ser legibles y estables ante reordenamientos.

/// <summary>Que dispara la programacion: una notificacion/alerta o la creacion de una actividad.</summary>
public enum ScheduledJobType
{
    /// <summary>Envia un mensaje/alerta por los canales elegidos.</summary>
    Notification,
    /// <summary>Crea una tarea consumiendo el concepto (categoria/subcategoria) elegido.</summary>
    Activity,
}

/// <summary>Estado operativo de la programacion.</summary>
public enum ScheduledJobStatus
{
    Active,
    Paused,
}

/// <summary>Prioridad heredada del origen (PROG_ACTIVIDADES.PRIORIDAD). No la captura el prototipo aun.</summary>
public enum ScheduledJobPriority
{
    Low,
    Normal,
    High,
}

/// <summary>Frecuencia de una regla de recurrencia (dropdown del prototipo: 4 opciones).</summary>
public enum ScheduledJobFrequency
{
    /// <summary>Una sola vez, en la fecha de vigencia "desde" a la hora indicada (= fecha especifica).</summary>
    Once,
    Daily,
    Weekly,
    Monthly,
}

/// <summary>Canal de entrega de la programacion.</summary>
public enum ScheduledJobChannelType
{
    Email,
    WhatsApp,
    Slack,
    Sms,
}

/// <summary>Resultado de una ejecucion del worker (bitacora scheduled_job_runs).</summary>
public enum ScheduledJobRunResult
{
    Ok,
    Error,
    Skipped,
}
