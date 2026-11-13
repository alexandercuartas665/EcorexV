using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Agendas;

/// <summary>
/// Implementacion de <see cref="IAgendaService"/> (ADR-0165). Tenant-scoped por el filtro global del DbContext
/// (nunca se filtra a mano por TenantId); en las altas fija el TenantId del contexto. Las franjas se reemplazan
/// en bloque dentro de una transaccion; los agregados se borran de forma logica.
/// </summary>
public sealed class AgendaService : IAgendaService
{
    private const string ErrConcurrencia = "Otra persona modifico este registro mientras lo editabas. Recarga y vuelve a intentar.";

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public AgendaService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    private Guid TenantId => _tenant.TenantId ?? Guid.Empty;

    // =====================================================================================================
    // Tipos de recurso
    // =====================================================================================================

    /// <summary>Tipos que se sugieren al tenant (nombre, icono, color, vinculo).</summary>
    public static readonly (string Nombre, string Icono, string Color, AgendaVinculo Vinculo)[] TiposSugeridos =
    {
        ("Persona", "persona", "blue", AgendaVinculo.Usuario),
        ("Grupo", "grupo", "violet", AgendaVinculo.Libre),
        ("Espacio", "espacio", "green", AgendaVinculo.Libre),
        ("Equipo", "equipo", "amber", AgendaVinculo.Libre),
        ("Vehiculo", "vehiculo", "slate", AgendaVinculo.Libre),
        ("Punto de atencion", "punto", "rose", AgendaVinculo.Libre)
    };

