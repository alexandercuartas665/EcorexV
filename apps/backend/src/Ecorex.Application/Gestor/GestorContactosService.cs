using System.Globalization;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Crm;
using Ecorex.Application.Directorio;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Gestor;

/// <summary>
/// Implementacion de <see cref="IGestorContactosService"/> (Gestor de Clientes, 000740).
/// Aislamiento por tenant via filtro global (nunca se filtra a mano por TenantId); el alta
/// estampa el TenantId del contexto. Las operaciones multi-tabla se resuelven en un solo
/// SaveChanges (transaccion implicita).
/// </summary>
public sealed class GestorContactosService : IGestorContactosService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IOportunidadEstadoService _estados;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public GestorContactosService(IApplicationDbContext db, ITenantContext tenant, IOportunidadEstadoService estados)
    {
        _db = db;
        _tenant = tenant;
        _estados = estados;
    }

    // ---- KPIs ----

    public async Task<GestorKpisDto> GetKpisAsync(CancellationToken cancellationToken = default)
    {
        // ProspectosFiltrados = pool de prospectos scrapeados; Contactados = terceros en la Bolsa;
        // CalificadosLead = terceros con perfil Cliente; el pipeline sale de las oportunidades abiertas.
        var prospectos = await _db.ProspectosScrapeados.AsNoTracking().CountAsync(cancellationToken);
        var contactados = await _db.Terceros.AsNoTracking()
            .CountAsync(t => t.BolsaColumnaId != null, cancellationToken);
        var calificados = await _db.Terceros.AsNoTracking()
            .CountAsync(t => (t.Perfiles & TerceroPerfil.Cliente) == TerceroPerfil.Cliente, cancellationToken);

        // Abierta = etapa configurable con Tipo Abierta cuando existe; si la oportunidad aun no tiene
        // etapa configurable (EstadoId null), cae al enum heredado (no Ganada/Perdida).
        var ops = await _db.Oportunidades.AsNoTracking()
            .Select(o => new
            {
                o.Valor,
                o.Etapa,
                EstadoTipo = o.Estado != null ? (OportunidadEstadoTipo?)o.Estado.Tipo : null
            })
            .ToListAsync(cancellationToken);
        var abiertas = ops.Where(o => o.EstadoTipo is OportunidadEstadoTipo tipo
            ? tipo == OportunidadEstadoTipo.Abierta
            : o.Etapa != OportunidadEtapa.Ganada && o.Etapa != OportunidadEtapa.Perdida).ToList();
        var oportunidadesAbiertas = abiertas.Count;
        var valorPipeline = abiertas.Sum(o => o.Valor);

        return new GestorKpisDto(prospectos, contactados, calificados, oportunidadesAbiertas, valorPipeline);
    }

    // ---- Prospectos scrapeados ----

    public async Task<IReadOnlyList<ProspectoDto>> ListProspectosAsync(
        string? fuente, CancellationToken cancellationToken = default)
    {
        var query = _db.ProspectosScrapeados.AsNoTracking().AsQueryable();
        var f = fuente?.Trim();
        if (!string.IsNullOrEmpty(f))
        {
            query = query.Where(p => p.Fuente == f);
        }
        var rows = await query
            .OrderByDescending(p => p.FechaCaptura).ThenBy(p => p.NombreCompleto)
            .ToListAsync(cancellationToken);
        return rows.Select(p => new ProspectoDto(
            p.Id, p.Fuente, p.NombreCompleto, p.Cargo, p.Empresa, p.Ciudad, p.Metrica, p.Badge,
            p.Telefono, p.Correo, p.TerceroId, p.TerceroId != null, p.FechaCaptura)).ToList();
    }

    public async Task<TerceroResult<Guid>> PromoverProspectoAsync(
        Guid prospectoId, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
        {
            return TerceroResult<Guid>.Invalid("No hay tenant activo.");
        }
        var prospecto = await _db.ProspectosScrapeados.FirstOrDefaultAsync(p => p.Id == prospectoId, cancellationToken);
        if (prospecto is null)
        {
            return TerceroResult<Guid>.NotFound("El prospecto no existe.");
        }
        if (prospecto.TerceroId is not null)
        {
            return TerceroResult<Guid>.Invalid("El prospecto ya fue promovido a un tercero.");
        }

        // Primera columna de la Bolsa (menor SortOrder); si no hay ninguna, se crean las default.
        var columnas = await _db.BolsaColumnas
            .Where(c => !c.IsArchived)
            .OrderBy(c => c.SortOrder)
            .ToListAsync(cancellationToken);
        if (columnas.Count == 0)
        {
            var defaults = BuildDefaultColumnas(tenantId);
            _db.BolsaColumnas.AddRange(defaults);
            columnas = defaults.ToList();
        }
        var primera = columnas[0];

        var tercero = new Tercero
        {
            TenantId = tenantId,
            Nombre = prospecto.NombreCompleto,
            Tipo = TerceroTipo.Persona,
            Perfiles = TerceroPerfil.Sospechoso,
            Estado = TerceroEstado.Prospecto,
            Cargo = Normalize(prospecto.Cargo),
            Ciudad = Normalize(prospecto.Ciudad),
            Email = Normalize(prospecto.Correo),
            Telefono = Normalize(prospecto.Telefono),
            Sector = Normalize(prospecto.Empresa),
            BolsaColumnaId = primera.Id
        };
        _db.Terceros.Add(tercero);
        prospecto.TerceroId = tercero.Id;

        // Alta del tercero + (posibles columnas default) + enlace del prospecto: un solo SaveChanges atomico.
        await _db.SaveChangesAsync(cancellationToken);
        return TerceroResult<Guid>.Ok(tercero.Id);
    }

    // ---- Bolsa (kanban de terceros por columna) ----

    public async Task<IReadOnlyList<BolsaColumnaDto>> ListColumnasAsync(CancellationToken cancellationToken = default)
    {
        var columnas = await _db.BolsaColumnas.AsNoTracking()
            .Where(c => !c.IsArchived)
            .OrderBy(c => c.SortOrder)
            .ToListAsync(cancellationToken);

        var counts = await _db.Terceros.AsNoTracking()
            .Where(t => t.BolsaColumnaId != null)
            .GroupBy(t => t.BolsaColumnaId!.Value)
            .Select(g => new { ColumnaId = g.Key, Total = g.Count() })
            .ToListAsync(cancellationToken);
        var byCol = counts.ToDictionary(x => x.ColumnaId, x => x.Total);

        return columnas.Select(c => new BolsaColumnaDto(
            c.Id, c.Nombre, c.Color, c.SortOrder, c.EsCliente,
            byCol.TryGetValue(c.Id, out var n) ? n : 0)).ToList();
    }

    public async Task<TerceroResult<BolsaColumnaDto>> SaveColumnaAsync(
        Guid? id, string nombre, string color, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
        {
            return TerceroResult<BolsaColumnaDto>.Invalid("No hay tenant activo.");
        }
        var name = (nombre ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return TerceroResult<BolsaColumnaDto>.Invalid("El nombre de la columna es obligatorio.");
        }
        if (name.Length > 120)
        {
            return TerceroResult<BolsaColumnaDto>.Invalid("El nombre no puede superar 120 caracteres.");
        }
        var col = string.IsNullOrWhiteSpace(color) ? "--t-slate" : color.Trim();

        BolsaColumna entity;
        if (id is Guid columnaId)
        {
            var found = await _db.BolsaColumnas.FirstOrDefaultAsync(c => c.Id == columnaId, cancellationToken);
            if (found is null)
            {
                return TerceroResult<BolsaColumnaDto>.NotFound("La columna no existe.");
            }
            entity = found;
            entity.Nombre = name;
            entity.Color = col;
        }
        else
        {
            var sortOrder = (await _db.BolsaColumnas.Select(c => (int?)c.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
            entity = new BolsaColumna
            {
                TenantId = tenantId,
                Nombre = name,
                Color = col,
                SortOrder = sortOrder
            };
            _db.BolsaColumnas.Add(entity);
        }
        await _db.SaveChangesAsync(cancellationToken);

        var total = await _db.Terceros.AsNoTracking().CountAsync(t => t.BolsaColumnaId == entity.Id, cancellationToken);
        return TerceroResult<BolsaColumnaDto>.Ok(new BolsaColumnaDto(
            entity.Id, entity.Nombre, entity.Color, entity.SortOrder, entity.EsCliente, total));
    }

    public async Task<TerceroResult<bool>> DeleteColumnaAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.BolsaColumnas.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (entity is null)
        {
            return TerceroResult<bool>.NotFound("La columna no existe.");
        }
        var enUso = await _db.Terceros.AsNoTracking().AnyAsync(t => t.BolsaColumnaId == id, cancellationToken);
        if (enUso)
        {
            return TerceroResult<bool>.Invalid("No se puede eliminar una columna con contactos. Muevelos primero.");
        }
        _db.BolsaColumnas.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return TerceroResult<bool>.Ok(true);
    }

    public async Task<IReadOnlyList<BolsaTerceroDto>> ListBolsaAsync(CancellationToken cancellationToken = default)
    {
        var terceros = await _db.Terceros.AsNoTracking()
            .Where(t => t.BolsaColumnaId != null)
            .OrderBy(t => t.Nombre)
            .Select(t => new
            {
                t.Id,
                ColumnaId = t.BolsaColumnaId!.Value,
                t.Nombre,
                t.Cargo,
                t.Sector,
                t.Vendedor
            })
            .ToListAsync(cancellationToken);

        var terceroIds = terceros.Select(t => t.Id).ToList();
        var opsAgg = await _db.Oportunidades.AsNoTracking()
            .Where(o => o.Etapa != OportunidadEtapa.Ganada && o.Etapa != OportunidadEtapa.Perdida
                && terceroIds.Contains(o.TerceroId))
            .GroupBy(o => o.TerceroId)
            .Select(g => new { TerceroId = g.Key, Total = g.Count(), Valor = g.Sum(x => x.Valor) })
            .ToListAsync(cancellationToken);
        var byTercero = opsAgg.ToDictionary(x => x.TerceroId);

        return terceros.Select(t =>
        {
            byTercero.TryGetValue(t.Id, out var k);
            return new BolsaTerceroDto(
                t.Id, t.ColumnaId, t.Nombre, t.Cargo ?? t.Sector, t.Vendedor,
                k?.Total ?? 0, k?.Valor ?? 0m);
        }).ToList();
    }

    public async Task<TerceroResult<bool>> MoverTerceroAsync(
        Guid terceroId, Guid columnaId, CancellationToken cancellationToken = default)
    {
        var tercero = await _db.Terceros.FirstOrDefaultAsync(t => t.Id == terceroId, cancellationToken);
        if (tercero is null)
        {
            return TerceroResult<bool>.NotFound("El tercero no existe.");
        }
        var columna = await _db.BolsaColumnas.FirstOrDefaultAsync(c => c.Id == columnaId, cancellationToken);
        if (columna is null)
        {
            return TerceroResult<bool>.NotFound("La columna destino no existe.");
        }
        tercero.BolsaColumnaId = columnaId;
        // Columna terminal "Cliente ganado": promueve el tercero a Cliente activo.
        if (columna.EsCliente)
        {
            tercero.Perfiles |= TerceroPerfil.Cliente;
            tercero.Estado = TerceroEstado.Activo;
        }
        await _db.SaveChangesAsync(cancellationToken);
        return TerceroResult<bool>.Ok(true);
    }

    // ---- Oportunidades ----

    public async Task<IReadOnlyList<OportunidadDto>> ListOportunidadesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await QueryOportunidadesRows(_db.Oportunidades.AsNoTracking(), cancellationToken);
        // Orden: por SortOrder de la etapa configurable cuando existe, si no cae al enum heredado.
        return rows
            .OrderBy(o => o.EstadoSort ?? (int)o.Etapa)
            .ThenBy(o => o.SortOrder)
            .ThenByDescending(o => o.Valor)
            .Select(ToDtoFromRow).ToList();
    }

    public async Task<IReadOnlyList<OportunidadDto>> ListOportunidadesByTerceroAsync(
        Guid terceroId, CancellationToken cancellationToken = default)
    {
        var rows = await QueryOportunidadesRows(
            _db.Oportunidades.AsNoTracking().Where(o => o.TerceroId == terceroId), cancellationToken);
        // Abiertas primero (por EstadoTipo cuando existe; si no, por el enum heredado), luego por orden de etapa.
        return rows
            .OrderBy(o => IsClosedRow(o) ? 1 : 0)
            .ThenBy(o => o.EstadoSort ?? (int)o.Etapa)
            .ThenByDescending(o => o.Valor)
            .Select(ToDtoFromRow).ToList();
    }

    // LEFT JOIN Oportunidades -> OportunidadEstados via la navegacion Estado (nullable en transicion:
    // EF genera el LEFT JOIN y los accesos protegidos por o.Estado != null caen a null si no hay etapa).
    private static async Task<List<OportunidadRow>> QueryOportunidadesRows(
        IQueryable<Oportunidad> source, CancellationToken cancellationToken)
        => await source
            .Select(o => new OportunidadRow(
                o.Id, o.TerceroId, o.Tercero!.Nombre, o.Nombre, o.Etapa, o.Valor, o.Responsable,
                o.Probabilidad, o.FechaCierre, o.Fuente, o.Descripcion, o.SortOrder,
                o.EstadoId,
                o.Estado != null ? o.Estado.Name : null,
                o.Estado != null ? o.Estado.Color : null,
                o.Estado != null ? (OportunidadEstadoTipo?)o.Estado.Tipo : null,
                o.Estado != null ? (int?)o.Estado.SortOrder : null))
            .ToListAsync(cancellationToken);

    private static bool IsClosedRow(OportunidadRow o)
        => o.EstadoTipo is OportunidadEstadoTipo tipo
            ? tipo != OportunidadEstadoTipo.Abierta
            : o.Etapa == OportunidadEtapa.Ganada || o.Etapa == OportunidadEtapa.Perdida;

    private static OportunidadDto ToDtoFromRow(OportunidadRow o) => new(
        o.Id, o.TerceroId, o.TerceroNombre, o.Nombre, o.Etapa, o.Valor, o.Responsable,
        o.Probabilidad, o.FechaCierre, o.Fuente, o.Descripcion,
        o.EstadoId, o.EstadoNombre, o.EstadoColor, o.EstadoTipo);

    private sealed record OportunidadRow(
        Guid Id, Guid TerceroId, string TerceroNombre, string Nombre, OportunidadEtapa Etapa,
        decimal Valor, string? Responsable, int Probabilidad, DateTimeOffset? FechaCierre,
        string? Fuente, string? Descripcion, int SortOrder,
        Guid? EstadoId, string? EstadoNombre, string? EstadoColor, OportunidadEstadoTipo? EstadoTipo,
        int? EstadoSort);

    public async Task<TerceroResult<OportunidadDto>> CreateOportunidadAsync(
        Guid terceroId, SaveOportunidadRequest req, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
        {
            return TerceroResult<OportunidadDto>.Invalid("No hay tenant activo.");
        }
        var tercero = await _db.Terceros.AsNoTracking().FirstOrDefaultAsync(t => t.Id == terceroId, cancellationToken);
        if (tercero is null)
        {
            return TerceroResult<OportunidadDto>.NotFound("El tercero no existe.");
        }
        var error = ValidateOportunidad(req);
        if (error is not null)
        {
            return TerceroResult<OportunidadDto>.Invalid(error);
        }

        var sortOrder = (await _db.Oportunidades.Where(o => o.TerceroId == terceroId)
            .Select(o => (int?)o.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var entity = new Oportunidad
        {
            TenantId = tenantId,
            TerceroId = terceroId,
            SortOrder = sortOrder
        };
        ApplyOportunidad(entity, req);

        // Etapa CONFIGURABLE (000740): la nueva oportunidad nace en la primera etapa no archivada del
        // pipeline. Se garantiza que exista al menos una sembrando los defaults si el tenant no tiene.
        await _estados.EnsureDefaultsAsync(cancellationToken);
        var primerEstado = await _db.OportunidadEstados.AsNoTracking()
            .Where(e => !e.IsArchived)
            .OrderBy(e => e.SortOrder)
            .Select(e => new { e.Id, e.Name, e.Color, e.Tipo })
            .FirstOrDefaultAsync(cancellationToken);
        if (primerEstado is not null) { entity.EstadoId = primerEstado.Id; }

        _db.Oportunidades.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return TerceroResult<OportunidadDto>.Ok(ToDto(
            entity, tercero.Nombre,
            primerEstado?.Name, primerEstado?.Color, primerEstado?.Tipo));
    }

    public async Task<TerceroResult<OportunidadDto>> UpdateOportunidadAsync(
        Guid id, SaveOportunidadRequest req, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Oportunidades.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (entity is null)
        {
            return TerceroResult<OportunidadDto>.NotFound("La oportunidad no existe.");
        }
        var error = ValidateOportunidad(req);
        if (error is not null)
        {
            return TerceroResult<OportunidadDto>.Invalid(error);
        }
        ApplyOportunidad(entity, req);
        await _db.SaveChangesAsync(cancellationToken);

        var nombre = await _db.Terceros.AsNoTracking()
            .Where(t => t.Id == entity.TerceroId).Select(t => t.Nombre).FirstOrDefaultAsync(cancellationToken);
        var estado = entity.EstadoId is Guid eid
            ? await _db.OportunidadEstados.AsNoTracking()
                .Where(e => e.Id == eid).Select(e => new { e.Name, e.Color, e.Tipo }).FirstOrDefaultAsync(cancellationToken)
            : null;
        return TerceroResult<OportunidadDto>.Ok(ToDto(
            entity, nombre ?? string.Empty, estado?.Name, estado?.Color, estado?.Tipo));
    }

    public async Task<TerceroResult<bool>> MoverEtapaAsync(
        Guid id, OportunidadEtapa etapa, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Oportunidades.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (entity is null)
        {
            return TerceroResult<bool>.NotFound("La oportunidad no existe.");
        }
        entity.Etapa = etapa;
        await _db.SaveChangesAsync(cancellationToken);
        return TerceroResult<bool>.Ok(true);
    }

    public async Task<TerceroResult<bool>> MoverEstadoAsync(
        Guid id, Guid estadoId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Oportunidades.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (entity is null)
        {
            return TerceroResult<bool>.NotFound("La oportunidad no existe.");
        }
        // La etapa destino debe existir para el tenant (filtro global aplica el aislamiento).
        var estado = await _db.OportunidadEstados.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == estadoId, cancellationToken);
        if (estado is null)
        {
            return TerceroResult<bool>.NotFound("La etapa destino no existe.");
        }
        entity.EstadoId = estadoId;
        // Mantiene el enum heredado alineado por si algun consumidor aun lo lee: mapea por SortOrder.
        if (Enum.IsDefined(typeof(OportunidadEtapa), estado.SortOrder))
        {
            entity.Etapa = (OportunidadEtapa)estado.SortOrder;
        }
        await _db.SaveChangesAsync(cancellationToken);
        return TerceroResult<bool>.Ok(true);
    }

    public async Task<TerceroResult<bool>> DeleteOportunidadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Oportunidades.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (entity is null)
        {
            return TerceroResult<bool>.NotFound("La oportunidad no existe.");
        }
        // Desligar las citas que apuntaban a esta oportunidad y borrar el registro: un solo
        // SaveChanges atomico (evita violar la FK Cita.OportunidadId).
        var citas = await _db.Citas.Where(c => c.OportunidadId == id).ToListAsync(cancellationToken);
        foreach (var cita in citas)
        {
            cita.OportunidadId = null;
        }
        _db.Oportunidades.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return TerceroResult<bool>.Ok(true);
    }

    // ---- Citas / Agenda ----

    public async Task<IReadOnlyList<CitaDto>> ListCitasAsync(
        int anio, int mes, CancellationToken cancellationToken = default)
    {
        if (mes < 1 || mes > 12)
        {
            return Array.Empty<CitaDto>();
        }
        var desde = new DateTimeOffset(anio, mes, 1, 0, 0, 0, TimeSpan.Zero);
        var hasta = desde.AddMonths(1);
        var rows = await _db.Citas.AsNoTracking()
            .Where(c => c.Inicio >= desde && c.Inicio < hasta)
            .OrderBy(c => c.Inicio)
            .Select(c => new
            {
                c.Id,
                c.TerceroId,
                TerceroNombre = c.Tercero != null ? c.Tercero.Nombre : null,
                c.OportunidadId,
                c.Titulo,
                c.Tipo,
                c.Inicio,
                c.DuracionMinutos,
                c.Nota,
                c.Completada
            })
            .ToListAsync(cancellationToken);
        return rows.Select(c => new CitaDto(
            c.Id, c.TerceroId, c.TerceroNombre, c.OportunidadId, c.Titulo, c.Tipo, c.Inicio,
            c.DuracionMinutos, c.Nota, c.Completada)).ToList();
    }

    public async Task<IReadOnlyList<CitaDto>> ListCitasByTerceroAsync(
        Guid terceroId, CancellationToken cancellationToken = default)
    {
        var rows = await _db.Citas.AsNoTracking()
            .Where(c => c.TerceroId == terceroId)
            .OrderByDescending(c => c.Inicio)
            .Select(c => new
            {
                c.Id,
                c.TerceroId,
                TerceroNombre = c.Tercero != null ? c.Tercero.Nombre : null,
                c.OportunidadId,
                c.Titulo,
                c.Tipo,
                c.Inicio,
                c.DuracionMinutos,
                c.Nota,
                c.Completada
            })
            .ToListAsync(cancellationToken);
        return rows.Select(c => new CitaDto(
            c.Id, c.TerceroId, c.TerceroNombre, c.OportunidadId, c.Titulo, c.Tipo, c.Inicio,
            c.DuracionMinutos, c.Nota, c.Completada)).ToList();
    }

    public async Task<TerceroResult<CitaDto>> CreateCitaAsync(
        SaveCitaRequest req, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
        {
            return TerceroResult<CitaDto>.Invalid("No hay tenant activo.");
        }
        var titulo = (req.Titulo ?? string.Empty).Trim();
        if (titulo.Length == 0)
        {
            return TerceroResult<CitaDto>.Invalid("El titulo de la cita es obligatorio.");
        }
        if (req.TerceroId is Guid tid
            && !await _db.Terceros.AsNoTracking().AnyAsync(t => t.Id == tid, cancellationToken))
        {
            return TerceroResult<CitaDto>.Invalid("El tercero de la cita no existe.");
        }
        if (req.OportunidadId is Guid oid
            && !await _db.Oportunidades.AsNoTracking().AnyAsync(o => o.Id == oid, cancellationToken))
        {
            return TerceroResult<CitaDto>.Invalid("La oportunidad de la cita no existe.");
        }

        var entity = new Cita { TenantId = tenantId };
        ApplyCita(entity, req, titulo);
        _db.Citas.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return TerceroResult<CitaDto>.Ok(await ToCitaDtoAsync(entity, cancellationToken));
    }

    public async Task<TerceroResult<CitaDto>> UpdateCitaAsync(
        Guid id, SaveCitaRequest req, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Citas.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (entity is null)
        {
            return TerceroResult<CitaDto>.NotFound("La cita no existe.");
        }
        var titulo = (req.Titulo ?? string.Empty).Trim();
        if (titulo.Length == 0)
        {
            return TerceroResult<CitaDto>.Invalid("El titulo de la cita es obligatorio.");
        }
        if (req.TerceroId is Guid tid
            && !await _db.Terceros.AsNoTracking().AnyAsync(t => t.Id == tid, cancellationToken))
        {
            return TerceroResult<CitaDto>.Invalid("El tercero de la cita no existe.");
        }
        if (req.OportunidadId is Guid oid
            && !await _db.Oportunidades.AsNoTracking().AnyAsync(o => o.Id == oid, cancellationToken))
        {
            return TerceroResult<CitaDto>.Invalid("La oportunidad de la cita no existe.");
        }
        ApplyCita(entity, req, titulo);
        await _db.SaveChangesAsync(cancellationToken);
        return TerceroResult<CitaDto>.Ok(await ToCitaDtoAsync(entity, cancellationToken));
    }

    public async Task<TerceroResult<bool>> DeleteCitaAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Citas.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (entity is null)
        {
            return TerceroResult<bool>.NotFound("La cita no existe.");
        }
        _db.Citas.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return TerceroResult<bool>.Ok(true);
    }

    public async Task<TerceroResult<bool>> SetCompletadaAsync(
        Guid id, bool completada, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Citas.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (entity is null)
        {
            return TerceroResult<bool>.NotFound("La cita no existe.");
        }
        entity.Completada = completada;
        await _db.SaveChangesAsync(cancellationToken);
        return TerceroResult<bool>.Ok(true);
    }

    // ---- Filtros dinamicos ----

    public async Task<IReadOnlyList<FiltroDto>> ListFiltrosAsync(CancellationToken cancellationToken = default)
    {
        var filtros = await _db.TerceroFiltros.AsNoTracking()
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Nombre)
            .ToListAsync(cancellationToken);

        // Se cargan las filas del universo una sola vez y cada filtro se evalua en memoria.
        var rows = await LoadTerceroRowsAsync(cancellationToken);
        return filtros.Select(f =>
        {
            var criterios = DeserializeCriterios(f.CriteriosJson);
            var conteo = CountMatching(rows, criterios);
            return new FiltroDto(
                f.Id, f.Nombre, f.Descripcion, f.Fuente, conteo, f.ConteoAnterior,
                Crecimiento(conteo, f.ConteoAnterior), criterios);
        }).ToList();
    }

    public async Task<TerceroResult<FiltroDto>> SaveFiltroAsync(
        Guid? id, SaveFiltroRequest req, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
        {
            return TerceroResult<FiltroDto>.Invalid("No hay tenant activo.");
        }
        var nombre = (req.Nombre ?? string.Empty).Trim();
        if (nombre.Length == 0)
        {
            return TerceroResult<FiltroDto>.Invalid("El nombre del filtro es obligatorio.");
        }
        if (nombre.Length > 150)
        {
            return TerceroResult<FiltroDto>.Invalid("El nombre no puede superar 150 caracteres.");
        }
        var criterios = req.Criterios ?? Array.Empty<FiltroCriterio>();
        var criteriosJson = JsonSerializer.Serialize(criterios, JsonOpts);

        var rows = await LoadTerceroRowsAsync(cancellationToken);
        var conteo = CountMatching(rows, criterios);

        TerceroFiltro entity;
        if (id is Guid filtroId)
        {
            var found = await _db.TerceroFiltros.FirstOrDefaultAsync(f => f.Id == filtroId, cancellationToken);
            if (found is null)
            {
                return TerceroResult<FiltroDto>.NotFound("El filtro no existe.");
            }
            entity = found;
            entity.Nombre = nombre;
            entity.Descripcion = Normalize(req.Descripcion);
            entity.Fuente = Normalize(req.Fuente);
            entity.CriteriosJson = criteriosJson;
        }
        else
        {
            var sortOrder = (await _db.TerceroFiltros.Select(f => (int?)f.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
            entity = new TerceroFiltro
            {
                TenantId = tenantId,
                Nombre = nombre,
                Descripcion = Normalize(req.Descripcion),
                Fuente = Normalize(req.Fuente),
                CriteriosJson = criteriosJson,
                // Al crear se fija el snapshot al conteo actual para que el % arranque coherente (0).
                ConteoAnterior = conteo,
                FechaSnapshot = DateTimeOffset.UtcNow,
                SortOrder = sortOrder
            };
            _db.TerceroFiltros.Add(entity);
        }
        await _db.SaveChangesAsync(cancellationToken);

        var criteriosOut = DeserializeCriterios(entity.CriteriosJson);
        return TerceroResult<FiltroDto>.Ok(new FiltroDto(
            entity.Id, entity.Nombre, entity.Descripcion, entity.Fuente, conteo, entity.ConteoAnterior,
            Crecimiento(conteo, entity.ConteoAnterior), criteriosOut));
    }

    public async Task<TerceroResult<bool>> DeleteFiltroAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.TerceroFiltros.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (entity is null)
        {
            return TerceroResult<bool>.NotFound("El filtro no existe.");
        }
        _db.TerceroFiltros.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return TerceroResult<bool>.Ok(true);
    }

    public async Task<int> ContarAsync(
        IReadOnlyList<FiltroCriterio> criterios, string? fuente, CancellationToken cancellationToken = default)
    {
        // El parametro fuente se reserva para cuando el contacto lleve columna de origen; hoy el
        // universo del conteo es el Tercero (sin columna Fuente), asi que fuente no filtra aqui.
        var rows = await LoadTerceroRowsAsync(cancellationToken);
        return CountMatching(rows, criterios ?? Array.Empty<FiltroCriterio>());
    }

    // ---- Demo seed (columnas por defecto compartidas) ----

    /// <summary>
    /// Columnas por defecto de la Bolsa (mismo set que siembra el seeder demo). Se usa como
    /// respaldo cuando se promueve un prospecto y el tenant aun no tiene columnas.
    /// </summary>
    public static IReadOnlyList<BolsaColumna> BuildDefaultColumnas(Guid tenantId) => new List<BolsaColumna>
    {
        new() { TenantId = tenantId, Nombre = "Sospechoso", Color = "--t-rose", SortOrder = 0 },
        new() { TenantId = tenantId, Nombre = "Incubadora", Color = "--t-blue", SortOrder = 1 },
        new() { TenantId = tenantId, Nombre = "Clientes", Color = "--t-green", SortOrder = 2, EsCliente = true },
        new() { TenantId = tenantId, Nombre = "Seguimiento Cotizacion", Color = "--t-amber", SortOrder = 3 },
        new() { TenantId = tenantId, Nombre = "Cierre", Color = "--t-violet", SortOrder = 4 }
    };

    // ---- Internos ----

    private static void ApplyOportunidad(Oportunidad entity, SaveOportunidadRequest req)
    {
        entity.Nombre = (req.Nombre ?? string.Empty).Trim();
        entity.Etapa = req.Etapa;
        entity.Valor = req.Valor;
        entity.Responsable = Normalize(req.Responsable);
        entity.Probabilidad = Math.Clamp(req.Probabilidad, 0, 100);
        entity.FechaCierre = req.FechaCierre;
        entity.Fuente = Normalize(req.Fuente);
        entity.Descripcion = Normalize(req.Descripcion);
    }

    private static string? ValidateOportunidad(SaveOportunidadRequest req)
    {
        var nombre = (req.Nombre ?? string.Empty).Trim();
        if (nombre.Length == 0) { return "El nombre de la oportunidad es obligatorio."; }
        if (nombre.Length > 200) { return "El nombre no puede superar 200 caracteres."; }
        if (req.Valor < 0) { return "El valor no puede ser negativo."; }
        return null;
    }

    private static OportunidadDto ToDto(
        Oportunidad o, string terceroNombre,
        string? estadoNombre = null, string? estadoColor = null, OportunidadEstadoTipo? estadoTipo = null) => new(
        o.Id, o.TerceroId, terceroNombre, o.Nombre, o.Etapa, o.Valor, o.Responsable,
        o.Probabilidad, o.FechaCierre, o.Fuente, o.Descripcion,
        o.EstadoId, estadoNombre, estadoColor, estadoTipo);

    private static void ApplyCita(Cita entity, SaveCitaRequest req, string titulo)
    {
        entity.TerceroId = req.TerceroId;
        entity.OportunidadId = req.OportunidadId;
        entity.Titulo = titulo;
        entity.Tipo = req.Tipo;
        entity.Inicio = req.Inicio == default ? DateTimeOffset.UtcNow : req.Inicio;
        entity.DuracionMinutos = req.DuracionMinutos < 0 ? 0 : req.DuracionMinutos;
        entity.Nota = Normalize(req.Nota);
    }

    private async Task<CitaDto> ToCitaDtoAsync(Cita entity, CancellationToken cancellationToken)
    {
        string? terceroNombre = null;
        if (entity.TerceroId is Guid tid)
        {
            terceroNombre = await _db.Terceros.AsNoTracking()
                .Where(t => t.Id == tid).Select(t => t.Nombre).FirstOrDefaultAsync(cancellationToken);
        }
        return new CitaDto(
            entity.Id, entity.TerceroId, terceroNombre, entity.OportunidadId, entity.Titulo,
            entity.Tipo, entity.Inicio, entity.DuracionMinutos, entity.Nota, entity.Completada);
    }

    /// <summary>Universo del conteo de filtros: terceros no inactivos, proyectados a memoria.</summary>
    private async Task<List<TerceroRow>> LoadTerceroRowsAsync(CancellationToken cancellationToken)
        => await _db.Terceros.AsNoTracking()
            .Where(t => t.Estado != TerceroEstado.Inactivo)
            .Select(t => new TerceroRow(
                t.Nombre, t.Ciudad, t.Vendedor, t.Sector, t.Cargo, t.Perfiles, t.Estado))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Cuenta las filas que cumplen TODOS los criterios (AND). Los criterios sobre campos no
    /// soportados (p. ej. campos dinamicos de FichasJson) se IGNORAN (no excluyen filas).
    /// </summary>
    private static int CountMatching(IReadOnlyList<TerceroRow> rows, IReadOnlyList<FiltroCriterio> criterios)
        => rows.Count(row => criterios.All(c => Matches(row, c)));

    private static bool Matches(TerceroRow row, FiltroCriterio criterio)
    {
        var campo = (criterio.Campo ?? string.Empty).Trim().ToLowerInvariant();
        var op = (criterio.Operador ?? "=").Trim();
        var valor = criterio.Valor ?? string.Empty;

        // Perfil: se compara contra el flag [Flags] TerceroPerfil.
        if (campo == "perfil")
        {
            if (!Enum.TryParse<TerceroPerfil>(valor, ignoreCase: true, out var perfil) || perfil == TerceroPerfil.Ninguno)
            {
                return true; // valor de perfil desconocido: no filtra.
            }
            var tiene = (row.Perfiles & perfil) == perfil;
            return op switch
            {
                "!=" => !tiene,
                _ => tiene // "=", "LIKE" y demas: posee el perfil.
            };
        }

        var actual = campo switch
        {
            "ciudad" => row.Ciudad,
            "vendedor" => row.Vendedor,
            "nombre" => row.Nombre,
            "sector" => row.Sector,
            "cargo" => row.Cargo,
            "estado" => row.Estado.ToString(),
            _ => null
        };
        // Campo no soportado (dinamico/ficha): se ignora el criterio.
        if (campo is not ("ciudad" or "vendedor" or "nombre" or "sector" or "cargo" or "estado"))
        {
            return true;
        }

        switch (op)
        {
            case "=":
                return string.Equals(actual ?? string.Empty, valor, StringComparison.OrdinalIgnoreCase);
            case "!=":
                return !string.Equals(actual ?? string.Empty, valor, StringComparison.OrdinalIgnoreCase);
            case "LIKE":
                return actual is not null && actual.Contains(valor, StringComparison.OrdinalIgnoreCase);
            case ">":
            case "<":
                if (decimal.TryParse(actual, NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && decimal.TryParse(valor, NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    return op == ">" ? a > b : a < b;
                }
                return false;
            default:
                return false;
        }
    }

    /// <summary>% de crecimiento del conteo frente al snapshot anterior (entero redondeado).</summary>
    private static int Crecimiento(int conteo, int conteoAnterior)
    {
        if (conteoAnterior == 0)
        {
            return conteo > 0 ? 100 : 0;
        }
        return (int)Math.Round((conteo - conteoAnterior) * 100m / conteoAnterior, MidpointRounding.AwayFromZero);
    }

    private static IReadOnlyList<FiltroCriterio> DeserializeCriterios(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return Array.Empty<FiltroCriterio>(); }
        try
        {
            return JsonSerializer.Deserialize<List<FiltroCriterio>>(json, JsonOpts) ?? new List<FiltroCriterio>();
        }
        catch (JsonException)
        {
            return Array.Empty<FiltroCriterio>();
        }
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Proyeccion en memoria de un tercero para evaluar los criterios de filtro.</summary>
    private sealed record TerceroRow(
        string Nombre,
        string? Ciudad,
        string? Vendedor,
        string? Sector,
        string? Cargo,
        TerceroPerfil Perfiles,
        TerceroEstado Estado);
}
