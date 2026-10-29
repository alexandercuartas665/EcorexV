using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Auditoria INMUTABLE y TENANT-SCOPED de una respuesta de formulario (ADR integridad de respuestas):
/// registra quien/cuando/que accion sobre una FormResponse, con el SNAPSHOT del jsonb anterior cuando
/// hay riesgo de perdida (borrado, anulacion) o del estado al enviar/generar PDF. Append-only: nunca se
/// actualiza ni se borra. CreatedAt (de BaseEntity) es la marca de tiempo del evento.
/// </summary>
public class FormResponseAudit : TenantEntity
{
    /// <summary>Respuesta auditada. Sin FK de navegacion dura: la respuesta puede soft-borrarse y el audit sobrevive.</summary>
    public Guid FormResponseId { get; set; }

    public Guid DefinitionId { get; set; }

    /// <summary>Reference de la respuesta al momento del evento (ej. numero de tarea "T00521").</summary>
    public string? Reference { get; set; }

    public FormResponseAuditAction Action { get; set; }

    /// <summary>Usuario del tenant que ejecuto la accion (null si fue un proceso/sistema).</summary>
    public Guid? ActorTenantUserId { get; set; }

    /// <summary>Agente IA que ejecuto la accion, si aplica.</summary>
    public Guid? ActorAiAgentId { get; set; }

    /// <summary>
    /// Snapshot del jsonb de la respuesta ANTERIOR a la accion (Delete/Void/Update) o del estado enviado
    /// (Submit/PdfSnapshot). Null en Create (nace vacio). Es lo que permite reconstruir datos perdidos.
    /// </summary>
    public string? DataSnapshot { get; set; }

    /// <summary>Nota libre (p.ej. motivo de anulacion, origen del snapshot).</summary>
    public string? Note { get; set; }
}
