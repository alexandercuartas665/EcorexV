using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// SNAPSHOT (version) de un formulario y sus plantillas de impresion, para poder REVERTIR si el agente
/// (o el usuario) daña el formulario. Entidad TENANT-SCOPED. Se toma automaticamente ANTES de aplicar un
/// lote de mutaciones confirmado desde el chat (Trigger=BeforeAgentBatch), o manualmente. Guarda el JSON
/// portable del formulario (Export) y el de las plantillas del tenant al momento, para restaurarlos in-place.
/// </summary>
public class FormBuilderSnapshot : TenantEntity
{
    /// <summary>Formulario al que pertenece el snapshot.</summary>
    public Guid FormDefinitionId { get; set; }

    /// <summary>Etiqueta legible (ej. "Antes de: crear seccion Totales" o "Version manual").</summary>
    public string Label { get; set; } = null!;

    /// <summary>Que disparo el snapshot (lote del agente o guardado manual).</summary>
    public FormSnapshotTrigger Trigger { get; set; } = FormSnapshotTrigger.Manual;

    /// <summary>Conversacion del chat que lo disparo (auditoria); null si fue manual.</summary>
    public Guid? ConversationId { get; set; }

    /// <summary>JSON portable del formulario completo (mismo formato que ExportAsync).</summary>
    public string FormJson { get; set; } = null!;

    /// <summary>JSON de las plantillas de impresion del tenant al momento del snapshot (para restaurarlas).</summary>
    public string? TemplatesJson { get; set; }

    /// <summary>TenantUser que provoco el snapshot (para auditoria).</summary>
    public Guid? CreatedByTenantUserId { get; set; }
}
