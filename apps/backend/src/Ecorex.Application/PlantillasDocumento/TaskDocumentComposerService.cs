using System.Text;
using Ecorex.Application.Common;
using Ecorex.Application.Documentos;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.PlantillasDocumento;

/// <summary>
/// Implementacion del puente TAREA &lt;-&gt; Gestor Documental (ver <see cref="ITaskDocumentComposerService"/>).
/// El documento se guarda como HTML (text/html) versionado en el Gestor Documental para que sea
/// re-editable (una version nueva parte del HTML de la vigente). Aislamiento por tenant via filtro
/// global; el alta la hace <see cref="IDocumentoService"/> (estampa usuario/tenant y audita).
/// </summary>
public sealed class TaskDocumentComposerService : ITaskDocumentComposerService
{
    /// <summary>Categoria del Gestor Documental donde caen los documentos generados desde tareas.</summary>
    private const string CategoriaTareasNombre = "Documentos de tareas";

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IDocumentoService _docs;
    private readonly IDocumentoFileStore _files;
    private readonly INotifyTokenResolver _tokens;

    public TaskDocumentComposerService(
        IApplicationDbContext db,
        ITenantContext tenant,
        IDocumentoService docs,
        IDocumentoFileStore files,
        INotifyTokenResolver tokens)
    {
        _db = db;
        _tenant = tenant;
        _docs = docs;
        _files = files;
        _tokens = tokens;
    }

    public async Task<IReadOnlyList<TaskDocTemplateDto>> ListPlantillasAsync(
        Guid taskId, CancellationToken cancellationToken = default)
    {
        var subId = await _db.TaskItems.AsNoTracking()
            .Where(t => t.Id == taskId).Select(t => t.SubcategoriaId).FirstOrDefaultAsync(cancellationToken);
        if (subId is not Guid sub) { return Array.Empty<TaskDocTemplateDto>(); }

        var groupIds = await _db.ActividadSubcategoriaPlantillaGrupos.AsNoTracking()
            .Where(x => x.SubcategoriaId == sub).Select(x => x.GroupId).ToListAsync(cancellationToken);
        if (groupIds.Count == 0) { return Array.Empty<TaskDocTemplateDto>(); }

        // Solo grupos y plantillas ACTIVAS. Orden: por grupo (SortOrder/Nombre) y luego plantilla.
        var groups = await _db.DocumentTemplateGroups.AsNoTracking()
            .Where(g => groupIds.Contains(g.Id) && g.IsActive)
            .OrderBy(g => g.SortOrder).ThenBy(g => g.Name)
            .Select(g => new { g.Id, g.Name })
            .ToListAsync(cancellationToken);
        var groupById = groups.ToDictionary(g => g.Id, g => g.Name);
        var activeGroupIds = groups.Select(g => g.Id).ToList();

        var templates = await _db.DocumentTemplates.AsNoTracking()
            .Where(t => activeGroupIds.Contains(t.GroupId) && t.IsActive)
            .OrderBy(t => t.GroupId).ThenBy(t => t.SortOrder).ThenBy(t => t.Name)
            .Select(t => new { t.Id, t.GroupId, t.Name })
            .ToListAsync(cancellationToken);

        return templates
            .Select(t => new TaskDocTemplateDto(
                t.Id, t.GroupId, groupById.TryGetValue(t.GroupId, out var gn) ? gn : "", t.Name))
            .ToList();
    }

    public async Task<TaskCoreResult<string>> RenderPlantillaAsync(
        Guid taskId, Guid templateId, CancellationToken cancellationToken = default)
    {
        var task = await _db.TaskItems.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null) { return TaskCoreResult<string>.NotFound("La tarea no existe."); }

