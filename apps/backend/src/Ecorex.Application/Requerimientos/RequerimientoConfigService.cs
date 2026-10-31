using Ecorex.Application.Actividades;
using Ecorex.Application.Common;
using Ecorex.Application.Forms;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Requerimientos;

public sealed class RequerimientoConfigService : IRequerimientoConfigService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IActividadCatalogoService _catalogo;
    private readonly IFormDefinitionService _forms;

    public RequerimientoConfigService(IApplicationDbContext db, ITenantContext tenant,
        IActividadCatalogoService catalogo, IFormDefinitionService forms)
    {
        _db = db;
        _tenant = tenant;
        _catalogo = catalogo;
        _forms = forms;
    }

    public async Task<IReadOnlyList<RequerimientoOrigenDto>> ListOrigenesAsync(CancellationToken ct = default)
    {
        var origenes = await _db.RequerimientoOrigenes.AsNoTracking().OrderBy(o => o.Nombre).ToListAsync(ct);
        var (subs, forms) = await NombresAsync(ct);
        return origenes.Select(o => Map(o, subs, forms)).ToList();
    }

    public async Task<RequerimientoOrigenDto?> SaveOrigenAsync(SaveRequerimientoOrigenRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        var name = (req.Nombre ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) { throw new InvalidOperationException("El nombre del origen es obligatorio."); }

        var clash = await _db.RequerimientoOrigenes
            .AnyAsync(o => o.Nombre == name && (req.Id == null || o.Id != req.Id), ct);
        if (clash) { throw new InvalidOperationException($"Ya existe un origen '{name}'."); }

        RequerimientoOrigen entity;
        if (req.Id is Guid id)
        {
            var existing = await _db.RequerimientoOrigenes.FirstOrDefaultAsync(o => o.Id == id, ct);
            if (existing is null) { return null; }
            entity = existing;
        }
        else
        {
            entity = new RequerimientoOrigen { TenantId = tenantId };
            _db.RequerimientoOrigenes.Add(entity);
        }

        entity.Nombre = name;
        entity.Area = (req.Area ?? "").Trim();
        entity.TipoFuente = req.TipoFuente;
        // La fuente solo aplica cuando el tipo la usa (Formulario/Contenedor); en Manual se limpia.
        entity.FuenteRefId = req.TipoFuente == Domain.Enums.RequerimientoFuenteTipo.Manual ? null : req.FuenteRefId;
        entity.SubcategoriaCompraId = req.SubcategoriaCompraId;
        entity.SubcategoriaCotizacionId = req.SubcategoriaCotizacionId;
        entity.Activo = req.Activo;
        await _db.SaveChangesAsync(ct);

        var (subs, forms) = await NombresAsync(ct);
        return Map(entity, subs, forms);
    }

    public async Task<bool> DeleteOrigenAsync(Guid id, CancellationToken ct = default)
    {
        var o = await _db.RequerimientoOrigenes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null) { return false; }
        _db.RequerimientoOrigenes.Remove(o);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ---- Modo de trabajo del modulo (Consolidado/Dividido): config por tenant en TenantConfiguration ----
    private const string ModoKey = "requerimientos.modo";

    public async Task<RequerimientoModo> GetModoAsync(CancellationToken ct = default)
    {
        var val = await _db.TenantConfigurations.AsNoTracking()
            .Where(c => c.ConfigKey == ModoKey)
            .Select(c => c.ConfigValue)
            .FirstOrDefaultAsync(ct);
        return Enum.TryParse<RequerimientoModo>(val, ignoreCase: true, out var m) ? m : RequerimientoModo.Consolidado;
    }

    public async Task SetModoAsync(RequerimientoModo modo, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return; }
        var row = await _db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == ModoKey, ct);
        if (row is null)
        {
            _db.TenantConfigurations.Add(new TenantConfiguration { TenantId = tid, ConfigKey = ModoKey, ConfigValue = modo.ToString() });
        }
        else
        {
            row.ConfigValue = modo.ToString();
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<RequerimientoOpcionDto>> ListSubcategoriasAsync(CancellationToken ct = default)
        => (await _catalogo.ListSubcategoriasAsync(null, includeArchived: false, ct))
            .OrderBy(s => s.Nombre)
            .Select(s => new RequerimientoOpcionDto(s.Id, s.Nombre)).ToList();

    public async Task<IReadOnlyList<RequerimientoOpcionDto>> ListFormulariosAsync(CancellationToken ct = default)
        => (await _forms.ListAsync(includeArchived: false, ct))
            .OrderBy(f => f.Title)
            .Select(f => new RequerimientoOpcionDto(f.Id, f.Title)).ToList();

    private async Task<(Dictionary<Guid, string> Subs, Dictionary<Guid, string> Forms)> NombresAsync(CancellationToken ct)
    {
        var subs = (await _catalogo.ListSubcategoriasAsync(null, includeArchived: true, ct))
            .GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First().Nombre);
        var forms = (await _forms.ListAsync(includeArchived: true, ct))
            .GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First().Title);
        return (subs, forms);
    }

    // ---- Ola 4: matriz de proveedores ----

    public async Task<IReadOnlyList<RequerimientoReglaDto>> ListReglasAsync(CancellationToken ct = default)
    {
        var reglas = await _db.RequerimientoProveedorReglas.AsNoTracking()
            .OrderBy(r => r.Dimension).ThenBy(r => r.Prioridad).ToListAsync(ct);
        if (reglas.Count == 0) { return new List<RequerimientoReglaDto>(); }

        var provIds = reglas.Select(r => r.ProveedorId).Distinct().ToList();
        var provs = await _db.Terceros.AsNoTracking().Where(t => provIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Nombre, ct);

        var nombres = new Dictionary<(RequerimientoDimension, string), string>();
        foreach (var grp in reglas.GroupBy(r => r.Dimension))
        {
            var guids = grp.Select(r => r.ValorDimension).Distinct()
                .Where(v => Guid.TryParse(v, out _)).Select(Guid.Parse).ToList();
            var cat = await ValoresCatalogoAsync(grp.Key, guids, ct);
            foreach (var r in grp)
            {
                if (Guid.TryParse(r.ValorDimension, out var g) && cat.TryGetValue(g, out var nm))
                {
                    nombres[(grp.Key, r.ValorDimension)] = nm;
                }
            }
        }

        return reglas.Select(r => new RequerimientoReglaDto(
            r.Id, r.Dimension, r.ValorDimension,
            nombres.TryGetValue((r.Dimension, r.ValorDimension), out var vn) ? vn : r.ValorDimension,
            r.ProveedorId, provs.TryGetValue(r.ProveedorId, out var pn) ? pn : null,
            r.Prioridad, r.Aprendida, r.Activo)).ToList();
    }

    public async Task<RequerimientoReglaDto?> SaveReglaAsync(SaveReglaRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        var valor = (req.ValorDimension ?? "").Trim();
        if (string.IsNullOrWhiteSpace(valor)) { throw new InvalidOperationException("Elige el valor de la dimension."); }
        if (req.ProveedorId == Guid.Empty) { throw new InvalidOperationException("Elige un proveedor."); }

        var clash = await _db.RequerimientoProveedorReglas.AnyAsync(x =>
            x.Dimension == req.Dimension && x.ValorDimension == valor && x.ProveedorId == req.ProveedorId
            && (req.Id == null || x.Id != req.Id), ct);
        if (clash) { throw new InvalidOperationException("Ya existe esa regla (misma dimension, valor y proveedor)."); }

        RequerimientoProveedorRegla entity;
        if (req.Id is Guid id)
        {
            var existing = await _db.RequerimientoProveedorReglas.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (existing is null) { return null; }
            entity = existing;
        }
        else
        {
            entity = new RequerimientoProveedorRegla { TenantId = tenantId };
            _db.RequerimientoProveedorReglas.Add(entity);
        }
        entity.Dimension = req.Dimension;
        entity.ValorDimension = valor;
        entity.ProveedorId = req.ProveedorId;
        entity.Prioridad = req.Prioridad;
        entity.Activo = req.Activo;
        entity.Aprendida = entity.Aprendida; // conserva el flag si venia aprendida
        await _db.SaveChangesAsync(ct);

        return (await ListReglasAsync(ct)).FirstOrDefault(r => r.Id == entity.Id);
    }

    public async Task<bool> DeleteReglaAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _db.RequerimientoProveedorReglas.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) { return false; }
        _db.RequerimientoProveedorReglas.Remove(r);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<RequerimientoOpcionDto>> ListProveedoresAsync(CancellationToken ct = default)
        => await _db.Terceros.AsNoTracking().OrderBy(t => t.Nombre)
            .Select(t => new RequerimientoOpcionDto(t.Id, t.Nombre)).Take(2000).ToListAsync(ct);

    public async Task<IReadOnlyList<RequerimientoOpcionDto>> ListValoresDimensionAsync(RequerimientoDimension dimension, CancellationToken ct = default)
        => dimension switch
        {
            RequerimientoDimension.Categoria => await _db.ItemTypes.AsNoTracking().OrderBy(x => x.Name)
                .Select(x => new RequerimientoOpcionDto(x.Id, x.Name)).ToListAsync(ct),
            RequerimientoDimension.Grupo => await _db.ItemGroups.AsNoTracking().OrderBy(x => x.Name)
                .Select(x => new RequerimientoOpcionDto(x.Id, x.Name)).ToListAsync(ct),
            RequerimientoDimension.Subgrupo => await _db.ItemSubgroups.AsNoTracking().OrderBy(x => x.Name)
                .Select(x => new RequerimientoOpcionDto(x.Id, x.Name)).ToListAsync(ct),
            RequerimientoDimension.Marca => await _db.Brands.AsNoTracking().OrderBy(x => x.Name)
                .Select(x => new RequerimientoOpcionDto(x.Id, x.Name)).ToListAsync(ct),
            RequerimientoDimension.Referencia => await _db.Items.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
                .Select(x => new RequerimientoOpcionDto(x.Id, (x.Sku != null ? x.Sku + " - " : "") + x.Name)).Take(2000).ToListAsync(ct),
            _ => new List<RequerimientoOpcionDto>()
        };

    private async Task<Dictionary<Guid, string>> ValoresCatalogoAsync(RequerimientoDimension dim, List<Guid> ids, CancellationToken ct)
        => dim switch
        {
            RequerimientoDimension.Categoria => await _db.ItemTypes.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct),
            RequerimientoDimension.Grupo => await _db.ItemGroups.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct),
            RequerimientoDimension.Subgrupo => await _db.ItemSubgroups.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct),
            RequerimientoDimension.Marca => await _db.Brands.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct),
            RequerimientoDimension.Referencia => await _db.Items.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => (x.Sku != null ? x.Sku + " - " : "") + x.Name, ct),
            _ => new Dictionary<Guid, string>()
        };

    private static RequerimientoOrigenDto Map(RequerimientoOrigen o,
        Dictionary<Guid, string> subs, Dictionary<Guid, string> forms)
        => new(
            o.Id, o.Nombre, o.Area, o.TipoFuente,
            o.FuenteRefId,
            o.FuenteRefId is Guid f && forms.TryGetValue(f, out var fn) ? fn : null,
            o.SubcategoriaCompraId,
            o.SubcategoriaCompraId is Guid s && subs.TryGetValue(s, out var sn) ? sn : null,
            o.SubcategoriaCotizacionId,
            o.SubcategoriaCotizacionId is Guid sc && subs.TryGetValue(sc, out var scn) ? scn : null,
            o.Activo);
}
