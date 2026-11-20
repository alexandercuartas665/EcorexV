using Ecorex.Application.Forms.Builder;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Gestor.TemplateAgent;

/// <summary>Adjunto del chat (imagen/PDF/HTML/texto) en base64.</summary>
public sealed record TemplateAgentAttachment(string FileName, string Mime, string Base64);

/// <summary>Accion mutante propuesta por el agente, pendiente de confirmacion. <paramref name="Irreversible"/>
/// = requiere la confirmacion EXPLICITA aparte (p.ej. someter una plantilla a Meta).</summary>
public sealed record TemplateAgentProposalDto(Guid MessageId, string ToolName, string ArgumentsJson, bool Irreversible);

/// <summary>Resultado de un turno (enviar / confirmar / rechazar).</summary>
public sealed record TemplateAgentTurnResult(
    bool Ok,
    string? Error,
    Guid ConversationId,
    Guid? TemplateId,
    string? AssistantText,
    IReadOnlyList<TemplateAgentProposalDto> Proposals,
    bool AwaitingConfirmation,
    int TurnInputTokens = 0,
    int TurnOutputTokens = 0,
    decimal TurnCostUsd = 0m,
    int TurnCachedInputTokens = 0,
    // true si en este turno se aplico un cambio a la plantilla (la UI recarga el editor).
    bool TemplateChanged = false)
{
    public static TemplateAgentTurnResult Fail(Guid conversationId, string error)
        => new(false, error, conversationId, null, null, Array.Empty<TemplateAgentProposalDto>(), false);
}

public sealed record TemplateAgentStartResult(bool Ok, string? Error, Guid ConversationId, Guid? TemplateId);

/// <summary>Contexto de ejecucion de una herramienta: el toolset puede fijar <see cref="TemplateId"/> al crear.</summary>
public sealed class TemplateAgentToolContext
{
    public Guid ConversationId { get; init; }
    public Guid? TemplateId { get; set; }
    public Guid ActorUserId { get; init; }
    /// <summary>true si la herramienta modifico la plantilla (para avisar a la UI).</summary>
    public bool TemplateChanged { get; set; }
}

/// <summary>
/// Herramientas de un tipo de plantilla (correo o WhatsApp) para el nucleo del agente (ADR-0175). El nucleo
/// es generico: conversacion, gate humano, versiones, proveedor, cupo y verificacion obligatoria; cada
/// toolset aporta el arnes (system prompt), las herramientas, el verificador y la foto para versionar.
/// </summary>
public interface ITemplateAgentToolset
{
    TemplateAgentKind Kind { get; }

    IReadOnlyList<AiToolSpec> GetSpecs();

    /// <summary>Herramientas de solo lectura: corren sin gate.</summary>
    IReadOnlySet<string> ReadOnlyTools { get; }

    /// <summary>Herramientas IRREVERSIBLES: ademas del gate, exigen confirmacion explicita aparte.</summary>
    IReadOnlySet<string> IrreversibleTools { get; }

    /// <summary>Herramientas que MODIFICAN la plantilla (antes del lote se toma una version).</summary>
    IReadOnlySet<string> TemplateMutatingTools { get; }

    /// <summary>Ejecuta una herramienta y devuelve su resultado JSON ({"ok":...}).</summary>
    Task<string> ExecuteAsync(TemplateAgentToolContext context, string toolName, string argumentsJson, CancellationToken cancellationToken = default);

    /// <summary>Verificacion determinista de la plantilla guardada (cierre obligatorio del turno).</summary>
    Task<TemplateVerifyResult?> VerifyAsync(Guid templateId, CancellationToken cancellationToken = default);

    /// <summary>Contenido actual de la plantilla en JSON (para la version previa a un cambio); null si no existe.</summary>
    Task<string?> SnapshotPayloadAsync(Guid templateId, CancellationToken cancellationToken = default);

    /// <summary>Restaura la plantilla desde el JSON de una version.</summary>
    Task<bool> RestoreAsync(Guid templateId, string payloadJson, CancellationToken cancellationToken = default);

    /// <summary>Nombre visible de la plantilla (titulo de la conversacion); null si no existe.</summary>
    Task<string?> GetTitleAsync(Guid templateId, CancellationToken cancellationToken = default);

