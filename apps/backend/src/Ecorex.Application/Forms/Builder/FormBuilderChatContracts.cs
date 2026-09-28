using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Forms.Builder;

/// <summary>Adjunto subido por el usuario en el chat (Excel/PDF/imagen), en base64.</summary>
public sealed record FormBuilderAttachment(string FileName, string Mime, string Base64);

/// <summary>Una PROPUESTA pendiente: una accion mutante que el agente quiere ejecutar y que espera confirmacion.</summary>
public sealed record FormBuilderProposalDto(Guid MessageId, string ToolName, string ArgumentsJson);

/// <summary>Resultado de un turno del asistente (enviar / confirmar / rechazar).</summary>
/// <param name="AssistantText">Texto del agente para mostrar (explicacion / pregunta / resumen).</param>
/// <param name="Proposals">Acciones mutantes pendientes de confirmacion (vacio en un turno final).</param>
/// <param name="AwaitingConfirmation">true si hay propuestas por confirmar antes de continuar.</param>
public sealed record FormBuilderTurnResult(
    bool Ok,
    string? Error,
    Guid ConversationId,
    Guid? FormDefinitionId,
    string? AssistantText,
    IReadOnlyList<FormBuilderProposalDto> Proposals,
    bool AwaitingConfirmation)
{
    public static FormBuilderTurnResult Fail(Guid conversationId, string error)
        => new(false, error, conversationId, null, null, Array.Empty<FormBuilderProposalDto>(), false);
}

/// <summary>Resultado de iniciar una conversacion.</summary>
public sealed record FormBuilderStartResult(bool Ok, string? Error, Guid ConversationId, Guid? FormDefinitionId);

/// <summary>Cuenta del proveedor de IA resuelta (la key sigue CIFRADA; el servicio la descifra).</summary>
public sealed record FormBuilderProviderInfo(bool Enabled, string? ApiKeyEncrypted, string? Model, string? BaseUrl);

/// <summary>
/// Persistencia de las conversaciones del asistente (implementada en Infrastructure sobre EcorexDbContext).
/// Aisla al servicio de EF. Todo es tenant-scoped por el filtro global; el TenantId lo sella el interceptor.
/// </summary>
public interface IFormBuilderChatStore
{
    Task<FormBuilderConversation> CreateConversationAsync(
        Guid? formDefinitionId, AiProvider provider, string? model, string title,
        Guid? startedByTenantUserId, CancellationToken cancellationToken = default);

    Task<FormBuilderConversation?> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken = default);

    Task SaveConversationAsync(FormBuilderConversation conversation, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FormBuilderMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default);

    Task<FormBuilderMessage> AddMessageAsync(FormBuilderMessage message, CancellationToken cancellationToken = default);

    Task SaveMessageAsync(FormBuilderMessage message, CancellationToken cancellationToken = default);

    /// <summary>Cuenta global del proveedor de IA (Gemini) o null si no existe. La key va cifrada.</summary>
    Task<FormBuilderProviderInfo?> ResolveProviderAsync(AiProvider provider, CancellationToken cancellationToken = default);

    /// <summary>Titulo del formulario (para el titulo de la conversacion / modo edicion); null si no existe.</summary>
    Task<string?> GetFormTitleAsync(Guid formDefinitionId, CancellationToken cancellationToken = default);

    /// <summary>Nombre del tenant (para el arnes); cadena vacia si no se encuentra.</summary>
    Task<string> GetTenantNameAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Asistente conversacional que crea/edita formularios (y su plantilla) desde Excel/PDF/imagen, con la IA
/// del tenant y las herramientas de FormAuthoringToolset. GATE HUMANO: las acciones mutantes se PROPONEN y
/// solo se ejecutan tras <see cref="ConfirmAsync"/>. La construccion es en vivo (cada accion confirmada
/// modifica el formulario borrador). Ver ARQUITECTURA y ARNES en docs/form-builder-chat.
/// </summary>
public interface IFormBuilderChatService
{
    /// <summary>Inicia una conversacion (formDefinitionId != null = editar existente; null = crear nuevo).</summary>
    Task<FormBuilderStartResult> StartAsync(Guid? formDefinitionId, Guid actorTenantUserId, CancellationToken cancellationToken = default);

    /// <summary>Envia un turno del usuario (texto y/o adjuntos) y corre el agente hasta un turno final o una propuesta.</summary>
    Task<FormBuilderTurnResult> SendAsync(Guid conversationId, string? userText, IReadOnlyList<FormBuilderAttachment>? attachments, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Confirma las propuestas pendientes: ejecuta sus herramientas y continua el agente.</summary>
    Task<FormBuilderTurnResult> ConfirmAsync(Guid conversationId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Rechaza las propuestas pendientes (con motivo opcional) y continua el agente.</summary>
    Task<FormBuilderTurnResult> RejectAsync(Guid conversationId, string? reason, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Devuelve la transcripcion (mensajes) de una conversacion para pintarla en la UI.</summary>
    Task<IReadOnlyList<FormBuilderMessage>> GetTranscriptAsync(Guid conversationId, CancellationToken cancellationToken = default);
}
