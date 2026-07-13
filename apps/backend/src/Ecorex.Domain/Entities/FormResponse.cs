using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Respuesta de un formulario dinamico como DOCUMENTO JSON (ADR-0015: se abandona el EAV
/// por-fila del legacy). Data = { fieldCode: { value, type } } en jsonb (PG) / nvarchar(max)
/// (SQL Server), mismo patron dual de TaskItem.CcEmails. Reference ancla la respuesta a un
/// caso externo (ej. numero de TaskItem). TENANT-SCOPED, concurrencia optimista portable.
/// </summary>
public class FormResponse : TenantEntity, IVersioned
{
    public Guid DefinitionId { get; set; }
    public FormDefinition? Definition { get; set; }

    /// <summary>Referencia externa (ej. numero de tarea "T00042"). Null = respuesta suelta.</summary>
    public string? Reference { get; set; }

    public FormResponseStatus Status { get; set; } = FormResponseStatus.Draft;

    /// <summary>Documento JSON { fieldCode: { value, type } } (jsonb / nvarchar(max) segun motor).</summary>
    public string Data { get; set; } = "{}";

    public DateTimeOffset? SubmittedAt { get; set; }

    public Guid? SubmittedByTenantUserId { get; set; }

    /// <summary>Token de concurrencia optimista portable (lo incrementa el interceptor).</summary>
    public long Version { get; set; }

    // ---- Registro transaccional (Formularios avanzados, ola F3; doc 01 D2). Solo aplica cuando
    // la definicion es transaccional; INDEPENDIENTE de Status (Draft/Submitted, ciclo de envio). ----

    /// <summary>Numero/clave del registro (consecutivo o clave natural). Null hasta confirmar.</summary>
    public string? RecordNumber { get; set; }

    /// <summary>Ciclo del registro: Draft -> Confirmed -> Voided (anular no borra ni libera el numero).</summary>
    public FormRecordStatus RecordStatus { get; set; } = FormRecordStatus.Draft;

    /// <summary>Fecha del hecho (por sistema al confirmar; los campos fecha del formulario son aparte).</summary>
    public DateTimeOffset? TransactionDate { get; set; }

    public DateTimeOffset? VoidedAt { get; set; }
    public Guid? VoidedByTenantUserId { get; set; }
    public string? VoidReason { get; set; }
}
