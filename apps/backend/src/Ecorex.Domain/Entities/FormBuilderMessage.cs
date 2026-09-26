using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Un mensaje de una <see cref="FormBuilderConversation"/>. Entidad TENANT-SCOPED. Guarda el turno de
/// usuario, la respuesta del agente, las llamadas a herramientas (function calling) y las PROPUESTAS
/// (acciones mutantes) con su estado de confirmacion (gate humano). Es la bitacora auditable del proceso.
/// </summary>
public class FormBuilderMessage : TenantEntity
{
    public Guid ConversationId { get; set; }
    public FormBuilderConversation? Conversation { get; set; }

    /// <summary>Orden del mensaje dentro de la conversacion (1-based).</summary>
    public int Sequence { get; set; }

    public FormBuilderMessageRole Role { get; set; }

    /// <summary>Texto del mensaje (turno de usuario o respuesta del agente).</summary>
    public string? Content { get; set; }

    /// <summary>Adjuntos del turno de usuario (JSON: nombre, mime, url) — Excel/PDF/imagen subidos.</summary>
    public string? AttachmentsJson { get; set; }

    /// <summary>Id de correlacion de la tool-call del modelo (para casar propuesta -> resultado).</summary>
    public string? ToolCallId { get; set; }

    /// <summary>Nombre de la herramienta (para Role Proposal/Tool).</summary>
    public string? ToolName { get; set; }

    /// <summary>Argumentos JSON de la herramienta que el modelo propuso ejecutar.</summary>
    public string? ToolArgsJson { get; set; }

    /// <summary>Resultado JSON de la herramienta tras ejecutarla.</summary>
    public string? ToolResultJson { get; set; }

    /// <summary>Estado de confirmacion cuando Role = Proposal (None para el resto).</summary>
    public FormBuilderProposalState ProposalState { get; set; } = FormBuilderProposalState.None;
}
