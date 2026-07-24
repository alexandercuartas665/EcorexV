using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Documentos;

/// <summary>
/// Implementacion de la mitad "Expedientes" (TRD). Ver <see cref="IExpedienteService"/>.
///
/// Nota sobre el origen: PROPIA marcaba como obligatorias "las 3 primeras" tipologias del
/// expediente con un <c>orden &lt; 3</c> a fuego, porque su subserie no guardaba esa bandera.
/// Aqui SubserieTipologia si la tiene, asi que la obligatoriedad se COPIA de la TRD, que es lo
/// que el usuario configuro.
/// </summary>
public sealed class ExpedienteService : IExpedienteService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IDocumentoFileStore _files;

    public ExpedienteService(IApplicationDbContext db, ITenantContext tenant, IDocumentoFileStore files)
    {
        _db = db;
        _tenant = tenant;
        _files = files;
    }

    // =======================================================================
    // Configuracion documental (TRD)
    // =======================================================================

    public async Task<IReadOnlyList<TrdSerieDto>> ListarTrdAsync(CancellationToken ct = default)
    {
        var series = await _db.SeriesDocumentales.AsNoTracking()
            .Where(s => s.Activa)
            .OrderBy(s => s.Orden).ThenBy(s => s.Nombre)
            .ToListAsync(ct);
        if (series.Count == 0) { return Array.Empty<TrdSerieDto>(); }

        // Tres consultas planas y armado en memoria: recorrer el arbol por nivel seria N+1.
        var serieIds = series.Select(s => s.Id).ToList();
        var subseries = await _db.SubseriesDocumentales.AsNoTracking()
            .Where(s => serieIds.Contains(s.SerieId) && s.Activa)
            .OrderBy(s => s.Orden).ThenBy(s => s.Nombre)
            .ToListAsync(ct);

        var subIds = subseries.Select(s => s.Id).ToList();
        var tipologias = await _db.SubserieTipologias.AsNoTracking()
            .Where(t => subIds.Contains(t.SubserieId))
            .OrderBy(t => t.Orden)
            .ToListAsync(ct);
        var campos = await _db.SubserieCampos.AsNoTracking()
            .Where(c => subIds.Contains(c.SubserieId))
            .OrderBy(c => c.Orden)
            .ToListAsync(ct);

        var tipsPorSub = tipologias.GroupBy(t => t.SubserieId).ToDictionary(g => g.Key, g => g.ToList());
        var camposPorSub = campos.GroupBy(c => c.SubserieId).ToDictionary(g => g.Key, g => g.ToList());
        var subsPorSerie = subseries.GroupBy(s => s.SerieId).ToDictionary(g => g.Key, g => g.ToList());

        return series.Select(s => new TrdSerieDto(
            s.Id, s.Nombre, s.Orden,
            (subsPorSerie.TryGetValue(s.Id, out var subs) ? subs : new List<SubserieDocumental>())
                .Select(sub => new TrdSubserieDto(
                    sub.Id, sub.SerieId, sub.Nombre, sub.Orden,
                    (tipsPorSub.TryGetValue(sub.Id, out var tp) ? tp : new List<SubserieTipologia>())
                        .Select(t => new TrdTipologiaDto(t.Id, t.Nombre, t.Obligatoria, t.Orden)).ToList(),
                    (camposPorSub.TryGetValue(sub.Id, out var cp) ? cp : new List<SubserieCampo>())
                        .Select(c => new TrdCampoDto(c.Id, c.Clave, c.Label, c.Orden)).ToList()))
                .ToList()))
            .ToList();
    }

    public async Task<DocumentoResult> CrearSerieAsync(CrearSerieRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return DocumentoResult.Fail("No hay tenant activo."); }
        var nombre = (req.Nombre ?? "").Trim();
        if (nombre.Length == 0) { return DocumentoResult.Fail("El nombre de la serie es obligatorio."); }
        if (await _db.SeriesDocumentales.AnyAsync(s => s.Nombre == nombre, ct))
        {
            return DocumentoResult.Fail($"Ya existe una serie \"{nombre}\".");
        }

        var orden = await _db.SeriesDocumentales.MaxAsync(s => (int?)s.Orden, ct) ?? -1;
        var serie = new SerieDocumental { TenantId = tenantId, Nombre = nombre, Orden = orden + 1, Activa = true };
        _db.SeriesDocumentales.Add(serie);
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(serie.Id);
    }

    public async Task<DocumentoResult> EliminarSerieAsync(Guid id, CancellationToken ct = default)
    {
        var serie = await _db.SeriesDocumentales.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (serie is null) { return DocumentoResult.Fail("La serie no existe."); }

        // Soft-delete en cascada logica: retirar la serie retira sus subseries de la TRD. Los
        // expedientes abiertos no se tocan (llevan el nombre copiado).
        var subs = await _db.SubseriesDocumentales.Where(s => s.SerieId == id).ToListAsync(ct);
        foreach (var s in subs) { s.Activa = false; }
        serie.Activa = false;
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(id);
    }

    public async Task<DocumentoResult> CrearSubserieAsync(CrearSubserieRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return DocumentoResult.Fail("No hay tenant activo."); }
        var nombre = (req.Nombre ?? "").Trim();
        if (nombre.Length == 0) { return DocumentoResult.Fail("El nombre de la subserie es obligatorio."); }

        var serie = await _db.SeriesDocumentales.AsNoTracking().FirstOrDefaultAsync(s => s.Id == req.SerieId && s.Activa, ct);
        if (serie is null) { return DocumentoResult.Fail("La serie no existe."); }
        if (await _db.SubseriesDocumentales.AnyAsync(s => s.SerieId == req.SerieId && s.Nombre == nombre, ct))
        {
            return DocumentoResult.Fail($"Ya existe una subserie \"{nombre}\" en esa serie.");
        }

        var orden = await _db.SubseriesDocumentales.Where(s => s.SerieId == req.SerieId)
            .MaxAsync(s => (int?)s.Orden, ct) ?? -1;
        var sub = new SubserieDocumental
        {
            TenantId = tenantId,
            SerieId = req.SerieId,
            Nombre = nombre,
            Orden = orden + 1,
            Activa = true
        };
        _db.SubseriesDocumentales.Add(sub);
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(sub.Id);
    }

    public async Task<DocumentoResult> EliminarSubserieAsync(Guid id, CancellationToken ct = default)
    {
        var sub = await _db.SubseriesDocumentales.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sub is null) { return DocumentoResult.Fail("La subserie no existe."); }
        sub.Activa = false;
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(id);
    }

    public async Task<DocumentoResult> CrearTipologiaCfgAsync(CrearTipologiaCfgRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return DocumentoResult.Fail("No hay tenant activo."); }
        var nombre = (req.Nombre ?? "").Trim();
        if (nombre.Length == 0) { return DocumentoResult.Fail("El nombre de la tipologia es obligatorio."); }

        var sub = await _db.SubseriesDocumentales.AsNoTracking().FirstOrDefaultAsync(s => s.Id == req.SubserieId, ct);
        if (sub is null) { return DocumentoResult.Fail("La subserie no existe."); }

        var orden = await _db.SubserieTipologias.Where(t => t.SubserieId == req.SubserieId)
            .MaxAsync(t => (int?)t.Orden, ct) ?? -1;
        var tip = new SubserieTipologia
        {
            TenantId = tenantId,
            SubserieId = req.SubserieId,
            Nombre = nombre,
            Obligatoria = req.Obligatoria,
            Orden = orden + 1
        };
        _db.SubserieTipologias.Add(tip);
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(tip.Id);
    }

    public async Task<DocumentoResult> EliminarTipologiaCfgAsync(Guid id, CancellationToken ct = default)
    {
        var tip = await _db.SubserieTipologias.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tip is null) { return DocumentoResult.Fail("La tipologia no existe."); }
        // La TRD es configuracion pura: aqui si se borra de verdad. Los expedientes ya abiertos
        // conservan su copia, asi que no se pierde historia.
        _db.SubserieTipologias.Remove(tip);
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(id);
    }

    public async Task<DocumentoResult> CrearCampoCfgAsync(CrearCampoCfgRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return DocumentoResult.Fail("No hay tenant activo."); }
        var label = (req.Label ?? "").Trim();
        if (label.Length == 0) { return DocumentoResult.Fail("El nombre del campo es obligatorio."); }

        var sub = await _db.SubseriesDocumentales.AsNoTracking().FirstOrDefaultAsync(s => s.Id == req.SubserieId, ct);
        if (sub is null) { return DocumentoResult.Fail("La subserie no existe."); }

        // La clave se deriva del label y debe ser unica dentro de la subserie: es la que viaja en
        // el JSON de metadatos y repetida haria ambiguo el valor.
        var baseClave = Slug(label);
        var usadas = await _db.SubserieCampos.Where(c => c.SubserieId == req.SubserieId)
            .Select(c => c.Clave).ToListAsync(ct);
        var clave = ClaveUnica(baseClave, usadas);

        var orden = await _db.SubserieCampos.Where(c => c.SubserieId == req.SubserieId)
            .MaxAsync(c => (int?)c.Orden, ct) ?? -1;
        var campo = new SubserieCampo
        {
            TenantId = tenantId,
            SubserieId = req.SubserieId,
            Clave = clave,
            Label = label,
            Orden = orden + 1
        };
        _db.SubserieCampos.Add(campo);
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(campo.Id);
    }

    public async Task<DocumentoResult> EliminarCampoCfgAsync(Guid id, CancellationToken ct = default)
    {
        var campo = await _db.SubserieCampos.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (campo is null) { return DocumentoResult.Fail("El campo no existe."); }
        _db.SubserieCampos.Remove(campo);
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(id);
    }

    // =======================================================================
    // Expedientes
    // =======================================================================

    public async Task<IReadOnlyList<ExpedienteListaDto>> ListarExpedientesAsync(CancellationToken ct = default)
    {
        var exps = await _db.Expedientes.AsNoTracking()
            .Where(e => e.Activo)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(ct);
        if (exps.Count == 0) { return Array.Empty<ExpedienteListaDto>(); }

        // Los contadores del checklist salen de UNA consulta agregada, no de una por expediente.
        var ids = exps.Select(e => e.Id).ToList();
        var resumen = await _db.ExpedienteTipologias.AsNoTracking()
            .Where(t => ids.Contains(t.ExpedienteId))
            .GroupBy(t => t.ExpedienteId)
            .Select(g => new
            {
                g.Key,
                Total = g.Count(),
                Cargadas = g.Count(x => x.ArchivoUrl != null),
                PendientesObligatorias = g.Count(x => x.Obligatoria && x.ArchivoUrl == null)
            })
            .ToDictionaryAsync(x => x.Key, x => x, ct);

        return exps.Select(e =>
        {
            resumen.TryGetValue(e.Id, out var r);
            return new ExpedienteListaDto(
                e.Id, e.Codigo, e.Nombre, e.Serie, e.Subserie,
                r?.Total ?? 0, r?.Cargadas ?? 0, r?.PendientesObligatorias ?? 0, e.CreatedAt);
        }).ToList();
    }

    public async Task<ExpedienteDetalleDto?> GetExpedienteAsync(Guid id, CancellationToken ct = default)
    {
        var e = await _db.Expedientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return null; }

        var campos = await _db.ExpedienteCampos.AsNoTracking()
            .Where(c => c.ExpedienteId == id).OrderBy(c => c.Orden).ToListAsync(ct);
        var tips = await _db.ExpedienteTipologias.AsNoTracking()
            .Where(t => t.ExpedienteId == id).OrderBy(t => t.Orden).ToListAsync(ct);

        var camposDto = campos.Select(c => new ExpedienteCampoDto(c.Id, c.Clave, c.Label, c.Orden)).ToList();

        var tipsDto = tips.Select(t => new ExpedienteTipologiaDto(
            t.Id, t.Nombre, t.Obligatoria, t.ArchivoUrl != null,
            t.ArchivoNombre, t.ArchivoMime, t.ArchivoTamano, t.ArchivoUrl, t.Orden,
            LeerMeta(t.MetaJson, campos))).ToList();

        return new ExpedienteDetalleDto(
            e.Id, e.Codigo, e.Nombre, e.Serie, e.Subserie,
            tips.Count, tips.Count(t => t.ArchivoUrl != null), camposDto, tipsDto);
    }

    public async Task<DocumentoResult> CrearExpedienteAsync(CrearExpedienteRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return DocumentoResult.Fail("No hay tenant activo."); }
        var nombre = (req.Nombre ?? "").Trim();
        if (nombre.Length == 0) { return DocumentoResult.Fail("El nombre del expediente es obligatorio."); }

        var sub = await _db.SubseriesDocumentales.AsNoTracking()
            .Include(s => s.Serie)
            .FirstOrDefaultAsync(s => s.Id == req.SubserieId && s.Activa, ct);
        if (sub is null) { return DocumentoResult.Fail("Elige la serie y la subserie."); }

        var serieNombre = sub.Serie?.Nombre ?? "";
        var tipsCfg = await _db.SubserieTipologias.AsNoTracking()
            .Where(t => t.SubserieId == sub.Id).OrderBy(t => t.Orden).ToListAsync(ct);
        var camposCfg = await _db.SubserieCampos.AsNoTracking()
            .Where(c => c.SubserieId == sub.Id).OrderBy(c => c.Orden).ToListAsync(ct);

        var prefijo = Prefijo(serieNombre);
        var codigo = await SiguienteCodigoAsync(prefijo, ct);

        var tx = _db.HasActiveTransaction ? null : await _db.BeginTransactionAsync(ct);
        try
        {
            var exp = new Expediente
            {
                TenantId = tenantId,
                Codigo = codigo,
                Nombre = nombre,
                Serie = serieNombre,
                Subserie = sub.Nombre,
                SubserieId = sub.Id,
                Activo = true
            };
            _db.Expedientes.Add(exp);

            // Copia (no referencia) de la plantilla: el checklist del expediente queda congelado.
            var orden = 0;
            foreach (var t in tipsCfg)
            {
                _db.ExpedienteTipologias.Add(new ExpedienteTipologia
                {
                    TenantId = tenantId,
                    ExpedienteId = exp.Id,
                    Nombre = t.Nombre,
                    Obligatoria = t.Obligatoria,
                    Orden = orden++
                });
            }
            var ordenCampo = 0;
            foreach (var c in camposCfg)
            {
                _db.ExpedienteCampos.Add(new ExpedienteCampo
                {
                    TenantId = tenantId,
                    ExpedienteId = exp.Id,
                    Clave = c.Clave,
                    Label = c.Label,
                    Orden = ordenCampo++
                });
            }

            await _db.SaveChangesAsync(ct);
            if (tx is not null) { await tx.CommitAsync(ct); }
            return DocumentoResult.Ok(exp.Id);
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

    public async Task<DocumentoResult> EliminarExpedienteAsync(Guid id, CancellationToken ct = default)
    {
        var exp = await _db.Expedientes.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (exp is null) { return DocumentoResult.Fail("El expediente no existe."); }
        exp.Activo = false;
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(id);
    }

    public async Task<DocumentoResult> AgregarTipologiaAsync(
        Guid expedienteId, AgregarTipologiaExpRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return DocumentoResult.Fail("No hay tenant activo."); }
        var nombre = (req.Nombre ?? "").Trim();
        if (nombre.Length == 0) { return DocumentoResult.Fail("El nombre de la tipologia es obligatorio."); }

        var exp = await _db.Expedientes.AsNoTracking().FirstOrDefaultAsync(e => e.Id == expedienteId && e.Activo, ct);
        if (exp is null) { return DocumentoResult.Fail("El expediente no existe."); }

        var orden = await _db.ExpedienteTipologias.Where(t => t.ExpedienteId == expedienteId)
            .MaxAsync(t => (int?)t.Orden, ct) ?? -1;
        var tip = new ExpedienteTipologia
        {
            TenantId = tenantId,
            ExpedienteId = expedienteId,
            Nombre = nombre,
            Obligatoria = req.Obligatoria,
            Orden = orden + 1
        };
        _db.ExpedienteTipologias.Add(tip);
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(tip.Id);
    }

    public async Task<DocumentoResult> EliminarTipologiaAsync(Guid tipologiaId, CancellationToken ct = default)
    {
        var tip = await _db.ExpedienteTipologias.FirstOrDefaultAsync(t => t.Id == tipologiaId, ct);
        if (tip is null) { return DocumentoResult.Fail("La casilla no existe."); }
        if (tip.ArchivoUrl is not null)
        {
            return DocumentoResult.Fail("La casilla tiene un archivo cargado: quitalo antes de eliminarla.");
        }
        _db.ExpedienteTipologias.Remove(tip);
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(tipologiaId);
    }

    public async Task<DocumentoResult> AgregarCampoAsync(
        Guid expedienteId, AgregarCampoExpRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return DocumentoResult.Fail("No hay tenant activo."); }
        var label = (req.Label ?? "").Trim();
        if (label.Length == 0) { return DocumentoResult.Fail("El nombre del campo es obligatorio."); }

        var exp = await _db.Expedientes.AsNoTracking().FirstOrDefaultAsync(e => e.Id == expedienteId && e.Activo, ct);
        if (exp is null) { return DocumentoResult.Fail("El expediente no existe."); }

        var usadas = await _db.ExpedienteCampos.Where(c => c.ExpedienteId == expedienteId)
            .Select(c => c.Clave).ToListAsync(ct);
        var clave = ClaveUnica(Slug(label), usadas);

        var orden = await _db.ExpedienteCampos.Where(c => c.ExpedienteId == expedienteId)
            .MaxAsync(c => (int?)c.Orden, ct) ?? -1;
        _db.ExpedienteCampos.Add(new ExpedienteCampo
        {
            TenantId = tenantId,
            ExpedienteId = expedienteId,
            Clave = clave,
            Label = label,
            Orden = orden + 1
        });
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(expedienteId);
    }

    public async Task<DocumentoResult> EliminarCampoAsync(Guid campoId, CancellationToken ct = default)
    {
        var campo = await _db.ExpedienteCampos.FirstOrDefaultAsync(c => c.Id == campoId, ct);
        if (campo is null) { return DocumentoResult.Fail("El campo no existe."); }
        _db.ExpedienteCampos.Remove(campo);
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(campoId);
    }

    public async Task<DocumentoResult> CargarTipologiaAsync(
        Guid tipologiaId, CargarTipologiaRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return DocumentoResult.Fail("No hay tenant activo."); }
        if (req.Contenido is null || req.Contenido.Length == 0) { return DocumentoResult.Fail("El archivo esta vacio."); }

        var tip = await _db.ExpedienteTipologias.FirstOrDefaultAsync(t => t.Id == tipologiaId, ct);
        if (tip is null) { return DocumentoResult.Fail("La casilla no existe."); }

        var url = await _files.SaveAsync(tenantId, req.Contenido, ExtensionDe(req.NombreArchivo), ct);

        // Reemplazar deja el binario anterior en disco a proposito: es la misma politica que en el
        // Archivo central (nunca se destruye un archivo que alguien pudo haber citado).
        tip.ArchivoUrl = url;
        tip.ArchivoNombre = Recortar(req.NombreArchivo, 255) ?? "archivo";
        tip.ArchivoMime = Recortar(req.TipoMime, 150) ?? "application/octet-stream";
        tip.ArchivoTamano = req.Contenido.LongLength;
        tip.ArchivoHashSha256 = Convert.ToHexString(SHA256.HashData(req.Contenido)).ToLowerInvariant();

        if (req.Meta is not null)
        {
            var dict = req.Meta
                .Where(m => !string.IsNullOrWhiteSpace(m.Clave))
                .ToDictionary(m => m.Clave, m => m.Valor, StringComparer.Ordinal);
            tip.MetaJson = dict.Count == 0 ? null : JsonSerializer.Serialize(dict);
        }

        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(tipologiaId);
    }

    public async Task<DocumentoResult> QuitarArchivoAsync(Guid tipologiaId, CancellationToken ct = default)
    {
        var tip = await _db.ExpedienteTipologias.FirstOrDefaultAsync(t => t.Id == tipologiaId, ct);
        if (tip is null) { return DocumentoResult.Fail("La casilla no existe."); }

        tip.ArchivoUrl = null;
        tip.ArchivoNombre = null;
        tip.ArchivoMime = null;
        tip.ArchivoTamano = 0;
        tip.ArchivoHashSha256 = null;
        tip.MetaJson = null;
        await _db.SaveChangesAsync(ct);
        return DocumentoResult.Ok(tipologiaId);
    }

    public async Task<(byte[] Contenido, string NombreArchivo, string TipoMime)?> DescargarTipologiaAsync(
        Guid tipologiaId, CancellationToken ct = default)
    {
        var tip = await _db.ExpedienteTipologias.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tipologiaId, ct);
        if (tip?.ArchivoUrl is null) { return null; }

        var bytes = await _files.ReadAsync(tip.ArchivoUrl, ct);
        if (bytes is null) { return null; }
        return (bytes, tip.ArchivoNombre ?? "archivo", tip.ArchivoMime ?? "application/octet-stream");
    }

    // =======================================================================
    // Apoyo
    // =======================================================================

    /// <summary>
    /// Consecutivo por serie: EXP-ACT-001. Se calcula desde el MAXIMO existente y no desde un
    /// COUNT: con un count, borrar un expediente reutilizaria un codigo ya emitido, que es
    /// exactamente lo que un consecutivo no puede hacer.
    /// </summary>
    private async Task<string> SiguienteCodigoAsync(string prefijo, CancellationToken ct)
    {
        var patron = $"EXP-{prefijo}-";
        var codigos = await _db.Expedientes.AsNoTracking()
            .Where(e => e.Codigo.StartsWith(patron))
            .Select(e => e.Codigo)
            .ToListAsync(ct);

        var max = 0;
        foreach (var c in codigos)
        {
            var cola = c[patron.Length..];
            if (int.TryParse(cola, out var n) && n > max) { max = n; }
        }
        return $"{patron}{max + 1:D3}";
    }

    /// <summary>Tres letras de la serie para el codigo del expediente (ACT de "Actas...").</summary>
    private static string Prefijo(string serie)
    {
        var letras = new string((serie ?? "").Where(char.IsLetter).Take(3).ToArray()).ToUpperInvariant();
        return letras.Length == 0 ? "EXP" : letras.PadRight(3, 'X');
    }

    /// <summary>Convierte un texto a clave tecnica: minusculas, sin acentos raros, separada por "_".</summary>
    private static string Slug(string texto)
    {
        var sb = new StringBuilder();
        foreach (var c in texto.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) { sb.Append(c); }
            else if (sb.Length > 0 && sb[^1] != '_') { sb.Append('_'); }
        }
        var slug = sb.ToString().Trim('_');
        return slug.Length == 0 ? "campo" : slug;
    }

    /// <summary>Agrega un sufijo numerico si la clave ya existe en el mismo ambito.</summary>
    private static string ClaveUnica(string baseClave, IReadOnlyCollection<string> usadas)
    {
        if (!usadas.Contains(baseClave)) { return baseClave; }
        for (var i = 2; i < 1000; i++)
        {
            var candidata = $"{baseClave}_{i}";
            if (!usadas.Contains(candidata)) { return candidata; }
        }
        return $"{baseClave}_{Guid.NewGuid():N}"[..80];
    }

    /// <summary>
    /// Empareja el JSON de metadatos con los campos del expediente. Se recorre la lista de CAMPOS
    /// (no las claves del JSON) para que un campo agregado despues aparezca vacio en vez de faltar.
    /// </summary>
    private static IReadOnlyList<ExpedienteMetaPairDto> LeerMeta(
        string? metaJson, IReadOnlyList<ExpedienteCampo> campos)
    {
        if (campos.Count == 0) { return Array.Empty<ExpedienteMetaPairDto>(); }

        Dictionary<string, string?>? valores = null;
        if (!string.IsNullOrWhiteSpace(metaJson))
        {
            try { valores = JsonSerializer.Deserialize<Dictionary<string, string?>>(metaJson); }
            catch (JsonException) { valores = null; }
        }

        return campos.Select(c => new ExpedienteMetaPairDto(
            c.Clave, c.Label,
            valores is not null && valores.TryGetValue(c.Clave, out var v) ? v : null)).ToList();
    }

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
