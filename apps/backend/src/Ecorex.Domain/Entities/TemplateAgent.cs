using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Conversacion del AGENTE EDITOR DE PLANTILLAS (ADR-0175): chat que crea/edita una plantilla de correo
/// (<see cref="TemplateAgentKind.Email"/>) o de WhatsApp (<see cref="TemplateAgentKind.WhatsApp"/>).
/// Entidad TENANT-SCOPED. <see cref="TemplateId"/> apunta a la EmailTemplate / WhatsAppTemplate que se
/// edita (null hasta que el agente crea una nueva). Persiste para retomar el chat y auditar.
/// </summary>
public class TemplateAgentConversation : TenantEntity
{
    public TemplateAgentKind Kind { get; set; }

    /// <summary>Plantilla que se edita (EmailTemplate.Id o WhatsAppTemplate.Id segun Kind). Null = nueva.</summary>
    public Guid? TemplateId { get; set; }

    public string Title { get; set; } = null!;

    public AiProvider Provider { get; set; }

    public string? Model { get; set; }

    public FormBuilderConversationStatus Status { get; set; } = FormBuilderConversationStatus.Active;

    /// <summary>Usuario (PlatformUser) que inicio la conversacion.</summary>
    public Guid? StartedByUserId { get; set; }

    public ICollection<TemplateAgentMessage> Messages { get; set; } = new List<TemplateAgentMessage>();
}

/// <summary>
/// Mensaje de una <see cref="TemplateAgentConversation"/>: turno de usuario, respuesta del agente o
/// PROPUESTA (accion mutante con gate humano). Misma forma que FormBuilderMessage.
/// </summary>
public class TemplateAgentMessage : TenantEntity
{
    public Guid ConversationId { get; set; }
    public TemplateAgentConversation? Conversation { get; set; }

    public int Sequence { get; set; }

    public FormBuilderMessageRole Role { get; set; }

    public string? Content { get; set; }

    public string? AttachmentsJson { get; set; }

    public string? ToolCallId { get; set; }

    public string? ToolName { get; set; }

    public string? ToolArgsJson { get; set; }

    public string? ToolResultJson { get; set; }

    public FormBuilderProposalState ProposalState { get; set; } = FormBuilderProposalState.None;
}

/// <summary>
/// Version guardada de una plantilla (correo o WhatsApp) ANTES de un cambio del agente o de una
/// restauracion (ADR-0175). Append-only. <see cref="PayloadJson"/> guarda el contenido completo para
/// poder "Restaurar" in-place.
/// </summary>
public class TemplateSnapshot : TenantEntity
{
    public TemplateAgentKind Kind { get; set; }

    public Guid TemplateId { get; set; }

    /// <summary>Etiqueta legible (ej. "Antes de: propose_template").</summary>
    public string Label { get; set; } = null!;

    /// <summary>Contenido completo de la plantilla en JSON (asunto + cuerpo, o los campos de WhatsApp).</summary>
    public string PayloadJson { get; set; } = "{}";

    public Guid? ConversationId { get; set; }

    public Guid? CreatedByUserId { get; set; }
}

/// <summary>
/// Baja de correos de un contacto (ADR-0175): quien abre el {enlace_baja} de un correo queda registrado aqui
/// por (tenant, correo) y el motor de acciones deja de enviarle correos. La Lista negra existente es de
/// TELEFONOS; esta es la equivalente para correo. Unico por (TenantId, Email).
/// </summary>
public class EmailUnsubscribe : TenantEntity
{
    /// <summary>Correo dado de baja, normalizado (trim + minusculas).</summary>
    public string Email { get; set; } = null!;

    /// <summary>De donde vino la baja: "enlace" (pagina publica) o "manual".</summary>
    public string Origen { get; set; } = "enlace";

    public string? Motivo { get; set; }
}
