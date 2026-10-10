using Ecorex.Application.Actividades;
using Ecorex.Application.Common;
using Ecorex.Application.Forms;
using Ecorex.Domain.Entities;
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

    private static RequerimientoOrigenDto Map(RequerimientoOrigen o,
        Dictionary<Guid, string> subs, Dictionary<Guid, string> forms)
        => new(
            o.Id, o.Nombre, o.Area, o.TipoFuente,
            o.FuenteRefId,
            o.FuenteRefId is Guid f && forms.TryGetValue(f, out var fn) ? fn : null,
            o.SubcategoriaCompraId,
            o.SubcategoriaCompraId is Guid s && subs.TryGetValue(s, out var sn) ? sn : null,
            o.Activo);
}
