using System.IO.Compression;
using Ecorex.Application.Common;
using Ecorex.Application.Confidentiality;
using Ecorex.Application.Integraciones.Maqueta;
using Ecorex.Application.Organization;
using Ecorex.Application.Storage;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Formacion;

/// <summary>
/// Modulo de FORMACION (ADR-0202, ola 1; basado en el modulo de capacitaciones de DokTrino). Cursos con modulos y
/// lecciones (video subido con streaming, enlace YouTube/Vimeo, PDF, imagen, texto con formato, wizard HTML y
/// archivo descargable), catalogo para todos los usuarios del tenant, asignacion como obligatorio (todos, usuario o
/// unidad del organigrama con fecha limite) y avance persistido por leccion. Tenant-scoped por el filtro global.
/// La autorizacion de pantalla va por la policy Perm:formacion:View (alumno) y Perm:formacion:Edit (administrar);
/// el servicio ademas solo deja ver cursos NO publicados a quien puede editar.
/// </summary>
public interface IFormacionService
{
    // ---- Administracion ----
    Task<IReadOnlyList<CursoResumenDto>> ListarCursosAdminAsync(CancellationToken ct = default);
    Task<CursoDetalleDto?> DetalleAsync(Guid cursoId, CancellationToken ct = default);
    Task<FormacionResult> GuardarCursoAsync(GuardarCursoRequest req, CancellationToken ct = default);
    Task<FormacionResult> SubirPortadaAsync(Guid cursoId, Stream content, string fileName, string? contentType, CancellationToken ct = default);
    Task<FormacionResult> EliminarCursoAsync(Guid cursoId, CancellationToken ct = default);
    Task<FormacionResult> GuardarModuloAsync(GuardarModuloRequest req, CancellationToken ct = default);
    Task<FormacionResult> EliminarModuloAsync(Guid moduloId, CancellationToken ct = default);
    Task<FormacionResult> MoverModuloAsync(Guid moduloId, int delta, CancellationToken ct = default);
    Task<FormacionResult> GuardarLeccionAsync(GuardarLeccionRequest req, CancellationToken ct = default);
    Task<FormacionResult> SubirArchivoLeccionAsync(Guid leccionId, Stream content, string fileName, string? contentType, CancellationToken ct = default);
    Task<FormacionResult> EliminarLeccionAsync(Guid leccionId, CancellationToken ct = default);
    Task<FormacionResult> MoverLeccionAsync(Guid leccionId, int delta, CancellationToken ct = default);
    Task<IReadOnlyList<AsignacionDto>> ListarAsignacionesAsync(Guid cursoId, CancellationToken ct = default);
    Task<FormacionResult> AgregarAsignacionAsync(AgregarAsignacionRequest req, CancellationToken ct = default);
    Task<FormacionResult> EliminarAsignacionAsync(Guid asignacionId, CancellationToken ct = default);
    Task<IReadOnlyList<FormacionOpcionDto>> ListarUsuariosAsync(CancellationToken ct = default);
    Task<IReadOnlyList<FormacionOpcionDto>> ListarUnidadesAsync(CancellationToken ct = default);

    // ---- Alumno ----
    Task<IReadOnlyList<CatalogoCursoDto>> CatalogoAsync(CancellationToken ct = default);
    Task<CursoVistaDto?> VerCursoAsync(Guid cursoId, bool puedeEditar, CancellationToken ct = default);
    Task<AvanceDto?> MarcarVistaAsync(Guid leccionId, bool puedeEditar, CancellationToken ct = default);
    Task RegistrarAperturaAsync(Guid cursoId, Guid? leccionId, bool puedeEditar, CancellationToken ct = default);

    // ---- Entrega de contenido ----
    /// <summary>Archivo de una leccion para servirlo (null si no existe, no tiene archivo o el curso no es visible).</summary>
    Task<Guid?> ResolverRecursoAsync(Guid leccionId, bool puedeEditar, CancellationToken ct = default);
    /// <summary>Wizard de una leccion visible para el usuario actual (null si no aplica).</summary>
    Task<WizardTarget?> ResolverWizardAsync(Guid leccionId, bool puedeEditar, CancellationToken ct = default);
    /// <summary>Wizard por tenant conocido (sin tenant en contexto): para el enlace firmado del iframe aislado.</summary>
    Task<WizardTarget?> ResolverWizardParaTenantAsync(Guid leccionId, Guid tenantId, CancellationToken ct = default);
}

public sealed class FormacionService : IFormacionService
{
    /// <summary>Modulo del gestor de archivos donde caen portadas y archivos de lecciones.</summary>
    public const string FileModule = "Formacion";

    public const long MaxVideoBytes = 1024L * 1024 * 1024;
    public const long MaxDocBytes = 100L * 1024 * 1024;
    public const long MaxImagenBytes = 20L * 1024 * 1024;
    public const long MaxWizardBytes = MockupService.MaxZipBytes;

    private static readonly string[] VideoExt = [".mp4", ".webm", ".m4v", ".mov", ".ogg"];
    private static readonly string[] ImagenExt = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg"];

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IFileStorageService _files;
    private readonly TimeProvider _clock;

    public FormacionService(IApplicationDbContext db, ITenantContext tenant, IFileStorageService files, TimeProvider clock)
    {
        _db = db;
        _tenant = tenant;
        _files = files;
        _clock = clock;
    }

    // =========================================== Administracion ===========================================