    /// <summary>Arnes (system prompt) del agente para este tipo de plantilla.</summary>
    Task<string> SystemPromptAsync(string tenantName, Guid? templateId, CancellationToken cancellationToken = default);
}

/// <summary>Persistencia del agente (Infrastructure, sobre EcorexDbContext). Tenant-scoped por el filtro global.</summary>
public interface ITemplateAgentStore
{
    Task<TemplateAgentConversation> CreateConversationAsync(TemplateAgentKind kind, Guid? templateId, AiProvider provider, string? model,
        string title, Guid? startedByUserId, CancellationToken cancellationToken = default);
    Task<TemplateAgentConversation?> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken = default);
    Task<TemplateAgentConversation?> GetLatestConversationAsync(TemplateAgentKind kind, Guid templateId, CancellationToken cancellationToken = default);
    Task SaveConversationAsync(TemplateAgentConversation conversation, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TemplateAgentMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default);
    Task<TemplateAgentMessage> AddMessageAsync(TemplateAgentMessage message, CancellationToken cancellationToken = default);
    Task SaveMessageAsync(TemplateAgentMessage message, CancellationToken cancellationToken = default);

    /// <summary>Cuenta global del proveedor (key CIFRADA) o null.</summary>
    Task<FormBuilderProviderInfo?> ResolveProviderAsync(AiProvider provider, CancellationToken cancellationToken = default);

    /// <summary>Proveedor marcado "IA gestion de plantillas"; si ninguno, el de formularios; si ninguno, null.</summary>
    Task<AiProvider?> GetTemplateBuilderProviderAsync(CancellationToken cancellationToken = default);

    Task<string> GetTenantNameAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Version guardada de una plantilla (lista del editor).</summary>
public sealed record TemplateSnapshotDto(Guid Id, string Label, DateTimeOffset CreatedAt);

/// <summary>Versiones (snapshots) de plantillas: tomar, listar y restaurar in-place (ADR-0175).</summary>
public interface ITemplateSnapshotService
{
    Task SnapshotAsync(TemplateAgentKind kind, Guid templateId, string label, Guid? conversationId, Guid? actorUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TemplateSnapshotDto>> ListAsync(TemplateAgentKind kind, Guid templateId, int take = 30, CancellationToken cancellationToken = default);
    /// <summary>Restaura la version (antes guarda la actual como "Antes de restaurar", asi se puede deshacer).</summary>
    Task<bool> RestoreAsync(Guid snapshotId, Guid? actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Contenido JSON de una version (para verla ANTES de restaurarla). Null si no existe.</summary>
    Task<string?> GetPayloadAsync(Guid snapshotId, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);
}

/// <summary>
/// Agente editor de plantillas (correo / WhatsApp) por chat, con GATE HUMANO: las acciones mutantes se
/// PROPONEN y solo corren tras <see cref="ConfirmAsync"/>; las irreversibles exigen confirmacion explicita.
/// </summary>
public interface ITemplateAgentChatService
{
    Task<TemplateAgentStartResult> StartAsync(TemplateAgentKind kind, Guid? templateId, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<TemplateAgentTurnResult> SendAsync(Guid conversationId, string? userText, IReadOnlyList<TemplateAgentAttachment>? attachments, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<TemplateAgentTurnResult> ConfirmAsync(Guid conversationId, Guid actorUserId, bool confirmIrreversible = false, CancellationToken cancellationToken = default);
    Task<TemplateAgentTurnResult> RejectAsync(Guid conversationId, string? reason, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TemplateAgentMessage>> GetTranscriptAsync(Guid conversationId, CancellationToken cancellationToken = default);
    /// <summary>Empieza un chat NUEVO para la plantilla (archiva el anterior).</summary>
    Task<TemplateAgentStartResult> ResetAsync(Guid conversationId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Deja una NOTA DEL SISTEMA en el chat activo de la plantilla cuando el usuario la cambio FUERA del
    /// chat (edicion manual o restaurar version), para que el agente no reaplique cambios viejos de su historial.</summary>
    Task NoteExternalChangeAsync(TemplateAgentKind kind, Guid templateId, string what, CancellationToken cancellationToken = default);
}

/// <summary>Prefijo de las notas del sistema dentro del chat (la UI no las pinta como mensajes del usuario).</summary>
public static class TemplateAgentNotes
{
    public const string Prefix = "(Nota del sistema:";
}
