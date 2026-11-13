using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Regla de RECORDATORIO de turnos (ADR-0167). Cada tenant define las suyas: a que servicios aplica, cuanto antes
/// del turno se avisa, en que horario se permite avisar y por que canal (WhatsApp con linea + plantilla, o llamada
/// con un agente de voz). Varias reglas = varios recordatorios (p.ej. WhatsApp 24 h antes y llamada 1 h antes solo
/// si no confirmo). Tenant-scoped + soft-delete + concurrencia optimista.
/// </summary>
public sealed class AgendaRecordatorioRegla : TenantEntity, ISoftDeletable, IVersioned
{
    /// <summary>Nombre visible. varchar(120).</summary>
    public string Nombre { get; set; } = string.Empty;

    public bool Activa { get; set; } = true;

    /// <summary>Servicios a los que aplica, JSON de ids. "[]" = todos los servicios.</summary>
    public string ServicioIdsJson { get; set; } = "[]";

    /// <summary>Minutos antes del inicio del turno (15 = un cuarto de hora, 60 = una hora, 1440 = un dia).</summary>
    public int AnticipacionMinutos { get; set; } = 1440;

    /// <summary>Hora local desde la que se permite avisar. null = sin restriccion.</summary>
    public TimeOnly? HoraDesde { get; set; }

    /// <summary>Hora local hasta la que se permite avisar. null = sin restriccion.</summary>
    public TimeOnly? HoraHasta { get; set; }

    /// <summary>Que hacer si el momento del aviso cae fuera del horario permitido.</summary>
    public AgendaRecordatorioFueraHorario FueraDeHorario { get; set; } = AgendaRecordatorioFueraHorario.Adelantar;

    /// <summary>Solo turnos que aun no estan confirmados (sirve para un segundo aviso de respaldo).</summary>
    public bool SoloSiNoConfirmado { get; set; }

    public AgendaRecordatorioCanal Canal { get; set; } = AgendaRecordatorioCanal.WhatsApp;

    // --- WhatsApp ---
    public Guid? WhatsAppLineId { get; set; }

    public Guid? PlantillaId { get; set; }

    /// <summary>Variable de la plantilla -> expresion con tokens del turno ({turno.cliente}, {turno.fecha}...). JSON.</summary>
    public string VariablesJson { get; set; } = "{}";

    // --- Llamada (etapa 2) ---
    /// <summary>Agente del sistema que habla en la llamada.</summary>
    public Guid? AiAgentId { get; set; }

    /// <summary>Linea de voz a usar. null = la por defecto del tenant.</summary>
    public Guid? VoiceLineId { get; set; }

    /// <summary>Objetivo / instruccion extra para el agente de voz. varchar(1000).</summary>
    public string? Objetivo { get; set; }

    // --- IVersioned ---
    public long Version { get; set; }

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByTenantUserId { get; set; }
}

/// <summary>
/// Un recordatorio ya procesado (ADR-0167): enviado, fallido u omitido. UNICO por (regla, turno, inicio del turno):
/// nunca se avisa dos veces el mismo horario, y si el turno se reprograma, el nuevo horario genera su propio envio.
/// </summary>
public sealed class AgendaRecordatorioEnvio : TenantEntity
{
    public Guid ReglaId { get; set; }

    public Guid TurnoId { get; set; }

    /// <summary>Inicio del turno (UTC) al que corresponde este envio.</summary>
    public DateTimeOffset InicioTurnoUtc { get; set; }

    public AgendaRecordatorioCanal Canal { get; set; }

    /// <summary>Momento en que tocaba avisar (ya ajustado al horario permitido).</summary>
    public DateTimeOffset ProgramadoUtc { get; set; }

    public DateTimeOffset ProcesadoUtc { get; set; }

    public AgendaRecordatorioEnvioEstado Estado { get; set; }

    /// <summary>Motivo del fallo u omision, o resumen del envio. varchar(500).</summary>
    public string? Detalle { get; set; }

    /// <summary>Id del mensaje (wamid) o de la llamada en el proveedor. varchar(200).</summary>
    public string? ReferenciaExterna { get; set; }
}
