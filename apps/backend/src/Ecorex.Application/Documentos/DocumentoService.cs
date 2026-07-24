using System.Security.Cryptography;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Documentos;

/// <summary>
/// Implementacion del Archivo central. Ver <see cref="IDocumentoService"/> para las reglas.
///
/// Nota sobre el filtro global: NINGUNA consulta de aqui filtra por TenantId a mano (regla 1).
/// Si una entidad de otro tenant llegara a esta clase, seria un fallo del DbContext, no de aqui.
/// </summary>
public sealed class DocumentoService : IDocumentoService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IDocumentoFileStore _files;

    public DocumentoService(IApplicationDbContext db, ITenantContext tenant, IDocumentoFileStore files)
    {
        _db = db;
        _tenant = tenant;
        _files = files;
    }

    private Guid ActorId => _tenant.UserId ?? Guid.Empty;

    // =======================================================================
    // Categorias
    // =======================================================================

    public async Task<IReadOnlyList<CategoriaDto>> ListarCategoriasAsync(
        bool incluirInactivas = false, CancellationToken ct = default)
    {
        var q = _db.DocumentoCategorias.AsNoTracking();
        if (!incluirInactivas) { q = q.Where(c => c.Activa); }
        var cats = await q.OrderBy(c => c.Orden).ThenBy(c => c.Nombre).ToListAsync(ct);
        if (cats.Count == 0) { return Array.Empty<CategoriaDto>(); }

        // Un solo GROUP BY para los conteos: N categorias no deben producir N consultas.
        var conteos = await _db.Documentos.AsNoTracking()
            .Where(d => d.Activo)
            .GroupBy(d => d.CategoriaId)
            .Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.C, ct);

        return cats.Select(c => new CategoriaDto(
            c.Id, c.EsBase, c.Nombre, c.Descripcion, c.Icono, c.Color, c.Activa, c.Orden,
            conteos.TryGetValue(c.Id, out var n) ? n : 0)).ToList();
    }

    public async Task<DocumentoResult> CrearCategoriaAsync(GuardarCategoriaRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return DocumentoResult.Fail("No hay tenant activo."); }
        var nombre = (req.Nombre ?? "").Trim();
        if (nombre.Length == 0) { return DocumentoResult.Fail("El nombre de la categoria es obligatorio."); }

        if (await _db.DocumentoCategorias.AnyAsync(c => c.Nombre == nombre, ct))
        {
            return DocumentoResult.Fail($"Ya existe una categoria llamada \"{nombre}\".");
        }

        var cat = new DocumentoCategoria
        {
            TenantId = tenantId,
            Nombre = nombre,
            Descripcion = Recortar(req.Descripcion, 600),
            Icono = Recortar(req.Icono, 60),
            Color = Recortar(req.Color, 20),
            Orden = req.Orden,
            EsBase = false,
            Activa = true
        };
        _db.DocumentoCategorias.Add(cat);
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(cat.Id);
    }

    public async Task<DocumentoResult> ActualizarCategoriaAsync(
        Guid id, GuardarCategoriaRequest req, CancellationToken ct = default)
    {
        var cat = await _db.DocumentoCategorias.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cat is null) { return DocumentoResult.Fail("La categoria no existe."); }
        // Las base las siembra el sistema y otras partes las asumen presentes con ese nombre.
        if (cat.EsBase) { return DocumentoResult.Fail("Las categorias del sistema no se pueden editar."); }

        var nombre = (req.Nombre ?? "").Trim();
        if (nombre.Length == 0) { return DocumentoResult.Fail("El nombre de la categoria es obligatorio."); }
        if (await _db.DocumentoCategorias.AnyAsync(c => c.Nombre == nombre && c.Id != id, ct))
        {
            return DocumentoResult.Fail($"Ya existe una categoria llamada \"{nombre}\".");
        }

        cat.Nombre = nombre;
        cat.Descripcion = Recortar(req.Descripcion, 600);
        cat.Icono = Recortar(req.Icono, 60);
        cat.Color = Recortar(req.Color, 20);
        cat.Orden = req.Orden;
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(cat.Id);
    }

    public async Task<DocumentoResult> DesactivarCategoriaAsync(Guid id, CancellationToken ct = default)
    {
        var cat = await _db.DocumentoCategorias.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cat is null) { return DocumentoResult.Fail("La categoria no existe."); }
        if (cat.EsBase) { return DocumentoResult.Fail("Las categorias del sistema no se pueden desactivar."); }

        // Se comprueba ANTES de tocar nada: desactivar una categoria con documentos los dejaria
        // invisibles sin haberlos archivado, que es justo el "borrado silencioso" que la regla 5 evita.
        var conDocs = await _db.Documentos.AnyAsync(d => d.CategoriaId == id && d.Activo, ct);
        if (conDocs) { return DocumentoResult.Fail("La categoria tiene documentos activos: archivalos o muevelos primero."); }

        cat.Activa = false;
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(cat.Id);
    }

    // =======================================================================
    // Carpetas
    // =======================================================================

    public async Task<IReadOnlyList<CarpetaDto>> ListarCarpetasAsync(Guid categoriaId, CancellationToken ct = default)
    {
        var planas = await _db.DocumentoCarpetas.AsNoTracking()
            .Where(c => c.CategoriaId == categoriaId && c.Activa)
            .OrderBy(c => c.Orden).ThenBy(c => c.Nombre)
            .ToListAsync(ct);
        if (planas.Count == 0) { return Array.Empty<CarpetaDto>(); }

        var conteos = await _db.Documentos.AsNoTracking()
            .Where(d => d.Activo && d.CarpetaId != null)
            .GroupBy(d => d.CarpetaId!.Value)
            .Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.C, ct);

        // El arbol se arma en memoria de una sola pasada: recorrer la BD por nivel seria N+1.
        var porPadre = planas.GroupBy(c => c.PadreId).ToDictionary(g => g.Key, g => g.ToList());
        return Construir(null);

        List<CarpetaDto> Construir(Guid? padreId)
        {
            if (!porPadre.TryGetValue(padreId, out var hijos)) { return new List<CarpetaDto>(); }
            return hijos.Select(c => new CarpetaDto(
                c.Id, c.CategoriaId, c.PadreId, c.Nombre, c.Descripcion, c.Orden, c.Activa,
                conteos.TryGetValue(c.Id, out var n) ? n : 0,
                Construir(c.Id))).ToList();
        }
    }

    public async Task<DocumentoResult> CrearCarpetaAsync(CrearCarpetaRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return DocumentoResult.Fail("No hay tenant activo."); }
        var nombre = (req.Nombre ?? "").Trim();
        if (nombre.Length == 0) { return DocumentoResult.Fail("El nombre de la carpeta es obligatorio."); }

        var cat = await _db.DocumentoCategorias.AsNoTracking().FirstOrDefaultAsync(c => c.Id == req.CategoriaId, ct);
        if (cat is null) { return DocumentoResult.Fail("La categoria no existe."); }

        if (req.PadreId is Guid padreId)
        {
            // La carpeta padre debe existir Y estar en la MISMA categoria: si no, el arbol
            // quedaria colgando entre dos categorias distintas.
            var padre = await _db.DocumentoCarpetas.AsNoTracking().FirstOrDefaultAsync(c => c.Id == padreId, ct);
            if (padre is null) { return DocumentoResult.Fail("La carpeta padre no existe."); }
            if (padre.CategoriaId != req.CategoriaId)
            {
                return DocumentoResult.Fail("La carpeta padre pertenece a otra categoria.");
            }
        }

        // La unicidad se valida aqui y no con un indice: PadreId es nullable y los indices unicos
        // con NULL no se comportan igual en PostgreSQL que en SQL Server (DAL dual).
        var duplicada = await _db.DocumentoCarpetas.AnyAsync(
            c => c.CategoriaId == req.CategoriaId && c.PadreId == req.PadreId && c.Nombre == nombre && c.Activa, ct);
        if (duplicada) { return DocumentoResult.Fail($"Ya existe una carpeta \"{nombre}\" en ese nivel."); }

        var carpeta = new DocumentoCarpeta
        {
            TenantId = tenantId,
            CategoriaId = req.CategoriaId,
            PadreId = req.PadreId,
            Nombre = nombre,
            Descripcion = Recortar(req.Descripcion, 600),
            Activa = true
        };
        _db.DocumentoCarpetas.Add(carpeta);
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(carpeta.Id);
    }

    public async Task<DocumentoResult> RenombrarCarpetaAsync(
        Guid id, RenombrarCarpetaRequest req, CancellationToken ct = default)
    {
        var carpeta = await _db.DocumentoCarpetas.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (carpeta is null) { return DocumentoResult.Fail("La carpeta no existe."); }

        var nombre = (req.Nombre ?? "").Trim();
        if (nombre.Length == 0) { return DocumentoResult.Fail("El nombre de la carpeta es obligatorio."); }

        var duplicada = await _db.DocumentoCarpetas.AnyAsync(
            c => c.CategoriaId == carpeta.CategoriaId && c.PadreId == carpeta.PadreId
                 && c.Nombre == nombre && c.Activa && c.Id != id, ct);
        if (duplicada) { return DocumentoResult.Fail($"Ya existe una carpeta \"{nombre}\" en ese nivel."); }

        carpeta.Nombre = nombre;
        carpeta.Descripcion = Recortar(req.Descripcion, 600);
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(carpeta.Id);
    }

    public async Task<DocumentoResult> DesactivarCarpetaAsync(Guid id, CancellationToken ct = default)
    {
        var carpeta = await _db.DocumentoCarpetas.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (carpeta is null) { return DocumentoResult.Fail("La carpeta no existe."); }

        if (await _db.Documentos.AnyAsync(d => d.CarpetaId == id && d.Activo, ct))
        {
            return DocumentoResult.Fail("La carpeta tiene documentos activos: archivalos o muevelos primero.");
        }
        if (await _db.DocumentoCarpetas.AnyAsync(c => c.PadreId == id && c.Activa, ct))
        {
            return DocumentoResult.Fail("La carpeta tiene subcarpetas activas: vaciala primero.");
        }

        carpeta.Activa = false;
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(carpeta.Id);
    }

    // =======================================================================
    // Etiquetas
    // =======================================================================

    public async Task<IReadOnlyList<EtiquetaDto>> ListarEtiquetasAsync(
        bool incluirInactivas = false, CancellationToken ct = default)
    {
        var q = _db.DocumentoEtiquetaCatalogos.AsNoTracking();
        if (!incluirInactivas) { q = q.Where(e => e.Activa); }
        var etiquetas = await q.OrderBy(e => e.Nombre).ToListAsync(ct);
        if (etiquetas.Count == 0) { return Array.Empty<EtiquetaDto>(); }

        var conteos = await _db.DocumentoEtiquetas.AsNoTracking()
            .Where(e => e.Documento!.Activo)
            .GroupBy(e => e.EtiquetaCatalogoId)
            .Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.C, ct);

        return etiquetas.Select(e => new EtiquetaDto(
            e.Id, e.EsBase, e.Nombre, e.Color, e.Activa,
            conteos.TryGetValue(e.Id, out var n) ? n : 0)).ToList();
    }

    public async Task<DocumentoResult> CrearEtiquetaAsync(GuardarEtiquetaRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return DocumentoResult.Fail("No hay tenant activo."); }
        var nombre = (req.Nombre ?? "").Trim();
        if (nombre.Length == 0) { return DocumentoResult.Fail("El nombre de la etiqueta es obligatorio."); }
        if (await _db.DocumentoEtiquetaCatalogos.AnyAsync(e => e.Nombre == nombre, ct))
        {
            return DocumentoResult.Fail($"Ya existe una etiqueta \"{nombre}\".");
        }

        var etiqueta = new DocumentoEtiquetaCatalogo
        {
            TenantId = tenantId,
            Nombre = nombre,
            Color = Recortar(req.Color, 20),
            EsBase = false,
            Activa = true
        };
        _db.DocumentoEtiquetaCatalogos.Add(etiqueta);
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(etiqueta.Id);
    }

    public async Task<DocumentoResult> ActualizarEtiquetaAsync(
        Guid id, GuardarEtiquetaRequest req, CancellationToken ct = default)
    {
        var etiqueta = await _db.DocumentoEtiquetaCatalogos.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (etiqueta is null) { return DocumentoResult.Fail("La etiqueta no existe."); }
        if (etiqueta.EsBase) { return DocumentoResult.Fail("Las etiquetas del sistema no se pueden editar."); }

        var nombre = (req.Nombre ?? "").Trim();
        if (nombre.Length == 0) { return DocumentoResult.Fail("El nombre de la etiqueta es obligatorio."); }
        if (await _db.DocumentoEtiquetaCatalogos.AnyAsync(e => e.Nombre == nombre && e.Id != id, ct))
        {
            return DocumentoResult.Fail($"Ya existe una etiqueta \"{nombre}\".");
        }

        etiqueta.Nombre = nombre;
        etiqueta.Color = Recortar(req.Color, 20);
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(etiqueta.Id);
    }

    public async Task<DocumentoResult> DesactivarEtiquetaAsync(Guid id, CancellationToken ct = default)
    {
        var etiqueta = await _db.DocumentoEtiquetaCatalogos.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (etiqueta is null) { return DocumentoResult.Fail("La etiqueta no existe."); }
        if (etiqueta.EsBase) { return DocumentoResult.Fail("Las etiquetas del sistema no se pueden desactivar."); }

        // No se borran los vinculos: desactivar la etiqueta la retira del catalogo, pero los
        // documentos que la llevan conservan su historia.
        etiqueta.Activa = false;
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(etiqueta.Id);
    }

    // =======================================================================
    // Documentos
    // =======================================================================

    public async Task<DocumentosPageDto> ListarDocumentosAsync(DocumentosFiltro filtro, CancellationToken ct = default)
    {
        var page = filtro.Page < 1 ? 1 : filtro.Page;
        var size = filtro.PageSize is < 1 or > 200 ? 30 : filtro.PageSize;

        var q = _db.Documentos.AsNoTracking().Where(d => d.Activo);

        if (filtro.CategoriaId is Guid cat) { q = q.Where(d => d.CategoriaId == cat); }
        if (filtro.CarpetaId is Guid carp) { q = q.Where(d => d.CarpetaId == carp); }
        if (filtro.Origen is OrigenDocumento or_) { q = q.Where(d => d.Origen == or_); }
        if (filtro.Estado is EstadoDocumento est) { q = q.Where(d => d.Estado == est); }
        if (filtro.Visibilidad is VisibilidadDocumento vis) { q = q.Where(d => d.Visibilidad == vis); }
        if (filtro.SoloDestacados) { q = q.Where(d => d.Destacado); }
        if (filtro.EtiquetaId is Guid etq)
        {
            q = q.Where(d => d.Etiquetas.Any(e => e.EtiquetaCatalogoId == etq));
        }
        if (!string.IsNullOrWhiteSpace(filtro.TextoBusqueda))
        {
            // Se busca en titulo y en el nombre del archivo: la gente recuerda uno u otro.
            var texto = filtro.TextoBusqueda.Trim();
            q = q.Where(d => EF.Functions.Like(d.Titulo, $"%{texto}%")
                          || EF.Functions.Like(d.NombreArchivoOriginal, $"%{texto}%"));
        }

        var total = await q.CountAsync(ct);
        var docs = await q
            .OrderByDescending(d => d.Destacado).ThenByDescending(d => d.CreatedAt)
            .Skip((page - 1) * size).Take(size)
            .Select(d => new
            {
                d.Id,
                d.Titulo,
                d.NombreArchivoOriginal,
                d.CategoriaId,
                CategoriaNombre = d.Categoria!.Nombre,
                d.CarpetaId,
                CarpetaNombre = d.Carpeta != null ? d.Carpeta.Nombre : null,
                d.Estado,
                d.Origen,
                d.Visibilidad,
                d.NumeroVersiones,
                d.Destacado,
                d.CreatedAt,
                d.UpdatedAt,
                Tam = d.VersionActual != null ? d.VersionActual.TamanoBytes : 0,
                Mime = d.VersionActual != null ? d.VersionActual.TipoMime : "",
                Url = d.VersionActual != null ? d.VersionActual.UrlStorage : null
            })
            .ToListAsync(ct);

        if (docs.Count == 0) { return new DocumentosPageDto(Array.Empty<DocumentoListaDto>(), total, page, size); }

        // Etiquetas y pines personales de la pagina, en dos consultas (no una por documento).
        var ids = docs.Select(d => d.Id).ToList();
        var etiquetas = await _db.DocumentoEtiquetas.AsNoTracking()
            .Where(e => ids.Contains(e.DocumentoId))
            .Select(e => new { e.DocumentoId, e.EtiquetaCatalogoId, e.EtiquetaCatalogo!.Nombre, e.EtiquetaCatalogo.Color })
            .ToListAsync(ct);
        var porDoc = etiquetas.GroupBy(e => e.DocumentoId).ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<EtiquetaResumenDto>)g
                .Select(x => new EtiquetaResumenDto(x.EtiquetaCatalogoId, x.Nombre, x.Color)).ToList());

        var actor = ActorId;
        var pines = await _db.DocumentoDestacadosPersonales.AsNoTracking()
            .Where(p => ids.Contains(p.DocumentoId) && p.UsuarioId == actor)
            .Select(p => p.DocumentoId)
            .ToListAsync(ct);
        var pinSet = pines.ToHashSet();

        var items = docs.Select(d => new DocumentoListaDto(
            d.Id, d.Titulo, d.NombreArchivoOriginal, d.CategoriaId, d.CategoriaNombre,
            d.CarpetaId, d.CarpetaNombre, d.Estado, d.Origen, d.Visibilidad, d.NumeroVersiones,
            d.Tam, d.Mime, d.Url, d.Destacado, pinSet.Contains(d.Id), d.CreatedAt, d.UpdatedAt,
            porDoc.TryGetValue(d.Id, out var et) ? et : Array.Empty<EtiquetaResumenDto>())).ToList();

        return new DocumentosPageDto(items, total, page, size);
    }

    public async Task<DocumentoDetalleDto?> GetDocumentoAsync(Guid id, CancellationToken ct = default)
    {
        var d = await _db.Documentos.AsNoTracking()
            .Include(x => x.Categoria)
            .Include(x => x.Carpeta)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) { return null; }

        var versiones = await _db.DocumentoVersiones.AsNoTracking()
            .Where(v => v.DocumentoId == id)
            .OrderByDescending(v => v.Numero)
            .Select(v => new VersionDto(v.Id, v.Numero, v.NombreArchivo, v.TipoMime, v.TamanoBytes,
                v.HashSha256, v.UrlStorage, v.NotasCambio, v.SubidoPorUsuarioId, v.CreatedAt))
            .ToListAsync(ct);

        var etiquetas = await _db.DocumentoEtiquetas.AsNoTracking()
            .Where(e => e.DocumentoId == id)
            .Select(e => new EtiquetaResumenDto(e.EtiquetaCatalogoId, e.EtiquetaCatalogo!.Nombre, e.EtiquetaCatalogo.Color))
            .ToListAsync(ct);

        var actor = ActorId;
        var pin = await _db.DocumentoDestacadosPersonales.AsNoTracking()
            .AnyAsync(p => p.DocumentoId == id && p.UsuarioId == actor, ct);

        return new DocumentoDetalleDto(
            d.Id, d.Titulo, d.Descripcion, d.NombreArchivoOriginal, d.CategoriaId,
            d.Categoria?.Nombre ?? "", d.CarpetaId, d.Carpeta?.Nombre, d.Estado, d.Origen,
            d.OrigenEntidadId, d.Visibilidad, d.NumeroVersiones, d.Destacado, pin, d.CreatedAt,
            d.SubidoPorUsuarioId,
            versiones.FirstOrDefault(v => v.Id == d.VersionActualId),
            versiones, etiquetas);
    }

    public async Task<DocumentoResult> SubirDocumentoAsync(SubirDocumentoRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return DocumentoResult.Fail("No hay tenant activo."); }
        var titulo = (req.Titulo ?? "").Trim();
        if (titulo.Length == 0) { return DocumentoResult.Fail("El titulo es obligatorio."); }
        if (req.Contenido is null || req.Contenido.Length == 0) { return DocumentoResult.Fail("El archivo esta vacio."); }

        var cat = await _db.DocumentoCategorias.AsNoTracking().FirstOrDefaultAsync(c => c.Id == req.CategoriaId && c.Activa, ct);
        if (cat is null) { return DocumentoResult.Fail("La categoria no existe o esta inactiva."); }

        if (req.CarpetaId is Guid carpetaId)
        {
            var carpeta = await _db.DocumentoCarpetas.AsNoTracking().FirstOrDefaultAsync(c => c.Id == carpetaId, ct);
            if (carpeta is null) { return DocumentoResult.Fail("La carpeta no existe."); }
            if (carpeta.CategoriaId != req.CategoriaId)
            {
                return DocumentoResult.Fail("La carpeta elegida pertenece a otra categoria.");
            }
        }

        // El archivo se escribe ANTES de abrir la transaccion: escribir en disco no es
        // transaccional y tenerlo dentro solo alargaria el bloqueo. Si la transaccion falla queda
        // un archivo huerfano (sin fila que lo apunte), que es preferible a una fila sin archivo.
        var url = await _files.SaveAsync(tenantId, req.Contenido, ExtensionDe(req.NombreArchivo), ct);

        var tx = _db.HasActiveTransaction ? null : await _db.BeginTransactionAsync(ct);
        try
        {
            var doc = new Documento
            {
                TenantId = tenantId,
                CategoriaId = req.CategoriaId,
                CarpetaId = req.CarpetaId,
                Titulo = titulo,
                Descripcion = Recortar(req.Descripcion, 2000),
                NombreArchivoOriginal = Recortar(req.NombreArchivo, 255) ?? "archivo",
                Origen = req.Origen,
                OrigenEntidadId = req.OrigenEntidadId,
                Estado = EstadoDocumento.Vigente,
                Visibilidad = req.Visibilidad,
                NumeroVersiones = 1,
                Activo = true,
                SubidoPorUsuarioId = ActorId
            };
            _db.Documentos.Add(doc);

            var version = new DocumentoVersion
            {
                TenantId = tenantId,
                DocumentoId = doc.Id,
                Numero = 1,
                NombreArchivo = Recortar(req.NombreArchivo, 255) ?? "archivo",
                TipoMime = Recortar(req.TipoMime, 150) ?? "application/octet-stream",
                TamanoBytes = req.Contenido.LongLength,
                UrlStorage = url,
                HashSha256 = Sha256Hex(req.Contenido),
                SubidoPorUsuarioId = ActorId
            };
            _db.DocumentoVersiones.Add(version);

            // Se guarda primero documento + version y DESPUES el puntero: version_actual_id tiene
            // FK a la version, que todavia no existe en BD en el primer SaveChanges.
            await _db.SaveChangesAsync(ct);
            doc.VersionActualId = version.Id;

            AgregarEtiquetas(doc.Id, tenantId, req.EtiquetaIds);
            Auditar(doc.Id, tenantId, TipoEventoDocumento.Subida, null);
            await _db.SaveChangesAsync(ct);

            if (tx is not null) { await tx.CommitAsync(ct); }
            return DocumentoResult.Ok(doc.Id);
        }
        catch
        {
            if (tx is not null) { await tx.RollbackAsync(ct); }
            throw;
        }
        finally
        {
            if (tx is not null) { await tx.DisposeAsync(); }
        }
    }

    public async Task<DocumentoResult> SubirNuevaVersionAsync(
        Guid documentoId, NuevaVersionRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return DocumentoResult.Fail("No hay tenant activo."); }
        if (req.Contenido is null || req.Contenido.Length == 0) { return DocumentoResult.Fail("El archivo esta vacio."); }

        var doc = await _db.Documentos.FirstOrDefaultAsync(d => d.Id == documentoId && d.Activo, ct);
        if (doc is null) { return DocumentoResult.Fail("El documento no existe."); }

        var maxNumero = await _db.DocumentoVersiones
            .Where(v => v.DocumentoId == documentoId)
            .MaxAsync(v => (int?)v.Numero, ct) ?? 0;

        var url = await _files.SaveAsync(tenantId, req.Contenido, ExtensionDe(req.NombreArchivo), ct);

        var tx = _db.HasActiveTransaction ? null : await _db.BeginTransactionAsync(ct);
        try
        {
            var version = new DocumentoVersion
            {
                TenantId = tenantId,
                DocumentoId = documentoId,
                Numero = maxNumero + 1,
                NombreArchivo = Recortar(req.NombreArchivo, 255) ?? "archivo",
                TipoMime = Recortar(req.TipoMime, 150) ?? "application/octet-stream",
                TamanoBytes = req.Contenido.LongLength,
                UrlStorage = url,
                HashSha256 = Sha256Hex(req.Contenido),
                NotasCambio = Recortar(req.NotasCambio, 1000),
                SubidoPorUsuarioId = ActorId
            };
            _db.DocumentoVersiones.Add(version);
            await _db.SaveChangesAsync(ct);

            var anterior = doc.NumeroVersiones;
            doc.VersionActualId = version.Id;
            doc.NumeroVersiones = version.Numero;
            doc.NombreArchivoOriginal = version.NombreArchivo;

            Auditar(documentoId, tenantId, TipoEventoDocumento.NuevaVersion,
                JsonSerializer.Serialize(new { versionAnterior = anterior, versionNueva = version.Numero }));
            await _db.SaveChangesAsync(ct);

            if (tx is not null) { await tx.CommitAsync(ct); }
            return DocumentoResult.Ok(version.Id);
        }
        catch
        {
            if (tx is not null) { await tx.RollbackAsync(ct); }
            throw;
        }
        finally
        {
            if (tx is not null) { await tx.DisposeAsync(); }
        }
    }

    public async Task<DocumentoResult> ActualizarMetadatosAsync(
        Guid id, ActualizarMetadatosRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return DocumentoResult.Fail("No hay tenant activo."); }
        var doc = await _db.Documentos.FirstOrDefaultAsync(d => d.Id == id && d.Activo, ct);
        if (doc is null) { return DocumentoResult.Fail("El documento no existe."); }

        var titulo = (req.Titulo ?? "").Trim();
        if (titulo.Length == 0) { return DocumentoResult.Fail("El titulo es obligatorio."); }

        var cat = await _db.DocumentoCategorias.AsNoTracking().FirstOrDefaultAsync(c => c.Id == req.CategoriaId && c.Activa, ct);
        if (cat is null) { return DocumentoResult.Fail("La categoria no existe o esta inactiva."); }
        if (req.CarpetaId is Guid carpetaId)
        {
            var carpeta = await _db.DocumentoCarpetas.AsNoTracking().FirstOrDefaultAsync(c => c.Id == carpetaId, ct);
            if (carpeta is null) { return DocumentoResult.Fail("La carpeta no existe."); }
            if (carpeta.CategoriaId != req.CategoriaId)
            {
                return DocumentoResult.Fail("La carpeta elegida pertenece a otra categoria.");
            }
        }

        var tx = _db.HasActiveTransaction ? null : await _db.BeginTransactionAsync(ct);
        try
        {
            // Cada clase de cambio deja su propio evento: "cambio metadatos" no dice si el
            // documento se movio de carpeta, y esa es justo la pregunta que se le hace a la bitacora.
            if (doc.CategoriaId != req.CategoriaId)
            {
                Auditar(id, tenantId, TipoEventoDocumento.CambioCategoria,
                    JsonSerializer.Serialize(new { de = doc.CategoriaId, a = req.CategoriaId }));
            }
            if (doc.CarpetaId != req.CarpetaId)
            {
                Auditar(id, tenantId, TipoEventoDocumento.CambioCarpeta,
                    JsonSerializer.Serialize(new { de = doc.CarpetaId, a = req.CarpetaId }));
            }
            if (doc.Visibilidad != req.Visibilidad)
            {
                Auditar(id, tenantId, TipoEventoDocumento.CambioVisibilidad,
                    JsonSerializer.Serialize(new { de = doc.Visibilidad.ToString(), a = req.Visibilidad.ToString() }));
            }

            doc.Titulo = titulo;
            doc.Descripcion = Recortar(req.Descripcion, 2000);
            doc.CategoriaId = req.CategoriaId;
            doc.CarpetaId = req.CarpetaId;
            doc.Visibilidad = req.Visibilidad;

            if (req.EtiquetaIds is not null)
            {
                var actuales = await _db.DocumentoEtiquetas.Where(e => e.DocumentoId == id).ToListAsync(ct);
                var deseadas = req.EtiquetaIds.Distinct().ToHashSet();
                var actualesIds = actuales.Select(a => a.EtiquetaCatalogoId).ToHashSet();
                if (!deseadas.SetEquals(actualesIds))
                {
                    _db.DocumentoEtiquetas.RemoveRange(actuales.Where(a => !deseadas.Contains(a.EtiquetaCatalogoId)));
                    AgregarEtiquetas(id, tenantId, deseadas.Where(x => !actualesIds.Contains(x)).ToList());
                    Auditar(id, tenantId, TipoEventoDocumento.CambioEtiquetas, null);
                }
            }

            Auditar(id, tenantId, TipoEventoDocumento.CambioMetadatos, null);
            await _db.SaveChangesAsync(ct);

            if (tx is not null) { await tx.CommitAsync(ct); }
            return DocumentoResult.Ok(id);
        }
        catch
        {
            if (tx is not null) { await tx.RollbackAsync(ct); }
            throw;
        }
        finally
        {
            if (tx is not null) { await tx.DisposeAsync(); }
        }
    }

    public async Task<DocumentoResult> CambiarEstadoAsync(
        Guid id, EstadoDocumento nuevoEstado, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return DocumentoResult.Fail("No hay tenant activo."); }
        var doc = await _db.Documentos.FirstOrDefaultAsync(d => d.Id == id && d.Activo, ct);
        if (doc is null) { return DocumentoResult.Fail("El documento no existe."); }

        // Solo se permiten las transiciones del ciclo implementado. EnFirma/Firmado estan
        // reservados para la firma electronica, que no existe: aceptarlos dejaria documentos en un
        // estado del que nada sabe sacarlos.
        var permitida = (doc.Estado, nuevoEstado) switch
        {
            (EstadoDocumento.Borrador, EstadoDocumento.Vigente) => true,
            (EstadoDocumento.Vigente, EstadoDocumento.Archivado) => true,
            (EstadoDocumento.Archivado, EstadoDocumento.Vigente) => true,
            _ => false
        };
        if (!permitida)
        {
            return DocumentoResult.Fail($"No se puede pasar de {doc.Estado} a {nuevoEstado}.");
        }

        var anterior = doc.Estado;
        doc.Estado = nuevoEstado;
        Auditar(id, tenantId,
            nuevoEstado == EstadoDocumento.Archivado ? TipoEventoDocumento.Archivado
                : anterior == EstadoDocumento.Archivado ? TipoEventoDocumento.Reactivado
                : TipoEventoDocumento.CambioEstado,
            JsonSerializer.Serialize(new { de = anterior.ToString(), a = nuevoEstado.ToString() }));
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(id);
    }

    public async Task<DocumentoResult> EliminarDocumentoAsync(Guid id, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return DocumentoResult.Fail("No hay tenant activo."); }
        var doc = await _db.Documentos.FirstOrDefaultAsync(d => d.Id == id && d.Activo, ct);
        if (doc is null) { return DocumentoResult.Fail("El documento no existe."); }

        // Regla 5: soft-delete. Ni la fila ni el binario se borran.
        doc.Activo = false;
        doc.Estado = EstadoDocumento.Archivado;
        Auditar(id, tenantId, TipoEventoDocumento.EliminacionLogica, null);
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(id);
    }

    // =======================================================================
    // Destacados
    // =======================================================================

    public async Task<DocumentoResult> AlternarDestacadoPersonalAsync(Guid documentoId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return DocumentoResult.Fail("No hay tenant activo."); }
        var actor = ActorId;
        if (actor == Guid.Empty) { return DocumentoResult.Fail("No hay usuario en sesion."); }

        var existente = await _db.DocumentoDestacadosPersonales
            .FirstOrDefaultAsync(p => p.DocumentoId == documentoId && p.UsuarioId == actor, ct);
        if (existente is not null)
        {
            // El pin personal SI se borra fisicamente: no es historia del documento, es una
            // preferencia de una persona, y conservarla desactivada no le sirve a nadie.
            _db.DocumentoDestacadosPersonales.Remove(existente);
        }
        else
        {
            _db.DocumentoDestacadosPersonales.Add(new DocumentoDestacadoPersonal
            {
                TenantId = tenantId,
                DocumentoId = documentoId,
                UsuarioId = actor
            });
        }
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(documentoId);
    }

    public async Task<DocumentoResult> MarcarDestacadoTenantAsync(
        Guid documentoId, bool destacar, CancellationToken ct = default)
    {
        var doc = await _db.Documentos.FirstOrDefaultAsync(d => d.Id == documentoId && d.Activo, ct);
        if (doc is null) { return DocumentoResult.Fail("El documento no existe."); }
        doc.Destacado = destacar;
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(documentoId);
    }

    // =======================================================================
    // Consumo
    // =======================================================================

    public async Task<(byte[] Contenido, string NombreArchivo, string TipoMime)?> DescargarAsync(
        Guid documentoId, Guid? versionId = null, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return null; }

        var doc = await _db.Documentos.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentoId && d.Activo, ct);
        if (doc is null) { return null; }

        var id = versionId ?? doc.VersionActualId;
        if (id is not Guid vid) { return null; }

        // Se exige que la version sea DE ESTE documento: sin esto, pasar el id de una version
        // ajena permitiria leer un archivo por el que no se paso ningun control.
        var version = await _db.DocumentoVersiones.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == vid && v.DocumentoId == documentoId, ct);
        if (version is null) { return null; }

        var bytes = await _files.ReadAsync(version.UrlStorage, ct);
        if (bytes is null) { return null; }

        _db.DocumentoConsumos.Add(new DocumentoConsumo
        {
            TenantId = tenantId,
            DocumentoId = documentoId,
            VersionId = vid,
            TipoEvento = TipoEventoConsumo.Descarga,
            Dispositivo = DispositivoConsumo.Unknown,
            UsuarioId = ActorId,
            OcurridoAt = DateTimeOffset.UtcNow
        });
        Auditar(documentoId, tenantId, TipoEventoDocumento.Descarga,
            JsonSerializer.Serialize(new { version = version.Numero }));
        await _db.SaveChangesAsync(ct);

        return (bytes, version.NombreArchivo, version.TipoMime);
    }

    public async Task RegistrarVistaAsync(
        Guid documentoId, DispositivoConsumo dispositivo = DispositivoConsumo.Unknown, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return; }
        var doc = await _db.Documentos.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentoId && d.Activo, ct);
        if (doc?.VersionActualId is not Guid vid) { return; }

        _db.DocumentoConsumos.Add(new DocumentoConsumo
        {
            TenantId = tenantId,
            DocumentoId = documentoId,
            VersionId = vid,
            TipoEvento = TipoEventoConsumo.Vista,
            Dispositivo = dispositivo,
            UsuarioId = ActorId,
            OcurridoAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);
    }

    // =======================================================================
    // Bitacora y metricas
    // =======================================================================

    public async Task<IReadOnlyList<AuditoriaEventoDto>> ListarAuditoriaAsync(
        Guid documentoId, CancellationToken ct = default)
        => await _db.DocumentoAuditorias.AsNoTracking()
            .Where(a => a.DocumentoId == documentoId)
            .OrderByDescending(a => a.OcurridoAt)
            .Select(a => new AuditoriaEventoDto(a.Id, a.TipoEvento, a.DetalleJson, a.UsuarioId, null, a.OcurridoAt))
            .ToListAsync(ct);

    public async Task<EstadisticasDocumentoDto> GetEstadisticasAsync(Guid documentoId, CancellationToken ct = default)
    {
        var consumos = await _db.DocumentoConsumos.AsNoTracking()
            .Where(c => c.DocumentoId == documentoId)
            .Select(c => new { c.TipoEvento, c.UsuarioId, c.OcurridoAt })
            .ToListAsync(ct);

        return new EstadisticasDocumentoDto(
            consumos.Count(c => c.TipoEvento == TipoEventoConsumo.Vista),
            consumos.Count(c => c.TipoEvento == TipoEventoConsumo.Descarga),
            consumos.Select(c => c.UsuarioId).Distinct().Count(),
            consumos.Count == 0 ? null : consumos.Max(c => c.OcurridoAt));
    }

    public async Task<ResumenDocumentosDto> GetResumenAsync(CancellationToken ct = default)
    {
        var desde = DateTimeOffset.UtcNow.AddDays(-30);
        var totalDocs = await _db.Documentos.AsNoTracking().CountAsync(d => d.Activo, ct);
        var recientes = await _db.Documentos.AsNoTracking().CountAsync(d => d.Activo && d.CreatedAt >= desde, ct);
        var totalCats = await _db.DocumentoCategorias.AsNoTracking().CountAsync(c => c.Activa, ct);
        var totalCarpetas = await _db.DocumentoCarpetas.AsNoTracking().CountAsync(c => c.Activa, ct);
        // Se suman TODAS las versiones: es el espacio que el archivo ocupa de verdad en disco,
        // no solo el de las versiones vigentes.
        var bytes = await _db.DocumentoVersiones.AsNoTracking().SumAsync(v => (long?)v.TamanoBytes, ct) ?? 0;

        return new ResumenDocumentosDto(totalDocs, totalCats, totalCarpetas, bytes, recientes);
    }

    // =======================================================================
    // Apoyo
    // =======================================================================

    private void AgregarEtiquetas(Guid documentoId, Guid tenantId, IEnumerable<Guid>? etiquetaIds)
    {
        if (etiquetaIds is null) { return; }
        foreach (var etiquetaId in etiquetaIds.Distinct())
        {
            _db.DocumentoEtiquetas.Add(new DocumentoEtiqueta
            {
                TenantId = tenantId,
                DocumentoId = documentoId,
                EtiquetaCatalogoId = etiquetaId
            });
        }
    }

    private void Auditar(Guid documentoId, Guid tenantId, TipoEventoDocumento tipo, string? detalleJson)
        => _db.DocumentoAuditorias.Add(new DocumentoAuditoria
        {
            TenantId = tenantId,
            DocumentoId = documentoId,
            TipoEvento = tipo,
            DetalleJson = detalleJson,
            UsuarioId = ActorId,
            OcurridoAt = DateTimeOffset.UtcNow
        });

    private static string Sha256Hex(byte[] contenido)
        => Convert.ToHexString(SHA256.HashData(contenido)).ToLowerInvariant();

    /// <summary>Extension en minusculas con punto, o ".bin" si el nombre no trae ninguna.</summary>
    private static string ExtensionDe(string? nombreArchivo)
    {
        var ext = System.IO.Path.GetExtension(nombreArchivo ?? "");
        return string.IsNullOrWhiteSpace(ext) ? ".bin" : ext.ToLowerInvariant();
    }

    private static string? Recortar(string? valor, int max)
    {
        if (string.IsNullOrWhiteSpace(valor)) { return null; }
        var v = valor.Trim();
        return v.Length <= max ? v : v[..max];
    }
}
