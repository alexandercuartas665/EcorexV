using System.Text;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Agendas;

/// <summary>
/// Implementacion de <see cref="IAgendaRecordatoriosService"/> (ADR-0167). Tenant-scoped por el filtro global (el
/// worker abre el ambito de cada tenant). Para cada regla activa y cada turno activo dentro de su ventana decide
/// esperar / enviar / omitir con <see cref="AgendaRecordatorioCalculo"/>; el envio se RESERVA primero (fila
/// Pendiente, indice unico regla+turno+inicio) y luego se manda la plantilla por la linea elegida, se registra en la
/// conversacion del cliente (para que el agente de la linea tenga el contexto si responde) y en el historial del turno.
/// </summary>
public sealed class AgendaRecordatoriosService : IAgendaRecordatoriosService
{
    private const int MaxTurnosPorPasada = 2000;
    private const int HistorialMax = 8000;

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IWhatsAppConnectorService _connector;
    private readonly IChatService? _chat;
    private readonly Func<DateTimeOffset> _ahoraUtc;

    public AgendaRecordatoriosService(IApplicationDbContext db, ITenantContext tenant, IWhatsAppConnectorService connector, IChatService chat)
        : this(db, tenant, connector, chat, () => DateTimeOffset.UtcNow)
    {
    }

    /// <summary>Constructor con reloj inyectable (pruebas). Sin <paramref name="chat"/> no se registra en la conversacion.</summary>
    public AgendaRecordatoriosService(IApplicationDbContext db, ITenantContext tenant, IWhatsAppConnectorService connector,
        IChatService? chat, Func<DateTimeOffset> ahoraUtc)
    {
        _db = db;
        _tenant = tenant;
        _connector = connector;
        _chat = chat;
        _ahoraUtc = ahoraUtc;
    }

    private Guid TenantId => _tenant.TenantId ?? Guid.Empty;

    // =====================================================================================================
    // Reglas
    // =====================================================================================================

    public async Task<IReadOnlyList<AgendaRecordatorioReglaDto>> ListReglasAsync(CancellationToken cancellationToken = default)
    {
        var reglas = await _db.AgendaRecordatorioReglas.AsNoTracking().OrderBy(r => r.AnticipacionMinutos).ThenBy(r => r.Nombre)
            .ToListAsync(cancellationToken);
        return reglas.Select(ADto).ToList();
    }