    public async Task<IReadOnlyList<CursoResumenDto>> ListarCursosAdminAsync(CancellationToken ct = default)
    {
        return await _db.Cursos.AsNoTracking()
            .OrderBy(c => c.Categoria).ThenBy(c => c.Titulo)
            .Select(c => new CursoResumenDto(c.Id, c.Titulo, c.Descripcion, c.Categoria, c.PortadaFileId, c.Publicado, c.DuracionMinutos,
                c.Modulos.Count, _db.CursoLecciones.Count(l => l.CursoId == c.Id),
                _db.CursoAsignaciones.Count(a => a.CursoId == c.Id), c.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<CursoDetalleDto?> DetalleAsync(Guid cursoId, CancellationToken ct = default)
    {
        var c = await _db.Cursos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == cursoId, ct);
        if (c is null) { return null; }
        var modulos = await _db.CursoModulos.AsNoTracking().Where(m => m.CursoId == cursoId)
            .OrderBy(m => m.Orden).ThenBy(m => m.CreatedAt).ToListAsync(ct);
        var lecciones = await _db.CursoLecciones.AsNoTracking().Where(l => l.CursoId == cursoId)
            .OrderBy(l => l.Orden).ThenBy(l => l.CreatedAt).ToListAsync(ct);
        var porModulo = lecciones.GroupBy(l => l.ModuloId).ToDictionary(g => g.Key, g => g.Select(Map).ToList());
        return new CursoDetalleDto(c.Id, c.Titulo, c.Descripcion, c.Categoria, c.PortadaFileId, c.Publicado, c.DuracionMinutos,
            modulos.Select(m => new ModuloDto(m.Id, m.Titulo, m.Descripcion, m.Orden,
                porModulo.TryGetValue(m.Id, out var ls) ? ls : new List<LeccionDto>())).ToList());
    }

    public async Task<FormacionResult> GuardarCursoAsync(GuardarCursoRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return FormacionResult.Mal("No hay tenant activo."); }
        var titulo = Clip(req.Titulo, 200);
        if (titulo is null) { return FormacionResult.Mal("El curso necesita un titulo."); }
        if (req.DuracionMinutos is < 0 or > 100_000) { return FormacionResult.Mal("Duracion invalida."); }

        Curso? curso;
        if (req.Id is Guid id)
        {
            curso = await _db.Cursos.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (curso is null) { return FormacionResult.Mal("Curso no encontrado."); }
        }
        else
        {
            curso = new Curso { TenantId = tenantId, CreatedByName = await NombreActualAsync(ct) };
            _db.Cursos.Add(curso);
        }
        curso.Titulo = titulo;
        curso.Descripcion = Clip(req.Descripcion, 2000);
        curso.Categoria = Clip(req.Categoria, 80);
        curso.Publicado = req.Publicado;
        curso.DuracionMinutos = req.DuracionMinutos;
        await _db.SaveChangesAsync(ct);
        return FormacionResult.Bien(curso.Id);
    }

    public async Task<FormacionResult> SubirPortadaAsync(Guid cursoId, Stream content, string fileName, string? contentType, CancellationToken ct = default)
    {
        var curso = await _db.Cursos.FirstOrDefaultAsync(c => c.Id == cursoId, ct);
        if (curso is null) { return FormacionResult.Mal("Curso no encontrado."); }
        if (!ImagenExt.Contains(Ext(fileName))) { return FormacionResult.Mal("La portada debe ser una imagen (png, jpg, gif, webp o svg)."); }
        StoredFileDto stored;
        try
        {
            stored = await _files.SaveStreamAsync(new FileStorageStreamSaveRequest(FileModule, cursoId.ToString(), "portada",
                fileName, contentType ?? MimeDe(fileName), new LimitedReadStream(content, MaxImagenBytes)), ct);
        }
        catch (ArchivoMuyGrandeException ex)
        {
            return FormacionResult.Mal($"La portada supera el limite de {ex.Limite / (1024 * 1024)} MB.");
        }
        var anterior = curso.PortadaFileId;
        curso.PortadaFileId = stored.Id;
        await _db.SaveChangesAsync(ct);
        await BorrarArchivoAsync(anterior);
        return FormacionResult.Bien(stored.Id);
    }

    public async Task<FormacionResult> EliminarCursoAsync(Guid cursoId, CancellationToken ct = default)
    {
        var curso = await _db.Cursos.FirstOrDefaultAsync(c => c.Id == cursoId, ct);
        if (curso is null) { return FormacionResult.Mal("Curso no encontrado."); }
        // Borrado logico (regla 5): el curso desaparece del catalogo; avance, archivos y estructura se conservan.
        curso.IsDeleted = true;
        curso.DeletedAt = _clock.GetUtcNow();
        curso.DeletedByTenantUserId = await TenantUserIdAsync(ct);
        curso.Publicado = false;
        await _db.SaveChangesAsync(ct);
        return FormacionResult.Bien(curso.Id);
    }

    public async Task<FormacionResult> GuardarModuloAsync(GuardarModuloRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return FormacionResult.Mal("No hay tenant activo."); }
        var titulo = Clip(req.Titulo, 200);
        if (titulo is null) { return FormacionResult.Mal("El modulo necesita un titulo."); }
        if (!await _db.Cursos.AnyAsync(c => c.Id == req.CursoId, ct)) { return FormacionResult.Mal("Curso no encontrado."); }

        CursoModulo? modulo;
        if (req.Id is Guid id)
        {
            modulo = await _db.CursoModulos.FirstOrDefaultAsync(m => m.Id == id && m.CursoId == req.CursoId, ct);
            if (modulo is null) { return FormacionResult.Mal("Modulo no encontrado."); }
        }
        else
        {
            var orden = await _db.CursoModulos.Where(m => m.CursoId == req.CursoId).Select(m => (int?)m.Orden).MaxAsync(ct) ?? 0;
            modulo = new CursoModulo { TenantId = tenantId, CursoId = req.CursoId, Orden = orden + 1 };
            _db.CursoModulos.Add(modulo);
        }
        modulo.Titulo = titulo;
        modulo.Descripcion = Clip(req.Descripcion, 1000);
        await _db.SaveChangesAsync(ct);
        return FormacionResult.Bien(modulo.Id);
    }

