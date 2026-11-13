using System.Text;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Voice;
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
    private readonly IRetellVoiceService? _voz;
    private readonly IAgendaTurnosService? _turnos;
    private readonly Func<DateTimeOffset> _ahoraUtc;

    /// <summary>Una llamada sin resultado despues de este tiempo se da por fallida.</summary>
    private static readonly TimeSpan EsperaMaximaLlamada = TimeSpan.FromHours(3);

    public AgendaRecordatoriosService(IApplicationDbContext db, ITenantContext tenant, IWhatsAppConnectorService connector, IChatService chat,
        IRetellVoiceService voz, IAgendaTurnosService turnos)
        : this(db, tenant, connector, chat, () => DateTimeOffset.UtcNow, voz, turnos)
    {
    }

    /// <summary>Constructor con reloj inyectable (pruebas). Sin <paramref name="chat"/> no se registra en la conversacion;
    /// sin <paramref name="voz"/> las reglas de llamada fallan con motivo; sin <paramref name="turnos"/> el resultado de
    /// la llamada solo se anota en el historial (no cambia el estado del turno).</summary>
    public AgendaRecordatoriosService(IApplicationDbContext db, ITenantContext tenant, IWhatsAppConnectorService connector,
        IChatService? chat, Func<DateTimeOffset> ahoraUtc, IRetellVoiceService? voz = null, IAgendaTurnosService? turnos = null)
    {
        _db = db;
        _tenant = tenant;
        _connector = connector;
        _chat = chat;
        _ahoraUtc = ahoraUtc;
        _voz = voz;
        _turnos = turnos;
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
        Guid? lineaId = null, plantillaId = null, vozId = null, agenteId = null;
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (request.Canal == AgendaRecordatorioCanal.Llamada)
        {
            // Llamada: linea de voz lista (habilitada, con key y numero) y, opcional, el agente cuyo prompt se suma al guion.
            if (request.VoiceLineId is not Guid vid)
            {
                return AgendaOpResult.Mal("Elige la linea de voz desde la que se hace la llamada.");
            }
            var voz = await _db.RetellVoiceLines.AsNoTracking().FirstOrDefaultAsync(l => l.Id == vid, cancellationToken);
            if (voz is null)
            {
                return AgendaOpResult.Mal("La linea de voz ya no existe.");
            }
            if (!voz.IsEnabled || string.IsNullOrEmpty(voz.RetellApiKeyEncrypted) || string.IsNullOrWhiteSpace(voz.FromNumber))
            {
                return AgendaOpResult.Mal($"La linea de voz '{voz.Name}' no esta lista: debe estar habilitada y tener API key y numero saliente.");
            }
            if (request.AiAgentId is Guid aid && !await _db.AiAgents.AnyAsync(a => a.Id == aid, cancellationToken))
            {
                return AgendaOpResult.Mal("El agente elegido ya no existe.");
            }
            vozId = vid;
            agenteId = request.AiAgentId;
        }
        else
        {
            // WhatsApp: linea + plantilla de esa linea, usable por el proveedor, y todas sus variables con dato.
            if (request.WhatsAppLineId is not Guid lid)
            {
                return AgendaOpResult.Mal("Elige la linea de WhatsApp desde la que se envia el recordatorio.");
            }
            var linea = await _db.WhatsAppLines.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lid, cancellationToken);
            if (linea is null)
            {
                return AgendaOpResult.Mal("La linea de WhatsApp ya no existe.");
            }
            if (request.PlantillaId is not Guid pid)
            {
                return AgendaOpResult.Mal("Elige la plantilla del recordatorio.");
            }
            var plantilla = await _db.WhatsAppTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == pid, cancellationToken);
            if (plantilla is null || plantilla.WhatsAppLineId != lid)
            {
                return AgendaOpResult.Mal("La plantilla no existe o no pertenece a la linea elegida.");
            }
            if (!PlantillaUsable(linea.Provider, plantilla.Status))
            {
                return AgendaOpResult.Mal(RequiereAprobacion(linea.Provider)
                    ? $"La plantilla '{plantilla.Name}' aun no esta aprobada por Meta (estado {plantilla.Status}); con esta linea solo se envian plantillas aprobadas."
                    : $"La plantilla '{plantilla.Name}' esta {plantilla.Status} y no se puede usar.");
            }
            foreach (var (token, _) in WhatsAppTemplateComponents.ParseVariables(plantilla.VariablesJson))
            {
                var expr = request.Variables.TryGetValue(token, out var e) && !string.IsNullOrWhiteSpace(e) ? e.Trim() : AgendaRecordatorioCalculo.Sugerida(token);
                if (string.IsNullOrWhiteSpace(expr))
                {
                    return AgendaOpResult.Mal($"Asigna un dato del turno a la variable '{token}' de la plantilla.");
                }
                variables[token] = expr.Length > 300 ? expr[..300] : expr;
            }
            lineaId = lid;
            plantillaId = pid;
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
        r.AiAgentId = agenteId;
        r.VoiceLineId = vozId;
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
        var lineasVoz = await _db.RetellVoiceLines.AsNoTracking().OrderByDescending(l => l.IsDefault).ThenBy(l => l.Name)
            .Select(l => new AgendaRecordatorioLineaVozDto(l.Id, l.Name,
                l.IsEnabled && l.RetellApiKeyEncrypted != null && l.FromNumber != null, l.RetellAgentId != null && l.RetellAgentId != ""))
            .ToListAsync(cancellationToken);
        return new AgendaRecordatorioOpcionesDto(
            lineas.Select(l => new AgendaRecordatorioLineaDto(l.Id, l.InstanceName, l.Provider.ToString(), l.Status == WhatsAppLineStatus.Connected)).ToList(),
            plantillas.Where(t => proveedor.ContainsKey(t.WhatsAppLineId)).Select(t => new AgendaRecordatorioPlantillaDto(
                t.Id, t.WhatsAppLineId, t.Name, t.Language, t.Status.ToString(), PlantillaUsable(proveedor[t.WhatsAppLineId], t.Status),
                WhatsAppTemplateComponents.ParseVariables(t.VariablesJson).Select(v => v.Token).ToList(), t.BodyText)).ToList(),
            servicios, agentes, lineasVoz);
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

    public async Task<IReadOnlyList<Guid>> TenantsConReglasAsync(CancellationToken cancellationToken = default)
    {
        // Barrido del worker (solo ids): se lee cruzando tenants a proposito; cada tenant se procesa luego en su
        // propio ambito con el filtro global. Tambien los que tienen llamadas esperando resultado (aunque ya no
        // tengan reglas activas), para cerrarlas.
        var conReglas = await _db.AgendaRecordatorioReglas.AsNoTracking().IgnoreQueryFilters()
            .Where(r => r.Activa && !r.IsDeleted)
            .Select(r => r.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var conLlamadas = await _db.AgendaRecordatorioEnvios.AsNoTracking().IgnoreQueryFilters()
            .Where(e => e.Estado == AgendaRecordatorioEnvioEstado.EnLlamada)
            .Select(e => e.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return conReglas.Union(conLlamadas).ToList();
    }

    // =====================================================================================================
    // Procesamiento
    // =====================================================================================================

    public async Task<AgendaRecordatorioCorrida> ProcesarAsync(CancellationToken cancellationToken = default)
    {
        var ahora = _ahoraUtc();
        var tenant = await _db.Tenants.AsNoTracking().Where(t => t.Id == TenantId)
            .Select(t => new { t.Name, t.TimeZoneId }).FirstOrDefaultAsync(cancellationToken);
        var zona = Zona(tenant?.TimeZoneId);

        // Primero se cierran las llamadas que ya tienen resultado (confirma / cancela / reprogramar / no contesto).
        var resueltas = await ResolverLlamadasAsync(zona, cancellationToken);

        var reglas = await _db.AgendaRecordatorioReglas.AsNoTracking()
            .Where(r => r.Activa)
            .ToListAsync(cancellationToken);
        if (reglas.Count == 0)
        {
            return new AgendaRecordatorioCorrida(0, 0, 0, Array.Empty<string>(), resueltas);
        }

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
            return new AgendaRecordatorioCorrida(0, 0, 0, Array.Empty<string>(), resueltas);
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
                    return new AgendaRecordatorioCorrida(enviados, fallidos, omitidos, detalle, resueltas);
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
                var llamada = regla.Canal == AgendaRecordatorioCanal.Llamada;
                var (ok, texto, referencia) = llamada
                    ? await LlamarAsync(regla, turno, valores, cancellationToken)
                    : await EnviarWhatsAppAsync(regla, turno, plantillas, valores, cancellationToken);

                var trackedEnvio = await _db.AgendaRecordatorioEnvios.FirstAsync(e => e.Id == envio.Id, cancellationToken);
                trackedEnvio.Estado = !ok ? AgendaRecordatorioEnvioEstado.Fallido
                    : llamada ? AgendaRecordatorioEnvioEstado.EnLlamada : AgendaRecordatorioEnvioEstado.Enviado;
                trackedEnvio.Detalle = Recortar(texto, 500);
                trackedEnvio.ReferenciaExterna = Recortar(referencia, 200);
                trackedEnvio.ProcesadoUtc = _ahoraUtc();
                var trackedTurno = await _db.AgendaTurnos.FirstAsync(t => t.Id == turno.Id, cancellationToken);
                var local = TimeZoneInfo.ConvertTime(_ahoraUtc(), zona).DateTime;
                var linea = (ok, llamada) switch
                {
                    (true, true) => $"Llamada de recordatorio en curso ({regla.Nombre}).",
                    (true, false) => $"Recordatorio por WhatsApp enviado ({regla.Nombre}).",
                    (false, true) => $"No se pudo hacer la llamada de recordatorio ({regla.Nombre}): {texto}",
                    _ => $"No se pudo enviar el recordatorio por WhatsApp ({regla.Nombre}): {texto}"
                };
                trackedTurno.Historial = Agregar(trackedTurno.Historial, $"{local:yyyy-MM-dd HH:mm} {linea}");
                await _db.SaveChangesAsync(cancellationToken);
                if (ok) { enviados++; } else { fallidos++; detalle.Add($"{turno.ClienteNombre}: {texto}"); }
            }
        }
        return new AgendaRecordatorioCorrida(enviados, fallidos, omitidos, detalle, resueltas);
    }

    /// <summary>
    /// Coloca la llamada de recordatorio con el agente de voz (ADR-0167 etapa 2). El guion es constante por regla y los
    /// datos del turno van como variables dinamicas (Retell reutiliza el mismo agente); el agente deja el resultado en
    /// el analisis post-llamada. Devuelve (ok, detalle o error, call_id). Nunca reintenta: no se llama dos veces.
    /// </summary>
    private async Task<(bool Ok, string Texto, string? Referencia)> LlamarAsync(
        AgendaRecordatorioRegla regla, AgendaTurno turno, IReadOnlyDictionary<string, string> valores, CancellationToken cancellationToken)
    {
        if (_voz is null)
        {
            return (false, "Las llamadas de voz no estan disponibles en este servidor.", null);
        }
        var numero = AgendaRecordatorioLlamada.TelefonoE164(turno.ClienteTelefono);
        if (numero is null)
        {
            return (false, string.IsNullOrWhiteSpace(turno.ClienteTelefono)
                ? "El turno no tiene telefono del cliente."
                : $"El telefono '{turno.ClienteTelefono}' no es valido para llamar.", null);
        }
        if (regla.VoiceLineId is not Guid vozId)
        {
            return (false, "La regla no tiene una linea de voz.", null);
        }
        var variables = AgendaRecordatorioLlamada.VariablesDinamicas(valores);
        VoicePlaceCallResult res;
        try
        {
            res = await _voz.PlaceCallAsync(new VoicePlaceCallRequest(
                ToNumber: numero,
                AiAgentId: regla.AiAgentId,
                PromptExtra: AgendaRecordatorioLlamada.Guion(regla.Objetivo),
                Objetivo: null,
                FormulariosPermitidos: Array.Empty<Guid>(),
                ContactVariables: new Dictionary<string, string?> { ["nombre"] = turno.ClienteNombre, ["empresa"] = variables["empresa"] },
                RetellVoiceLineId: vozId,
                ExtraDynamicVariables: variables,
                AnalysisFields: AgendaRecordatorioLlamada.CamposAnalisis,
                Source: AgendaRecordatorioLlamada.Origen), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, $"Error del proveedor de voz: {ex.Message}", null);
        }
        return res.Placed && !string.IsNullOrWhiteSpace(res.CallId)
            ? (true, $"Llamada colocada a {numero}; esperando el resultado.", res.CallId)
            : (false, res.Error ?? "No se pudo colocar la llamada.", null);
    }

    /// <summary>
    /// Cierra las llamadas de recordatorio que ya tienen resultado: lee la VoiceCall (la actualiza el webhook de Retell
    /// y, si no llega, el sondeo de <see cref="IRetellVoiceService.GetCallResultAsync"/>), interpreta resultado_turno y lo
    /// aplica al turno. Una llamada sin resultado tras <see cref="EsperaMaximaLlamada"/> queda fallida.
    /// </summary>
    private async Task<int> ResolverLlamadasAsync(TimeZoneInfo zona, CancellationToken cancellationToken)
    {
        var pendientes = await _db.AgendaRecordatorioEnvios
            .Where(e => e.Estado == AgendaRecordatorioEnvioEstado.EnLlamada)
            .OrderBy(e => e.ProcesadoUtc)
            .Take(200)
            .ToListAsync(cancellationToken);
        if (pendientes.Count == 0)
        {
            return 0;
        }
        var ahora = _ahoraUtc();
        var resueltas = 0;
        foreach (var envio in pendientes)
        {
            if (_voz is null || string.IsNullOrWhiteSpace(envio.ReferenciaExterna))
            {
                await CerrarLlamadaAsync(envio, AgendaRecordatorioEnvioEstado.Fallido, "No se pudo leer el resultado de la llamada (sin servicio de voz).", null, zona, cancellationToken);
                continue;
            }
            VoiceCallResultDto? res;
            try
            {
                res = await _voz.GetCallResultAsync(envio.ReferenciaExterna, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                continue; // fallo transitorio leyendo el resultado: se reintenta en la proxima pasada
            }
            if (res is null)
            {
                await CerrarLlamadaAsync(envio, AgendaRecordatorioEnvioEstado.Fallido, "No se encontro la llamada.", null, zona, cancellationToken);
                continue;
            }
            if (res.Status is not ("Analyzed" or "Failed" or "Error"))
            {
                if (ahora - envio.ProcesadoUtc > EsperaMaximaLlamada)
                {
                    await CerrarLlamadaAsync(envio, AgendaRecordatorioEnvioEstado.Fallido,
                        $"La llamada no entrego resultado en {EsperaMaximaLlamada.TotalHours:0} horas (estado {res.Status}).", null, zona, cancellationToken);
                }
                continue;
            }

            var (resultado, nota) = AgendaRecordatorioLlamada.Interpretar(res.Status, res.CustomData);
            await CerrarLlamadaAsync(envio,
                resultado == AgendaLlamadaResultado.NoContesto ? AgendaRecordatorioEnvioEstado.Fallido : AgendaRecordatorioEnvioEstado.Enviado,
                "Llamada: " + AgendaRecordatorioLlamada.Describir(resultado, nota) + ".", (resultado, nota), zona, cancellationToken);
            resueltas++;
        }
        return resueltas;
    }

    /// <summary>Cierra el envio de una llamada y, si hay resultado, lo aplica al turno: confirma, cancela (motivo de la
    /// llamada) o lo anota en el historial. Solo toca el turno si sigue en el mismo horario que se recordo.</summary>
    private async Task CerrarLlamadaAsync(AgendaRecordatorioEnvio envio, AgendaRecordatorioEnvioEstado estado, string detalle,
        (AgendaLlamadaResultado Resultado, string? Nota)? resultado, TimeZoneInfo zona, CancellationToken cancellationToken)
    {
        const string Actor = "Recordatorio por llamada";
        envio.Estado = estado;
        envio.ProcesadoUtc = _ahoraUtc();
        var turno = await _db.AgendaTurnos.FirstOrDefaultAsync(t => t.Id == envio.TurnoId, cancellationToken);
        string? anotar = resultado is null ? $"Llamada de recordatorio sin resultado: {detalle}" : null;
        if (turno is not null && resultado is { } r)
        {
            var mismoHorario = turno.InicioUtc == envio.InicioTurnoUtc;
            var cambio = r.Resultado switch
            {
                AgendaLlamadaResultado.Confirma when turno.Estado == AgendaTurnoEstado.Reservado => AgendaTurnoEstado.Confirmado,
                AgendaLlamadaResultado.Cancela when turno.Estado is AgendaTurnoEstado.Reservado or AgendaTurnoEstado.Confirmado => AgendaTurnoEstado.Cancelado,
                _ => (AgendaTurnoEstado?)null
            };
            if (cambio is { } nuevo && mismoHorario && _turnos is not null)
            {
                var motivo = nuevo == AgendaTurnoEstado.Confirmado
                    ? "El cliente confirmo en la llamada de recordatorio"
                    : "Cancelado por el cliente en la llamada de recordatorio" + (string.IsNullOrWhiteSpace(r.Nota) ? "" : $": {r.Nota}");
                var res = await _turnos.CambiarEstadoAsync(new CambiarEstadoTurnoRequest(turno.Id, nuevo, motivo, turno.Version, Actor), cancellationToken);
                if (!res.Ok)
                {
                    detalle += $" No se pudo actualizar el turno: {res.Error}";
                    anotar = $"Llamada de recordatorio: {AgendaRecordatorioLlamada.Describir(r.Resultado, r.Nota)}. No se pudo actualizar el turno: {res.Error}";
                }
            }
            else
            {
                anotar = $"Llamada de recordatorio: {AgendaRecordatorioLlamada.Describir(r.Resultado, r.Nota)}."
                    + (r.Resultado == AgendaLlamadaResultado.Reprogramar ? " Pendiente de reprogramar." : "")
                    + (cambio is not null && !mismoHorario ? " El turno cambio de horario despues de la llamada; no se modifico." : "");
            }
        }
        envio.Detalle = Recortar(detalle, 500);
        if (turno is not null && anotar is not null)
        {
            var local = TimeZoneInfo.ConvertTime(_ahoraUtc(), zona).DateTime;
            turno.Historial = Agregar(turno.Historial, $"{local:yyyy-MM-dd HH:mm} {anotar}");
        }
        await _db.SaveChangesAsync(cancellationToken);
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
