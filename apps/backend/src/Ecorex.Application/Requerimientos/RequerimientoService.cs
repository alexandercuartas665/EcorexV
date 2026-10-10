using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Requerimientos;

public sealed class RequerimientoService : IRequerimientoService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ITaskItemService _tasks;

    public RequerimientoService(IApplicationDbContext db, ITenantContext tenant, ITaskItemService tasks)
    {
        _db = db;
        _tenant = tenant;
        _tasks = tasks;
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
                .Select(g => new { g.Key, Count = g.Count(), Suma = g.Sum(x => x.Cantidad), Monto = g.Sum(x => x.Cantidad * x.PrecioUnitario) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => (x.Count, x.Suma, x.Monto));

        return rows.Select(r => new RequerimientoDto(
            r.Id, r.OrigenId,
            r.OrigenId is Guid oi && origenes.TryGetValue(oi, out var on) ? on : null,
            r.Area, r.ItemId,
            r.ItemId is Guid ii && items.TryGetValue(ii, out var it) ? it.Name : null,
            r.ItemId is Guid ii2 && items.TryGetValue(ii2, out var it2) ? it2.Sku : null,
            r.Descripcion, r.Cantidad, r.UnidadMedida, r.FechaNecesidad, r.Estado, r.ClaveAgrupacion,
            hijosPorReq.TryGetValue(r.Id, out var h) ? h.Count : 0,
            hijosPorReq.TryGetValue(r.Id, out var h2) ? h2.Suma : 0m,
            hijosPorReq.TryGetValue(r.Id, out var h3) ? h3.Monto : 0m)).ToList();
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

    public async Task<RequerimientoDto?> CerrarAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _db.RequerimientosCompra.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) { return null; }
        if (r.Estado != RequerimientoEstado.Ejecutado)
        {
            throw new InvalidOperationException("Solo se puede cerrar una necesidad Ejecutada.");
        }
        r.Estado = RequerimientoEstado.Cerrado;
        await _db.SaveChangesAsync(ct);
        return (await ListAsync(new RequerimientoFiltro(), ct)).FirstOrDefault(x => x.Id == id);
    }

    public async Task<RequerimientoDto?> ReabrirAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _db.RequerimientosCompra.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) { return null; }
        if (r.Estado != RequerimientoEstado.Cerrado)
        {
            throw new InvalidOperationException("Solo se puede reabrir una necesidad Cerrada.");
        }
        r.Estado = RequerimientoEstado.Ejecutado;
        await _db.SaveChangesAsync(ct);
        return (await ListAsync(new RequerimientoFiltro(), ct)).FirstOrDefault(x => x.Id == id);
    }

    public async Task<PlanificacionDto?> GetPlanificacionAsync(Guid requerimientoId, CancellationToken ct = default)
    {
        var r = await _db.RequerimientosCompra.AsNoTracking().FirstOrDefaultAsync(x => x.Id == requerimientoId, ct);
        if (r is null) { return null; }
        var hijos = await _db.RequerimientoCompraHijos.AsNoTracking()
            .Where(h => h.RequerimientoId == requerimientoId)
            .OrderBy(h => h.FechaProgramadaCompra).ToListAsync(ct);
        var provIds = hijos.Where(h => h.ProveedorId != null).Select(h => h.ProveedorId!.Value).Distinct().ToList();
        var provs = await _db.Terceros.AsNoTracking().Where(t => provIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Nombre, ct);
        return BuildPlanificacion(r, hijos, provs);
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
            h.ProveedorId = inp.ProveedorId;
            h.PrecioUnitario = inp.PrecioUnitario < 0 ? 0 : inp.PrecioUnitario;
            // El hijo pasa a 'Con proveedor' si se le asigno uno (no se pisa si ya esta ejecutado).
            if (h.TaskItemId is null)
            {
                h.Estado = inp.ProveedorId is not null ? RequerimientoEstado.ConProveedor : RequerimientoEstado.Planificado;
            }
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

    private static PlanificacionDto BuildPlanificacion(RequerimientoCompra r, List<RequerimientoCompraHijo> hijos,
        IReadOnlyDictionary<Guid, string> proveedores)
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
                h.ProveedorId,
                h.ProveedorId is Guid p && proveedores.TryGetValue(p, out var pn) ? pn : null,
                h.Estado, h.TaskItemId, h.PrecioUnitario)).ToList());
    }

    public async Task<IReadOnlyList<RequerimientoOpcionDto>> ListProveedoresAsync(CancellationToken ct = default)
        => await _db.Terceros.AsNoTracking().OrderBy(t => t.Nombre)
            .Select(t => new RequerimientoOpcionDto(t.Id, t.Nombre)).Take(2000).ToListAsync(ct);

    public async Task<RequerimientoOpcionDto?> SugerirProveedorAsync(Guid itemId, CancellationToken ct = default)
    {
        var item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null) { return null; }

        // De MAS especifico a mas general: Referencia > Marca > Subgrupo > Grupo > Categoria. Gana el primero que aplique.
        var orden = new (Domain.Enums.RequerimientoDimension Dim, Guid? Val)[]
        {
            (Domain.Enums.RequerimientoDimension.Referencia, item.Id),
            (Domain.Enums.RequerimientoDimension.Marca, item.BrandId),
            (Domain.Enums.RequerimientoDimension.Subgrupo, item.SubgroupId),
            (Domain.Enums.RequerimientoDimension.Grupo, item.GroupId),
            (Domain.Enums.RequerimientoDimension.Categoria, item.ItemTypeId),
        };
        foreach (var (dim, val) in orden)
        {
            if (val is not Guid g) { continue; }
            var valStr = g.ToString();
            var regla = await _db.RequerimientoProveedorReglas.AsNoTracking()
                .Where(x => x.Activo && x.Dimension == dim && x.ValorDimension == valStr)
                .OrderBy(x => x.Prioridad).FirstOrDefaultAsync(ct);
            if (regla is not null)
            {
                var nombre = await _db.Terceros.AsNoTracking().Where(t => t.Id == regla.ProveedorId)
                    .Select(t => t.Nombre).FirstOrDefaultAsync(ct);
                return new RequerimientoOpcionDto(regla.ProveedorId, nombre ?? "(proveedor)");
            }
        }
        return null;
    }

    public async Task<EjecutarCompraResultado> EjecutarHijoAsync(Guid hijoId, Guid actorUserId, string actorName, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid) { return new EjecutarCompraResultado(false, "Sin tenant en contexto.", null, null, null); }

        var hijo = await _db.RequerimientoCompraHijos.FirstOrDefaultAsync(h => h.Id == hijoId, ct);
        if (hijo is null) { return new EjecutarCompraResultado(false, "No se encontro el registro a ejecutar.", null, null, null); }
        // Idempotencia: si ya tiene actividad, no se crea otra.
        if (hijo.TaskItemId is not null)
        {
            return new EjecutarCompraResultado(false, "Esta compra ya fue ejecutada (ya tiene actividad).", hijo.TaskItemId, null, await GetPlanificacionAsync(hijo.RequerimientoId, ct));
        }

        var padre = await _db.RequerimientosCompra.FirstOrDefaultAsync(r => r.Id == hijo.RequerimientoId, ct);
        if (padre is null) { return new EjecutarCompraResultado(false, "No se encontro la necesidad padre.", null, null, null); }

        // El proceso de compra (subcategoria) lo define el ORIGEN de la necesidad.
        Guid? subcategoriaId = null;
        if (padre.OrigenId is Guid oid)
        {
            subcategoriaId = await _db.RequerimientoOrigenes.AsNoTracking()
                .Where(o => o.Id == oid).Select(o => o.SubcategoriaCompraId).FirstOrDefaultAsync(ct);
        }
        if (subcategoriaId is null)
        {
            return new EjecutarCompraResultado(false,
                "El origen de esta necesidad no tiene un proceso de compra (subcategoria) configurado. Asignalo en Configuracion de Requerimientos.",
                null, null, await GetPlanificacionAsync(padre.Id, ct));
        }

        // Datos del proveedor (para enlazar el Tercero y llevar su correo/telefono a la actividad).
        string? provNombre = null, provEmail = null, provTel = null;
        if (hijo.ProveedorId is Guid pid)
        {
            var prov = await _db.Terceros.AsNoTracking().Where(t => t.Id == pid)
                .Select(t => new { t.Nombre, t.Email, t.Telefono }).FirstOrDefaultAsync(ct);
            provNombre = prov?.Nombre; provEmail = prov?.Email; provTel = prov?.Telefono;
        }

        var titulo = $"Compra: {padre.Descripcion} x {hijo.Cantidad:#,##0.##}";
        if (titulo.Length > 200) { titulo = titulo[..200]; }
        var desc = new System.Text.StringBuilder();
        desc.Append("Compra programada generada desde Requerimientos de Compra.\n");
        desc.Append($"Producto: {padre.Descripcion}\n");
        desc.Append($"Cantidad: {hijo.Cantidad:#,##0.##} {padre.UnidadMedida}\n");
        if (hijo.FechaProgramadaCompra is { } fp) { desc.Append($"Fecha programada de compra: {fp:yyyy-MM-dd}\n"); }
        if (!string.IsNullOrWhiteSpace(hijo.TiempoEntrega)) { desc.Append($"Tiempo de entrega: {hijo.TiempoEntrega}\n"); }
        if (!string.IsNullOrWhiteSpace(hijo.CaracteristicasDespacho)) { desc.Append($"Despacho: {hijo.CaracteristicasDespacho}\n"); }
        if (provNombre is not null) { desc.Append($"Proveedor: {provNombre}\n"); }
        if (hijo.PrecioUnitario > 0)
        {
            desc.Append($"Precio unitario: {hijo.PrecioUnitario:#,##0.##}\n");
            desc.Append($"Total estimado: {(hijo.Cantidad * hijo.PrecioUnitario):#,##0.##}\n");
        }

        var req = new CreateTaskItemRequest(
            Title: titulo,
            ActivityTypeId: null,
            SubcategoriaId: subcategoriaId,
            BoardId: null,
            Description: desc.ToString().TrimEnd(),
            RequesterName: provNombre,
            RequesterEmail: provEmail,
            RequesterPhone: provTel,
            TerceroId: hijo.ProveedorId,
            DueDate: hijo.FechaProgramadaCompra);

        var created = await _tasks.CreateAsync(req, actorUserId, actorName, ct);
        if (!created.IsOk || created.Value is null)
        {
            return new EjecutarCompraResultado(false, created.Error ?? "No se pudo crear la actividad de compra.", null, null, await GetPlanificacionAsync(padre.Id, ct));
        }
        var taskId = created.Value.Item.Id;
        var taskNumber = created.Value.Item.Number;

        // Enlaza y avanza el estado del hijo.
        hijo.TaskItemId = taskId;
        hijo.Estado = RequerimientoEstado.Ejecutado;

        // El padre pasa a Ejecutado cuando TODOS sus hijos estan ejecutados.
        var hermanos = await _db.RequerimientoCompraHijos.AsNoTracking()
            .Where(h => h.RequerimientoId == padre.Id && h.Id != hijo.Id)
            .Select(h => h.Estado).ToListAsync(ct);
        if (hermanos.All(e => e == RequerimientoEstado.Ejecutado))
        {
            padre.Estado = RequerimientoEstado.Ejecutado;
        }

        // Aprendizaje: graba/actualiza una regla Aprendida en la dimension MAS especifica del item -> proveedor.
        if (hijo.ProveedorId is Guid provId && padre.ItemId is Guid itemId)
        {
            await AprenderReglaAsync(itemId, provId, ct);
        }

        await _db.SaveChangesAsync(ct);
        return new EjecutarCompraResultado(true, null, taskId, taskNumber, await GetPlanificacionAsync(padre.Id, ct));
    }

    /// <summary>Memoria del ultimo ejercicio de compra: upsert de una regla Aprendida en la dimension MAS
    /// especifica que tenga el item (Referencia > Marca > Subgrupo > Grupo > Categoria).</summary>
    private async Task AprenderReglaAsync(Guid itemId, Guid proveedorId, CancellationToken ct)
    {
        if (_tenant.TenantId is not Guid tenantId) { return; }
        var item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null) { return; }

        var orden = new (RequerimientoDimension Dim, Guid? Val)[]
        {
            (RequerimientoDimension.Referencia, item.Id),
            (RequerimientoDimension.Marca, item.BrandId),
            (RequerimientoDimension.Subgrupo, item.SubgroupId),
            (RequerimientoDimension.Grupo, item.GroupId),
            (RequerimientoDimension.Categoria, item.ItemTypeId),
        };
        var elegida = orden.FirstOrDefault(o => o.Val is not null);
        if (elegida.Val is not Guid dimVal) { return; }
        var valStr = dimVal.ToString();

        var existente = await _db.RequerimientoProveedorReglas
            .FirstOrDefaultAsync(x => x.Dimension == elegida.Dim && x.ValorDimension == valStr && x.ProveedorId == proveedorId, ct);
        if (existente is null)
        {
            _db.RequerimientoProveedorReglas.Add(new RequerimientoProveedorRegla
            {
                TenantId = tenantId,
                Dimension = elegida.Dim,
                ValorDimension = valStr,
                ProveedorId = proveedorId,
                Prioridad = 50, // aprendida: pisa por prioridad a reglas manuales mas altas (100 por defecto)
                Aprendida = true,
                Activo = true,
            });
        }
        else
        {
            existente.Aprendida = true;
            existente.Activo = true;
        }
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
