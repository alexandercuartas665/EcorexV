using System.Text;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Agendas;

/// <summary>
/// Implementacion de <see cref="IAgendaTurnosService"/> (ADR-0165 olas 3 y 4). Tenant-scoped por el filtro global;
/// los festivos y dias no operativos se leen del calendario de la entidad (TenantOperatingDays), sin duplicarlo.
/// Para no entregar el mismo cupo dos veces, reservar/reprogramar tocan la fila de cada recurso asignado dentro de
/// la transaccion: su Version (token de concurrencia) hace fallar a la segunda reserva simultanea.
/// </summary>
public sealed class AgendaTurnosService : IAgendaTurnosService
{
    private const string ErrConcurrencia = "Otra persona modifico este turno mientras lo editabas. Recarga y vuelve a intentar.";
    private const string ErrCupoTomado = "Ese horario se acaba de ocupar. Elige otro hueco.";
    private const int HistorialMax = 8000;

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly Func<DateTimeOffset> _ahoraUtc;

    public AgendaTurnosService(IApplicationDbContext db, ITenantContext tenant)
        : this(db, tenant, () => DateTimeOffset.UtcNow)
    {
    }

    /// <summary>Constructor con reloj inyectable (pruebas).</summary>
    public AgendaTurnosService(IApplicationDbContext db, ITenantContext tenant, Func<DateTimeOffset> ahoraUtc)
    {
        _db = db;
        _tenant = tenant;
        _ahoraUtc = ahoraUtc;
    }

    private Guid TenantId => _tenant.TenantId ?? Guid.Empty;

    // =====================================================================================================
    // Servicios agendables
    // =====================================================================================================

