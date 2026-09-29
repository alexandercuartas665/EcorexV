using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.PlantillasDocumento;

/// <summary>
/// Implementacion de <see cref="IDocumentTemplateService"/> (catalogo de plantillas de documento).
/// Aislamiento por tenant via filtro global (nunca se filtra a mano por TenantId); el alta estampa
/// el TenantId del contexto. La baja es logica (IsActive). Eliminar un grupo remueve el grupo y sus
/// plantillas en un unico SaveChanges (cascada por FK).
/// </summary>
public sealed class DocumentTemplateService : IDocumentTemplateService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public DocumentTemplateService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    // ---- Grupos ----

    public async Task<IReadOnlyList<DocumentTemplateGroupDto>> ListGroupsAsync(
        bool includeInactive = true, CancellationToken cancellationToken = default)
    {
        var groups = await _db.DocumentTemplateGroups.AsNoTracking()
            .Where(g => includeInactive || g.IsActive)
            .OrderBy(g => g.SortOrder).ThenBy(g => g.Name)
            .ToListAsync(cancellationToken);

        var counts = await _db.DocumentTemplates.AsNoTracking()
            .GroupBy(t => t.GroupId)
            .Select(g => new { GroupId = g.Key, Total = g.Count(), Activas = g.Count(t => t.IsActive) })
            .ToListAsync(cancellationToken);
        var byGroup = counts.ToDictionary(x => x.GroupId);

        return groups.Select(g =>
        {
            byGroup.TryGetValue(g.Id, out var k);
            return new DocumentTemplateGroupDto(
                g.Id, g.Name, g.Description, g.IsActive, g.SortOrder, k?.Activas ?? 0, k?.Total ?? 0, g.HeaderHtml);
        }).ToList();
    }

    public async Task<TaskCoreResult<DocumentTemplateGroupDto>> CreateGroupAsync(
        SaveDocumentTemplateGroupRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
        {
            return TaskCoreResult<DocumentTemplateGroupDto>.Invalid("No hay tenant activo.");
        }
        var name = (request.Name ?? "").Trim();
        if (name.Length == 0)
        {
            return TaskCoreResult<DocumentTemplateGroupDto>.Invalid("El nombre es obligatorio.");
        }
        if (name.Length > 150)
        {
            return TaskCoreResult<DocumentTemplateGroupDto>.Invalid("El nombre no puede superar 150 caracteres.");
        }
        if (await _db.DocumentTemplateGroups.AnyAsync(g => g.Name == name, cancellationToken))
        {
            return TaskCoreResult<DocumentTemplateGroupDto>.Invalid("Ya existe un grupo con ese nombre.");
        }

        var sortOrder = request.SortOrder
            ?? (await _db.DocumentTemplateGroups.Select(g => (int?)g.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var entity = new DocumentTemplateGroup
        {
            TenantId = tenantId,
            Name = name,
            Description = Normalize(request.Description),
            SortOrder = sortOrder
        };
        _db.DocumentTemplateGroups.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<DocumentTemplateGroupDto>.Ok(new DocumentTemplateGroupDto(
            entity.Id, entity.Name, entity.Description, entity.IsActive, entity.SortOrder, 0, 0));
    }

    public async Task<TaskCoreResult<DocumentTemplateGroupDto>> UpdateGroupAsync(
        Guid groupId, SaveDocumentTemplateGroupRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await _db.DocumentTemplateGroups.FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
        if (entity is null)
        {
            return TaskCoreResult<DocumentTemplateGroupDto>.NotFound("El grupo no existe.");
        }
        var name = (request.Name ?? "").Trim();
        if (name.Length == 0)
        {
            return TaskCoreResult<DocumentTemplateGroupDto>.Invalid("El nombre es obligatorio.");
        }
        if (name.Length > 150)
        {
            return TaskCoreResult<DocumentTemplateGroupDto>.Invalid("El nombre no puede superar 150 caracteres.");
        }
        if (await _db.DocumentTemplateGroups.AnyAsync(g => g.Name == name && g.Id != groupId, cancellationToken))
        {
            return TaskCoreResult<DocumentTemplateGroupDto>.Invalid("Ya existe un grupo con ese nombre.");
        }

        entity.Name = name;
        entity.Description = Normalize(request.Description);
        if (request.SortOrder is int so) { entity.SortOrder = so; }
        await _db.SaveChangesAsync(cancellationToken);

        var total = await _db.DocumentTemplates.CountAsync(t => t.GroupId == groupId, cancellationToken);
        var activas = await _db.DocumentTemplates.CountAsync(t => t.GroupId == groupId && t.IsActive, cancellationToken);
        return TaskCoreResult<DocumentTemplateGroupDto>.Ok(new DocumentTemplateGroupDto(
            entity.Id, entity.Name, entity.Description, entity.IsActive, entity.SortOrder, activas, total, entity.HeaderHtml));
    }

    public async Task<TaskCoreResult<DocumentTemplateGroupDto>> SetGroupActiveAsync(
        Guid groupId, bool active, CancellationToken cancellationToken = default)
    {
        var entity = await _db.DocumentTemplateGroups.FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
        if (entity is null)
        {
            return TaskCoreResult<DocumentTemplateGroupDto>.NotFound("El grupo no existe.");
        }
        if (entity.IsActive == active)
        {
            return TaskCoreResult<DocumentTemplateGroupDto>.Invalid(active
                ? "El grupo ya esta activo."
                : "El grupo ya esta inactivo.");
        }
        entity.IsActive = active;
        await _db.SaveChangesAsync(cancellationToken);
        var total = await _db.DocumentTemplates.CountAsync(t => t.GroupId == groupId, cancellationToken);
        var activas = await _db.DocumentTemplates.CountAsync(t => t.GroupId == groupId && t.IsActive, cancellationToken);
        return TaskCoreResult<DocumentTemplateGroupDto>.Ok(new DocumentTemplateGroupDto(
            entity.Id, entity.Name, entity.Description, entity.IsActive, entity.SortOrder, activas, total, entity.HeaderHtml));
    }

    public async Task<TaskCoreResult<DocumentTemplateGroupDto>> SetGroupHeaderHtmlAsync(
        Guid groupId, string? headerHtml, CancellationToken cancellationToken = default)
    {
        var entity = await _db.DocumentTemplateGroups.FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
        if (entity is null)
        {
            return TaskCoreResult<DocumentTemplateGroupDto>.NotFound("El grupo no existe.");
        }
        entity.HeaderHtml = string.IsNullOrWhiteSpace(headerHtml) ? null : headerHtml;
        await _db.SaveChangesAsync(cancellationToken);
        var total = await _db.DocumentTemplates.CountAsync(t => t.GroupId == groupId, cancellationToken);
        var activas = await _db.DocumentTemplates.CountAsync(t => t.GroupId == groupId && t.IsActive, cancellationToken);
        return TaskCoreResult<DocumentTemplateGroupDto>.Ok(new DocumentTemplateGroupDto(
            entity.Id, entity.Name, entity.Description, entity.IsActive, entity.SortOrder, activas, total, entity.HeaderHtml));
    }

    public async Task<TaskCoreResult<bool>> DeleteGroupAsync(
        Guid groupId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.DocumentTemplateGroups
            .Include(g => g.Templates)
            .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
        if (entity is null)
        {
            return TaskCoreResult<bool>.NotFound("El grupo no existe.");
        }
        // El grupo puede estar en uso por conceptos (000270). La FK concepto->grupo es NO ACTION,
        // asi que se bloquea el borrado si hay conceptos que lo referencian (evita dejarlos huerfanos).
        if (await _db.ActividadSubcategoriaPlantillaGrupos.AnyAsync(x => x.GroupId == groupId, cancellationToken))
        {
            return TaskCoreResult<bool>.Invalid(
                "El grupo esta asignado a uno o mas conceptos. Quitalo de los conceptos antes de borrarlo.");
        }

        // Borrado del grupo + sus plantillas en el mismo SaveChanges (multi-tabla atomica).
        _db.DocumentTemplates.RemoveRange(entity.Templates);
        _db.DocumentTemplateGroups.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<bool>.Ok(true);
    }

    // ---- Plantillas ----

    public async Task<IReadOnlyList<DocumentTemplateDto>> ListTemplatesAsync(
        Guid? groupId = null, bool includeInactive = true, CancellationToken cancellationToken = default)
    {
        var query = _db.DocumentTemplates.AsNoTracking().AsQueryable();
        if (groupId is Guid gid) { query = query.Where(t => t.GroupId == gid); }
        if (!includeInactive) { query = query.Where(t => t.IsActive); }
        var rows = await query
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public async Task<DocumentTemplateDto?> GetTemplateAsync(
        Guid templateId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.DocumentTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<TaskCoreResult<DocumentTemplateDto>> CreateTemplateAsync(
        SaveDocumentTemplateRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
        {
            return TaskCoreResult<DocumentTemplateDto>.Invalid("No hay tenant activo.");
        }
        var error = await ValidateTemplateAsync(request, cancellationToken);
        if (error is string e)
        {
            return TaskCoreResult<DocumentTemplateDto>.Invalid(e);
        }

        var sortOrder = request.SortOrder
            ?? (await _db.DocumentTemplates.Where(t => t.GroupId == request.GroupId)
                    .Select(t => (int?)t.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var entity = new DocumentTemplate
        {
            TenantId = tenantId,
            GroupId = request.GroupId,
            Name = (request.Name ?? "").Trim(),
            HtmlContent = request.HtmlContent ?? "",
            SortOrder = sortOrder
        };
        _db.DocumentTemplates.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<DocumentTemplateDto>.Ok(ToDto(entity));
    }

    public async Task<TaskCoreResult<DocumentTemplateDto>> UpdateTemplateAsync(
        Guid templateId, SaveDocumentTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await _db.DocumentTemplates.FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);
        if (entity is null)
        {
            return TaskCoreResult<DocumentTemplateDto>.NotFound("La plantilla no existe.");
        }
        var error = await ValidateTemplateAsync(request, cancellationToken);
        if (error is string e)
        {
            return TaskCoreResult<DocumentTemplateDto>.Invalid(e);
        }

        entity.GroupId = request.GroupId;
        entity.Name = (request.Name ?? "").Trim();
        entity.HtmlContent = request.HtmlContent ?? "";
        if (request.SortOrder is int so) { entity.SortOrder = so; }
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<DocumentTemplateDto>.Ok(ToDto(entity));
    }

    public async Task<TaskCoreResult<DocumentTemplateDto>> SetTemplateActiveAsync(
        Guid templateId, bool active, CancellationToken cancellationToken = default)
    {
        var entity = await _db.DocumentTemplates.FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);
        if (entity is null)
        {
            return TaskCoreResult<DocumentTemplateDto>.NotFound("La plantilla no existe.");
        }
        if (entity.IsActive == active)
        {
            return TaskCoreResult<DocumentTemplateDto>.Invalid(active
                ? "La plantilla ya esta activa."
                : "La plantilla ya esta inactiva.");
        }
        entity.IsActive = active;
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<DocumentTemplateDto>.Ok(ToDto(entity));
    }

    public async Task<TaskCoreResult<bool>> DeleteTemplateAsync(
        Guid templateId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.DocumentTemplates.FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);
        if (entity is null)
        {
            return TaskCoreResult<bool>.NotFound("La plantilla no existe.");
        }
        _db.DocumentTemplates.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<bool>.Ok(true);
    }

    public IReadOnlyList<PlantillaTokenDto> GetTokenCatalog() => TokenCatalog;

    // ---- Helpers ----

    private async Task<string?> ValidateTemplateAsync(
        SaveDocumentTemplateRequest request, CancellationToken cancellationToken)
    {
        var name = (request.Name ?? "").Trim();
        if (name.Length == 0) { return "El nombre es obligatorio."; }
        if (name.Length > 200) { return "El nombre no puede superar 200 caracteres."; }
        if (!await _db.DocumentTemplateGroups.AnyAsync(g => g.Id == request.GroupId, cancellationToken))
        {
            return "El grupo destino no existe.";
        }
        return null;
    }

    private static DocumentTemplateDto ToDto(DocumentTemplate t) => new(
        t.Id, t.GroupId, t.Name, t.HtmlContent, t.IsActive, t.SortOrder);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Catalogo estatico de tokens que resuelve INotifyTokenResolver con el contexto de la tarea.
    /// Se muestra como paleta en el editor para insertar {ns.clave} en la plantilla.
    /// </summary>
    private static readonly IReadOnlyList<PlantillaTokenDto> TokenCatalog = new List<PlantillaTokenDto>
    {
        new("{tarea.numero}", "Numero de la tarea (T00123)", "Tarea"),
        new("{tarea.titulo}", "Titulo de la tarea", "Tarea"),
        new("{tarea.cliente}", "Cliente/empresa de la tarea", "Tarea"),
        new("{tarea.contacto}", "Contacto de la tarea", "Tarea"),
        new("{tarea.solicitante}", "Solicitante", "Tarea"),
        new("{tarea.email}", "Correo del contacto", "Tarea"),
        new("{tarea.telefono}", "Telefono del contacto", "Tarea"),
        new("{tarea.celular}", "Celular del contacto", "Tarea"),
        new("{tarea.documento}", "Documento/identificacion del contacto", "Tarea"),
        new("{tarea.nit}", "NIT del contacto", "Tarea"),
        // Empresa (Entidad principal del tenant): utiles sobre todo en el MEMBRETE del grupo.
        new("{empresa.razonsocial}", "Razon social / nombre de la empresa", "Empresa"),
        new("{empresa.nombrecomercial}", "Nombre comercial", "Empresa"),
        new("{empresa.sigla}", "Sigla", "Empresa"),
        new("{empresa.nit}", "NIT / Tax ID (con DV si existe)", "Empresa"),
        new("{empresa.direccion}", "Direccion", "Empresa"),
        new("{empresa.ciudad}", "Ciudad", "Empresa"),
        new("{empresa.telefono}", "Telefono", "Empresa"),
        new("{empresa.email}", "Correo", "Empresa"),
        new("{empresa.web}", "Sitio web", "Empresa"),
        new("{empresa.logo}", "Logo (usar como <img src=\"{empresa.logo}\">)", "Empresa"),
        new("{sistema.fecha}", "Fecha actual (zona del tenant)", "Sistema"),
        new("{sistema.hora}", "Hora actual", "Sistema"),
        new("{sistema.fechahora}", "Fecha y hora actual", "Sistema"),
        new("{form.CODIGO}", "Respuesta del formulario por codigo de campo", "Formulario"),
        // Tercero del Directorio enlazado a la tarea (si se eligio del lookup al crearla). {directorio.*}
        // es alias de {tercero.*}; ademas admite campos de las fichas, p.ej. {directorio.direccion}.
        new("{tercero.nombre}", "Nombre/razon social del tercero", "Tercero / Directorio"),
        new("{tercero.identificacion}", "Identificacion (NIT/cedula) del tercero", "Tercero / Directorio"),
        new("{tercero.email}", "Correo del tercero", "Tercero / Directorio"),
        new("{tercero.telefono}", "Telefono del tercero", "Tercero / Directorio"),
        new("{tercero.ciudad}", "Ciudad del tercero", "Tercero / Directorio"),
        new("{directorio.direccion}", "Direccion (campo de ficha del Directorio)", "Tercero / Directorio"),
    };
}
