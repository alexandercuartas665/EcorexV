using Ecorex.Application.Tenancy;

namespace Ecorex.Application.PlantillasDocumento;

/// <summary>
/// Catalogo de plantillas de documento del tenant activo (configuracion): grupos (categorias) y
/// sus plantillas (HTML con tokens {ns.clave}). Aislamiento por tenant via filtro global (nunca se
/// filtra a mano por TenantId); el alta estampa el TenantId del contexto. La baja es logica
/// (IsActive); eliminar un grupo borra sus plantillas en cascada. Alimenta el editor de documentos
/// de una tarea (Gestor Documental) y el picker "Plantillas de documento" del concepto (000270).
/// Reusa <see cref="TaskCoreResult{T}"/> como resultado tipado del nucleo.
/// </summary>
public interface IDocumentTemplateService
{
    // ---- Grupos ----
    Task<IReadOnlyList<DocumentTemplateGroupDto>> ListGroupsAsync(
        bool includeInactive = true, CancellationToken cancellationToken = default);

    Task<TaskCoreResult<DocumentTemplateGroupDto>> CreateGroupAsync(
        SaveDocumentTemplateGroupRequest request, CancellationToken cancellationToken = default);

    Task<TaskCoreResult<DocumentTemplateGroupDto>> UpdateGroupAsync(
        Guid groupId, SaveDocumentTemplateGroupRequest request, CancellationToken cancellationToken = default);

    Task<TaskCoreResult<DocumentTemplateGroupDto>> SetGroupActiveAsync(
        Guid groupId, bool active, CancellationToken cancellationToken = default);

    Task<TaskCoreResult<bool>> DeleteGroupAsync(
        Guid groupId, CancellationToken cancellationToken = default);

    // ---- Plantillas ----
    Task<IReadOnlyList<DocumentTemplateDto>> ListTemplatesAsync(
        Guid? groupId = null, bool includeInactive = true, CancellationToken cancellationToken = default);

    Task<DocumentTemplateDto?> GetTemplateAsync(
        Guid templateId, CancellationToken cancellationToken = default);

    Task<TaskCoreResult<DocumentTemplateDto>> CreateTemplateAsync(
        SaveDocumentTemplateRequest request, CancellationToken cancellationToken = default);

    Task<TaskCoreResult<DocumentTemplateDto>> UpdateTemplateAsync(
        Guid templateId, SaveDocumentTemplateRequest request, CancellationToken cancellationToken = default);

    Task<TaskCoreResult<DocumentTemplateDto>> SetTemplateActiveAsync(
        Guid templateId, bool active, CancellationToken cancellationToken = default);

    Task<TaskCoreResult<bool>> DeleteTemplateAsync(
        Guid templateId, CancellationToken cancellationToken = default);

    /// <summary>Catalogo de tokens disponibles para la paleta del editor (tarea/contacto/sistema/form).</summary>
    IReadOnlyList<PlantillaTokenDto> GetTokenCatalog();
}
