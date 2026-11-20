namespace Ecorex.Application.Tenancy;

/// <summary>
/// Servicio del modulo de plantillas HSM de WhatsApp (tenant-scoped). CRUD con resultados
/// tipados. Con lineas YCloud, Submit crea la plantilla de verdad en Meta y SyncStatus/SyncPending
/// traen el estado de aprobacion (ADR-0184); con Cloud/Emulator siguen siendo transiciones locales.
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

    /// <summary>Envio de PRUEBA de la plantilla a un numero. <paramref name="values"/> = valores de las variables
    /// EN ORDEN (los que teclee el usuario); si es null/vacio se usan los ejemplos. Manda por la linea de la
    /// plantilla (YCloud requiere Aprobada; Evolution la renderiza a texto/media al vuelo).</summary>
    Task<WhatsAppTemplateResult<bool>> TestSendAsync(Guid id, string phone, IReadOnlyList<string>? values = null, string? headerMediaUrl = null, IReadOnlyList<WhatsAppTestButtonValue>? buttonValues = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consulta en YCloud/Meta el estado de aprobacion de UNA plantilla (ADR-0184) y lo reconcilia (estado,
    /// motivo de rechazo, id del proveedor). No toca el contenido. Solo aplica a plantillas de lineas YCloud.
    /// </summary>
    Task<WhatsAppTemplateResult<WhatsAppStatusSyncReport>> SyncStatusAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sincroniza TODAS las plantillas del tenant activo que estan en revision (Submitted) en lineas YCloud
    /// (ADR-0184). Una llamada de listado por linea. La usa el worker periodico y el boton de la pantalla.
    /// </summary>
    Task<WhatsAppTemplateResult<WhatsAppStatusSyncReport>> SyncPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>Barrido de PLATAFORMA (solo ids): tenants con alguna plantilla YCloud en revision. Lo usa el
    /// worker antes de fijar el tenant ambiente; ignora el filtro global a proposito.</summary>
    Task<IReadOnlyList<Guid>> FindTenantsWithPendingTemplatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Trae (importa) al tenant las plantillas HSM que ya existen en la cuenta de YCloud de la
    /// linea indicada (debe ser una linea con Provider=YCloud y API key + WABA configurados). Hace UPSERT
    /// por (Nombre, Idioma): crea las nuevas, actualiza las existentes con lo que trae el proveedor y deja
    /// las variables POSICIONALES ({{1}}...). No borra nada. Devuelve el conteo por resultado.</summary>
    Task<WhatsAppTemplateResult<WhatsAppImportReport>> ImportFromYCloudAsync(Guid whatsAppLineId, CancellationToken cancellationToken = default);

    /// <summary>Catalogo de variables que el editor puede insertar.</summary>
    IReadOnlyList<WhatsAppTemplateVariableDef> Catalog();
}
