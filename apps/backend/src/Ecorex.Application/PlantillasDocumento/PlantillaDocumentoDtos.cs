namespace Ecorex.Application.PlantillasDocumento;

/// <summary>
/// Grupo (categoria) de plantillas de documento del tenant, con el conteo de plantillas para las
/// tarjetas de la pagina de configuracion.
/// </summary>
public sealed record DocumentTemplateGroupDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    int SortOrder,
    int TemplatesActivas,
    int TemplatesTotales,
    string? HeaderHtml = null);

/// <summary>Plantilla de documento (HTML con tokens {ns.clave}).</summary>
public sealed record DocumentTemplateDto(
    Guid Id,
    Guid GroupId,
    string Name,
    string HtmlContent,
    bool IsActive,
    int SortOrder);

/// <summary>Alta/edicion de un grupo de plantillas.</summary>
public sealed record SaveDocumentTemplateGroupRequest(
    string Name,
    string? Description = null,
    int? SortOrder = null);

/// <summary>Alta/edicion de una plantilla de documento.</summary>
public sealed record SaveDocumentTemplateRequest(
    Guid GroupId,
    string Name,
    string HtmlContent,
    int? SortOrder = null);

/// <summary>Token disponible para insertar en el editor de la plantilla (paleta de tokens).</summary>
public sealed record PlantillaTokenDto(string Token, string Descripcion, string Grupo);