    public async Task<AgendaOpResult> SaveReglaAsync(SaveAgendaRecordatorioReglaRequest request, CancellationToken cancellationToken = default)
    {
        var nombre = Limpio(request.Nombre, 120);
        if (nombre is null)
        {
            return AgendaOpResult.Mal("Escribe un nombre para la regla (por ejemplo \"WhatsApp un dia antes\").");
        }
        if (request.AnticipacionMinutos < 1 || request.AnticipacionMinutos > 43200)
        {
            return AgendaOpResult.Mal("La anticipacion debe estar entre 1 minuto y 30 dias.");
        }
        if (request.HoraDesde.HasValue != request.HoraHasta.HasValue)
        {
            return AgendaOpResult.Mal("Para limitar el horario escribe la hora de inicio y la de fin; para no limitarlo deja ambas vacias.");
        }
        if (request.HoraDesde is TimeOnly hd && request.HoraHasta is TimeOnly hh && hh <= hd)
        {
            return AgendaOpResult.Mal("La hora final del horario permitido debe ser posterior a la inicial.");
        }
        var servicios = request.ServicioIds.Distinct().ToList();
        if (servicios.Count > 0 && await _db.AgendaServicios.CountAsync(s => servicios.Contains(s.Id), cancellationToken) != servicios.Count)
        {
            return AgendaOpResult.Mal("Uno de los servicios elegidos ya no existe.");
        }
        if (request.Canal == AgendaRecordatorioCanal.Llamada)
        {
            return AgendaOpResult.Mal("El recordatorio por llamada llega en la siguiente etapa; por ahora usa WhatsApp.");
        }

        // WhatsApp: linea + plantilla de esa linea, usable por el proveedor, y todas sus variables con dato.
        if (request.WhatsAppLineId is not Guid lineaId)
        {
            return AgendaOpResult.Mal("Elige la linea de WhatsApp desde la que se envia el recordatorio.");
        }
        var linea = await _db.WhatsAppLines.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lineaId, cancellationToken);
        if (linea is null)
        {
            return AgendaOpResult.Mal("La linea de WhatsApp ya no existe.");
        }
        if (request.PlantillaId is not Guid plantillaId)
        {
            return AgendaOpResult.Mal("Elige la plantilla del recordatorio.");
        }
        var plantilla = await _db.WhatsAppTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == plantillaId, cancellationToken);
        if (plantilla is null || plantilla.WhatsAppLineId != lineaId)
        {
            return AgendaOpResult.Mal("La plantilla no existe o no pertenece a la linea elegida.");
        }
        if (!PlantillaUsable(linea.Provider, plantilla.Status))
        {
            return AgendaOpResult.Mal(RequiereAprobacion(linea.Provider)
                ? $"La plantilla '{plantilla.Name}' aun no esta aprobada por Meta (estado {plantilla.Status}); con esta linea solo se envian plantillas aprobadas."
                : $"La plantilla '{plantilla.Name}' esta {plantilla.Status} y no se puede usar.");
        }
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (token, _) in WhatsAppTemplateComponents.ParseVariables(plantilla.VariablesJson))
        {
            var expr = request.Variables.TryGetValue(token, out var e) && !string.IsNullOrWhiteSpace(e) ? e.Trim() : AgendaRecordatorioCalculo.Sugerida(token);
            if (string.IsNullOrWhiteSpace(expr))
            {
                return AgendaOpResult.Mal($"Asigna un dato del turno a la variable '{token}' de la plantilla.");
            }
            variables[token] = expr.Length > 300 ? expr[..300] : expr;
        }

        AgendaRecordatorioRegla? r;
        if (request.Id is Guid id)
        {
            r = await _db.AgendaRecordatorioReglas.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (r is null)
            {
                return AgendaOpResult.Mal("La regla ya no existe.");
            }
            if (request.Version is long v && v != r.Version)
            {
                return AgendaOpResult.Mal("Otra persona modifico esta regla mientras la editabas. Recarga y vuelve a intentar.");
            }
        }
        else
        {
            r = new AgendaRecordatorioRegla { TenantId = TenantId };
            _db.AgendaRecordatorioReglas.Add(r);
        }
        r.Nombre = nombre;
        r.Activa = request.Activa;
        r.ServicioIdsJson = JsonSerializer.Serialize(servicios);
        r.AnticipacionMinutos = request.AnticipacionMinutos;
        r.HoraDesde = request.HoraDesde;
        r.HoraHasta = request.HoraHasta;
        r.FueraDeHorario = request.FueraDeHorario;
        r.SoloSiNoConfirmado = request.SoloSiNoConfirmado;
        r.Canal = request.Canal;
        r.WhatsAppLineId = lineaId;
        r.PlantillaId = plantillaId;
        r.VariablesJson = JsonSerializer.Serialize(variables);
        r.AiAgentId = request.AiAgentId;
        r.VoiceLineId = request.VoiceLineId;
        r.Objetivo = Limpio(request.Objetivo, 1000);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AgendaOpResult.Mal("Otra persona modifico esta regla mientras la editabas. Recarga y vuelve a intentar.");
        }
        return AgendaOpResult.Bien(r.Id);
    }

    public async Task<AgendaOpResult> DeleteReglaAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var r = await _db.AgendaRecordatorioReglas.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (r is null)
        {
            return AgendaOpResult.Mal("La regla ya no existe.");
        }
        r.IsDeleted = true;
        r.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return AgendaOpResult.Bien(id);
    }

    public async Task<AgendaRecordatorioOpcionesDto> OpcionesAsync(CancellationToken cancellationToken = default)
    {
        var lineas = await _db.WhatsAppLines.AsNoTracking().OrderBy(l => l.InstanceName).ToListAsync(cancellationToken);
        var plantillas = await _db.WhatsAppTemplates.AsNoTracking().OrderBy(t => t.Name).ToListAsync(cancellationToken);
        var proveedor = lineas.ToDictionary(l => l.Id, l => l.Provider);
        var servicios = await _db.AgendaServicios.AsNoTracking().OrderBy(s => s.Nombre)
            .Select(s => new AgendaOpcionDto(s.Id, s.Nombre)).ToListAsync(cancellationToken);
        var agentes = await _db.AiAgents.AsNoTracking().Where(a => a.IsActive).OrderBy(a => a.Name)
            .Select(a => new AgendaOpcionDto(a.Id, a.Name)).ToListAsync(cancellationToken);
        return new AgendaRecordatorioOpcionesDto(
            lineas.Select(l => new AgendaRecordatorioLineaDto(l.Id, l.InstanceName, l.Provider.ToString(), l.Status == WhatsAppLineStatus.Connected)).ToList(),
            plantillas.Where(t => proveedor.ContainsKey(t.WhatsAppLineId)).Select(t => new AgendaRecordatorioPlantillaDto(
                t.Id, t.WhatsAppLineId, t.Name, t.Language, t.Status.ToString(), PlantillaUsable(proveedor[t.WhatsAppLineId], t.Status),
                WhatsAppTemplateComponents.ParseVariables(t.VariablesJson).Select(v => v.Token).ToList(), t.BodyText)).ToList(),
            servicios, agentes);
    }

    public async Task<IReadOnlyList<AgendaRecordatorioEnvioDto>> ListEnviosAsync(int maximo = 100, Guid? turnoId = null, CancellationToken cancellationToken = default)
    {
        var q = _db.AgendaRecordatorioEnvios.AsNoTracking();
        if (turnoId is Guid tid)
        {
            q = q.Where(e => e.TurnoId == tid);
        }
        var envios = await q.OrderByDescending(e => e.ProcesadoUtc).Take(Math.Clamp(maximo, 1, 500)).ToListAsync(cancellationToken);
        var reglaIds = envios.Select(e => e.ReglaId).Distinct().ToList();
        var turnoIds = envios.Select(e => e.TurnoId).Distinct().ToList();
        // Filtro global (tenant + borrado logico): una regla ya quitada se muestra como "(regla quitada)".
        var reglas = await _db.AgendaRecordatorioReglas.AsNoTracking()
            .Where(r => reglaIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Nombre, cancellationToken);
        var turnos = await _db.AgendaTurnos.AsNoTracking().Include(t => t.Servicio)
            .Where(t => turnoIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, cancellationToken);
        return envios.Select(e =>
        {
            turnos.TryGetValue(e.TurnoId, out var t);
            return new AgendaRecordatorioEnvioDto(e.Id, e.ReglaId, reglas.GetValueOrDefault(e.ReglaId) ?? "(regla quitada)", e.TurnoId,
                t?.ClienteNombre ?? "-", t?.Servicio?.Nombre ?? "-", t?.Fecha ?? default, t?.HoraInicio ?? default, e.Canal,
                e.ProgramadoUtc, e.ProcesadoUtc, e.Estado, e.Detalle);
        }).ToList();
    }

    public async Task<IReadOnlyList<Guid>> TenantsConReglasAsync(CancellationToken cancellationToken = default) =>
        // Barrido del worker (solo ids): se lee cruzando tenants a proposito; cada tenant se procesa luego en su
        // propio ambito con el filtro global.
        await _db.AgendaRecordatorioReglas.AsNoTracking().IgnoreQueryFilters()
            .Where(r => r.Activa && !r.IsDeleted)
            .Select(r => r.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);

    // =====================================================================================================
    // Procesamiento
    // =====================================================================================================

    public async Task<AgendaRecordatorioCorrida> ProcesarAsync(CancellationToken cancellationToken = default)
    {
        var ahora = _ahoraUtc();
        var reglas = await _db.AgendaRecordatorioReglas.AsNoTracking()
            .Where(r => r.Activa && r.Canal == AgendaRecordatorioCanal.WhatsApp)
            .ToListAsync(cancellationToken);
        if (reglas.Count == 0)
        {
            return new AgendaRecordatorioCorrida(0, 0, 0, Array.Empty<string>());
        }
        var tenant = await _db.Tenants.AsNoTracking().Where(t => t.Id == TenantId)
            .Select(t => new { t.Name, t.TimeZoneId }).FirstOrDefaultAsync(cancellationToken);
        var zona = Zona(tenant?.TimeZoneId);

        // Ventana: lo mas lejano que puede tocar avisar (+1 dia por si "Adelantar" lo movio al dia anterior).
        var hasta = ahora.AddMinutes(reglas.Max(r => r.AnticipacionMinutos) + 1440);
        var turnos = await _db.AgendaTurnos.AsNoTracking()
            .Include(t => t.Servicio)
            .Include(t => t.Recursos)
            .Where(t => (t.Estado == AgendaTurnoEstado.Reservado || t.Estado == AgendaTurnoEstado.Confirmado)
                && t.InicioUtc > ahora && t.InicioUtc <= hasta)
            .OrderBy(t => t.InicioUtc)
            .Take(MaxTurnosPorPasada)
            .ToListAsync(cancellationToken);
        if (turnos.Count == 0)
        {
            return new AgendaRecordatorioCorrida(0, 0, 0, Array.Empty<string>());
        }

        var turnoIds = turnos.Select(t => t.Id).ToList();
        var hechos = (await _db.AgendaRecordatorioEnvios.AsNoTracking()
                .Where(e => turnoIds.Contains(e.TurnoId))
                .Select(e => new { e.ReglaId, e.TurnoId, e.InicioTurnoUtc })
                .ToListAsync(cancellationToken))
            .Select(e => (e.ReglaId, e.TurnoId, e.InicioTurnoUtc))
            .ToHashSet();
        var recursoIds = turnos.SelectMany(t => t.Recursos.Select(r => r.RecursoId)).Distinct().ToList();
        var nombresRecursos = await _db.AgendaRecursos.AsNoTracking().Where(r => recursoIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Nombre, cancellationToken);
        var taskIds = turnos.Where(t => t.TaskItemId.HasValue).Select(t => t.TaskItemId!.Value).ToList();
        var tickets = await _db.TaskItems.AsNoTracking().Where(t => taskIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Number, cancellationToken);

        var plantillaIds = reglas.Where(r => r.PlantillaId.HasValue).Select(r => r.PlantillaId!.Value).Distinct().ToList();
        var plantillas = await _db.WhatsAppTemplates.AsNoTracking().Where(t => plantillaIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        int enviados = 0, fallidos = 0, omitidos = 0;
        var detalle = new List<string>();
        foreach (var regla in reglas)
        {
            var servicios = LeerIds(regla.ServicioIdsJson);
            foreach (var turno in turnos)
            {
                if (servicios.Count > 0 && !servicios.Contains(turno.ServicioId))
                {
                    continue;
                }
                if (hechos.Contains((regla.Id, turno.Id, turno.InicioUtc)))
                {
                    continue;
                }
                var momento = AgendaRecordatorioCalculo.Momento(turno.InicioUtc, regla.AnticipacionMinutos, regla.HoraDesde, regla.HoraHasta, regla.FueraDeHorario, zona);
                var (accion, programado, motivo) = AgendaRecordatorioCalculo.Evaluar(momento, turno.InicioUtc, turno.HorarioDesdeUtc,
                    turno.Estado, regla.SoloSiNoConfirmado, turno.InicioUtc.AddMinutes(-regla.AnticipacionMinutos), ahora);
                if (accion == AgendaRecordatorioAccion.Esperar)
                {
                    continue;
                }

                // Reserva del envio (indice unico regla+turno+inicio): si otro proceso ya lo tomo, aqui falla y se
                // corta la pasada de este tenant sin enviar nada dos veces.
                var envio = new AgendaRecordatorioEnvio
                {
                    TenantId = TenantId,
                    ReglaId = regla.Id,
                    TurnoId = turno.Id,
                    InicioTurnoUtc = turno.InicioUtc,
                    Canal = regla.Canal,
                    ProgramadoUtc = programado ?? ahora,
                    ProcesadoUtc = ahora,
                    Estado = accion == AgendaRecordatorioAccion.Omitir ? AgendaRecordatorioEnvioEstado.Omitido : AgendaRecordatorioEnvioEstado.Pendiente,
                    Detalle = motivo
                };
                _db.AgendaRecordatorioEnvios.Add(envio);
                try
                {
                    await _db.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    detalle.Add("Otro proceso ya estaba enviando estos recordatorios; se reintenta en la proxima pasada.");
                    return new AgendaRecordatorioCorrida(enviados, fallidos, omitidos, detalle);
                }
                hechos.Add((regla.Id, turno.Id, turno.InicioUtc));
                if (accion == AgendaRecordatorioAccion.Omitir)
                {
                    omitidos++;
                    continue;
                }

                var recursos = string.Join(" y ", turno.Recursos.Select(r => nombresRecursos.GetValueOrDefault(r.RecursoId)).Where(n => n is not null));
                var valores = AgendaRecordatorioCalculo.Valores(turno.ClienteNombre, turno.Servicio?.Nombre ?? "su servicio",
                    turno.Fecha, turno.HoraInicio, turno.HoraFin, recursos,
                    turno.TaskItemId is Guid kid ? tickets.GetValueOrDefault(kid) : null, turno.ClienteTelefono, tenant?.Name);
                var (ok, texto, referencia) = await EnviarWhatsAppAsync(regla, turno, plantillas, valores, cancellationToken);

                var trackedEnvio = await _db.AgendaRecordatorioEnvios.FirstAsync(e => e.Id == envio.Id, cancellationToken);
                trackedEnvio.Estado = ok ? AgendaRecordatorioEnvioEstado.Enviado : AgendaRecordatorioEnvioEstado.Fallido;
                trackedEnvio.Detalle = Recortar(texto, 500);
                trackedEnvio.ReferenciaExterna = Recortar(referencia, 200);
                trackedEnvio.ProcesadoUtc = _ahoraUtc();
                var trackedTurno = await _db.AgendaTurnos.FirstAsync(t => t.Id == turno.Id, cancellationToken);
                var local = TimeZoneInfo.ConvertTime(_ahoraUtc(), zona).DateTime;
                trackedTurno.Historial = Agregar(trackedTurno.Historial,
                    $"{local:yyyy-MM-dd HH:mm} " + (ok
                        ? $"Recordatorio por WhatsApp enviado ({regla.Nombre})."
                        : $"No se pudo enviar el recordatorio por WhatsApp ({regla.Nombre}): {texto}"));
                await _db.SaveChangesAsync(cancellationToken);
                if (ok) { enviados++; } else { fallidos++; detalle.Add($"{turno.ClienteNombre}: {texto}"); }
            }
        }
        return new AgendaRecordatorioCorrida(enviados, fallidos, omitidos, detalle);
    }

    /// <summary>Manda la plantilla de la regla al cliente y la registra en su conversacion. Devuelve (ok, texto o error, id del mensaje).</summary>
    private async Task<(bool Ok, string Texto, string? Referencia)> EnviarWhatsAppAsync(
        AgendaRecordatorioRegla regla, AgendaTurno turno, IReadOnlyDictionary<Guid, WhatsAppTemplate> plantillas,
        IReadOnlyDictionary<string, string> valores, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(turno.ClienteTelefono))
        {
            return (false, "El turno no tiene telefono del cliente.", null);
        }
        if (regla.WhatsAppLineId is not Guid lineaId || regla.PlantillaId is not Guid plantillaId || !plantillas.TryGetValue(plantillaId, out var plantilla))
        {
            return (false, "La regla no tiene una linea o plantilla valida.", null);
        }
        var mapa = LeerMapa(regla.VariablesJson);
        var parametros = new List<string>();
        foreach (var (token, _) in WhatsAppTemplateComponents.ParseVariables(plantilla.VariablesJson))
        {
            var expr = mapa.TryGetValue(token, out var e) ? e : AgendaRecordatorioCalculo.Sugerida(token);
            var valor = AgendaRecordatorioCalculo.Render(expr, valores);
            if (string.IsNullOrWhiteSpace(valor))
            {
                return (false, $"La variable '{token}' de la plantilla quedo vacia para este turno.", null);
            }
            parametros.Add(valor);
        }
        LineSendResult res;
        try
        {
            res = await _connector.SendTemplateAsync(lineaId, turno.ClienteTelefono!, plantilla.Name, plantilla.Language, parametros, Guid.Empty,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, $"Error del proveedor de WhatsApp: {ex.Message}", null);
        }
        if (!res.Ok)
        {
            return (false, res.Error ?? "El proveedor de WhatsApp no acepto el mensaje.", null);
        }
        var texto = WhatsAppConnectorService.RenderTemplateText(plantilla, parametros);

        // Queda en la conversacion del cliente: si responde ("confirmo", "no puedo"), el agente de la linea ve el
        // recordatorio como contexto. Best-effort: un fallo aqui no anula el envio.
        if (_chat is not null)
        {
            try
            {
                var conv = await _chat.GetOrCreateForLineAsync(lineaId, turno.ClienteTelefono!, turno.ClienteNombre, cancellationToken);
                if (conv is not null)
                {
                    await _chat.RecordOutboundAsync(conv.Id, lineaId, texto, "text", res.MessageId, Guid.Empty, "Recordatorio de agenda", cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return (true, texto + $" (no se pudo registrar en la conversacion: {ex.Message})", res.MessageId);
            }
        }
        return (true, texto, res.MessageId);
    }

    // =====================================================================================================
    // Utilidades
    // =====================================================================================================

    private static bool RequiereAprobacion(WhatsAppProvider p) => p is WhatsAppProvider.YCloud or WhatsAppProvider.Cloud;

    /// <summary>YCloud / Cloud API (Meta) solo envian plantillas aprobadas; Evolution y el emulador la convierten a texto.</summary>
    private static bool PlantillaUsable(WhatsAppProvider proveedor, WhatsAppTemplateStatus estado) =>
        RequiereAprobacion(proveedor)
            ? estado == WhatsAppTemplateStatus.Approved
            : estado is not (WhatsAppTemplateStatus.Rejected or WhatsAppTemplateStatus.Disabled or WhatsAppTemplateStatus.Paused);

    private static AgendaRecordatorioReglaDto ADto(AgendaRecordatorioRegla r) => new(
        r.Id, r.Nombre, r.Activa, LeerIds(r.ServicioIdsJson), r.AnticipacionMinutos, r.HoraDesde, r.HoraHasta, r.FueraDeHorario,
        r.SoloSiNoConfirmado, r.Canal, r.WhatsAppLineId, r.PlantillaId, LeerMapa(r.VariablesJson), r.AiAgentId, r.VoiceLineId,
        r.Objetivo, r.Version);

    private static IReadOnlyList<Guid> LeerIds(string? json)
    {
        try { return string.IsNullOrWhiteSpace(json) ? Array.Empty<Guid>() : JsonSerializer.Deserialize<List<Guid>>(json) ?? new List<Guid>(); }
        catch (JsonException) { return Array.Empty<Guid>(); }
    }

    private static Dictionary<string, string> LeerMapa(string? json)
    {
        try
        {
            var d = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return new Dictionary<string, string>(d ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException) { return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
    }

    private static TimeZoneInfo Zona(string? id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(id) ? "America/Bogota" : id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("COT", TimeSpan.FromHours(-5), "COT", "COT");
        }
    }

    private static string Agregar(string? historial, string linea)
    {
        var sb = new StringBuilder(historial ?? string.Empty);
        if (sb.Length > 0) { sb.Append('\n'); }
        sb.Append(linea);
        var t = sb.ToString();
        return t.Length > HistorialMax ? t[^HistorialMax..] : t;
    }

    private static string? Recortar(string? s, int max) => s is null ? null : (s.Length > max ? s[..max] : s);

    private static string? Limpio(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) { return null; }
        var t = s.Trim();
        return t.Length > max ? t[..max] : t;
    }
}
