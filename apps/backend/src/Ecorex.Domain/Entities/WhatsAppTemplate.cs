using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Plantilla HSM de WhatsApp (mensaje plantilla que Meta debe aprobar). Portada del backbone
/// CUBOT.travels y adaptada a ECOREX. Entidad TENANT-SCOPED. Distinta de <see cref="MessageTemplate"/>
/// (pregrabados internos de texto libre): estas tienen categoria, idioma, variables y un ciclo de
/// aprobacion (Draft -> Submitted -> Approved/Rejected).
///
/// El <see cref="BodyText"/> se guarda en formato EDITABLE con tokens amigables ({{cliente}},
/// {{empresa}}, ...). En una fase posterior el servicio lo compilaria a los placeholders
/// posicionales de Meta ({{1}}, {{2}}, ...). En este corte NO hay integracion real con la
/// WhatsApp Cloud API (ver ADR-0029): Submit es un stub que solo cambia el estado.
/// Unica por tenant en (Name, Language).
/// </summary>
public class WhatsAppTemplate : TenantEntity
{
    /// <summary>Nombre tecnico de la plantilla en Meta (minusculas, guion_bajo). Unico por tenant+idioma.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Codigo de idioma de Meta (ej. "es", "es_CO", "en_US").</summary>
    public string Language { get; set; } = "es";

    /// <summary>Categoria de Meta: Marketing, Utility o Authentication.</summary>
    public WhatsAppTemplateCategory Category { get; set; } = WhatsAppTemplateCategory.Utility;

    /// <summary>Tipo de header (en este corte solo texto). Null/None = sin header.</summary>
    public WhatsAppTemplateHeaderType? HeaderType { get; set; }
    public string? HeaderText { get; set; }

    /// <summary>Cuerpo editable con tokens amigables {{empresa}}, {{asesor}}, {{cliente}}, etc.</summary>
    public string BodyText { get; set; } = null!;

    public string? FooterText { get; set; }

    /// <summary>
    /// JSON con las variables usadas y su ejemplo: [{ "token": "cliente", "example": "Juan" }].
    /// jsonb en PostgreSQL, nvarchar(max) en SQL Server (DAL dual).
    /// </summary>
    public string VariablesJson { get; set; } = "[]";

    // === Proveedor / destino del submit ======================================
    /// <summary>Proveedor de la linea (referencia). Null hasta elegir linea.</summary>
    public WhatsAppProvider? Provider { get; set; }

    /// <summary>Linea por la que se someteria (define la WABA y las credenciales). NO ACTION.</summary>
    public Guid WhatsAppLineId { get; set; }
    public WhatsAppLine? WhatsAppLine { get; set; }

    public string? WabaId { get; set; }

    // === Estado de aprobacion ================================================
    /// <summary>Draft / Submitted / Approved / Rejected / Paused / Disabled.</summary>
    public WhatsAppTemplateStatus Status { get; set; } = WhatsAppTemplateStatus.Draft;

    /// <summary>Id de la plantilla en el proveedor/Meta (tras someter; hoy sin integracion real).</summary>
    public string? ProviderTemplateId { get; set; }
    public string? RejectionReason { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }

    /// <summary>Archivada (soft-delete): no se borra fisicamente, se marca inactiva.</summary>
    public bool IsActive { get; set; } = true;
}
