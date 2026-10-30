using System.Text;
using System.Text.Json;
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
    private readonly IRequerimientoConfigService _config;
    private readonly IAiInferenceService _ai;

    public RequerimientoService(IApplicationDbContext db, ITenantContext tenant, ITaskItemService tasks,
        IRequerimientoConfigService config, IAiInferenceService ai)
    {
        _db = db;
        _tenant = tenant;
        _tasks = tasks;
        _config = config;
        _ai = ai;
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
        // Numero de la actividad origen (para mostrar/linkear "de que actividad vino la solicitud").
        var taskIds = rows.Where(r => r.SourceTaskItemId != null).Select(r => r.SourceTaskItemId!.Value).Distinct().ToList();
        var taskNums = taskIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.TaskItems.AsNoTracking().Where(t => taskIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Number, ct);
        // Carga TODOS los hijos (no solo el agregado): los necesita la grilla para las filas hijas expandibles
        // y el export. Resuelve el nombre del proveedor en memoria.
        var hijosRows = await _db.RequerimientoCompraHijos.AsNoTracking()
            .Where(h => reqIds.Contains(h.RequerimientoId))
            .OrderBy(h => h.FechaProgramadaCompra).ToListAsync(ct);
        var provIds = hijosRows.Where(h => h.ProveedorId != null).Select(h => h.ProveedorId!.Value).Distinct().ToList();
        var provs = provIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Terceros.AsNoTracking().Where(t => provIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Nombre, ct);
        var hijosPorReq = hijosRows
            .GroupBy(h => h.RequerimientoId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RequerimientoHijoDto>)g.Select(h => new RequerimientoHijoDto(
                h.Id, h.Cantidad, h.FechaProgramadaCompra, h.CaracteristicasDespacho, h.TiempoEntrega,
                h.ProveedorId,
                h.ProveedorId is Guid p && provs.TryGetValue(p, out var pn) ? pn : null,
                h.Estado, h.TaskItemId, h.PrecioUnitario, h.CotizacionTaskItemId, h.ProveedorContactoId)).ToList());
        var emptyHijos = (IReadOnlyList<RequerimientoHijoDto>)Array.Empty<RequerimientoHijoDto>();

        return rows.Select(r =>
        {
            var hl = hijosPorReq.TryGetValue(r.Id, out var hs) ? hs : emptyHijos;
            return new RequerimientoDto(
                r.Id, r.OrigenId,
                r.OrigenId is Guid oi && origenes.TryGetValue(oi, out var on) ? on : null,
                r.Area, r.ItemId,
                r.ItemId is Guid ii && items.TryGetValue(ii, out var it) ? it.Name : null,
                r.ItemId is Guid ii2 && items.TryGetValue(ii2, out var it2) ? it2.Sku : null,
                r.Descripcion, r.Cantidad, r.UnidadMedida, r.FechaNecesidad, r.Estado, r.ClaveAgrupacion,
                hl.Count,
                hl.Sum(x => x.Cantidad),
                hl.Sum(x => x.Total),
                hl,
                r.SolicitanteUserId, r.SolicitanteNombre, r.SourceTaskItemId,
                r.SourceTaskItemId is Guid st && taskNums.TryGetValue(st, out var tn) ? tn : null,
                r.ForzarDividir);
        }).ToList();
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

        // Solicitante: snapshot del nombre/correo si no viene dado (traza de quien pidio).
        var solicitanteNombre = req.SolicitanteNombre;
        if (string.IsNullOrWhiteSpace(solicitanteNombre) && req.SolicitanteUserId is Guid su)
        {
            // Nombre visible: DisplayName del PlatformUser, fallback al correo del TenantUser (mismo patron que
            // el campo de sistema "Usuario" en formularios). Se guarda como snapshot para no perderlo despues.
            solicitanteNombre = await _db.TenantUsers.AsNoTracking()
                .Where(u => u.Id == su)
                .Join(_db.PlatformUsers.AsNoTracking().IgnoreQueryFilters(), tu => tu.PlatformUserId, pu => pu.Id,
                    (tu, pu) => pu.DisplayName ?? tu.Email)
                .FirstOrDefaultAsync(ct);
        }

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
            SolicitanteUserId = req.SolicitanteUserId,
            SolicitanteNombre = string.IsNullOrWhiteSpace(solicitanteNombre) ? null : solicitanteNombre!.Trim(),
            SourceTaskItemId = req.SourceTaskItemId,
            Observaciones = string.IsNullOrWhiteSpace(req.Observaciones) ? null : req.Observaciones!.Trim(),
            ObservacionesDespacho = string.IsNullOrWhiteSpace(req.ObservacionesDespacho) ? null : req.ObservacionesDespacho!.Trim(),
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

    public async Task<RequerimientoDto?> SetForzarDividirAsync(Guid id, bool dividir, CancellationToken ct = default)
    {
        var r = await _db.RequerimientosCompra.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) { return null; }
        if (r.ForzarDividir != dividir)
        {
            r.ForzarDividir = dividir;
            await _db.SaveChangesAsync(ct);
        }
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
            // El contacto elegido solo aplica si hay proveedor; si se quita el proveedor, se limpia.
            h.ProveedorContactoId = inp.ProveedorId is null ? null : inp.ProveedorContactoId;
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

    public async Task<PlanificarGrupoResult> PlanificarGrupoAsync(PlanificarGrupoRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return new PlanificarGrupoResult(false, "Sin tenant resuelto.", 0); }

        var lineas = (req.Lineas ?? Array.Empty<PlanificarGrupoLinea>()).Where(l => l.Cantidad > 0).ToList();
        if (lineas.Count == 0) { return new PlanificarGrupoResult(false, "No hay cantidades para planificar.", 0); }

        var ids = lineas.Select(l => l.RequerimientoId).Distinct().ToList();
        var padres = await _db.RequerimientosCompra.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        var existentes = (await _db.RequerimientoCompraHijos.Where(h => ids.Contains(h.RequerimientoId)).ToListAsync(ct))
            .GroupBy(h => h.RequerimientoId).ToDictionary(g => g.Key, g => g.Sum(h => h.Cantidad));

        // 1) Validar TODO antes de tocar nada (transaccional: todo o nada).
        foreach (var l in lineas)
        {
            var padre = padres.FirstOrDefault(p => p.Id == l.RequerimientoId);
            if (padre is null) { return new PlanificarGrupoResult(false, "Una necesidad del grupo ya no existe.", 0); }
            var yaPlan = existentes.GetValueOrDefault(padre.Id);
            if (yaPlan + l.Cantidad > padre.Cantidad)
            {
                return new PlanificarGrupoResult(false,
                    $"En '{padre.Descripcion}' lo planificado ({yaPlan + l.Cantidad:#,##0.##}) excederia la necesidad ({padre.Cantidad:#,##0.##}).", 0);
            }
        }

        // 2) Agregar una distribucion a cada necesidad (sin tocar las existentes) + un solo SaveChanges.
        var precio = req.PrecioUnitario < 0 ? 0 : req.PrecioUnitario;
        var despacho = (req.CaracteristicasDespacho ?? "").Trim();
        var entrega = (req.TiempoEntrega ?? "").Trim();
        var creados = 0;
        foreach (var l in lineas)
        {
            var padre = padres.First(p => p.Id == l.RequerimientoId);
            _db.RequerimientoCompraHijos.Add(new RequerimientoCompraHijo
            {
                TenantId = tenantId,
                RequerimientoId = padre.Id,
                Cantidad = l.Cantidad,
                FechaProgramadaCompra = req.FechaProgramadaCompra,
                CaracteristicasDespacho = despacho,
                TiempoEntrega = entrega,
                ProveedorId = req.ProveedorId,
                ProveedorContactoId = req.ProveedorId is null ? null : req.ProveedorContactoId,
                PrecioUnitario = precio,
                Estado = req.ProveedorId is not null ? RequerimientoEstado.ConProveedor : RequerimientoEstado.Planificado,
            });
            if (padre.Estado is RequerimientoEstado.Recibido or RequerimientoEstado.Agrupado or RequerimientoEstado.Planificado)
            {
                padre.Estado = RequerimientoEstado.Planificado;
            }
            creados++;
        }

        await _db.SaveChangesAsync(ct);
        return new PlanificarGrupoResult(true, null, creados);
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
                h.Estado, h.TaskItemId, h.PrecioUnitario, h.CotizacionTaskItemId, h.ProveedorContactoId)).ToList());
    }

    public async Task<IReadOnlyList<RequerimientoOpcionDto>> ListProveedoresAsync(CancellationToken ct = default)
        => await _db.Terceros.AsNoTracking()
            .Where(t => (t.Perfiles & TerceroPerfil.Proveedor) == TerceroPerfil.Proveedor)
            .OrderBy(t => t.Nombre)
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

        // Datos del proveedor (para enlazar el Tercero y llevar el contacto elegido a la actividad).
        string? provNombre = null, provEmail = null, provTel = null;
        if (hijo.ProveedorId is Guid pid)
        {
            var c = await GetProveedorContactoAsync(pid, hijo.ProveedorContactoId, ct);
            provNombre = c?.Contacto ?? c?.Nombre; provEmail = c?.Email; provTel = c?.Telefono;
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

    public async Task<EjecutarCompraResultado> CotizarHijoAsync(Guid hijoId, Guid actorUserId, string actorName, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid) { return new EjecutarCompraResultado(false, "Sin tenant en contexto.", null, null, null); }

        var hijo = await _db.RequerimientoCompraHijos.FirstOrDefaultAsync(h => h.Id == hijoId, ct);
        if (hijo is null) { return new EjecutarCompraResultado(false, "No se encontro el registro a cotizar.", null, null, null); }
        // Idempotencia: si ya tiene actividad de cotizacion, no se crea otra.
        if (hijo.CotizacionTaskItemId is not null)
        {
            return new EjecutarCompraResultado(false, "Esta distribucion ya tiene una cotizacion (actividad creada).", hijo.CotizacionTaskItemId, null, await GetPlanificacionAsync(hijo.RequerimientoId, ct));
        }

        var padre = await _db.RequerimientosCompra.FirstOrDefaultAsync(r => r.Id == hijo.RequerimientoId, ct);
        if (padre is null) { return new EjecutarCompraResultado(false, "No se encontro la necesidad padre.", null, null, null); }

        // El proceso de COTIZACION (subcategoria) lo define el ORIGEN de la necesidad.
        Guid? subcategoriaId = null;
        if (padre.OrigenId is Guid oid)
        {
            subcategoriaId = await _db.RequerimientoOrigenes.AsNoTracking()
                .Where(o => o.Id == oid).Select(o => o.SubcategoriaCotizacionId).FirstOrDefaultAsync(ct);
        }
        if (subcategoriaId is null)
        {
            return new EjecutarCompraResultado(false,
                "El origen de esta necesidad no tiene un proceso de COTIZACION (subcategoria) configurado. Asignalo en Configuracion de Requerimientos.",
                null, null, await GetPlanificacionAsync(padre.Id, ct));
        }

        // Datos del proveedor (para enlazar el Tercero y llevar su contacto/correo/telefono a la actividad).
        string? provNombre = null, provEmail = null, provTel = null;
        if (hijo.ProveedorId is Guid pid)
        {
            var c = await GetProveedorContactoAsync(pid, hijo.ProveedorContactoId, ct);
            provNombre = c?.Contacto ?? c?.Nombre; provEmail = c?.Email; provTel = c?.Telefono;
        }

        var titulo = $"Cotizacion: {padre.Descripcion} x {hijo.Cantidad:#,##0.##}";
        if (titulo.Length > 200) { titulo = titulo[..200]; }
        var desc = new System.Text.StringBuilder();
        desc.Append("Solicitud de cotizacion generada desde Requerimientos de Compra.\n");
        desc.Append($"Producto: {padre.Descripcion}\n");
        desc.Append($"Cantidad: {hijo.Cantidad:#,##0.##} {padre.UnidadMedida}\n");
        if (hijo.FechaProgramadaCompra is { } fp) { desc.Append($"Fecha programada de compra: {fp:yyyy-MM-dd}\n"); }
        if (!string.IsNullOrWhiteSpace(hijo.TiempoEntrega)) { desc.Append($"Tiempo de entrega esperado: {hijo.TiempoEntrega}\n"); }
        if (!string.IsNullOrWhiteSpace(hijo.CaracteristicasDespacho)) { desc.Append($"Despacho: {hijo.CaracteristicasDespacho}\n"); }
        if (provNombre is not null) { desc.Append($"Proveedor/contacto: {provNombre}\n"); }

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
            return new EjecutarCompraResultado(false, created.Error ?? "No se pudo crear la actividad de cotizacion.", null, null, await GetPlanificacionAsync(padre.Id, ct));
        }

        // Enlaza la actividad de cotizacion al hijo. NO cambia el estado de la compra.
        hijo.CotizacionTaskItemId = created.Value.Item.Id;
        await _db.SaveChangesAsync(ct);
        return new EjecutarCompraResultado(true, null, created.Value.Item.Id, created.Value.Item.Number, await GetPlanificacionAsync(padre.Id, ct));
    }

    public Task<ProveedorContactoDto?> GetProveedorContactoAsync(Guid proveedorId, CancellationToken ct = default)
        => GetProveedorContactoAsync(proveedorId, null, ct);

    public async Task<ProveedorContactoDto?> GetProveedorContactoAsync(Guid proveedorId, Guid? contactoId, CancellationToken ct = default)
    {
        var t = await _db.Terceros.AsNoTracking()
            .Where(x => x.Id == proveedorId)
            .Select(x => new
            {
                x.Nombre,
                x.Email,
                x.Telefono,
                // Contacto especifico si se pidio; si no, el primero por nombre.
                Contacto = (contactoId == null
                        ? x.Contactos.OrderBy(c => c.Nombre)
                        : x.Contactos.Where(c => c.Id == contactoId).OrderBy(c => c.Nombre))
                    .Select(c => new { c.Nombre, c.Email, c.Telefono })
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(ct);
        if (t is null) { return null; }
        // Prefiere los datos del contacto embebido; cae a los del propio proveedor.
        var contactoNombre = t.Contacto?.Nombre;
        var email = !string.IsNullOrWhiteSpace(t.Contacto?.Email) ? t.Contacto!.Email : t.Email;
        var tel = !string.IsNullOrWhiteSpace(t.Contacto?.Telefono) ? t.Contacto!.Telefono : t.Telefono;
        return new ProveedorContactoDto(proveedorId, t.Nombre, contactoNombre, email, tel);
    }

    public async Task<IReadOnlyList<ProveedorContactoItemDto>> ListContactosAsync(Guid proveedorId, CancellationToken ct = default)
        => await _db.TerceroContactos.AsNoTracking()
            .Where(c => c.TerceroId == proveedorId)
            .OrderBy(c => c.Nombre)
            .Select(c => new ProveedorContactoItemDto(c.Id, c.Nombre, c.Cargo, c.Email, c.Telefono))
            .ToListAsync(ct);

    public async Task<ProveedorContactoItemDto> GuardarContactoAsync(Guid proveedorId, ContactoInput input, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { throw new InvalidOperationException("Sin tenant en contexto."); }
        var nombre = (input.Nombre ?? "").Trim();
        if (nombre.Length == 0) { throw new InvalidOperationException("El contacto debe tener nombre."); }
        var prov = await _db.Terceros.AsNoTracking().FirstOrDefaultAsync(t => t.Id == proveedorId, ct);
        if (prov is null) { throw new InvalidOperationException("No se encontro el proveedor."); }

        TerceroContacto c;
        if (input.Id is Guid cid && await _db.TerceroContactos.FirstOrDefaultAsync(x => x.Id == cid && x.TerceroId == proveedorId, ct) is { } found)
        {
            c = found;
        }
        else
        {
            c = new TerceroContacto { TenantId = tenantId, TerceroId = proveedorId };
            _db.TerceroContactos.Add(c);
        }
        c.Nombre = nombre;
        c.Cargo = string.IsNullOrWhiteSpace(input.Cargo) ? null : input.Cargo!.Trim();
        c.Email = string.IsNullOrWhiteSpace(input.Email) ? null : input.Email!.Trim();
        c.Telefono = string.IsNullOrWhiteSpace(input.Telefono) ? null : input.Telefono!.Trim();
        await _db.SaveChangesAsync(ct);
        return new ProveedorContactoItemDto(c.Id, c.Nombre, c.Cargo, c.Email, c.Telefono);
    }

    public async Task EliminarContactoAsync(Guid contactoId, CancellationToken ct = default)
    {
        var c = await _db.TerceroContactos.FirstOrDefaultAsync(x => x.Id == contactoId, ct);
        if (c is null) { return; }
        _db.TerceroContactos.Remove(c);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<UltimoPrecioDto?> GetUltimoPrecioAsync(Guid? itemId, string descripcion, Guid proveedorId, Guid excluirRequerimientoId, CancellationToken ct = default)
    {
        if (proveedorId == Guid.Empty) { return null; }
        var q = _db.RequerimientoCompraHijos.AsNoTracking()
            .Where(h => h.ProveedorId == proveedorId && h.PrecioUnitario > 0m && h.RequerimientoId != excluirRequerimientoId);
        // Empareja por item cuando existe; si no, por la descripcion del padre.
        if (itemId is Guid iid)
        {
            q = q.Where(h => h.Requerimiento!.ItemId == iid);
        }
        else
        {
            var desc = (descripcion ?? string.Empty).Trim();
            if (desc.Length == 0) { return null; }
            q = q.Where(h => h.Requerimiento!.ItemId == null && h.Requerimiento!.Descripcion == desc);
        }
        // El mas reciente (Id es Guid v7 ordenable por tiempo, respaldado por CreatedAt).
        var hit = await q
            .OrderByDescending(h => h.CreatedAt)
            .Select(h => new { h.PrecioUnitario, h.FechaProgramadaCompra, h.CreatedAt })
            .FirstOrDefaultAsync(ct);
        return hit is null ? null : new UltimoPrecioDto(hit.PrecioUnitario, hit.FechaProgramadaCompra ?? hit.CreatedAt);
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

    // =====================================================================================================
    // Ola 13: comparador de cotizaciones de proveedor (tarjetas + diferencias + evaluacion por IA)
    // =====================================================================================================

    /// <summary>Clave de agrupacion por producto, IGUAL que la de la grilla (ConsolKey): "i:{ItemId}" o
    /// "d:{descripcion en minusculas, recortada y con espacios colapsados}".</summary>
    private static string ClaveProducto(Guid? itemId, string? descripcion)
        => itemId is Guid id
            ? "i:" + id
            : "d:" + string.Join(' ', (descripcion ?? "").Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public async Task<IReadOnlyList<CotizacionDto>> ListCotizacionesAsync(Guid? itemId, string descripcion, decimal cantidad, CancellationToken ct = default)
    {
        var clave = ClaveProducto(itemId, descripcion);
        var rows = await _db.RequerimientoCotizaciones.AsNoTracking()
            .Where(c => c.ClaveProducto == clave).ToListAsync(ct);
        return await MapCotizacionesAsync(rows, itemId, descripcion, ct);
    }

    private async Task<IReadOnlyList<CotizacionDto>> MapCotizacionesAsync(
        List<RequerimientoCotizacion> rows, Guid? itemId, string descripcion, CancellationToken ct)
    {
        if (rows.Count == 0) { return Array.Empty<CotizacionDto>(); }
        var provIds = rows.Select(r => r.ProveedorId).Distinct().ToList();
        var provs = await _db.Terceros.AsNoTracking().Where(t => provIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Nombre, ct);
        // Contacto (con quien se hablo) resuelto por lote desde el Directorio, para pintar nombre/tel/correo.
        var contIds = rows.Where(r => r.ProveedorContactoId is not null)
            .Select(r => r.ProveedorContactoId!.Value).Distinct().ToList();
        var conts = contIds.Count == 0
            ? new Dictionary<Guid, (string Nombre, string? Tel, string? Mail)>()
            : await _db.TerceroContactos.AsNoTracking().Where(c => contIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => (Nombre: c.Nombre, Tel: c.Telefono, Mail: c.Email), ct);
        var list = new List<CotizacionDto>();
        foreach (var c in rows)
        {
            // Variacion vs el ultimo precio del historico de ESE proveedor para el MISMO producto.
            var ult = await GetUltimoPrecioAsync(itemId, descripcion, c.ProveedorId, Guid.Empty, ct);
            decimal? varPct = (ult is { Precio: > 0m } && c.PrecioUnitario > 0m)
                ? Math.Round((c.PrecioUnitario - ult.Precio) / ult.Precio * 100m, 1)
                : null;
            string? cNom = null, cTel = null, cMail = null;
            if (c.ProveedorContactoId is Guid cid && conts.TryGetValue(cid, out var ci))
            {
                cNom = ci.Nombre; cTel = ci.Tel; cMail = ci.Mail;
            }
            list.Add(new CotizacionDto(
                c.Id, c.ProveedorId, provs.TryGetValue(c.ProveedorId, out var pn) ? pn : null,
                c.PrecioUnitario, c.TiempoEntrega, c.CaracteristicasDespacho, c.CondicionesPago,
                c.VigenciaHasta, c.Notas, c.Fuente, c.ProveedorContactoId, ult?.Precio, varPct,
                c.FechaCotizacion, cNom, cTel, cMail));
        }
        // Mas barato primero (los de precio 0 = sin precio al final).
        return list
            .OrderBy(x => x.PrecioUnitario <= 0m ? decimal.MaxValue : x.PrecioUnitario)
            .ThenBy(x => x.ProveedorNombre)
            .ToList();
    }

    public async Task<IReadOnlyList<CotizacionDto>> GuardarCotizacionAsync(SaveCotizacionRequest req, decimal cantidad, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant."); }
        if (req.ProveedorId == Guid.Empty) { throw new InvalidOperationException("Elige un proveedor."); }
        var clave = ClaveProducto(req.ItemId, req.Descripcion);

        RequerimientoCotizacion entity;
        if (req.Id is Guid id)
        {
            var ex = await _db.RequerimientoCotizaciones.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (ex is null) { throw new InvalidOperationException("La oferta ya no existe."); }
            entity = ex;
        }
        else
        {
            // Upsert por (producto, proveedor): si ya hay oferta de ese proveedor, se edita (no se duplica).
            var ex = await _db.RequerimientoCotizaciones
                .FirstOrDefaultAsync(c => c.ClaveProducto == clave && c.ProveedorId == req.ProveedorId, ct);
            if (ex is not null) { entity = ex; }
            else
            {
                entity = new RequerimientoCotizacion { TenantId = tid, ClaveProducto = clave };
                _db.RequerimientoCotizaciones.Add(entity);
            }
        }
        entity.ClaveProducto = clave;
        entity.ItemId = req.ItemId;
        entity.ProveedorId = req.ProveedorId;
        entity.ProveedorContactoId = req.ProveedorContactoId;
        entity.PrecioUnitario = req.PrecioUnitario;
        entity.TiempoEntrega = (req.TiempoEntrega ?? "").Trim();
        entity.CaracteristicasDespacho = (req.CaracteristicasDespacho ?? "").Trim();
        entity.CondicionesPago = (req.CondicionesPago ?? "").Trim();
        entity.VigenciaHasta = req.VigenciaHasta;
        entity.Notas = string.IsNullOrWhiteSpace(req.Notas) ? null : req.Notas.Trim();
        // Fecha/hora en que se cotizo: la indicada; si no, conserva la previa; si es nueva, el momento de captura.
        entity.FechaCotizacion = req.FechaCotizacion ?? entity.FechaCotizacion ?? DateTimeOffset.UtcNow;
        // Guardada a mano = oferta Manual (aunque viniera sembrada, el humano la pacto/ajusto).
        entity.Fuente = RequerimientoCotizacionFuente.Manual;
        await _db.SaveChangesAsync(ct);
        return await ListCotizacionesAsync(req.ItemId, req.Descripcion, cantidad, ct);
    }

    public async Task<bool> EliminarCotizacionAsync(Guid id, CancellationToken ct = default)
    {
        var c = await _db.RequerimientoCotizaciones.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) { return false; }
        _db.RequerimientoCotizaciones.Remove(c);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<CotizacionDto>> SembrarCotizacionesAsync(Guid? itemId, string descripcion, decimal cantidad, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant."); }
        var clave = ClaveProducto(itemId, descripcion);
        var yaProv = await _db.RequerimientoCotizaciones.AsNoTracking()
            .Where(c => c.ClaveProducto == clave).Select(c => c.ProveedorId).ToListAsync(ct);
        var yaSet = yaProv.ToHashSet();

        // Candidatos: proveedores de la matriz del item + proveedores con historico del producto.
        var candidatos = new HashSet<Guid>();
        if (itemId is Guid iid)
        {
            var item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == iid, ct);
            if (item is not null)
            {
                var dims = new (RequerimientoDimension Dim, Guid? Val)[]
                {
                    (RequerimientoDimension.Referencia, item.Id),
                    (RequerimientoDimension.Marca, item.BrandId),
                    (RequerimientoDimension.Subgrupo, item.SubgroupId),
                    (RequerimientoDimension.Grupo, item.GroupId),
                    (RequerimientoDimension.Categoria, item.ItemTypeId),
                };
                foreach (var (dim, val) in dims)
                {
                    if (val is not Guid v) { continue; }
                    var vs = v.ToString();
                    var provs = await _db.RequerimientoProveedorReglas.AsNoTracking()
                        .Where(r => r.Activo && r.Dimension == dim && r.ValorDimension == vs)
                        .Select(r => r.ProveedorId).ToListAsync(ct);
                    foreach (var p in provs) { candidatos.Add(p); }
                }
            }
        }
        // Historico: proveedores a los que ya se les compro este producto (con precio).
        var histQ = _db.RequerimientoCompraHijos.AsNoTracking()
            .Where(h => h.ProveedorId != null && h.PrecioUnitario > 0m);
        if (itemId is Guid hid) { histQ = histQ.Where(h => h.Requerimiento!.ItemId == hid); }
        else
        {
            var desc = (descripcion ?? "").Trim();
            histQ = histQ.Where(h => h.Requerimiento!.ItemId == null && h.Requerimiento!.Descripcion == desc);
        }
        foreach (var p in await histQ.Select(h => h.ProveedorId!.Value).Distinct().ToListAsync(ct)) { candidatos.Add(p); }

        var nuevos = 0;
        foreach (var provId in candidatos)
        {
            if (yaSet.Contains(provId)) { continue; }
            var ult = await GetUltimoPrecioAsync(itemId, descripcion, provId, Guid.Empty, ct);
            _db.RequerimientoCotizaciones.Add(new RequerimientoCotizacion
            {
                TenantId = tid,
                ClaveProducto = clave,
                ItemId = itemId,
                ProveedorId = provId,
                PrecioUnitario = ult?.Precio ?? 0m,
                TiempoEntrega = string.Empty,
                CaracteristicasDespacho = string.Empty,
                CondicionesPago = string.Empty,
                FechaCotizacion = DateTimeOffset.UtcNow,
                Fuente = ult is not null ? RequerimientoCotizacionFuente.Historico : RequerimientoCotizacionFuente.Matriz,
            });
            nuevos++;
        }
        if (nuevos > 0) { await _db.SaveChangesAsync(ct); }
        return await ListCotizacionesAsync(itemId, descripcion, cantidad, ct);
    }

    public async Task<EvaluacionCotizacionesDto> EvaluarCotizacionesAsync(Guid? itemId, string descripcion, decimal cantidad, CancellationToken ct = default)
    {
        var ofertas = await ListCotizacionesAsync(itemId, descripcion, cantidad, ct);
        if (ofertas.Count == 0)
        {
            return new EvaluacionCotizacionesDto(false, "No hay cotizaciones para evaluar.", null, string.Empty, Array.Empty<CotizacionRankItem>());
        }
        var agentId = await _config.GetAgenteCotizacionesAsync(ct);
        if (agentId is not Guid aid || aid == Guid.Empty)
        {
            return new EvaluacionCotizacionesDto(false, "Configura un agente evaluador en la Configuracion del modulo.", null, string.Empty, Array.Empty<CotizacionRankItem>());
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Producto: {(string.IsNullOrWhiteSpace(descripcion) ? "(sin descripcion)" : descripcion)}. Cantidad a comprar: {cantidad}.");
        sb.AppendLine("Ofertas de proveedores (compara precio, tiempo de entrega, despacho, condiciones de pago, vigencia y variacion vs el ultimo precio historico):");
        foreach (var o in ofertas)
        {
            var total = o.PrecioUnitario * cantidad;
            sb.AppendLine($"- proveedor_id={o.ProveedorId}; proveedor=\"{o.ProveedorNombre}\"; precio_unitario={o.PrecioUnitario}; total={total}; entrega=\"{o.TiempoEntrega}\"; despacho=\"{o.CaracteristicasDespacho}\"; pago=\"{o.CondicionesPago}\"; vigencia={(o.VigenciaHasta?.ToString("yyyy-MM-dd") ?? "n/d")}; ultimo_precio={(o.UltimoPrecio?.ToString() ?? "n/d")}; variacion_pct={(o.VariacionPct?.ToString() ?? "n/d")}");
        }
        sb.AppendLine();
        sb.AppendLine("Evalua y elige la MEJOR opcion de compra considerando costo total, tiempo de entrega y condiciones. Responde UNICAMENTE un JSON valido, sin texto adicional ni markdown, con esta forma exacta:");
        sb.AppendLine("{\"ganador_proveedor_id\":\"<guid>\",\"resumen\":\"<por que gana, 1-2 frases>\",\"ranking\":[{\"proveedor_id\":\"<guid>\",\"posicion\":1,\"motivo\":\"<breve>\"}]}");

        const string sys = "Eres un analista de compras. Comparas cotizaciones de proveedores y eliges la mejor por costo total, tiempo de entrega y condiciones. Responde SIEMPRE y SOLO con el JSON pedido, sin explicaciones fuera del JSON.";

        AiChatResult res;
        try
        {
            res = await _ai.TestChatAsync(aid, new[] { new AiChatTurn("user", sb.ToString()) },
                systemPromptOverride: sys, actorUserId: _tenant.UserId, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            return new EvaluacionCotizacionesDto(false, "No se pudo invocar al agente: " + ex.Message, null, string.Empty, Array.Empty<CotizacionRankItem>());
        }
        if (!res.Ok || string.IsNullOrWhiteSpace(res.Text))
        {
            return new EvaluacionCotizacionesDto(false, res.Error ?? "El agente no devolvio respuesta.", null, string.Empty, Array.Empty<CotizacionRankItem>());
        }
        return ParseEvaluacion(res.Text!, ofertas);
    }

    private static EvaluacionCotizacionesDto ParseEvaluacion(string text, IReadOnlyList<CotizacionDto> ofertas)
    {
        var json = ExtractJson(text);
        if (json is null)
        {
            return new EvaluacionCotizacionesDto(false, "No se pudo interpretar la respuesta del agente.", null, text.Trim(), Array.Empty<CotizacionRankItem>());
        }
        var validos = ofertas.Select(o => o.ProveedorId).ToHashSet();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Guid? ganador = null;
            if (root.TryGetProperty("ganador_proveedor_id", out var gEl)
                && Guid.TryParse(gEl.GetString(), out var g) && validos.Contains(g)) { ganador = g; }
            var resumen = root.TryGetProperty("resumen", out var rEl) ? (rEl.GetString() ?? string.Empty) : string.Empty;
            var ranking = new List<CotizacionRankItem>();
            if (root.TryGetProperty("ranking", out var rk) && rk.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in rk.EnumerateArray())
                {
                    if (!it.TryGetProperty("proveedor_id", out var pEl)
                        || !Guid.TryParse(pEl.GetString(), out var pid) || !validos.Contains(pid)) { continue; }
                    var pos = it.TryGetProperty("posicion", out var posEl) && posEl.TryGetInt32(out var pv) ? pv : ranking.Count + 1;
                    var motivo = it.TryGetProperty("motivo", out var mEl) ? (mEl.GetString() ?? string.Empty) : string.Empty;
                    ranking.Add(new CotizacionRankItem(pid, pos, motivo));
                }
            }
            if (ganador is null && ranking.Count > 0) { ganador = ranking.OrderBy(x => x.Posicion).First().ProveedorId; }
            return new EvaluacionCotizacionesDto(true, null, ganador, resumen, ranking.OrderBy(x => x.Posicion).ToList());
        }
        catch
        {
            return new EvaluacionCotizacionesDto(false, "La respuesta del agente no es un JSON valido.", null, text.Trim(), Array.Empty<CotizacionRankItem>());
        }
    }

    /// <summary>Extrae el primer objeto JSON del texto, quitando fences de markdown si los hay.</summary>
    private static string? ExtractJson(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("```", StringComparison.Ordinal))
        {
            var nl = t.IndexOf('\n');
            if (nl >= 0) { t = t[(nl + 1)..]; }
            var fe = t.LastIndexOf("```", StringComparison.Ordinal);
            if (fe >= 0) { t = t[..fe]; }
            t = t.Trim();
        }
        var start = t.IndexOf('{');
        var end = t.LastIndexOf('}');
        if (start < 0 || end <= start) { return null; }
        return t.Substring(start, end - start + 1);
    }
}