    public async Task<FormacionResult> EliminarModuloAsync(Guid moduloId, CancellationToken ct = default)
    {
        var modulo = await _db.CursoModulos.FirstOrDefaultAsync(m => m.Id == moduloId, ct);
        if (modulo is null) { return FormacionResult.Mal("Modulo no encontrado."); }
        var archivos = await _db.CursoLecciones.Where(l => l.ModuloId == moduloId && l.ArchivoId != null)
            .Select(l => l.ArchivoId!.Value).ToListAsync(ct);
        // Lecciones y sus vistas caen por cascada (son parte del agregado curso); los binarios se liberan despues.
        _db.CursoModulos.Remove(modulo);
        await _db.SaveChangesAsync(ct);
        foreach (var a in archivos) { await BorrarArchivoAsync(a); }
        await RecalcularAvanceCursoAsync(modulo.CursoId, ct);
        return FormacionResult.Bien(moduloId);
    }

    public async Task<FormacionResult> MoverModuloAsync(Guid moduloId, int delta, CancellationToken ct = default)
    {
        var modulo = await _db.CursoModulos.AsNoTracking().FirstOrDefaultAsync(m => m.Id == moduloId, ct);
        if (modulo is null) { return FormacionResult.Mal("Modulo no encontrado."); }
        var hermanos = await _db.CursoModulos.Where(m => m.CursoId == modulo.CursoId)
            .OrderBy(m => m.Orden).ThenBy(m => m.CreatedAt).ToListAsync(ct);
        Reordenar(hermanos, h => h.Id == moduloId, delta, (h, o) => h.Orden = o);
        await _db.SaveChangesAsync(ct);
        return FormacionResult.Bien(moduloId);
    }

    public async Task<FormacionResult> GuardarLeccionAsync(GuardarLeccionRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return FormacionResult.Mal("No hay tenant activo."); }
        var titulo = Clip(req.Titulo, 200);
        if (titulo is null) { return FormacionResult.Mal("La leccion necesita un titulo."); }
        if (!Enum.IsDefined(req.Tipo)) { return FormacionResult.Mal("Tipo de leccion invalido."); }
        var modulo = await _db.CursoModulos.AsNoTracking().FirstOrDefaultAsync(m => m.Id == req.ModuloId, ct);
        if (modulo is null) { return FormacionResult.Mal("Modulo no encontrado."); }

        string? url = null;
        if (req.Tipo == CursoLeccionTipo.VideoEnlace && !string.IsNullOrWhiteSpace(req.Url))
        {
            url = req.Url.Trim();
            if (url.Length > 500 || FormacionVideoLinks.ToEmbedUrl(url) is null)
            {
                return FormacionResult.Mal("El enlace debe ser de YouTube o Vimeo (ej. https://www.youtube.com/watch?v=... o https://vimeo.com/123456).");
            }
        }

        CursoLeccion? leccion;
        var nueva = req.Id is null;
        if (req.Id is Guid id)
        {
            leccion = await _db.CursoLecciones.FirstOrDefaultAsync(l => l.Id == id, ct);
            if (leccion is null) { return FormacionResult.Mal("Leccion no encontrada."); }
        }
        else
        {
            var orden = await _db.CursoLecciones.Where(l => l.ModuloId == req.ModuloId).Select(l => (int?)l.Orden).MaxAsync(ct) ?? 0;
            leccion = new CursoLeccion { TenantId = tenantId, ModuloId = req.ModuloId, CursoId = modulo.CursoId, Orden = orden + 1 };
            _db.CursoLecciones.Add(leccion);
        }