    public async Task<IReadOnlyList<AgendaTipoDto>> ListTiposAsync(CancellationToken cancellationToken = default)
    {
        var tipos = await _db.AgendaTiposRecurso.AsNoTracking()
            .OrderBy(t => t.Orden).ThenBy(t => t.Nombre)
            .ToListAsync(cancellationToken);
        var conteo = await _db.AgendaRecursos.AsNoTracking()
            .GroupBy(r => r.TipoRecursoId)
            .Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N, cancellationToken);
        return tipos
            .Select(t => new AgendaTipoDto(t.Id, t.Nombre, t.Icono, t.Color, t.Vinculo, t.Orden, t.Activo, conteo.GetValueOrDefault(t.Id)))
            .ToList();
    }

    public async Task<AgendaOpResult> SaveTipoAsync(SaveAgendaTipoRequest request, CancellationToken cancellationToken = default)
    {
        var nombre = Limpio(request.Nombre, 80);
        if (nombre is null)
        {
            return AgendaOpResult.Mal("Escribe el nombre del tipo de recurso.");
        }
        var duplicado = await _db.AgendaTiposRecurso.AnyAsync(
            t => t.Nombre.ToLower() == nombre.ToLower() && t.Id != (request.Id ?? Guid.Empty), cancellationToken);
        if (duplicado)
        {
            return AgendaOpResult.Mal($"Ya existe un tipo de recurso llamado \"{nombre}\".");
        }

        AgendaTipoRecurso? tipo;
        if (request.Id is Guid id)
        {
            tipo = await _db.AgendaTiposRecurso.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
            if (tipo is null)
            {
                return AgendaOpResult.Mal("El tipo de recurso ya no existe.");
            }
        }
        else
        {
            var orden = await _db.AgendaTiposRecurso.Select(t => (int?)t.Orden).MaxAsync(cancellationToken) ?? -1;
            tipo = new AgendaTipoRecurso { TenantId = TenantId, Orden = orden + 1 };
            _db.AgendaTiposRecurso.Add(tipo);
        }
        tipo.Nombre = nombre;
        tipo.Icono = Limpio(request.Icono, 30) ?? "persona";
        tipo.Color = Limpio(request.Color, 20) ?? "blue";
        tipo.Vinculo = request.Vinculo;
        tipo.Activo = request.Activo;
        await _db.SaveChangesAsync(cancellationToken);
        return AgendaOpResult.Bien(tipo.Id);
    }

    public async Task<AgendaOpResult> DeleteTipoAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tipo = await _db.AgendaTiposRecurso.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tipo is null)
        {
            return AgendaOpResult.Mal("El tipo de recurso ya no existe.");
        }
        var usados = await _db.AgendaRecursos.CountAsync(r => r.TipoRecursoId == id, cancellationToken);
        if (usados > 0)
        {
            return AgendaOpResult.Mal($"No se puede quitar: {usados} recurso(s) usan este tipo. Cambialos de tipo o inactiva el tipo.");
        }
        MarcarBorrado(tipo);
        await _db.SaveChangesAsync(cancellationToken);
        return AgendaOpResult.Bien(id);
    }

    public async Task<int> CrearTiposSugeridosAsync(CancellationToken cancellationToken = default)
    {
        var existentes = (await _db.AgendaTiposRecurso.Select(t => t.Nombre).ToListAsync(cancellationToken))
            .Select(n => n.ToLowerInvariant())
            .ToHashSet();
        var orden = await _db.AgendaTiposRecurso.Select(t => (int?)t.Orden).MaxAsync(cancellationToken) ?? -1;
        var creados = 0;
        foreach (var s in TiposSugeridos)
        {
            if (!existentes.Add(s.Nombre.ToLowerInvariant()))
            {
                continue;
            }
            _db.AgendaTiposRecurso.Add(new AgendaTipoRecurso
            {
                TenantId = TenantId,
                Nombre = s.Nombre,
                Icono = s.Icono,
                Color = s.Color,
                Vinculo = s.Vinculo,
                Orden = ++orden
            });
            creados++;
        }
        if (creados > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        return creados;
    }

    // =====================================================================================================
    // Recursos
    // =====================================================================================================

    public async Task<IReadOnlyList<AgendaRecursoDto>> ListRecursosAsync(CancellationToken cancellationToken = default)
    {
        var recursos = await _db.AgendaRecursos.AsNoTracking()
            .Include(r => r.TipoRecurso)
            .OrderBy(r => r.Nombre)
            .ToListAsync(cancellationToken);
        return await ProyectarAsync(recursos, cancellationToken);
    }

    public async Task<AgendaRecursoDto?> GetRecursoAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var r = await _db.AgendaRecursos.AsNoTracking()
            .Include(x => x.TipoRecurso)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (r is null)
        {
            return null;
        }
        return (await ProyectarAsync(new List<AgendaRecurso> { r }, cancellationToken))[0];
    }

    private async Task<IReadOnlyList<AgendaRecursoDto>> ProyectarAsync(List<AgendaRecurso> recursos, CancellationToken cancellationToken)
    {
        var ids = recursos.Select(r => r.Id).ToList();
        var sedeIds = recursos.Where(r => r.SedeId.HasValue).Select(r => r.SedeId!.Value).Distinct().ToList();
        var userIds = recursos.Where(r => r.TenantUserId.HasValue).Select(r => r.TenantUserId!.Value).Distinct().ToList();

        var sedes = await _db.Entidades.AsNoTracking()
            .Where(e => sedeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.Nombre, cancellationToken);
        var usuarios = await NombresUsuariosAsync(userIds, cancellationToken);

        var vigencias = await _db.AgendaRecursoVigencias.AsNoTracking()
            .Include(v => v.Franjas)
            .Where(v => ids.Contains(v.RecursoId))
            .ToListAsync(cancellationToken);
        var hoy = await HoyAsync(cancellationToken);

        return recursos.Select(r =>
        {
            var calc = vigencias.Where(v => v.RecursoId == r.Id).Select(ACalc).ToList();
            var actual = AgendaCalculo.VigenciaPara(calc, hoy);
            var vigente = actual ?? calc.Where(v => v.Desde > hoy).OrderBy(v => v.Desde).FirstOrDefault();
            return new AgendaRecursoDto(
                r.Id, r.Nombre, r.Descripcion,
                r.TipoRecursoId, r.TipoRecurso?.Nombre ?? "-", r.TipoRecurso?.Icono ?? "persona", r.TipoRecurso?.Color ?? "slate",
                r.TipoRecurso?.Vinculo ?? AgendaVinculo.Libre,
                r.SedeId, r.SedeId is Guid s ? sedes.GetValueOrDefault(s) : null,
                r.TenantUserId, r.TenantUserId is Guid u ? usuarios.GetValueOrDefault(u) : null,
                r.AgenteHabilitado, r.Activo, r.Version,
                vigente?.Nombre, vigente is null ? 0 : AgendaCalculo.CuposSemana(vigente.Franjas),
                vigente?.Desde, actual is null && vigente is not null);
        }).ToList();
    }

    public async Task<AgendaOpResult> SaveRecursoAsync(SaveAgendaRecursoRequest request, CancellationToken cancellationToken = default)
    {
        var nombre = Limpio(request.Nombre, 150);
        if (nombre is null)
        {
            return AgendaOpResult.Mal("Escribe el nombre del recurso.");
        }
        var tipo = await _db.AgendaTiposRecurso.AsNoTracking().FirstOrDefaultAsync(t => t.Id == request.TipoRecursoId, cancellationToken);
        if (tipo is null)
        {
            return AgendaOpResult.Mal("Elige el tipo de recurso.");
        }
        if (request.SedeId is Guid sedeId && !await _db.Entidades.AnyAsync(e => e.Id == sedeId, cancellationToken))
        {
            return AgendaOpResult.Mal("La sede elegida ya no existe.");
        }
        Guid? userId = tipo.Vinculo == AgendaVinculo.Usuario ? request.TenantUserId : null;
        if (tipo.Vinculo == AgendaVinculo.Usuario && userId is null)
        {
            return AgendaOpResult.Mal($"Un recurso de tipo \"{tipo.Nombre}\" se vincula a un usuario: elige cual.");
        }
        if (userId is Guid uid && !await _db.TenantUsers.AnyAsync(u => u.Id == uid, cancellationToken))
        {
            return AgendaOpResult.Mal("El usuario elegido ya no existe en esta empresa.");
        }

        AgendaRecurso? r;
        if (request.Id is Guid id)
        {
            r = await _db.AgendaRecursos.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (r is null)
            {
                return AgendaOpResult.Mal("El recurso ya no existe.");
            }
            if (request.Version is long v && v != r.Version)
            {
                return AgendaOpResult.Mal(ErrConcurrencia);
            }
        }
        else
        {
            r = new AgendaRecurso { TenantId = TenantId };
            _db.AgendaRecursos.Add(r);
        }
        r.Nombre = nombre;
        r.Descripcion = Limpio(request.Descripcion, 500);
        r.TipoRecursoId = tipo.Id;
        r.SedeId = request.SedeId;
        r.TenantUserId = userId;
        r.AgenteHabilitado = request.AgenteHabilitado;
        r.Activo = request.Activo;
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AgendaOpResult.Mal(ErrConcurrencia);
        }
        return AgendaOpResult.Bien(r.Id);
    }

    public async Task<AgendaOpResult> DeleteRecursoAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var r = await _db.AgendaRecursos.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (r is null)
        {
            return AgendaOpResult.Mal("El recurso ya no existe.");
        }
        var hoy = await HoyAsync(cancellationToken);
        var pendientes = await _db.AgendaTurnoRecursos.CountAsync(o => o.RecursoId == id && o.Fecha >= hoy
            && (o.Turno!.Estado == AgendaTurnoEstado.Reservado || o.Turno!.Estado == AgendaTurnoEstado.Confirmado), cancellationToken);
        if (pendientes > 0)
        {
            return AgendaOpResult.Mal($"No se puede quitar: tiene {pendientes} turno(s) proximos. Reprogramalos o cancelalos, o inactiva el recurso.");
        }
        // Borrado logico en cascada diferida: el recurso y sus vigencias/novedades quedan marcados en una sola
        // transaccion (las franjas cuelgan de la vigencia y no se consultan sin ella).
        await using var tx = _db.HasActiveTransaction ? null : await _db.BeginTransactionAsync(cancellationToken);
        MarcarBorrado(r);
        foreach (var v in await _db.AgendaRecursoVigencias.Where(x => x.RecursoId == id).ToListAsync(cancellationToken))
        {
            MarcarBorrado(v);
        }
        foreach (var n in await _db.AgendaNovedades.Where(x => x.RecursoId == id).ToListAsync(cancellationToken))
        {
            MarcarBorrado(n);
        }
        await _db.SaveChangesAsync(cancellationToken);
        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }
        return AgendaOpResult.Bien(id);
    }

    public async Task<IReadOnlyList<AgendaOpcionDto>> ListSedesAsync(CancellationToken cancellationToken = default) =>
        await _db.Entidades.AsNoTracking()
            .Where(e => e.Kind == EntidadKind.Sede && e.IsActive)
            .OrderBy(e => e.Nombre)
            .Select(e => new AgendaOpcionDto(e.Id, e.Nombre))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AgendaOpcionDto>> ListUsuariosAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.TenantUsers.AsNoTracking()
            .Where(u => u.Status == PlatformUserStatus.Active)
            .Select(u => new { u.Id, u.Email, Nombre = u.PlatformUser != null ? u.PlatformUser.DisplayName : null })
            .ToListAsync(cancellationToken);
        return rows
            .Select(u => new AgendaOpcionDto(u.Id, string.IsNullOrWhiteSpace(u.Nombre) ? u.Email : u.Nombre!))
            .OrderBy(u => u.Nombre, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private async Task<Dictionary<Guid, string>> NombresUsuariosAsync(List<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }
        var rows = await _db.TenantUsers.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Email, Nombre = u.PlatformUser != null ? u.PlatformUser.DisplayName : null })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(u => u.Id, u => string.IsNullOrWhiteSpace(u.Nombre) ? u.Email : u.Nombre!);
    }

    // =====================================================================================================
    // Agendas base (plantillas)
    // =====================================================================================================

    public async Task<IReadOnlyList<AgendaPlantillaDto>> ListPlantillasAsync(CancellationToken cancellationToken = default)
    {
        var plantillas = await _db.AgendaPlantillas.AsNoTracking()
            .Include(p => p.Franjas)
            .OrderBy(p => p.Nombre)
            .ToListAsync(cancellationToken);
        var uso = await _db.AgendaRecursoVigencias.AsNoTracking()
            .Where(v => v.PlantillaOrigenId != null)
            .GroupBy(v => v.PlantillaOrigenId!.Value)
            .Select(g => new { g.Key, N = g.Select(v => v.RecursoId).Distinct().Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N, cancellationToken);
        return plantillas.Select(p =>
        {
            var franjas = p.Franjas.Select(ADto).OrderBy(OrdenFranja).ToList();
            return new AgendaPlantillaDto(p.Id, p.Nombre, p.Descripcion, p.Activa, p.Version, franjas,
                AgendaCalculo.CuposSemana(franjas), uso.GetValueOrDefault(p.Id));
        }).ToList();
    }

    public async Task<AgendaOpResult> SavePlantillaAsync(SaveAgendaPlantillaRequest request, CancellationToken cancellationToken = default)
    {
        var nombre = Limpio(request.Nombre, 120);
        if (nombre is null)
        {
            return AgendaOpResult.Mal("Escribe el nombre de la agenda base.");
        }
        var error = AgendaCalculo.ValidarFranjas(request.Franjas);
        if (error is not null)
        {
            return AgendaOpResult.Mal(error);
        }
        var duplicado = await _db.AgendaPlantillas.AnyAsync(
            p => p.Nombre.ToLower() == nombre.ToLower() && p.Id != (request.Id ?? Guid.Empty), cancellationToken);
        if (duplicado)
        {
            return AgendaOpResult.Mal($"Ya existe una agenda base llamada \"{nombre}\".");
        }

        await using var tx = _db.HasActiveTransaction ? null : await _db.BeginTransactionAsync(cancellationToken);
        AgendaPlantilla? p;
        if (request.Id is Guid id)
        {
            p = await _db.AgendaPlantillas.Include(x => x.Franjas).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (p is null)
            {
                return AgendaOpResult.Mal("La agenda base ya no existe.");
            }
            if (request.Version is long v && v != p.Version)
            {
                return AgendaOpResult.Mal(ErrConcurrencia);
            }
            _db.AgendaPlantillaFranjas.RemoveRange(p.Franjas);
            p.Franjas.Clear();
        }
        else
        {
            p = new AgendaPlantilla { TenantId = TenantId };
            _db.AgendaPlantillas.Add(p);
        }
        p.Nombre = nombre;
        p.Descripcion = Limpio(request.Descripcion, 300);
        p.Activa = request.Activa;
        // Toca la plantilla aunque solo cambien las franjas, para que su Version avance (concurrencia optimista).
        p.UpdatedAt = DateTimeOffset.UtcNow;
        // Las franjas nuevas se registran como ALTAS explicitas: nacen con Id (Guid v7) y, colgadas de la coleccion de
        // una plantilla ya rastreada, EF las tomaria por existentes y lanzaria un UPDATE sin filas (concurrencia).
        foreach (var f in request.Franjas)
        {
            _db.AgendaPlantillaFranjas.Add(new AgendaPlantillaFranja
            {
                TenantId = TenantId,
                PlantillaId = p.Id,
                Plantilla = p,
                DiaSemana = f.Dia,
                HoraInicio = f.Inicio,
                HoraFin = f.Fin,
                IntervaloMinutos = f.IntervaloMinutos,
                Cupo = f.Cupo
            });
        }
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            if (tx is not null)
            {
                await tx.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            return AgendaOpResult.Mal(ErrConcurrencia);
        }
        return AgendaOpResult.Bien(p.Id);
    }

    public async Task<AgendaOpResult> DuplicarPlantillaAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var origen = await _db.AgendaPlantillas.AsNoTracking().Include(p => p.Franjas).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (origen is null)
        {
            return AgendaOpResult.Mal("La agenda base ya no existe.");
        }
        var nombres = (await _db.AgendaPlantillas.Select(p => p.Nombre).ToListAsync(cancellationToken))
            .Select(n => n.ToLowerInvariant()).ToHashSet();
        var baseNombre = origen.Nombre.Length > 108 ? origen.Nombre[..108] : origen.Nombre;
        var nombre = $"{baseNombre} (copia)";
        for (var i = 2; nombres.Contains(nombre.ToLowerInvariant()); i++)
        {
            nombre = $"{baseNombre} (copia {i})";
        }
        return await SavePlantillaAsync(new SaveAgendaPlantillaRequest(
            null, nombre, origen.Descripcion, origen.Activa, origen.Franjas.Select(ADto).ToList()), cancellationToken);
    }

    public async Task<AgendaOpResult> DeletePlantillaAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var p = await _db.AgendaPlantillas.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (p is null)
        {
            return AgendaOpResult.Mal("La agenda base ya no existe.");
        }
        // Las agendas de los recursos importadas de esta plantilla son copias: no se tocan.
        MarcarBorrado(p);
        await _db.SaveChangesAsync(cancellationToken);
        return AgendaOpResult.Bien(id);
    }

    // =====================================================================================================
    // Agenda del recurso (vigencias)
    // =====================================================================================================

    public async Task<IReadOnlyList<AgendaVigenciaDto>> ListVigenciasAsync(Guid recursoId, CancellationToken cancellationToken = default)
    {
        var vigencias = await _db.AgendaRecursoVigencias.AsNoTracking()
            .Include(v => v.Franjas)
            .Where(v => v.RecursoId == recursoId)
            .OrderByDescending(v => v.VigenteDesde)
            .ToListAsync(cancellationToken);
        var origenIds = vigencias.Where(v => v.PlantillaOrigenId.HasValue).Select(v => v.PlantillaOrigenId!.Value).Distinct().ToList();
        var origenes = await _db.AgendaPlantillas.AsNoTracking()
            .Include(p => p.Franjas)
            .Where(p => origenIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        return vigencias.Select(v =>
        {
            var franjas = v.Franjas.Select(ADto).OrderBy(OrdenFranja).ToList();
            AgendaPlantilla? origen = v.PlantillaOrigenId is Guid o ? origenes.GetValueOrDefault(o) : null;
            var difiere = origen is not null && !AgendaCalculo.MismasFranjas(franjas, origen.Franjas.Select(ADto));
            return new AgendaVigenciaDto(v.Id, v.RecursoId, v.Nombre, v.VigenteDesde, v.VigenteHasta,
                v.PlantillaOrigenId, origen?.Nombre, difiere, franjas, AgendaCalculo.CuposSemana(franjas));
        }).ToList();
    }

    public async Task<AgendaOpResult> ImportarPlantillaAsync(ImportarPlantillaRequest request, CancellationToken cancellationToken = default)
    {
        if (!await _db.AgendaRecursos.AnyAsync(r => r.Id == request.RecursoId, cancellationToken))
        {
            return AgendaOpResult.Mal("El recurso ya no existe.");
        }
        var plantilla = await _db.AgendaPlantillas.AsNoTracking().Include(p => p.Franjas)
            .FirstOrDefaultAsync(p => p.Id == request.PlantillaId, cancellationToken);
        if (plantilla is null)
        {
            return AgendaOpResult.Mal("La agenda base ya no existe.");
        }
        if (request.Hasta is DateOnly h && h < request.Desde)
        {
            return AgendaOpResult.Mal("La fecha final debe ser igual o posterior a la inicial.");
        }
        var vigencia = new AgendaRecursoVigencia
        {
            TenantId = TenantId,
            RecursoId = request.RecursoId,
            Nombre = Limpio(request.Nombre, 120) ?? plantilla.Nombre,
            VigenteDesde = request.Desde,
            VigenteHasta = request.Hasta,
            PlantillaOrigenId = plantilla.Id,
            Franjas = plantilla.Franjas.Select(f => new AgendaRecursoFranja
            {
                TenantId = TenantId,
                DiaSemana = f.DiaSemana,
                HoraInicio = f.HoraInicio,
                HoraFin = f.HoraFin,
                IntervaloMinutos = f.IntervaloMinutos,
                Cupo = f.Cupo
            }).ToList()
        };
        _db.AgendaRecursoVigencias.Add(vigencia);
        await _db.SaveChangesAsync(cancellationToken);
        return AgendaOpResult.Bien(vigencia.Id);
    }

    public async Task<AgendaOpResult> SaveVigenciaAsync(SaveVigenciaRequest request, CancellationToken cancellationToken = default)
    {
        var nombre = Limpio(request.Nombre, 120);
        if (nombre is null)
        {
            return AgendaOpResult.Mal("Escribe un nombre para esta agenda.");
        }
        if (request.Hasta is DateOnly h && h < request.Desde)
        {
            return AgendaOpResult.Mal("La fecha final debe ser igual o posterior a la inicial.");
        }
        var error = AgendaCalculo.ValidarFranjas(request.Franjas);
        if (error is not null)
        {
            return AgendaOpResult.Mal(error);
        }
        await using var tx = _db.HasActiveTransaction ? null : await _db.BeginTransactionAsync(cancellationToken);
        var v = await _db.AgendaRecursoVigencias.Include(x => x.Franjas).FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (v is null)
        {
            return AgendaOpResult.Mal("Esta agenda ya no existe.");
        }
        v.Nombre = nombre;
        v.VigenteDesde = request.Desde;
        v.VigenteHasta = request.Hasta;
        _db.AgendaRecursoFranjas.RemoveRange(v.Franjas);
        v.Franjas.Clear();
        // Altas explicitas (ver SavePlantillaAsync): evita que EF tome las franjas nuevas por existentes.
        foreach (var f in request.Franjas)
        {
            _db.AgendaRecursoFranjas.Add(new AgendaRecursoFranja
            {
                TenantId = TenantId,
                VigenciaId = v.Id,
                Vigencia = v,
                DiaSemana = f.Dia,
                HoraInicio = f.Inicio,
                HoraFin = f.Fin,
                IntervaloMinutos = f.IntervaloMinutos,
                Cupo = f.Cupo
            });
        }
        await _db.SaveChangesAsync(cancellationToken);
        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }
        return AgendaOpResult.Bien(v.Id);
    }

    public async Task<AgendaOpResult> DeleteVigenciaAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var v = await _db.AgendaRecursoVigencias.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (v is null)
        {
            return AgendaOpResult.Mal("Esta agenda ya no existe.");
        }
        MarcarBorrado(v);
        await _db.SaveChangesAsync(cancellationToken);
        return AgendaOpResult.Bien(id);
    }

    // =====================================================================================================
    // Novedades
    // =====================================================================================================

    public async Task<IReadOnlyList<AgendaNovedadDto>> ListNovedadesAsync(Guid recursoId, CancellationToken cancellationToken = default) =>
        await _db.AgendaNovedades.AsNoTracking()
            .Where(n => n.RecursoId == recursoId)
            .OrderByDescending(n => n.FechaDesde)
            .Select(n => new AgendaNovedadDto(n.Id, n.RecursoId, n.Tipo, n.FechaDesde, n.FechaHasta, n.HoraDesde, n.HoraHasta, n.Nota))
            .ToListAsync(cancellationToken);

    public async Task<AgendaOpResult> SaveNovedadAsync(SaveAgendaNovedadRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Hasta < request.Desde)
        {
            return AgendaOpResult.Mal("La fecha final debe ser igual o posterior a la inicial.");
        }
        if (request.HoraDesde.HasValue != request.HoraHasta.HasValue)
        {
            return AgendaOpResult.Mal("Para un cierre parcial escribe la hora de inicio y la de fin; para el dia completo deja ambas vacias.");
        }
        if (request.HoraDesde is TimeOnly hd && request.HoraHasta is TimeOnly hh && hh <= hd)
        {
            return AgendaOpResult.Mal("La hora de fin debe ser posterior a la de inicio.");
        }
        if (!await _db.AgendaRecursos.AnyAsync(r => r.Id == request.RecursoId, cancellationToken))
        {
            return AgendaOpResult.Mal("El recurso ya no existe.");
        }
        AgendaNovedad? n;
        if (request.Id is Guid id)
        {
            n = await _db.AgendaNovedades.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (n is null)
            {
                return AgendaOpResult.Mal("La novedad ya no existe.");
            }
        }
        else
        {
            n = new AgendaNovedad { TenantId = TenantId, RecursoId = request.RecursoId };
            _db.AgendaNovedades.Add(n);
        }
        n.Tipo = request.Tipo;
        n.FechaDesde = request.Desde;
        n.FechaHasta = request.Hasta;
        n.HoraDesde = request.HoraDesde;
        n.HoraHasta = request.HoraHasta;
        n.Nota = Limpio(request.Nota, 300);
        await _db.SaveChangesAsync(cancellationToken);

        // Turnos ya tomados que quedan dentro de la novedad: no se mueven solos, se avisa para reprogramarlos.
        var dentro = await _db.AgendaTurnoRecursos.AsNoTracking()
            .Where(o => o.RecursoId == request.RecursoId && o.Fecha >= request.Desde && o.Fecha <= request.Hasta
                && (o.Turno!.Estado == AgendaTurnoEstado.Reservado || o.Turno!.Estado == AgendaTurnoEstado.Confirmado))
            .Select(o => new { o.HoraInicio, o.HoraFin })
            .ToListAsync(cancellationToken);
        var afectados = dentro.Count(o => request.HoraDesde is not TimeOnly hd || request.HoraHasta is not TimeOnly hh
            || (hd < o.HoraFin && hh > o.HoraInicio));
        return AgendaOpResult.Bien(n.Id) with
        {
            Aviso = afectados == 0 ? null
                : $"{afectados} turno(s) ya tomados quedan dentro de esta novedad. Reprogramalos o cancelalos en Turnos (aparecen marcados)."
        };
    }

    public async Task<AgendaOpResult> DeleteNovedadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var n = await _db.AgendaNovedades.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (n is null)
        {
            return AgendaOpResult.Mal("La novedad ya no existe.");
        }
        MarcarBorrado(n);
        await _db.SaveChangesAsync(cancellationToken);
        return AgendaOpResult.Bien(id);
    }

    // =====================================================================================================
    // Disponibilidad
    // =====================================================================================================

    public async Task<IReadOnlyList<AgendaDiaDto>> SemanaAsync(Guid recursoId, DateOnly fecha, CancellationToken cancellationToken = default)
    {
        var lunes = AgendaCalculo.LunesDe(fecha);
        var domingo = lunes.AddDays(6);
        var vigencias = (await _db.AgendaRecursoVigencias.AsNoTracking()
                .Include(v => v.Franjas)
                .Where(v => v.RecursoId == recursoId && v.VigenteDesde <= domingo && (v.VigenteHasta == null || v.VigenteHasta >= lunes))
                .ToListAsync(cancellationToken))
            .Select(ACalc).ToList();
        var bloqueos = await _db.AgendaNovedades.AsNoTracking()
            .Where(n => n.RecursoId == recursoId && n.FechaDesde <= domingo && n.FechaHasta >= lunes)
            .Select(n => new { n.Tipo, n.FechaDesde, n.FechaHasta, n.HoraDesde, n.HoraHasta, n.Nota })
            .ToListAsync(cancellationToken);
        var bloqueosCalc = bloqueos.Select(b => new AgendaBloqueo(b.FechaDesde, b.FechaHasta, b.HoraDesde, b.HoraHasta,
            AgendaCalculo.NombreNovedad(b.Tipo) + (b.HoraDesde is TimeOnly hd && b.HoraHasta is TimeOnly hh ? $" {hd:HH\\:mm}-{hh:HH\\:mm}" : string.Empty)))
            .ToList();
        var cierres = await _db.TenantOperatingDays.AsNoTracking()
            .Where(d => d.Date >= lunes && d.Date <= domingo)
            .ToDictionaryAsync(d => d.Date, d => string.IsNullOrWhiteSpace(d.Reason) ? "Dia no operativo" : d.Reason!, cancellationToken);

        // Turnos tomados (no cancelados) que ocupan este recurso en la semana (ADR-0165 ola 4).
        var ocupaciones = await _db.AgendaTurnoRecursos.AsNoTracking()
            .Where(o => o.RecursoId == recursoId && o.Fecha >= lunes && o.Fecha <= domingo
                && o.Turno!.Estado != AgendaTurnoEstado.Cancelado)
            .Select(o => new { o.Fecha, o.HoraInicio, o.HoraFin })
            .ToListAsync(cancellationToken);

        return Enumerable.Range(0, 7)
            .Select(i => lunes.AddDays(i))
            .Select(d => AgendaCalculo.Dia(d, vigencias, bloqueosCalc, cierres.GetValueOrDefault(d),
                ocupaciones.Where(o => o.Fecha == d).Select(o => new AgendaOcupacion(o.HoraInicio, o.HoraFin))))
            .ToList();
    }

    // =====================================================================================================
    // Utilidades
    // =====================================================================================================

    private static AgendaFranjaDto ADto(AgendaPlantillaFranja f) => new(f.DiaSemana, f.HoraInicio, f.HoraFin, f.IntervaloMinutos, f.Cupo);

    private static AgendaFranjaDto ADto(AgendaRecursoFranja f) => new(f.DiaSemana, f.HoraInicio, f.HoraFin, f.IntervaloMinutos, f.Cupo);

    private static AgendaVigenciaCalc ACalc(AgendaRecursoVigencia v) =>
        new(v.Nombre, v.VigenteDesde, v.VigenteHasta, v.Franjas.Select(ADto).ToList());

    /// <summary>Orden de presentacion: lunes primero, luego por hora.</summary>
    private static int OrdenFranja(AgendaFranjaDto f) => (((int)f.Dia + 6) % 7) * 10000 + f.Inicio.Hour * 100 + f.Inicio.Minute;

    /// <summary>Fecha de hoy en la zona horaria del tenant (regla 9; America/Bogota si no tiene o no se reconoce).</summary>
    public async Task<DateOnly> HoyAsync(CancellationToken cancellationToken = default)
    {
        var tzId = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == TenantId)
            .Select(t => t.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);
        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(tzId) ? "America/Bogota" : tzId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            tz = TimeZoneInfo.CreateCustomTimeZone("COT", TimeSpan.FromHours(-5), "COT", "COT");
        }
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime);
    }

    private static void MarcarBorrado(Ecorex.Domain.Common.ISoftDeletable e)
    {
        e.IsDeleted = true;
        e.DeletedAt = DateTimeOffset.UtcNow;
    }

    private static string? Limpio(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }
        var t = s.Trim();
        return t.Length > max ? t[..max] : t;
    }
}
