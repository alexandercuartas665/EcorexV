using Ecorex.Application.Tenancy;

namespace Ecorex.Application.PlantillasDocumento;

/// <summary>
/// Puente TAREA &lt;-&gt; Gestor Documental (ola 2 de plantillas de documento): permite redactar un
/// documento de una tarea a partir de una plantilla habilitada por su concepto (000270), resolviendo
/// los tokens con el contexto real de la tarea (<see cref="Workflows.INotifyTokenResolver"/>), y lo
/// guarda como <c>Documento</c> versionado en el Gestor Documental (Origen = Tarea). NO envia correos:
/// solo genera el documento y sostiene sus versiones; la tarea muestra la version vigente (o la que el
/// usuario active). Aislamiento por tenant via filtro global. Reusa <see cref="TaskCoreResult{T}"/>.
/// </summary>
public interface ITaskDocumentComposerService
{
    /// <summary>Plantillas activas de los grupos habilitados por el concepto de la tarea (vacio si el
    /// concepto no ofrece grupos o la tarea no tiene concepto).</summary>
    Task<IReadOnlyList<TaskDocTemplateDto>> ListPlantillasAsync(
        Guid taskId, CancellationToken cancellationToken = default);

    /// <summary>Resuelve el HTML de la plantilla con los tokens de la tarea (texto listo para editar).</summary>
    Task<TaskCoreResult<string>> RenderPlantillaAsync(
        Guid taskId, Guid templateId, CancellationToken cancellationToken = default);

    /// <summary>Documentos (Gestor Documental) nacidos de esta tarea, con su version vigente.</summary>
    Task<IReadOnlyList<TaskDocumentoDto>> ListDocumentosAsync(
        Guid taskId, CancellationToken cancellationToken = default);

    /// <summary>Historial de versiones de un documento de tarea (para activar una version).</summary>
    Task<IReadOnlyList<TaskDocumentoVersionDto>> ListVersionesAsync(
        Guid documentoId, CancellationToken cancellationToken = default);

    /// <summary>HTML de una version (o la vigente si versionId es null), para reabrir en el editor.</summary>
    Task<TaskCoreResult<string>> GetVersionHtmlAsync(
        Guid documentoId, Guid? versionId = null, CancellationToken cancellationToken = default);

    /// <summary>Crea el documento (version 1) desde el HTML redactado. Devuelve el id del documento.
    /// Si <paramref name="grupoId"/> tiene membrete, se resuelve con los tokens de la tarea y se CONGELA
    /// en el documento (se antepondra al generar el PDF).</summary>
    Task<TaskCoreResult<Guid>> GuardarNuevoAsync(
        Guid taskId, string titulo, string html, Guid? grupoId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Arma el HTML imprimible de un documento (membrete congelado + cuerpo de la version) como pagina
    /// standalone lista para renderizar a PDF. Devuelve tambien un nombre de archivo amigable. Se acota
    /// al TenantId del propio documento (via su id): pensado para un endpoint sin contexto de tenant.
    /// </summary>
    Task<TaskCoreResult<TaskDocumentoPrintDto>> BuildPrintHtmlAsync(
        Guid documentoId, Guid? versionId = null, CancellationToken cancellationToken = default);

    /// <summary>Agrega una version nueva a un documento existente y la deja vigente.</summary>
    Task<TaskCoreResult<Guid>> GuardarNuevaVersionAsync(
        Guid documentoId, string html, string? notas, CancellationToken cancellationToken = default);

    /// <summary>Fija como vigente una version ya existente (activar version).</summary>
    Task<TaskCoreResult<bool>> ActivarVersionAsync(
        Guid documentoId, Guid versionId, CancellationToken cancellationToken = default);

    /// <summary>Elimina (soft-delete) un documento de tarea.</summary>
    Task<TaskCoreResult<bool>> EliminarAsync(
        Guid documentoId, CancellationToken cancellationToken = default);
}
