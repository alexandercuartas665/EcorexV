using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Conversacion del ASISTENTE DE CREACION DE FORMULARIOS (chat que arma formularios desde Excel/PDF/imagen).
/// Entidad TENANT-SCOPED. Se liga al formulario que se esta construyendo/editando (FormDefinitionId, null
/// hasta que el agente lo crea). Persiste para retomar y auditar. Los mensajes viven en FormBuilderMessage.
/// </summary>
public class FormBuilderConversation : TenantEntity
{
    /// <summary>Formulario que se construye/edita. Null hasta que el agente ejecuta create_form.</summary>
    public Guid? FormDefinitionId { get; set; }

    /// <summary>Titulo corto para la lista de conversaciones (derivado del archivo o "Nuevo formulario").</summary>
    public string Title { get; set; } = null!;

    /// <summary>Proveedor de IA usado en la conversacion.</summary>
    public AiProvider Provider { get; set; }

    /// <summary>Modelo concreto (ej. gemini-2.5-pro); null cae al modelo por defecto del proveedor.</summary>
    public string? Model { get; set; }

    public FormBuilderConversationStatus Status { get; set; } = FormBuilderConversationStatus.Active;

    /// <summary>TenantUser que inicio la conversacion (para auditoria).</summary>
    public Guid? StartedByTenantUserId { get; set; }

    public ICollection<FormBuilderMessage> Messages { get; set; } = new List<FormBuilderMessage>();
}