        // Cambiar el tipo invalida el archivo anterior si ya no corresponde (un PDF no sirve como video).
        Guid? archivoALiberar = null;
        if (!nueva && leccion.Tipo != req.Tipo && leccion.ArchivoId is not null)
        {
            archivoALiberar = leccion.ArchivoId;
            LimpiarArchivo(leccion);
        }
        if (leccion.ModuloId != req.ModuloId)
        {
            leccion.ModuloId = req.ModuloId;
            leccion.CursoId = modulo.CursoId;
        }
        leccion.Titulo = titulo;
        leccion.Descripcion = Clip(req.Descripcion, 1000);
        leccion.Tipo = req.Tipo;
        leccion.Url = req.Tipo == CursoLeccionTipo.VideoEnlace ? url : null;
        leccion.Contenido = req.Tipo == CursoLeccionTipo.Texto ? LimpiarTexto(req.Contenido) : null;
        leccion.DuracionMinutos = req.DuracionMinutos is > 0 and <= 100_000 ? req.DuracionMinutos : null;
        await _db.SaveChangesAsync(ct);
        await BorrarArchivoAsync(archivoALiberar);
        if (nueva) { await RecalcularAvanceCursoAsync(modulo.CursoId, ct); }
        return FormacionResult.Bien(leccion.Id);
    }

    public async Task<FormacionResult> SubirArchivoLeccionAsync(Guid leccionId, Stream content, string fileName, string? contentType, CancellationToken ct = default)
    {
        var leccion = await _db.CursoLecciones.FirstOrDefaultAsync(l => l.Id == leccionId, ct);
        if (leccion is null) { return FormacionResult.Mal("Leccion no encontrada."); }
        var ext = Ext(fileName);
        var mime = string.IsNullOrWhiteSpace(contentType) ? MimeDe(fileName) : contentType!;

        StoredFileDto stored;
        string? rootPrefix = null;
        string? entry = null;
        try
        {
            switch (leccion.Tipo)
            {
                case CursoLeccionTipo.Video:
                    if (!VideoExt.Contains(ext)) { return FormacionResult.Mal("El video debe ser mp4, webm, m4v, mov u ogg (recomendado: mp4 H.264)."); }
                    stored = await Guardar(content, MaxVideoBytes);
                    break;
                case CursoLeccionTipo.Pdf:
                    if (ext != ".pdf") { return FormacionResult.Mal("El manual debe ser un PDF."); }
                    stored = await Guardar(content, MaxDocBytes, "application/pdf");
                    break;
                case CursoLeccionTipo.Imagen:
                    if (!ImagenExt.Contains(ext)) { return FormacionResult.Mal("La imagen debe ser png, jpg, gif, webp o svg."); }
                    stored = await Guardar(content, MaxImagenBytes);
                    break;
                case CursoLeccionTipo.Archivo:
                    stored = await Guardar(content, MaxDocBytes);
                    break;
                case CursoLeccionTipo.WizardHtml:
                    {
                        if (ext is not (".zip" or ".html" or ".htm")) { return FormacionResult.Mal("El wizard debe ser un .zip (carpeta con index.html) o un .html."); }
                        var bytes = await LeerTodoAsync(content, MaxWizardBytes, ct);
                        if (bytes is null) { return FormacionResult.Mal($"El wizard supera {MaxWizardBytes / (1024 * 1024)} MB."); }
                        // Un .html suelto se envuelve en un ZIP como index.html: un solo camino de servido (el de las maquetas).
                        var zip = ext == ".zip" ? bytes : EnvolverHtml(bytes);
                        var insp = MockupService.InspectZip(zip);
                        if (insp.Error is not null) { return FormacionResult.Mal(insp.Error); }
                        rootPrefix = insp.RootPrefix;
                        entry = insp.EntryPath;
                        var zipName = Path.GetFileNameWithoutExtension(fileName) + ".zip";
                        stored = await _files.SaveAsync(new FileStorageSaveRequest(FileModule, leccion.CursoId.ToString(), leccion.Id.ToString("N"),
                            zipName, "application/zip", zip), ct);
                        break;
                    }
                default:
                    return FormacionResult.Mal("Este tipo de leccion no lleva archivo.");
            }
        }
        catch (ArchivoMuyGrandeException ex)
        {
            return FormacionResult.Mal($"El archivo supera el limite de {ex.Limite / (1024 * 1024)} MB.");
        }

        var anterior = leccion.ArchivoId;
        leccion.ArchivoId = stored.Id;
        leccion.ArchivoNombre = Clip(Path.GetFileName(fileName), 255);
        leccion.ArchivoMime = Clip(stored.ContentType ?? mime, 120);
        leccion.ArchivoBytes = stored.SizeBytes;
        leccion.WizardRootPrefix = rootPrefix;
        leccion.WizardEntryPath = entry;
        await _db.SaveChangesAsync(ct);
        await BorrarArchivoAsync(anterior);
        return FormacionResult.Bien(stored.Id);

        Task<StoredFileDto> Guardar(Stream s, long max, string? forcedMime = null)
            => _files.SaveStreamAsync(new FileStorageStreamSaveRequest(FileModule, leccion.CursoId.ToString(), leccion.Id.ToString("N"),
                fileName, forcedMime ?? mime, new LimitedReadStream(s, max)), ct);
    }

    public async Task<FormacionResult> EliminarLeccionAsync(Guid leccionId, CancellationToken ct = default)
    {
        var leccion = await _db.CursoLecciones.FirstOrDefaultAsync(l => l.Id == leccionId, ct);
        if (leccion is null) { return FormacionResult.Mal("Leccion no encontrada."); }
        var archivo = leccion.ArchivoId;
        _db.CursoLecciones.Remove(leccion);
        await _db.SaveChangesAsync(ct);
        await BorrarArchivoAsync(archivo);
        await RecalcularAvanceCursoAsync(leccion.CursoId, ct);
        return FormacionResult.Bien(leccionId);
    }

    public async Task<FormacionResult> MoverLeccionAsync(Guid leccionId, int delta, CancellationToken ct = default)
    {
        var leccion = await _db.CursoLecciones.AsNoTracking().FirstOrDefaultAsync(l => l.Id == leccionId, ct);
        if (leccion is null) { return FormacionResult.Mal("Leccion no encontrada."); }
        var hermanas = await _db.CursoLecciones.Where(l => l.ModuloId == leccion.ModuloId)
            .OrderBy(l => l.Orden).ThenBy(l => l.CreatedAt).ToListAsync(ct);
        Reordenar(hermanas, h => h.Id == leccionId, delta, (h, o) => h.Orden = o);
        await _db.SaveChangesAsync(ct);
        return FormacionResult.Bien(leccionId);
    }

    public async Task<IReadOnlyList<AsignacionDto>> ListarAsignacionesAsync(Guid cursoId, CancellationToken ct = default)
    {
        var asignaciones = await _db.CursoAsignaciones.AsNoTracking().Where(a => a.CursoId == cursoId)
            .OrderBy(a => a.CreatedAt).ToListAsync(ct);
        if (asignaciones.Count == 0) { return Array.Empty<AsignacionDto>(); }
        var usuarios = await NombresUsuariosAsync(ct);
        var graph = await CargarOrganigramaAsync(ct);
        var unidades = graph.Nombres;
        var totalUsuarios = usuarios.Count;
        return asignaciones.Select(a =>
        {
            var (nombre, personas) = a.Destino switch
            {
                CursoAsignacionDestino.Todos => ("Todos los usuarios", totalUsuarios),
                CursoAsignacionDestino.Usuario => (a.TenantUserId is Guid u && usuarios.TryGetValue(u, out var n) ? n : "(usuario no encontrado)", 1),
                _ => (a.OrgUnitId is Guid o && unidades.TryGetValue(o, out var un) ? un : "(unidad no encontrada)",
                    a.OrgUnitId is Guid o2 ? OrgAssigneeTree.ResolveForUnit(o2, graph.Units, graph.Members).Count : 0)
            };
            return new AsignacionDto(a.Id, a.CursoId, a.Destino, a.TenantUserId, a.OrgUnitId, nombre, a.FechaLimite, personas);
        }).ToList();
    }

    public async Task<FormacionResult> AgregarAsignacionAsync(AgregarAsignacionRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return FormacionResult.Mal("No hay tenant activo."); }
        if (!await _db.Cursos.AnyAsync(c => c.Id == req.CursoId, ct)) { return FormacionResult.Mal("Curso no encontrado."); }
        Guid? userId = null;
        Guid? unitId = null;
        switch (req.Destino)
        {
            case CursoAsignacionDestino.Todos:
                break;
            case CursoAsignacionDestino.Usuario:
                if (req.TenantUserId is not Guid u || !await _db.TenantUsers.AnyAsync(x => x.Id == u, ct))
                {
                    return FormacionResult.Mal("Elige un usuario valido.");
                }
                userId = u;
                break;
            case CursoAsignacionDestino.Unidad:
                if (req.OrgUnitId is not Guid o || !await _db.OrgUnits.AnyAsync(x => x.Id == o && !x.IsArchived, ct))
                {
                    return FormacionResult.Mal("Elige una dependencia o cargo valido.");
                }
                unitId = o;
                break;
            default:
                return FormacionResult.Mal("Destino invalido.");
        }
        var duplicada = await _db.CursoAsignaciones.AnyAsync(a => a.CursoId == req.CursoId && a.Destino == req.Destino
            && a.TenantUserId == userId && a.OrgUnitId == unitId, ct);
        if (duplicada) { return FormacionResult.Mal("Esa asignacion ya existe."); }
        var asignacion = new CursoAsignacion
        {
            TenantId = tenantId,
            CursoId = req.CursoId,
            Destino = req.Destino,
            TenantUserId = userId,
            OrgUnitId = unitId,
            FechaLimite = req.FechaLimite
        };
        _db.CursoAsignaciones.Add(asignacion);
        await _db.SaveChangesAsync(ct);
        return FormacionResult.Bien(asignacion.Id);
    }

    public async Task<FormacionResult> EliminarAsignacionAsync(Guid asignacionId, CancellationToken ct = default)
    {
        var a = await _db.CursoAsignaciones.FirstOrDefaultAsync(x => x.Id == asignacionId, ct);
        if (a is null) { return FormacionResult.Mal("Asignacion no encontrada."); }
        _db.CursoAsignaciones.Remove(a);
        await _db.SaveChangesAsync(ct);
        return FormacionResult.Bien(asignacionId);
    }

    public async Task<IReadOnlyList<FormacionOpcionDto>> ListarUsuariosAsync(CancellationToken ct = default)
    {
        var usuarios = await _db.TenantUsers.AsNoTracking()
            .Where(u => u.Status == Domain.Enums.PlatformUserStatus.Active)
            .Select(u => new { u.Id, u.Email, Nombre = u.PlatformUser != null ? u.PlatformUser.DisplayName : null })
            .ToListAsync(ct);
        return usuarios.Select(u => new FormacionOpcionDto(u.Id, string.IsNullOrWhiteSpace(u.Nombre) ? u.Email : u.Nombre!, u.Email))
            .OrderBy(u => u.Nombre, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<FormacionOpcionDto>> ListarUnidadesAsync(CancellationToken ct = default)
    {
        var unidades = await _db.OrgUnits.AsNoTracking().Where(u => !u.IsArchived)
            .OrderBy(u => u.Name)
            .Select(u => new { u.Id, u.Name, u.Classifier })
            .ToListAsync(ct);
        return unidades.Select(u => new FormacionOpcionDto(u.Id, u.Name, u.Classifier.ToString())).ToList();
    }

    // =============================================== Alumno ===============================================

    public async Task<IReadOnlyList<CatalogoCursoDto>> CatalogoAsync(CancellationToken ct = default)
    {
        var me = await TenantUserIdAsync(ct);
        var cursos = await _db.Cursos.AsNoTracking().Where(c => c.Publicado)
            .Select(c => new
            {
                c.Id, c.Titulo, c.Descripcion, c.Categoria, c.PortadaFileId, c.DuracionMinutos,
                Lecciones = _db.CursoLecciones.Count(l => l.CursoId == c.Id)
            })
            .ToListAsync(ct);
        if (cursos.Count == 0) { return Array.Empty<CatalogoCursoDto>(); }
        var ids = cursos.Select(c => c.Id).ToList();
        var obligatorios = me is Guid u ? await AsignacionesDeAsync(u, ids, ct) : new Dictionary<Guid, DateOnly?>();
        var progresos = me is Guid u2
            ? await _db.CursoProgresos.AsNoTracking().Where(p => p.TenantUserId == u2 && ids.Contains(p.CursoId)).ToDictionaryAsync(p => p.CursoId, ct)
            : new Dictionary<Guid, CursoProgreso>();
        var hoy = await HoyAsync(ct);

        return cursos.Select(c =>
        {
            var obligatorio = obligatorios.TryGetValue(c.Id, out var limite);
            progresos.TryGetValue(c.Id, out var p);
            var completado = p?.FechaCompletado is not null;
            return new CatalogoCursoDto(c.Id, c.Titulo, c.Descripcion, c.Categoria, c.PortadaFileId, c.DuracionMinutos, c.Lecciones,
                obligatorio, limite, obligatorio && !completado && limite is DateOnly l && l < hoy,
                p is not null, p?.Porcentaje ?? 0, completado);
        })
        .OrderByDescending(c => c.Obligatorio && !c.Completado)
        .ThenBy(c => c.FechaLimite ?? DateOnly.MaxValue)
        .ThenBy(c => c.Categoria).ThenBy(c => c.Titulo)
        .ToList();
    }

    public async Task<CursoVistaDto?> VerCursoAsync(Guid cursoId, bool puedeEditar, CancellationToken ct = default)
    {
        var detalle = await DetalleAsync(cursoId, ct);
        if (detalle is null || (!detalle.Publicado && !puedeEditar)) { return null; }
        var me = await TenantUserIdAsync(ct);
        IReadOnlySet<Guid> vistas = new HashSet<Guid>();
        CursoProgreso? progreso = null;
        DateOnly? limite = null;
        var obligatorio = false;
        if (me is Guid u)
        {
            vistas = (await _db.CursoLeccionVistas.AsNoTracking().Where(v => v.CursoId == cursoId && v.TenantUserId == u)
                .Select(v => v.LeccionId).ToListAsync(ct)).ToHashSet();
            progreso = await _db.CursoProgresos.AsNoTracking().FirstOrDefaultAsync(p => p.CursoId == cursoId && p.TenantUserId == u, ct);
            var asign = await AsignacionesDeAsync(u, new[] { cursoId }, ct);
            obligatorio = asign.TryGetValue(cursoId, out limite);
        }
        return new CursoVistaDto(detalle, vistas, progreso?.Porcentaje ?? 0, progreso?.FechaCompletado is not null,
            progreso?.UltimaLeccionId, obligatorio, limite);
    }

    public async Task RegistrarAperturaAsync(Guid cursoId, Guid? leccionId, bool puedeEditar, CancellationToken ct = default)
    {
        var me = await TenantUserIdAsync(ct);
        if (me is not Guid u || _tenant.TenantId is not Guid tenantId) { return; }
        var visible = await _db.Cursos.AnyAsync(c => c.Id == cursoId && (c.Publicado || puedeEditar), ct);
        if (!visible) { return; }
        var progreso = await _db.CursoProgresos.FirstOrDefaultAsync(p => p.CursoId == cursoId && p.TenantUserId == u, ct);
        if (progreso is null)
        {
            progreso = new CursoProgreso { TenantId = tenantId, CursoId = cursoId, TenantUserId = u, FechaInicio = _clock.GetUtcNow() };
            _db.CursoProgresos.Add(progreso);
        }
        if (leccionId is Guid l && await _db.CursoLecciones.AnyAsync(x => x.Id == l && x.CursoId == cursoId, ct))
        {
            progreso.UltimaLeccionId = l;
        }
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Dos pestanas abrieron el curso a la vez: el indice unico gano la otra; no es un error para el alumno
            // (el contexto es efimero por operacion, no queda la entidad colgada).
        }
    }

    public async Task<AvanceDto?> MarcarVistaAsync(Guid leccionId, bool puedeEditar, CancellationToken ct = default)
    {
        var me = await TenantUserIdAsync(ct);
        if (me is not Guid u || _tenant.TenantId is not Guid tenantId) { return null; }
        var leccion = await _db.CursoLecciones.AsNoTracking().FirstOrDefaultAsync(l => l.Id == leccionId, ct);
        if (leccion is null) { return null; }
        var visible = await _db.Cursos.AnyAsync(c => c.Id == leccion.CursoId && (c.Publicado || puedeEditar), ct);
        if (!visible) { return null; }

        await using var tx = _db.HasActiveTransaction ? null : await _db.BeginTransactionAsync(ct);
        if (!await _db.CursoLeccionVistas.AnyAsync(v => v.LeccionId == leccionId && v.TenantUserId == u, ct))
        {
            _db.CursoLeccionVistas.Add(new CursoLeccionVista
            {
                TenantId = tenantId, LeccionId = leccionId, CursoId = leccion.CursoId, TenantUserId = u, VistaEn = _clock.GetUtcNow()
            });
        }
        var progreso = await _db.CursoProgresos.FirstOrDefaultAsync(p => p.CursoId == leccion.CursoId && p.TenantUserId == u, ct);
        if (progreso is null)
        {
            progreso = new CursoProgreso { TenantId = tenantId, CursoId = leccion.CursoId, TenantUserId = u, FechaInicio = _clock.GetUtcNow() };
            _db.CursoProgresos.Add(progreso);
        }
        progreso.UltimaLeccionId = leccionId;
        await _db.SaveChangesAsync(ct);
        var avance = await AplicarAvanceAsync(progreso, ct);
        await _db.SaveChangesAsync(ct);
        if (tx is not null) { await tx.CommitAsync(ct); }
        return avance;
    }

    // ======================================= Entrega de contenido =======================================

    public async Task<Guid?> ResolverRecursoAsync(Guid leccionId, bool puedeEditar, CancellationToken ct = default)
    {
        return await _db.CursoLecciones.AsNoTracking()
            .Where(l => l.Id == leccionId && l.ArchivoId != null && l.Tipo != CursoLeccionTipo.WizardHtml
                && _db.Cursos.Any(c => c.Id == l.CursoId && (c.Publicado || puedeEditar)))
            .Select(l => l.ArchivoId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<WizardTarget?> ResolverWizardAsync(Guid leccionId, bool puedeEditar, CancellationToken ct = default)
    {
        return await _db.CursoLecciones.AsNoTracking()
            .Where(l => l.Id == leccionId && l.Tipo == CursoLeccionTipo.WizardHtml && l.ArchivoId != null && l.WizardEntryPath != null
                && _db.Cursos.Any(c => c.Id == l.CursoId && (c.Publicado || puedeEditar)))
            .Select(l => new WizardTarget(l.TenantId, l.Id, l.ArchivoId!.Value, l.WizardRootPrefix, l.WizardEntryPath!))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<WizardTarget?> ResolverWizardParaTenantAsync(Guid leccionId, Guid tenantId, CancellationToken ct = default)
    {
        // Sin tenant en contexto: el enlace firmado trae el tenant; se acota a el (IgnoreQueryFilters) y al curso
        // vivo. La firma solo se emite a quien ya pudo ver la leccion (ResolverWizardAsync en la pagina).
        return await _db.CursoLecciones.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.Id == leccionId && l.TenantId == tenantId && l.Tipo == CursoLeccionTipo.WizardHtml
                && l.ArchivoId != null && l.WizardEntryPath != null
                && _db.Cursos.IgnoreQueryFilters().Any(c => c.Id == l.CursoId && c.TenantId == tenantId && !c.IsDeleted))
            .Select(l => new WizardTarget(l.TenantId, l.Id, l.ArchivoId!.Value, l.WizardRootPrefix, l.WizardEntryPath!))
            .FirstOrDefaultAsync(ct);
    }

    // ============================================== Helpers ==============================================

    private static LeccionDto Map(CursoLeccion l) => new(l.Id, l.ModuloId, l.Titulo, l.Descripcion, l.Orden, l.Tipo,
        l.ArchivoId, l.ArchivoNombre, l.ArchivoMime, l.ArchivoBytes, l.Url, l.Contenido, l.WizardEntryPath, l.DuracionMinutos);

    /// <summary>Recalcula porcentaje y completado del progreso con las vistas vigentes (lecciones que aun existen).</summary>
    private async Task<AvanceDto> AplicarAvanceAsync(CursoProgreso progreso, CancellationToken ct)
    {
        var total = await _db.CursoLecciones.CountAsync(l => l.CursoId == progreso.CursoId, ct);
        var vistas = await _db.CursoLeccionVistas.CountAsync(v => v.CursoId == progreso.CursoId && v.TenantUserId == progreso.TenantUserId
            && _db.CursoLecciones.Any(l => l.Id == v.LeccionId), ct);
        progreso.LeccionesVistas = vistas;
        progreso.Porcentaje = total == 0 ? 0 : (int)Math.Round(vistas * 100.0 / total, MidpointRounding.AwayFromZero);
        var completo = total > 0 && vistas >= total;
        if (completo && progreso.FechaCompletado is null) { progreso.FechaCompletado = _clock.GetUtcNow(); }
        // Si el curso gano lecciones nuevas, deja de estar completo hasta verlas.
        if (!completo) { progreso.FechaCompletado = null; }
        return new AvanceDto(vistas, total, progreso.Porcentaje, completo);
    }

    /// <summary>Tras agregar o quitar lecciones, el porcentaje de todos los que iniciaron el curso se ajusta.</summary>
    private async Task RecalcularAvanceCursoAsync(Guid cursoId, CancellationToken ct)
    {
        var progresos = await _db.CursoProgresos.Where(p => p.CursoId == cursoId).ToListAsync(ct);
        if (progresos.Count == 0) { return; }
        foreach (var p in progresos) { await AplicarAvanceAsync(p, ct); }
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Cursos (de los pedidos) asignados al usuario, con la fecha limite mas cercana (null = sin limite).</summary>
    private async Task<Dictionary<Guid, DateOnly?>> AsignacionesDeAsync(Guid tenantUserId, IReadOnlyCollection<Guid> cursoIds, CancellationToken ct)
    {
        var asignaciones = await _db.CursoAsignaciones.AsNoTracking().Where(a => cursoIds.Contains(a.CursoId))
            .Select(a => new { a.CursoId, a.Destino, a.TenantUserId, a.OrgUnitId, a.FechaLimite }).ToListAsync(ct);
        var result = new Dictionary<Guid, DateOnly?>();
        if (asignaciones.Count == 0) { return result; }
        OrganigramaCargado? graph = null;
        var cacheUnidad = new Dictionary<Guid, bool>();
        foreach (var a in asignaciones)
        {
            bool aplica;
            switch (a.Destino)
            {
                case CursoAsignacionDestino.Todos:
                    aplica = true;
                    break;
                case CursoAsignacionDestino.Usuario:
                    aplica = a.TenantUserId == tenantUserId;
                    break;
                default:
                    if (a.OrgUnitId is not Guid o) { aplica = false; break; }
                    if (!cacheUnidad.TryGetValue(o, out aplica))
                    {
                        graph ??= await CargarOrganigramaAsync(ct);
                        aplica = OrgAssigneeTree.ResolveForUnit(o, graph.Units, graph.Members).Contains(tenantUserId);
                        cacheUnidad[o] = aplica;
                    }
                    break;
            }
            if (!aplica) { continue; }
            if (!result.TryGetValue(a.CursoId, out var actual))
            {
                result[a.CursoId] = a.FechaLimite;
            }
            else if (a.FechaLimite is DateOnly f && (actual is null || f < actual))
            {
                result[a.CursoId] = f;
            }
        }
        return result;
    }

    private sealed record OrganigramaCargado(
        IReadOnlyList<OrgAssigneeTree.UnitRow> Units, IReadOnlyList<OrgAssigneeTree.MemberRow> Members, Dictionary<Guid, string> Nombres);

    private async Task<OrganigramaCargado> CargarOrganigramaAsync(CancellationToken ct)
    {
        var units = await _db.OrgUnits.AsNoTracking().Where(u => !u.IsArchived)
            .Select(u => new { Row = new OrgAssigneeTree.UnitRow(u.Id, u.ParentId, u.Classifier, u.ResponsibleTenantUserId, u.TenantUserId), u.Name })
            .ToListAsync(ct);
        var members = await _db.OrgUnitMembers.AsNoTracking()
            .Select(m => new OrgAssigneeTree.MemberRow(m.OrgUnitId, m.TenantUserId)).ToListAsync(ct);
        return new OrganigramaCargado(units.Select(x => x.Row).ToList(), members, units.ToDictionary(x => x.Row.Id, x => x.Name));
    }

    private async Task<Dictionary<Guid, string>> NombresUsuariosAsync(CancellationToken ct)
    {
        var usuarios = await _db.TenantUsers.AsNoTracking()
            .Select(u => new { u.Id, u.Email, Nombre = u.PlatformUser != null ? u.PlatformUser.DisplayName : null })
            .ToListAsync(ct);
        return usuarios.ToDictionary(u => u.Id, u => string.IsNullOrWhiteSpace(u.Nombre) ? u.Email : u.Nombre!);
    }

    private async Task<Guid?> TenantUserIdAsync(CancellationToken ct)
    {
        if (_tenant.UserId is not Guid platformUserId) { return null; }
        return await _db.TenantUsers.AsNoTracking().Where(u => u.PlatformUserId == platformUserId)
            .Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
    }

    private async Task<string?> NombreActualAsync(CancellationToken ct)
    {
        if (_tenant.UserId is not Guid platformUserId) { return null; }
        var u = await _db.TenantUsers.AsNoTracking().Where(x => x.PlatformUserId == platformUserId)
            .Select(x => new { x.Email, Nombre = x.PlatformUser != null ? x.PlatformUser.DisplayName : null }).FirstOrDefaultAsync(ct);
        return u is null ? null : Clip(string.IsNullOrWhiteSpace(u.Nombre) ? u.Email : u.Nombre, 150);
    }

    /// <summary>Hoy en la zona horaria del tenant (regla 9; America/Bogota por defecto).</summary>
    private async Task<DateOnly> HoyAsync(CancellationToken ct)
    {
        var tzId = _tenant.TenantId is Guid t
            ? await _db.Tenants.AsNoTracking().Where(x => x.Id == t).Select(x => x.TimeZoneId).FirstOrDefaultAsync(ct)
            : null;
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(tzId) ? "America/Bogota" : tzId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            tz = TimeZoneInfo.CreateCustomTimeZone("COT", TimeSpan.FromHours(-5), "COT", "COT");
        }
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), tz).DateTime);
    }

    private static void Reordenar<T>(List<T> items, Func<T, bool> esElemento, int delta, Action<T, int> setOrden)
    {
        var idx = items.FindIndex(x => esElemento(x));
        if (idx < 0) { return; }
        var destino = Math.Clamp(idx + Math.Sign(delta), 0, items.Count - 1);
        if (destino != idx)
        {
            var item = items[idx];
            items.RemoveAt(idx);
            items.Insert(destino, item);
        }
        for (var i = 0; i < items.Count; i++) { setOrden(items[i], i + 1); }
    }

    private static void LimpiarArchivo(CursoLeccion l)
    {
        l.ArchivoId = null;
        l.ArchivoNombre = null;
        l.ArchivoMime = null;
        l.ArchivoBytes = null;
        l.WizardRootPrefix = null;
        l.WizardEntryPath = null;
    }

    private async Task BorrarArchivoAsync(Guid? fileId)
    {
        if (fileId is not Guid id) { return; }
        try { await _files.DeleteAsync(id, CancellationToken.None); }
        catch (Exception) { /* best effort: un binario huerfano no debe tumbar la operacion */ }
    }

    private static string? LimpiarTexto(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) { return null; }
        var limpio = ConfidentialityHtmlSanitizer.Sanitize(html);
        return limpio.Length > 200_000 ? limpio[..200_000] : limpio;
    }

    private static byte[] EnvolverHtml(byte[] html)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("index.html", CompressionLevel.Fastest);
            using var s = entry.Open();
            s.Write(html, 0, html.Length);
        }
        return ms.ToArray();
    }

    private static async Task<byte[]?> LeerTodoAsync(Stream s, long max, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = await s.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + n > max) { return null; }
            ms.Write(buffer, 0, n);
        }
        return ms.ToArray();
    }

    private static string Ext(string? fileName) => (Path.GetExtension(fileName ?? string.Empty) ?? string.Empty).ToLowerInvariant();

    private static string MimeDe(string fileName) => Ext(fileName) switch
    {
        ".mp4" or ".m4v" => "video/mp4",
        ".webm" => "video/webm",
        ".mov" => "video/quicktime",
        ".ogg" => "video/ogg",
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".zip" => "application/zip",
        ".html" or ".htm" => "text/html",
        _ => "application/octet-stream"
    };

    private static string? Clip(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) { return null; }
        var t = s.Trim();
        return t.Length > max ? t[..max] : t;
    }

    /// <summary>Lanzada por <see cref="LimitedReadStream"/> al pasar el tope de una subida.</summary>
    private sealed class ArchivoMuyGrandeException(long limite) : Exception("Archivo muy grande.")
    {
        public long Limite { get; } = limite;
    }

    /// <summary>Stream de solo lectura que corta (excepcion) si se leen mas de <c>max</c> bytes.</summary>
    private sealed class LimitedReadStream(Stream inner, long max) : Stream
    {
        private long _read;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => Track(inner.Read(buffer, offset, count));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Track(await inner.ReadAsync(buffer, cancellationToken));
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Track(int n)
        {
            _read += n;
            if (_read > max) { throw new ArchivoMuyGrandeException(max); }
            return n;
        }
    }
}