    public async Task<IReadOnlyList<AgendaServicioDto>> ListServiciosAsync(CancellationToken cancellationToken = default)
    {
        var servicios = await _db.AgendaServicios.AsNoTracking()
            .Include(s => s.Requisitos)
            .OrderBy(s => s.Nombre)
            .ToListAsync(cancellationToken);
        var hoy = (await AhoraLocalAsync(cancellationToken)).Fecha;
        var proximos = await _db.AgendaTurnos.AsNoTracking()
            .Where(t => t.Fecha >= hoy && (t.Estado == AgendaTurnoEstado.Reservado || t.Estado == AgendaTurnoEstado.Confirmado))
            .GroupBy(t => t.ServicioId)
            .Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N, cancellationToken);
        return servicios.Select(s => new AgendaServicioDto(
            s.Id, s.Nombre, s.Descripcion, s.DuracionMinutos, s.AnticipacionMinMinutos, s.AnticipacionMaxDias,
            s.AgentePuedeReservar, s.Activo, s.Version,
            s.Requisitos.OrderBy(r => r.Orden).Select(r => new AgendaRequisitoDto(r.Nombre, LeerIds(r.RecursoIdsJson))).ToList(),
            proximos.GetValueOrDefault(s.Id))).ToList();
    }

    public async Task<AgendaOpResult> SaveServicioAsync(SaveAgendaServicioRequest request, CancellationToken cancellationToken = default)
    {
        var nombre = Limpio(request.Nombre, 150);
        if (nombre is null)
        {
            return AgendaOpResult.Mal("Escribe el nombre del servicio.");
        }
        if (request.DuracionMinutos < AgendaCalculo.IntervaloMin || request.DuracionMinutos > AgendaCalculo.IntervaloMax)
        {
            return AgendaOpResult.Mal($"La duracion debe estar entre {AgendaCalculo.IntervaloMin} y {AgendaCalculo.IntervaloMax} minutos.");
        }
        if (request.AnticipacionMinMinutos < 0 || request.AnticipacionMaxDias < 1 || request.AnticipacionMaxDias > 365)
        {
            return AgendaOpResult.Mal("La anticipacion minima no puede ser negativa y la maxima debe estar entre 1 y 365 dias.");
        }
        var requisitos = request.Requisitos
            .Select(r => new { Nombre = Limpio(r.Nombre, 80), Ids = r.RecursoIds.Distinct().ToList() })
            .ToList();
        if (requisitos.Count == 0)
        {
            return AgendaOpResult.Mal("Agrega al menos un requisito: que recurso(s) atiende este servicio.");
        }
        if (requisitos.Any(r => r.Nombre is null))
        {
            return AgendaOpResult.Mal("Cada requisito necesita un nombre (por ejemplo \"Medico\" o \"Linea\").");
        }
        if (requisitos.GroupBy(r => r.Nombre!.ToLowerInvariant()).Any(g => g.Count() > 1))
        {
            return AgendaOpResult.Mal("Hay dos requisitos con el mismo nombre.");
        }
        if (requisitos.Any(r => r.Ids.Count == 0))
        {
            return AgendaOpResult.Mal("Cada requisito necesita al menos un recurso que lo pueda cumplir.");
        }
        var todos = requisitos.SelectMany(r => r.Ids).Distinct().ToList();
        var existentes = await _db.AgendaRecursos.CountAsync(r => todos.Contains(r.Id), cancellationToken);
        if (existentes != todos.Count)
        {
            return AgendaOpResult.Mal("Uno de los recursos elegidos ya no existe.");
        }
        var duplicado = await _db.AgendaServicios.AnyAsync(
            s => s.Nombre.ToLower() == nombre.ToLower() && s.Id != (request.Id ?? Guid.Empty), cancellationToken);
        if (duplicado)
        {
            return AgendaOpResult.Mal($"Ya existe un servicio llamado \"{nombre}\".");
        }

        await using var tx = _db.HasActiveTransaction ? null : await _db.BeginTransactionAsync(cancellationToken);
        AgendaServicio? s;
        if (request.Id is Guid id)
        {
            s = await _db.AgendaServicios.Include(x => x.Requisitos).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (s is null)
            {
                return AgendaOpResult.Mal("El servicio ya no existe.");
            }
            if (request.Version is long v && v != s.Version)
            {
                return AgendaOpResult.Mal("Otra persona modifico este servicio mientras lo editabas. Recarga y vuelve a intentar.");
            }
            _db.AgendaServicioRequisitos.RemoveRange(s.Requisitos);
            s.Requisitos.Clear();
        }
        else
        {
            s = new AgendaServicio { TenantId = TenantId };
            _db.AgendaServicios.Add(s);
        }
        s.Nombre = nombre;
        s.Descripcion = Limpio(request.Descripcion, 500);
        s.DuracionMinutos = request.DuracionMinutos;
        s.AnticipacionMinMinutos = request.AnticipacionMinMinutos;
        s.AnticipacionMaxDias = request.AnticipacionMaxDias;
        s.AgentePuedeReservar = request.AgentePuedeReservar;
        s.Activo = request.Activo;
        s.UpdatedAt = DateTimeOffset.UtcNow;
        var orden = 0;
        foreach (var r in requisitos)
        {
            // Alta explicita (ver AgendaService.SavePlantillaAsync): evita que EF tome el requisito por existente.
            _db.AgendaServicioRequisitos.Add(new AgendaServicioRequisito
            {
                TenantId = TenantId,
                ServicioId = s.Id,
                Servicio = s,
                Nombre = r.Nombre!,
                Orden = orden++,
                RecursoIdsJson = JsonSerializer.Serialize(r.Ids)
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
            return AgendaOpResult.Mal("Otra persona modifico este servicio mientras lo editabas. Recarga y vuelve a intentar.");
        }
        return AgendaOpResult.Bien(s.Id);
    }

    public async Task<AgendaOpResult> DeleteServicioAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var s = await _db.AgendaServicios.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (s is null)
        {
            return AgendaOpResult.Mal("El servicio ya no existe.");
        }
        var hoy = (await AhoraLocalAsync(cancellationToken)).Fecha;
        var pendientes = await _db.AgendaTurnos.CountAsync(t => t.ServicioId == id && t.Fecha >= hoy
            && (t.Estado == AgendaTurnoEstado.Reservado || t.Estado == AgendaTurnoEstado.Confirmado), cancellationToken);
        if (pendientes > 0)
        {
            return AgendaOpResult.Mal($"No se puede quitar: tiene {pendientes} turno(s) proximos. Cancelalos o inactiva el servicio.");
        }
        s.IsDeleted = true;
        s.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return AgendaOpResult.Bien(id);
    }

    // =====================================================================================================
    // Huecos
    // =====================================================================================================

    public async Task<IReadOnlyList<AgendaHueco>> HuecosAsync(Guid servicioId, DateOnly desde, int dias, Guid? excluirTurnoId = null, CancellationToken cancellationToken = default)
    {
        var servicio = await _db.AgendaServicios.AsNoTracking().Include(s => s.Requisitos)
            .FirstOrDefaultAsync(s => s.Id == servicioId, cancellationToken);
        if (servicio is null || !servicio.Activo)
        {
            return Array.Empty<AgendaHueco>();
        }
        var ahora = await AhoraLocalAsync(cancellationToken);
        var (minFecha, minHora) = Minimo(ahora, servicio.AnticipacionMinMinutos);
        var maxFecha = ahora.Fecha.AddDays(servicio.AnticipacionMaxDias);
        var inicio = desde < minFecha ? minFecha : desde;
        var fin = desde.AddDays(Math.Clamp(dias, 1, 31) - 1);
        if (fin > maxFecha)
        {
            fin = maxFecha;
        }
        if (fin < inicio)
        {
            return Array.Empty<AgendaHueco>();
        }
        var requisitos = Requisitos(servicio);
        var datos = await CargarAsync(requisitos.SelectMany(r => r.RecursoIds).Distinct().ToList(), inicio, fin, excluirTurnoId, cancellationToken);
        var huecos = new List<AgendaHueco>();
        for (var d = inicio; d <= fin; d = d.AddDays(1))
        {
            huecos.AddRange(AgendaTurnosCalculo.HuecosDia(d, servicio.DuracionMinutos, requisitos, datos.Dia(d), d == minFecha ? minHora : null));
        }
        return huecos;
    }

    // =====================================================================================================
    // Reservar / reprogramar / estado
    // =====================================================================================================

    public async Task<AgendaOpResult> ReservarAsync(ReservarTurnoRequest request, CancellationToken cancellationToken = default)
    {
        var cliente = Limpio(request.ClienteNombre, 150);
        if (cliente is null)
        {
            return AgendaOpResult.Mal("Escribe el nombre de quien toma el turno.");
        }
        var servicio = await _db.AgendaServicios.AsNoTracking().Include(s => s.Requisitos)
            .FirstOrDefaultAsync(s => s.Id == request.ServicioId, cancellationToken);
        if (servicio is null || !servicio.Activo)
        {
            return AgendaOpResult.Mal("El servicio no existe o esta inactivo.");
        }
        var ahora = await AhoraLocalAsync(cancellationToken);
        var error = ValidarVentana(servicio, ahora, request.Fecha, request.Inicio);
        if (error is not null)
        {
            return AgendaOpResult.Mal(error);
        }
        var fin = request.Inicio.AddMinutes(servicio.DuracionMinutos);

        await using var tx = _db.HasActiveTransaction ? null : await _db.BeginTransactionAsync(cancellationToken);
        var requisitos = Requisitos(servicio);
        var datos = await CargarAsync(requisitos.SelectMany(r => r.RecursoIds).Distinct().ToList(), request.Fecha, request.Fecha, null, cancellationToken);
        var asignacion = AgendaTurnosCalculo.Asignar(requisitos, datos.Dia(request.Fecha), request.Fecha, request.Inicio, fin);
        if (asignacion is null)
        {
            return AgendaOpResult.Mal("Ese horario no esta disponible para este servicio. Elige un hueco libre.");
        }
        await BloquearRecursosAsync(asignacion.Select(a => a.RecursoId), cancellationToken);

        var turno = new AgendaTurno
        {
            TenantId = TenantId,
            ServicioId = servicio.Id,
            Fecha = request.Fecha,
            HoraInicio = request.Inicio,
            HoraFin = fin,
            Estado = AgendaTurnoEstado.Reservado,
            Origen = request.Origen,
            ClienteNombre = cliente,
            ClienteTelefono = Limpio(request.ClienteTelefono, 40),
            Nota = Limpio(request.Nota, 500)
        };
        (turno.InicioUtc, turno.FinUtc) = await UtcAsync(request.Fecha, request.Inicio, fin, cancellationToken);
        turno.Historial = Linea(ahora.Momento, $"Reservado para {request.Fecha:yyyy-MM-dd} {request.Inicio:HH\\:mm}-{fin:HH\\:mm} ({request.Origen}) con {Recursos(asignacion)}.");
        _db.AgendaTurnos.Add(turno);
        AgregarOcupacion(turno, asignacion);
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
            return AgendaOpResult.Mal(ErrCupoTomado);
        }
        return AgendaOpResult.Bien(turno.Id);
    }

    public async Task<AgendaOpResult> ReprogramarAsync(ReprogramarTurnoRequest request, CancellationToken cancellationToken = default)
    {
        await using var tx = _db.HasActiveTransaction ? null : await _db.BeginTransactionAsync(cancellationToken);
        var turno = await _db.AgendaTurnos.Include(t => t.Recursos).FirstOrDefaultAsync(t => t.Id == request.TurnoId, cancellationToken);
        if (turno is null)
        {
            return AgendaOpResult.Mal("El turno ya no existe.");
        }
        if (request.Version is long v && v != turno.Version)
        {
            return AgendaOpResult.Mal(ErrConcurrencia);
        }
        if (!AgendaTurnosCalculo.Reprogramable(turno.Estado))
        {
            return AgendaOpResult.Mal($"Un turno {Estado(turno.Estado)} no se puede reprogramar.");
        }
        var servicio = await _db.AgendaServicios.AsNoTracking().Include(s => s.Requisitos)
            .FirstOrDefaultAsync(s => s.Id == turno.ServicioId, cancellationToken);
        if (servicio is null)
        {
            return AgendaOpResult.Mal("El servicio del turno ya no existe.");
        }
        var ahora = await AhoraLocalAsync(cancellationToken);
        var error = ValidarVentana(servicio, ahora, request.Fecha, request.Inicio);
        if (error is not null)
        {
            return AgendaOpResult.Mal(error);
        }
        if (request.Fecha == turno.Fecha && request.Inicio == turno.HoraInicio)
        {
            return AgendaOpResult.Mal("El turno ya esta en ese horario.");
        }
        var fin = request.Inicio.AddMinutes(servicio.DuracionMinutos);
        var requisitos = Requisitos(servicio);
        var datos = await CargarAsync(requisitos.SelectMany(r => r.RecursoIds).Distinct().ToList(), request.Fecha, request.Fecha, turno.Id, cancellationToken);
        var asignacion = AgendaTurnosCalculo.Asignar(requisitos, datos.Dia(request.Fecha), request.Fecha, request.Inicio, fin);
        if (asignacion is null)
        {
            return AgendaOpResult.Mal("Ese horario no esta disponible para este servicio. Elige un hueco libre.");
        }
        await BloquearRecursosAsync(asignacion.Select(a => a.RecursoId).Concat(turno.Recursos.Select(r => r.RecursoId)).Distinct(), cancellationToken);

        var antes = $"{turno.Fecha:yyyy-MM-dd} {turno.HoraInicio:HH\\:mm}";
        _db.AgendaTurnoRecursos.RemoveRange(turno.Recursos);
        turno.Recursos.Clear();
        turno.Fecha = request.Fecha;
        turno.HoraInicio = request.Inicio;
        turno.HoraFin = fin;
        (turno.InicioUtc, turno.FinUtc) = await UtcAsync(request.Fecha, request.Inicio, fin, cancellationToken);
        turno.Estado = AgendaTurnoEstado.Reservado;
        turno.Reprogramaciones++;
        var motivo = Limpio(request.Motivo, 300);
        turno.Historial = Agregar(turno.Historial, Linea(ahora.Momento,
            $"Reprogramado de {antes} a {request.Fecha:yyyy-MM-dd} {request.Inicio:HH\\:mm} con {Recursos(asignacion)}." + (motivo is null ? "" : $" Motivo: {motivo}")));
        AgregarOcupacion(turno, asignacion);
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
            return AgendaOpResult.Mal(ErrCupoTomado);
        }
        return AgendaOpResult.Bien(turno.Id);
    }

    public async Task<AgendaOpResult> CambiarEstadoAsync(CambiarEstadoTurnoRequest request, CancellationToken cancellationToken = default)
    {
        var turno = await _db.AgendaTurnos.FirstOrDefaultAsync(t => t.Id == request.TurnoId, cancellationToken);
        if (turno is null)
        {
            return AgendaOpResult.Mal("El turno ya no existe.");
        }
        if (request.Version is long v && v != turno.Version)
        {
            return AgendaOpResult.Mal(ErrConcurrencia);
        }
        if (!AgendaTurnosCalculo.PuedeCambiar(turno.Estado, request.Estado))
        {
            return AgendaOpResult.Mal($"Un turno {Estado(turno.Estado)} no puede pasar a {Estado(request.Estado)}.");
        }
        var motivo = Limpio(request.Motivo, 300);
        if (request.Estado == AgendaTurnoEstado.Cancelado && motivo is null)
        {
            return AgendaOpResult.Mal("Escribe el motivo de la cancelacion.");
        }
        var ahora = await AhoraLocalAsync(cancellationToken);
        turno.Estado = request.Estado;
        turno.Historial = Agregar(turno.Historial, Linea(ahora.Momento,
            $"Paso a {Estado(request.Estado)}." + (motivo is null ? "" : $" Motivo: {motivo}")));
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AgendaOpResult.Mal(ErrConcurrencia);
        }
        return AgendaOpResult.Bien(turno.Id);
    }

    // =====================================================================================================
    // Consultas
    // =====================================================================================================

    public async Task<IReadOnlyList<AgendaTurnoDto>> ListTurnosAsync(DateOnly desde, DateOnly hasta, Guid? recursoId = null, Guid? servicioId = null, CancellationToken cancellationToken = default)
    {
        var q = _db.AgendaTurnos.AsNoTracking()
            .Include(t => t.Servicio)
            .Include(t => t.Recursos)
            .Where(t => t.Fecha >= desde && t.Fecha <= hasta);
        if (recursoId is Guid rid)
        {
            q = q.Where(t => t.Recursos.Any(r => r.RecursoId == rid));
        }
        if (servicioId is Guid sid)
        {
            q = q.Where(t => t.ServicioId == sid);
        }
        var turnos = await q.OrderBy(t => t.Fecha).ThenBy(t => t.HoraInicio).ToListAsync(cancellationToken);
        return await ProyectarAsync(turnos, cancellationToken);
    }

    public async Task<AgendaTurnoDto?> GetTurnoAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var t = await _db.AgendaTurnos.AsNoTracking().Include(x => x.Servicio).Include(x => x.Recursos)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return t is null ? null : (await ProyectarAsync(new List<AgendaTurno> { t }, cancellationToken))[0];
    }

    private async Task<IReadOnlyList<AgendaTurnoDto>> ProyectarAsync(List<AgendaTurno> turnos, CancellationToken cancellationToken)
    {
        var ids = turnos.SelectMany(t => t.Recursos.Select(r => r.RecursoId)).Distinct().ToList();
        // Filtro global (tenant + borrado logico): un recurso ya quitado se muestra como "-".
        var nombres = await _db.AgendaRecursos.AsNoTracking()
            .Where(r => ids.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Nombre, cancellationToken);

        // Conflictos: turnos activos que quedaron dentro de una novedad de alguno de sus recursos o en un dia que el
        // calendario de la entidad cierra (se registraron despues de tomar el turno). Se marcan para reprogramarlos.
        var activos = turnos.Where(t => AgendaTurnosCalculo.Reprogramable(t.Estado)).ToList();
        var conflictos = new Dictionary<Guid, string>();
        if (activos.Count > 0)
        {
            var desde = activos.Min(t => t.Fecha);
            var hasta = activos.Max(t => t.Fecha);
            var novedades = await _db.AgendaNovedades.AsNoTracking()
                .Where(n => ids.Contains(n.RecursoId) && n.FechaDesde <= hasta && n.FechaHasta >= desde)
                .ToListAsync(cancellationToken);
            var cierres = await _db.TenantOperatingDays.AsNoTracking()
                .Where(d => d.Date >= desde && d.Date <= hasta)
                .ToDictionaryAsync(d => d.Date, d => d.Reason ?? "Dia no operativo", cancellationToken);
            foreach (var t in activos)
            {
                if (cierres.TryGetValue(t.Fecha, out var motivo))
                {
                    conflictos[t.Id] = motivo;
                    continue;
                }
                var choque = novedades.FirstOrDefault(n => t.Recursos.Any(r => r.RecursoId == n.RecursoId)
                    && n.FechaDesde <= t.Fecha && n.FechaHasta >= t.Fecha
                    && (n.HoraDesde is null || n.HoraHasta is null || (n.HoraDesde < t.HoraFin && n.HoraHasta > t.HoraInicio)));
                if (choque is not null)
                {
                    conflictos[t.Id] = $"{AgendaCalculo.NombreNovedad(choque.Tipo)} de {nombres.GetValueOrDefault(choque.RecursoId) ?? "un recurso"}";
                }
            }
        }

        return turnos.Select(t => new AgendaTurnoDto(
            t.Id, t.ServicioId, t.Servicio?.Nombre ?? "-", t.Fecha, t.HoraInicio, t.HoraFin, t.Estado, t.Origen,
            t.ClienteNombre, t.ClienteTelefono, t.Nota, t.Reprogramaciones, t.Historial, t.Version,
            t.Recursos.Select(r => new AgendaTurnoRecursoDto(r.Requisito, r.RecursoId, nombres.GetValueOrDefault(r.RecursoId) ?? "-")).ToList(),
            conflictos.GetValueOrDefault(t.Id)))
            .ToList();
    }

    // =====================================================================================================
    // Carga de datos para el calculo
    // =====================================================================================================

    /// <summary>Datos de un rango de fechas para un juego de recursos, con acceso por fecha.</summary>
    private sealed class DatosRango
    {
        public Dictionary<Guid, string> Nombres { get; init; } = new();
        public Dictionary<Guid, List<AgendaVigenciaCalc>> Vigencias { get; init; } = new();
        public Dictionary<Guid, List<AgendaBloqueo>> Bloqueos { get; init; } = new();
        public HashSet<DateOnly> Cierres { get; init; } = new();
        public Dictionary<(Guid, DateOnly), List<AgendaOcupacion>> Ocupaciones { get; init; } = new();

        public IReadOnlyDictionary<Guid, AgendaRecursoDia> Dia(DateOnly fecha)
        {
            var cerrado = Cierres.Contains(fecha);
            var dict = new Dictionary<Guid, AgendaRecursoDia>();
            foreach (var (id, nombre) in Nombres)
            {
                var vigencia = AgendaCalculo.VigenciaPara(Vigencias.GetValueOrDefault(id) ?? new List<AgendaVigenciaCalc>(), fecha);
                var franjas = vigencia?.Franjas.Where(f => f.Dia == fecha.DayOfWeek).OrderBy(f => f.Inicio).ToList()
                    ?? new List<AgendaFranjaDto>();
                dict[id] = new AgendaRecursoDia(id, nombre, franjas,
                    Bloqueos.GetValueOrDefault(id) ?? new List<AgendaBloqueo>(),
                    cerrado,
                    Ocupaciones.GetValueOrDefault((id, fecha)) ?? new List<AgendaOcupacion>());
            }
            return dict;
        }
    }

    private async Task<DatosRango> CargarAsync(List<Guid> recursoIds, DateOnly desde, DateOnly hasta, Guid? excluirTurnoId, CancellationToken cancellationToken)
    {
        // Solo recursos ACTIVOS del tenant (el filtro global ya descarta los de otro tenant y los borrados).
        var nombres = await _db.AgendaRecursos.AsNoTracking()
            .Where(r => recursoIds.Contains(r.Id) && r.Activo)
            .ToDictionaryAsync(r => r.Id, r => r.Nombre, cancellationToken);
        var ids = nombres.Keys.ToList();

        var vigencias = await _db.AgendaRecursoVigencias.AsNoTracking()
            .Include(v => v.Franjas)
            .Where(v => ids.Contains(v.RecursoId) && v.VigenteDesde <= hasta && (v.VigenteHasta == null || v.VigenteHasta >= desde))
            .ToListAsync(cancellationToken);
        var novedades = await _db.AgendaNovedades.AsNoTracking()
            .Where(n => ids.Contains(n.RecursoId) && n.FechaDesde <= hasta && n.FechaHasta >= desde)
            .ToListAsync(cancellationToken);
        // Festivos y dias no operativos: el calendario de la entidad (configuracion de la entidad), no uno propio.
        var cierres = await _db.TenantOperatingDays.AsNoTracking()
            .Where(d => d.Date >= desde && d.Date <= hasta)
            .Select(d => d.Date)
            .ToListAsync(cancellationToken);
        var ocupQuery = _db.AgendaTurnoRecursos.AsNoTracking()
            .Where(o => ids.Contains(o.RecursoId) && o.Fecha >= desde && o.Fecha <= hasta
                && o.Turno!.Estado != AgendaTurnoEstado.Cancelado);
        if (excluirTurnoId is Guid ex)
        {
            ocupQuery = ocupQuery.Where(o => o.TurnoId != ex);
        }
        var ocupaciones = await ocupQuery.Select(o => new { o.RecursoId, o.Fecha, o.HoraInicio, o.HoraFin }).ToListAsync(cancellationToken);

        return new DatosRango
        {
            Nombres = nombres,
            Vigencias = vigencias.GroupBy(v => v.RecursoId).ToDictionary(g => g.Key, g => g.Select(v =>
                new AgendaVigenciaCalc(v.Nombre, v.VigenteDesde, v.VigenteHasta,
                    v.Franjas.Select(f => new AgendaFranjaDto(f.DiaSemana, f.HoraInicio, f.HoraFin, f.IntervaloMinutos, f.Cupo)).ToList())).ToList()),
            Bloqueos = novedades.GroupBy(n => n.RecursoId).ToDictionary(g => g.Key, g => g.Select(n =>
                new AgendaBloqueo(n.FechaDesde, n.FechaHasta, n.HoraDesde, n.HoraHasta, AgendaCalculo.NombreNovedad(n.Tipo))).ToList()),
            Cierres = cierres.ToHashSet(),
            Ocupaciones = ocupaciones.GroupBy(o => (o.RecursoId, o.Fecha)).ToDictionary(g => g.Key, g => g.Select(o => new AgendaOcupacion(o.HoraInicio, o.HoraFin)).ToList())
        };
    }

    /// <summary>
    /// Toca la fila de cada recurso asignado para que su Version avance: si otra reserva simultanea uso el mismo
    /// recurso, una de las dos falla con DbUpdateConcurrencyException al guardar (no se entrega el cupo dos veces).
    /// </summary>
    private async Task BloquearRecursosAsync(IEnumerable<Guid> recursoIds, CancellationToken cancellationToken)
    {
        var ids = recursoIds.ToList();
        var recursos = await _db.AgendaRecursos.Where(r => ids.Contains(r.Id)).ToListAsync(cancellationToken);
        var ahora = DateTimeOffset.UtcNow;
        foreach (var r in recursos)
        {
            r.UpdatedAt = ahora;
        }
    }

    private void AgregarOcupacion(AgendaTurno turno, IReadOnlyList<AgendaAsignacion> asignacion)
    {
        foreach (var a in asignacion)
        {
            _db.AgendaTurnoRecursos.Add(new AgendaTurnoRecurso
            {
                TenantId = TenantId,
                TurnoId = turno.Id,
                Turno = turno,
                RecursoId = a.RecursoId,
                Requisito = a.Requisito.Length > 80 ? a.Requisito[..80] : a.Requisito,
                Fecha = turno.Fecha,
                HoraInicio = turno.HoraInicio,
                HoraFin = turno.HoraFin
            });
        }
    }

    // =====================================================================================================
    // Utilidades
    // =====================================================================================================

    private readonly record struct AhoraLocal(DateOnly Fecha, TimeOnly Hora, DateTime Momento);

    private async Task<TimeZoneInfo> ZonaAsync(CancellationToken cancellationToken)
    {
        var tzId = await _db.Tenants.AsNoTracking().Where(t => t.Id == TenantId).Select(t => t.TimeZoneId).FirstOrDefaultAsync(cancellationToken);
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(tzId) ? "America/Bogota" : tzId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("COT", TimeSpan.FromHours(-5), "COT", "COT");
        }
    }

    private async Task<AhoraLocal> AhoraLocalAsync(CancellationToken cancellationToken)
    {
        var tz = await ZonaAsync(cancellationToken);
        var local = TimeZoneInfo.ConvertTime(_ahoraUtc(), tz).DateTime;
        return new AhoraLocal(DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local), local);
    }

    private async Task<(DateTimeOffset, DateTimeOffset)> UtcAsync(DateOnly fecha, TimeOnly ini, TimeOnly fin, CancellationToken cancellationToken)
    {
        var tz = await ZonaAsync(cancellationToken);
        DateTimeOffset Utc(TimeOnly h)
        {
            var local = fecha.ToDateTime(h, DateTimeKind.Unspecified);
            return new DateTimeOffset(local, tz.GetUtcOffset(local)).ToUniversalTime();
        }
        return (Utc(ini), Utc(fin));
    }

    /// <summary>Primer momento reservable segun la anticipacion minima.</summary>
    private static (DateOnly Fecha, TimeOnly? Hora) Minimo(AhoraLocal ahora, int anticipacionMin)
    {
        var limite = ahora.Momento.AddMinutes(anticipacionMin);
        return (DateOnly.FromDateTime(limite), TimeOnly.FromDateTime(limite));
    }

    private static string? ValidarVentana(AgendaServicio servicio, AhoraLocal ahora, DateOnly fecha, TimeOnly inicio)
    {
        var limite = ahora.Momento.AddMinutes(servicio.AnticipacionMinMinutos);
        var pedido = fecha.ToDateTime(inicio);
        if (pedido < limite)
        {
            return servicio.AnticipacionMinMinutos > 0
                ? $"Este servicio se reserva con al menos {servicio.AnticipacionMinMinutos} minutos de anticipacion."
                : "Ese horario ya paso.";
        }
        if (fecha > ahora.Fecha.AddDays(servicio.AnticipacionMaxDias))
        {
            return $"Este servicio se reserva maximo con {servicio.AnticipacionMaxDias} dias de anticipacion.";
        }
        if (inicio.AddMinutes(servicio.DuracionMinutos) <= inicio)
        {
            return "El turno no puede pasar de la medianoche.";
        }
        return null;
    }

    private static IReadOnlyList<AgendaRequisitoCalc> Requisitos(AgendaServicio s) =>
        s.Requisitos.OrderBy(r => r.Orden).Select(r => new AgendaRequisitoCalc(r.Nombre, LeerIds(r.RecursoIdsJson))).ToList();

    private static IReadOnlyList<Guid> LeerIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<Guid>();
        }
        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(json) ?? new List<Guid>();
        }
        catch (JsonException)
        {
            return Array.Empty<Guid>();
        }
    }

    private static string Recursos(IReadOnlyList<AgendaAsignacion> a) =>
        string.Join(", ", a.Select(x => $"{x.Requisito}: {x.RecursoNombre}"));

    public static string Estado(AgendaTurnoEstado e) => e switch
    {
        AgendaTurnoEstado.Reservado => "reservado",
        AgendaTurnoEstado.Confirmado => "confirmado",
        AgendaTurnoEstado.Atendido => "atendido",
        AgendaTurnoEstado.NoAsistio => "no asistio",
        _ => "cancelado"
    };

    private static string Linea(DateTime momento, string texto) => $"{momento:yyyy-MM-dd HH:mm} {texto}";

    private static string Agregar(string? historial, string linea)
    {
        var sb = new StringBuilder(historial ?? string.Empty);
        if (sb.Length > 0)
        {
            sb.Append('\n');
        }
        sb.Append(linea);
        var t = sb.ToString();
        return t.Length > HistorialMax ? t[^HistorialMax..] : t;
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