        var template = await _db.DocumentTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == templateId && t.IsActive, cancellationToken);
        if (template is null) { return TaskCoreResult<string>.NotFound("La plantilla no existe o esta inactiva."); }

        var tokens = await _tokens.BuildAsync(task, cancellationToken);
        var html = _tokens.Render(template.HtmlContent, tokens);
        return TaskCoreResult<string>.Ok(html);
    }

    public async Task<IReadOnlyList<TaskDocumentoDto>> ListDocumentosAsync(
        Guid taskId, CancellationToken cancellationToken = default)
    {
        var docs = await _db.Documentos.AsNoTracking()
            .Where(d => d.Origen == OrigenDocumento.Tarea && d.OrigenEntidadId == taskId && d.Activo)
            .OrderByDescending(d => d.UpdatedAt ?? d.CreatedAt)
            .Select(d => new { d.Id, d.Titulo, d.NumeroVersiones, d.VersionActualId, d.UpdatedAt, d.CreatedAt })
            .ToListAsync(cancellationToken);
        if (docs.Count == 0) { return Array.Empty<TaskDocumentoDto>(); }

        var versionIds = docs.Where(d => d.VersionActualId is Guid).Select(d => d.VersionActualId!.Value).ToList();
        var urls = await _db.DocumentoVersiones.AsNoTracking()
            .Where(v => versionIds.Contains(v.Id))
            .Select(v => new { v.Id, v.UrlStorage })
            .ToListAsync(cancellationToken);
        var urlById = urls.ToDictionary(v => v.Id, v => v.UrlStorage);

        return docs.Select(d => new TaskDocumentoDto(
            d.Id, d.Titulo, d.NumeroVersiones, d.VersionActualId,
            d.VersionActualId is Guid vid && urlById.TryGetValue(vid, out var u) ? u : null,
            d.UpdatedAt ?? d.CreatedAt)).ToList();
    }

    public async Task<IReadOnlyList<TaskDocumentoVersionDto>> ListVersionesAsync(
        Guid documentoId, CancellationToken cancellationToken = default)
    {
        var actualId = await _db.Documentos.AsNoTracking()
            .Where(d => d.Id == documentoId).Select(d => d.VersionActualId).FirstOrDefaultAsync(cancellationToken);

        var versiones = await _db.DocumentoVersiones.AsNoTracking()
            .Where(v => v.DocumentoId == documentoId)
            .OrderByDescending(v => v.Numero)
            .Select(v => new { v.Id, v.Numero, v.NotasCambio, v.UrlStorage, v.CreatedAt })
            .ToListAsync(cancellationToken);

        return versiones.Select(v => new TaskDocumentoVersionDto(
            v.Id, v.Numero, v.NotasCambio, v.Id == actualId, v.UrlStorage, v.CreatedAt)).ToList();
    }

    public async Task<TaskCoreResult<string>> GetVersionHtmlAsync(
        Guid documentoId, Guid? versionId = null, CancellationToken cancellationToken = default)
    {
        var doc = await _db.Documentos.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentoId && d.Activo, cancellationToken);
        if (doc is null) { return TaskCoreResult<string>.NotFound("El documento no existe."); }

        var targetId = versionId ?? doc.VersionActualId;
        if (targetId is not Guid vid) { return TaskCoreResult<string>.NotFound("El documento no tiene version."); }

        var version = await _db.DocumentoVersiones.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == vid && v.DocumentoId == documentoId, cancellationToken);
        if (version is null) { return TaskCoreResult<string>.NotFound("La version no existe."); }

        var bytes = await _files.ReadAsync(version.UrlStorage, cancellationToken);
        if (bytes is null) { return TaskCoreResult<string>.Invalid("El archivo de la version no esta disponible."); }
        return TaskCoreResult<string>.Ok(Encoding.UTF8.GetString(bytes));
    }

    public async Task<TaskCoreResult<TaskDocumentoPrintDto>> BuildPrintHtmlAsync(
        Guid documentoId, Guid? versionId = null, CancellationToken cancellationToken = default)
    {
        // Endpoint SIN contexto de tenant: se ignora el filtro global y se acota por el id del documento
        // (que trae su propio TenantId). Solo se leen ese documento y su version.
        var doc = await _db.Documentos.AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Id == documentoId && d.Activo, cancellationToken);
        if (doc is null) { return TaskCoreResult<TaskDocumentoPrintDto>.NotFound("El documento no existe."); }

        var targetId = versionId ?? doc.VersionActualId;
        if (targetId is not Guid vid) { return TaskCoreResult<TaskDocumentoPrintDto>.NotFound("El documento no tiene version."); }

        var version = await _db.DocumentoVersiones.AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.Id == vid && v.DocumentoId == documentoId, cancellationToken);
        if (version is null) { return TaskCoreResult<TaskDocumentoPrintDto>.NotFound("La version no existe."); }

        var bytes = await _files.ReadAsync(version.UrlStorage, cancellationToken);
        if (bytes is null) { return TaskCoreResult<TaskDocumentoPrintDto>.Invalid("El archivo de la version no esta disponible."); }
        var body = Encoding.UTF8.GetString(bytes);

        var html = BuildPrintableDocument(doc.MembreteHtml, body, doc.Titulo);
        var fileName = Slug(doc.Titulo);
        return TaskCoreResult<TaskDocumentoPrintDto>.Ok(new TaskDocumentoPrintDto(html, fileName));
    }

    /// <summary>Envuelve membrete (opcional) + cuerpo en una pagina HTML A4 lista para el motor headless.</summary>
    private static string BuildPrintableDocument(string? membreteHtml, string body, string titulo)
    {
        var membrete = string.IsNullOrWhiteSpace(membreteHtml)
            ? ""
            : $"<header class=\"doc-membrete\">{membreteHtml}</header>";
        var title = System.Net.WebUtility.HtmlEncode(titulo ?? "documento");
        return "<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\">"
            + $"<title>{title}</title><style>"
            + "*{box-sizing:border-box}"
            + "html,body{margin:0;padding:0;background:#fff;color:#111;"
            + "font-family:Arial,Helvetica,sans-serif;font-size:12pt;line-height:1.45}"
            + ".doc-membrete{margin:0 0 18px;padding:0 0 12px;border-bottom:1px solid #ddd}"
            + ".doc-membrete img{max-height:110px}"
            + ".doc-body{white-space:normal}"
            + "table{border-collapse:collapse}"
            + "img{max-width:100%}"
            + "</style></head><body>"
            + membrete
            + $"<main class=\"doc-body\">{body}</main>"
            + "</body></html>";
    }

    public async Task<TaskCoreResult<Guid>> GuardarNuevoAsync(
        Guid taskId, string titulo, string html, Guid? grupoId = null, CancellationToken cancellationToken = default)
    {
        var task = await _db.TaskItems.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null) { return TaskCoreResult<Guid>.NotFound("La tarea no existe."); }

        var name = (titulo ?? "").Trim();
        if (name.Length == 0) { return TaskCoreResult<Guid>.Invalid("El titulo del documento es obligatorio."); }

        var categoriaId = await EnsureCategoriaTareasAsync(cancellationToken);
        if (categoriaId is not Guid catId) { return TaskCoreResult<Guid>.Invalid("No hay tenant activo."); }

        // Membrete del grupo: se resuelve con los tokens de la tarea y se CONGELA en el documento.
        var membrete = await ResolveMembreteAsync(task, grupoId, cancellationToken);

        var bytes = Encoding.UTF8.GetBytes(html ?? "");
        var fileName = $"{task.Number}-{Slug(name)}.html";
        var res = await _docs.SubirDocumentoAsync(new SubirDocumentoRequest(
            CategoriaId: catId,
            CarpetaId: null,
            Titulo: name,
            Descripcion: null,
            NombreArchivo: fileName,
            TipoMime: "text/html",
            Contenido: bytes,
            Visibilidad: VisibilidadDocumento.Equipo,
            EtiquetaIds: null,
            Origen: OrigenDocumento.Tarea,
            OrigenEntidadId: task.Id), cancellationToken);

        if (!res.IsOk) { return TaskCoreResult<Guid>.Invalid(res.Error ?? "No se pudo guardar el documento."); }

        // Congela el membrete en el documento recien creado (segunda escritura, mismo tenant).
        if (membrete is not null && res.Id is Guid newId)
        {
            var doc = await _db.Documentos.FirstOrDefaultAsync(d => d.Id == newId, cancellationToken);
            if (doc is not null)
            {
                doc.MembreteHtml = membrete;
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        return TaskCoreResult<Guid>.Ok(res.Id ?? Guid.Empty);
    }

    /// <summary>Resuelve el membrete del grupo con los tokens de la tarea. Null si no hay grupo o no
    /// tiene membrete (o queda vacio tras resolver).</summary>
    private async Task<string?> ResolveMembreteAsync(
        Domain.Entities.TaskItem task, Guid? grupoId, CancellationToken cancellationToken)
    {
        if (grupoId is not Guid gid) { return null; }
        var header = await _db.DocumentTemplateGroups.AsNoTracking()
            .Where(g => g.Id == gid).Select(g => g.HeaderHtml).FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(header)) { return null; }
        var tokens = await _tokens.BuildAsync(task, cancellationToken);
        var resolved = _tokens.Render(header, tokens);
        return string.IsNullOrWhiteSpace(resolved) ? null : resolved;
    }

    public async Task<TaskCoreResult<Guid>> GuardarNuevaVersionAsync(
        Guid documentoId, string html, string? notas, CancellationToken cancellationToken = default)
    {
        var doc = await _db.Documentos.AsNoTracking()
            .Where(d => d.Id == documentoId && d.Activo)
            .Select(d => new { d.Titulo, d.OrigenEntidadId })
            .FirstOrDefaultAsync(cancellationToken);
        if (doc is null) { return TaskCoreResult<Guid>.NotFound("El documento no existe."); }

        var number = doc.OrigenEntidadId is Guid tid
            ? await _db.TaskItems.AsNoTracking().Where(t => t.Id == tid).Select(t => t.Number).FirstOrDefaultAsync(cancellationToken)
            : null;
        var prefix = string.IsNullOrWhiteSpace(number) ? "" : number + "-";

        var bytes = Encoding.UTF8.GetBytes(html ?? "");
        var fileName = $"{prefix}{Slug(doc.Titulo)}.html";
        var res = await _docs.SubirNuevaVersionAsync(documentoId, new NuevaVersionRequest(
            NombreArchivo: fileName,
            TipoMime: "text/html",
            Contenido: bytes,
            NotasCambio: notas), cancellationToken);

        return res.IsOk
            ? TaskCoreResult<Guid>.Ok(res.Id ?? Guid.Empty)
            : TaskCoreResult<Guid>.Invalid(res.Error ?? "No se pudo guardar la version.");
    }

    public async Task<TaskCoreResult<bool>> ActivarVersionAsync(
        Guid documentoId, Guid versionId, CancellationToken cancellationToken = default)
    {
        var res = await _docs.ActivarVersionAsync(documentoId, versionId, cancellationToken);
        return res.IsOk ? TaskCoreResult<bool>.Ok(true) : TaskCoreResult<bool>.Invalid(res.Error ?? "No se pudo activar la version.");
    }

    public async Task<TaskCoreResult<bool>> EliminarAsync(
        Guid documentoId, CancellationToken cancellationToken = default)
    {
        var res = await _docs.EliminarDocumentoAsync(documentoId, cancellationToken);
        return res.IsOk ? TaskCoreResult<bool>.Ok(true) : TaskCoreResult<bool>.Invalid(res.Error ?? "No se pudo eliminar el documento.");
    }

    /// <summary>Devuelve el id de la categoria "Documentos de tareas" del tenant, creandola si falta.</summary>
    private async Task<Guid?> EnsureCategoriaTareasAsync(CancellationToken cancellationToken)
    {
        if (_tenant.TenantId is not Guid tenantId) { return null; }

        var existing = await _db.DocumentoCategorias
            .FirstOrDefaultAsync(c => c.Nombre == CategoriaTareasNombre && c.Activa, cancellationToken);
        if (existing is not null) { return existing.Id; }

        var orden = (await _db.DocumentoCategorias.Select(c => (int?)c.Orden).MaxAsync(cancellationToken) ?? -1) + 1;
        var cat = new DocumentoCategoria
        {
            TenantId = tenantId,
            Nombre = CategoriaTareasNombre,
            Descripcion = "Documentos generados desde tareas a partir de plantillas.",
            Icono = "file",
            Color = "#2F6FEB",
            EsBase = false,
            Activa = true,
            Orden = orden
        };
        _db.DocumentoCategorias.Add(cat);
        await _db.SaveChangesAsync(cancellationToken);
        return cat.Id;
    }

    /// <summary>Slug ASCII para el nombre de archivo (alfanumerico y guiones, en minuscula).</summary>
    private static string Slug(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            if (char.IsLetterOrDigit(ch) && ch < 128) { sb.Append(char.ToLowerInvariant(ch)); }
            else if (ch is ' ' or '-' or '_') { sb.Append('-'); }
        }
        var slug = sb.ToString().Trim('-');
        while (slug.Contains("--")) { slug = slug.Replace("--", "-"); }
        if (slug.Length == 0) { slug = "documento"; }
        return slug.Length <= 60 ? slug : slug[..60];
    }
}
