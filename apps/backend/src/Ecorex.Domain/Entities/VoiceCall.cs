using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Registro de una llamada de voz IA (Retell). Se crea al colocar la llamada (respuesta 201) y se actualiza
/// con los eventos de webhook (call_started/ended/analyzed). TENANT-SCOPED. La correlacion con el motor de
/// acciones es por <see cref="CallId"/> = call_id de Retell (que ademas queda como ExternalRef del
/// <see cref="ContactWorkflowRun"/>). NO guarda credenciales.
/// </summary>
public class VoiceCall : TenantEntity
{
    /// <summary>call_id devuelto por Retell (unico por tenant); clave de correlacion.</summary>
    public string CallId { get; set; } = null!;

    /// <summary>Linea de voz que coloco la llamada (para resolver la key al verificar el webhook).</summary>
    public Guid? RetellVoiceLineId { get; set; }

    /// <summary>Agente Retell usado (agent_id).</summary>
    public string? RetellAgentId { get; set; }

    public string FromNumber { get; set; } = null!;
    public string ToNumber { get; set; } = null!;

    public VoiceCallStatus Status { get; set; } = VoiceCallStatus.Registered;

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public int? DurationSeconds { get; set; }

    /// <summary>Motivo de desconexion reportado por Retell (disconnection_reason): dial_no_answer, voicemail_reached,
    /// dial_busy, dial_failed, user_hangup, etc. Lo usa la politica de auto-reintento para distinguir "sin contacto"
    /// (reintentar) de "hubo contacto" (dejar que el agente decida por el transcript).</summary>
    public string? DisconnectionReason { get; set; }

    /// <summary>Costo total reportado por Retell (USD), si viene en el analisis.</summary>
    public decimal? CostUsd { get; set; }

    public string? TranscriptText { get; set; }

    /// <summary>URL de la grabacion de la llamada (recording_url de Retell), si vino en el webhook.</summary>
    public string? RecordingUrl { get; set; }

    /// <summary>Analisis post-llamada (call_analysis) serializado como JSON.</summary>
    public string? AnalysisJson { get; set; }

    // ---- Origen (agente ECOREX + objetivo + whitelist de formularios, snapshot) ----

    public Guid? AiAgentId { get; set; }

    /// <summary>Nombre de ContactCallObjetivo (OfrecerProducto | LlenarFormulario | Personalizado).</summary>
    public string? Objetivo { get; set; }

    /// <summary>Formularios que el agente puede llenar (FormDefinition.Id), serializados como arreglo JSON.
    /// Es la whitelist DURA para el volcado a FormResponse: jamas se escribe fuera de esta lista.</summary>
    public string? FormulariosPermitidosJson { get; set; }

    /// <summary>Run del motor de acciones que coloco la llamada (su Id se asigna antes de llamar). Un run con
    /// reintentos por "no contesto" queda con varias VoiceCall (una por intento); ExternalRef = la ultima.</summary>
    public Guid? ContactWorkflowRunId { get; set; }

    public string? ErrorText { get; set; }

    /// <summary>Ultima vez que se consulto a Retell (GET get-call) por respaldo, cuando el webhook no llego.</summary>
    public DateTimeOffset? LastSyncedAtUtc { get; set; }
}
