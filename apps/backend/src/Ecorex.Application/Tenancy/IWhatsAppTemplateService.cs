namespace Ecorex.Application.Tenancy;

/// <summary>
/// Servicio del modulo de plantillas HSM de WhatsApp (tenant-scoped). CRUD con resultados
/// tipados. Submit y SyncStatus son STUBS: en este corte NO hay integracion real con la
/// WhatsApp Cloud API de Meta (deuda documentada, ADR-0029). Submit solo cambia el estado a
/// Submitted; SyncStatus es no-op.
/// </summary>
public interface IWhatsAppTemplateService
{
    Task<IReadOnlyList<WhatsAppTemplateDto>> ListAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<WhatsAppTemplateDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Crea un borrador (Status = Draft). No contacta a ningun proveedor.</summary>
    Task<WhatsAppTemplateResult<WhatsAppTemplateDto>> CreateAsync(SaveWhatsAppTemplateRequest request, CancellationToken cancellationToken = default);

    /// <summary>Actualiza una plantilla. Solo se permite editar mientras esta en Draft o Rejected.</summary>
    Task<WhatsAppTemplateResult<WhatsAppTemplateDto>> UpdateAsync(Guid id, SaveWhatsAppTemplateRequest request, CancellationToken cancellationToken = default);

    /// <summary>Archiva/restaura la plantilla (soft-delete; no hay DELETE fisico).</summary>
    Task<WhatsAppTemplateResult<bool>> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default);

    /// <summary>
    /// STUB (ADR-0029): marca la plantilla como Submitted + SubmittedAt=now y escribe auditoria.
    /// NO hay llamada real a Meta/WhatsApp Cloud API. Solo transiciona desde Draft o Rejected.
    /// </summary>
    Task<WhatsAppTemplateResult<WhatsAppTemplateDto>> SubmitAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// STUB (ADR-0029): la sincronizacion de estado con el proveedor no esta implementada (no hay
    /// integracion real con Meta). Devuelve un resultado NotImplemented sin tocar la plantilla.
    /// </summary>
    Task<WhatsAppTemplateResult<bool>> SyncStatusAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Trae (importa) al tenant las plantillas HSM que ya existen en la cuenta de YCloud de la
    /// linea indicada (debe ser una linea con Provider=YCloud y API key + WABA configurados). Hace UPSERT
    /// por (Nombre, Idioma): crea las nuevas, actualiza las existentes con lo que trae el proveedor y deja
    /// las variables POSICIONALES ({{1}}...). No borra nada. Devuelve el conteo por resultado.</summary>
    Task<WhatsAppTemplateResult<WhatsAppImportReport>> ImportFromYCloudAsync(Guid whatsAppLineId, CancellationToken cancellationToken = default);

    /// <summary>Catalogo de variables que el editor puede insertar.</summary>
    IReadOnlyList<WhatsAppTemplateVariableDef> Catalog();
}
