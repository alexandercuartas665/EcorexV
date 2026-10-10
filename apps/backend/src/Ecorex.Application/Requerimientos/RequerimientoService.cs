using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Requerimientos;

public sealed class RequerimientoService : IRequerimientoService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public RequerimientoService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<RequerimientoDto>> ListAsync(RequerimientoFiltro filtro, CancellationToken ct = default)
    {
        var q = _db.RequerimientosCompra.AsNoTracking().AsQueryable();
        if (filtro.Estado is { } est) { q = q.Where(r => r.Estado == est); }
        if (filtro.OrigenId is { } oid) { q = q.Where(r => r.OrigenId == oid); }
        if (!string.IsNullOrWhiteSpace(filtro.Texto))
        {
            var t = filtro.Texto.Trim();
            q = q.Where(r => r.Descripcion.Contains(t) || r.Area.Contains(t));
        }
        var rows = await q.OrderByDescending(r => r.FechaNecesidad).ThenBy(r => r.Descripcion).ToListAsync(ct);

        // Nombres de origen e item (resueltos en memoria para evitar joins con filtros globales distintos).
        var origenIds = rows.Where(r => r.OrigenId != null).Select(r => r.OrigenId!.Value).Distinct().ToList();
        var itemIds = rows.Where(r => r.ItemId != null).Select(r => r.ItemId!.Value).Distinct().ToList();
        var origenes = await _db.RequerimientoOrigenes.AsNoTracking()
            .Where(o => origenIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Nombre, ct);
        var items = await _db.Items.AsNoTracking()
            .Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => new { i.Name, i.Sku }, ct);
        var reqIds = rows.Select(r => r.Id).ToList();
        var hijosPorReq = (await _db.RequerimientoCompraHijos.AsNoTracking()
                .Where(h => reqIds.Contains(h.RequerimientoId))
                .GroupBy(h => h.RequerimientoId)
                .Select(g => new { g.Key, Count = g.Count(), Suma = g.Sum(x => x.Cantidad) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => (x.Count, x.Suma));

        return rows.Select(r => new RequerimientoDto(
            r.Id, r.OrigenId,
            r.OrigenId is Guid oi && origenes.TryGetValue(oi, out var on) ? on : null,
            r.Area, r.ItemId,
            r.ItemId is Guid ii && items.TryGetValue(ii, out var it) ? it.Name : null,
            r.ItemId is Guid ii2 && items.TryGetValue(ii2, out var it2) ? it2.Sku : null,
            r.Descripcion, r.Cantidad, r.UnidadMedida, r.FechaNecesidad, r.Estado, r.ClaveAgrupacion,
            hijosPorReq.TryGetValue(r.Id, out var h) ? h.Count : 0,
            hijosPorReq.TryGetValue(r.Id, out var h2) ? h2.Suma : 0m)).ToList();
    }

    public async Task<RequerimientoDto?> CrearAsync(CrearRequerimientoRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        var desc = (req.Descripcion ?? "").Trim();
        // Si hay item y no descripcion, usa el nombre del item como descripcion.
        if (string.IsNullOrWhiteSpace(desc) && req.ItemId is Guid iid)
        {
            desc = (await _db.Items.AsNoTracking().Where(i => i.Id == iid).Select(i => i.Name).FirstOrDefaultAsync(ct)) ?? "";
        }
        if (string.IsNullOrWhiteSpace(desc)) { throw new InvalidOperationException("Indica un item o una descripcion del producto."); }
        if (req.Cantidad <= 0) { throw new InvalidOperationException("La cantidad debe ser mayor que cero."); }

        // La clave de agrupacion une pedidos del MISMO producto: el item si existe, si no el texto normalizado.
        var clave = req.ItemId is Guid ci ? ci.ToString() : desc.ToUpperInvariant();

        var entity = new RequerimientoCompra
        {
            TenantId = tenantId,
            OrigenId = req.OrigenId,
            Area = (req.Area ?? "").Trim(),
            ItemId = req.ItemId,
            Descripcion = desc,
            Cantidad = req.Cantidad,
            UnidadMedida = (req.UnidadMedida ?? "").Trim(),
            FechaNecesidad = req.FechaNecesidad,
            Estado = RequerimientoEstado.Recibido,
            ClaveAgrupacion = clave,
        };
        _db.RequerimientosCompra.Add(entity);
        await _db.SaveChangesAsync(ct);

        var list = await ListAsync(new RequerimientoFiltro(), ct);
        return list.FirstOrDefault(r => r.Id == entity.Id);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _db.RequerimientosCompra.Include(x => x.Hijos).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) { return false; }
        if (r.Hijos.Count > 0) { throw new InvalidOperationException("No se puede eliminar: tiene registros hijos (divisiones)."); }
        _db.RequerimientosCompra.Remove(r);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<PlanificacionDto?> GetPlanificacionAsync(Guid requerimientoId, CancellationToken ct = default)
    {
        var r = await _db.RequerimientosCompra.AsNoTracking().FirstOrDefaultAsync(x => x.Id == requerimientoId, ct);
        if (r is null) { return null; }
        var hijos = await _db.RequerimientoCompraHijos.AsNoTracking()
            .Where(h => h.RequerimientoId == requerimientoId)
            .OrderBy(h => h.FechaProgramadaCompra).ToListAsync(ct);
        return BuildPlanificacion(r, hijos);
    }

    public async Task<PlanificacionDto?> GuardarHijosAsync(GuardarHijosRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        var padre = await _db.RequerimientosCompra.FirstOrDefaultAsync(x => x.Id == req.RequerimientoId, ct);
        if (padre is null) { return null; }

        var inputs = req.Hijos ?? Array.Empty<HijoInput>();
        if (inputs.Any(h => h.Cantidad <= 0)) { throw new InvalidOperationException("Cada registro hijo debe tener cantidad mayor que cero."); }
        var suma = inputs.Sum(h => h.Cantidad);
        // Decision: la suma de los hijos NO puede exceder al padre (se permite dejar restante).
        if (suma > padre.Cantidad)
        {
            throw new InvalidOperationException($"La suma de los hijos ({suma:#,##0.##}) excede la cantidad del padre ({padre.Cantidad:#,##0.##}).");
        }

        var existentes = await _db.RequerimientoCompraHijos
            .Where(h => h.RequerimientoId == req.RequerimientoId).ToListAsync(ct);
        var mantenidos = inputs.Where(i => i.Id is not null).Select(i => i.Id!.Value).ToHashSet();

        // Borra los que ya no estan (salvo que esten ejecutados: tienen TaskItemId).
        foreach (var ex in existentes)
        {
            if (!mantenidos.Contains(ex.Id))
            {
                if (ex.TaskItemId is not null) { throw new InvalidOperationException("No se puede quitar un hijo ya ejecutado (tiene actividad)."); }
                _db.RequerimientoCompraHijos.Remove(ex);
            }
        }

        // Upsert de cada input.
        foreach (var inp in inputs)
        {
            RequerimientoCompraHijo h;
            if (inp.Id is Guid id && existentes.FirstOrDefault(e => e.Id == id) is { } found) { h = found; }
            else { h = new RequerimientoCompraHijo { TenantId = tenantId, RequerimientoId = padre.Id, Estado = RequerimientoEstado.Planificado }; _db.RequerimientoCompraHijos.Add(h); }
            h.Cantidad = inp.Cantidad;
            h.FechaProgramadaCompra = inp.FechaProgramadaCompra;
            h.CaracteristicasDespacho = (inp.CaracteristicasDespacho ?? "").Trim();
            h.TiempoEntrega = (inp.TiempoEntrega ?? "").Trim();
        }

        // El padre pasa a Planificado si queda con hijos; vuelve a Recibido si queda sin ninguno.
        // (No se pisa un estado mas avanzado como Ejecutado/Cerrado.)
        if (padre.Estado is RequerimientoEstado.Recibido or RequerimientoEstado.Agrupado or RequerimientoEstado.Planificado)
        {
            padre.Estado = inputs.Count > 0 ? RequerimientoEstado.Planificado : RequerimientoEstado.Recibido;
        }

        await _db.SaveChangesAsync(ct);
        return await GetPlanificacionAsync(padre.Id, ct);
    }

    private static PlanificacionDto BuildPlanificacion(RequerimientoCompra r, List<RequerimientoCompraHijo> hijos)
    {
        var planificada = hijos.Sum(h => h.Cantidad);
        return new PlanificacionDto(
            r.Id,
            r.Descripcion,
            r.Cantidad,
            planificada,
            r.Cantidad - planificada,
            hijos.Select(h => new RequerimientoHijoDto(
                h.Id, h.Cantidad, h.FechaProgramadaCompra, h.CaracteristicasDespacho, h.TiempoEntrega,
                h.ProveedorId, h.Estado, h.TaskItemId)).ToList());
    }

    public async Task<IReadOnlyList<ItemOpcionDto>> ListItemsAsync(CancellationToken ct = default)
        => await _db.Items.AsNoTracking().Where(i => i.IsActive)
            .OrderBy(i => i.Name)
            .Select(i => new ItemOpcionDto(i.Id, i.Name, i.Sku))
            .Take(1000)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<RequerimientoOpcionDto>> ListOrigenesActivosAsync(CancellationToken ct = default)
        => await _db.RequerimientoOrigenes.AsNoTracking().Where(o => o.Activo)
            .OrderBy(o => o.Nombre)
            .Select(o => new RequerimientoOpcionDto(o.Id, o.Nombre))
            .ToListAsync(ct);
}
