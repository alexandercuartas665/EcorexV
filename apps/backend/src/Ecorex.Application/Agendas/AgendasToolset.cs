using System.Globalization;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Agendas;

/// <summary>
/// Herramientas del agente de IA para AGENDAS (ADR-0165 ola 5): ver los servicios que puede agendar, consultar
/// huecos, separar, ver los turnos del cliente, reprogramar y cancelar. Reglas:
/// - Solo servicios ACTIVOS con "el agente de IA puede reservarlo" y solo recursos con "el agente puede agendar".
/// - El cliente es el de la conversacion: su telefono sale de la conversacion (no del modelo) y su contacto del
///   Directorio se busca por los ultimos 10 digitos. Solo se tocan turnos de ESE cliente.
/// - Reservar crea la tarea del concepto del servicio (si tiene) y la marca como tocada en el turno, para que la
///   alerta de cierre avise a su asignado (ADR-0163).
/// </summary>
public sealed class AgendasToolset : IAgentToolset
{
    private const string ActorName = "Agente IA";
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-CO");

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IApplicationDbContext _db;
    private readonly IAgendaTurnosService _turnos;

    public AgendasToolset(IApplicationDbContext db, IAgendaTurnosService turnos)
    {
        _db = db;
        _turnos = turnos;
    }

    public string GroupKey => "agendas";
    public string GroupLabel => "Agendas y turnos";

    public IReadOnlyList<AiToolSpec> GetSpecs() => new[]
    {
        new AiToolSpec(
            "servicios_agendables",
            "Lista los servicios que puedes agendar (nombre, duracion en minutos, descripcion). Usala para saber " +
            "que se puede reservar antes de consultar_disponibilidad.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new AiToolSpec(
            "consultar_disponibilidad",
            "Huecos libres de un servicio (ya descuenta festivos, novedades y turnos tomados). Ofrece al cliente 2 o 3 " +
            "opciones concretas. REGLA: ofrece SOLO fechas y horas que devuelva esta herramienta; NUNCA inventes horarios. " +
            "Si la fecha pedida no tiene huecos, la respuesta trae 'siguientes' con los proximos dias que si tienen.",
            """{"type":"object","properties":{"servicio":{"type":"string","description":"Nombre del servicio (como lo devuelve servicios_agendables)"},"fecha":{"type":"string","description":"Fecha desde la que buscar, yyyy-MM-dd (opcional; por defecto hoy)"},"dias":{"type":"integer","description":"Cuantos dias revisar desde esa fecha (1 a 14; por defecto 7)"}},"required":["servicio"],"additionalProperties":false}"""),
        new AiToolSpec(
            "reservar_turno",
            "Separa un turno para el cliente de esta conversacion en un hueco libre (fecha + hora que devolvio " +
            "consultar_disponibilidad). Confirma con el cliente la fecha y la hora ANTES de llamarla. Devuelve el turno " +
            "y, si aplica, el numero de la tarea: entregale al cliente la fecha, la hora y ese numero.",
            """{"type":"object","properties":{"servicio":{"type":"string","description":"Nombre del servicio"},"fecha":{"type":"string","description":"yyyy-MM-dd"},"hora":{"type":"string","description":"HH:mm (24 horas)"},"nombre":{"type":"string","description":"Nombre de quien asiste"},"nota":{"type":"string","description":"Motivo o datos utiles (placa, sintomas, direccion). Opcional"}},"required":["servicio","fecha","hora","nombre"],"additionalProperties":false}"""),
        new AiToolSpec(
            "turnos_del_cliente",
            "Proximos turnos del cliente de esta conversacion (servicio, fecha, hora, estado, id). Usala antes de " +
            "reservar para no duplicar y cuando pregunte por su cita.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new AiToolSpec(
            "reprogramar_turno",
            "Mueve un turno del cliente a otro hueco libre del mismo servicio. El id sale de turnos_del_cliente.",
            """{"type":"object","properties":{"turno_id":{"type":"string"},"fecha":{"type":"string","description":"yyyy-MM-dd"},"hora":{"type":"string","description":"HH:mm"},"motivo":{"type":"string"}},"required":["turno_id","fecha","hora"],"additionalProperties":false}"""),
        new AiToolSpec(
            "confirmar_turno",
            "Marca como CONFIRMADO un turno del cliente. Usala cuando el cliente responda a un recordatorio que si " +
            "asiste (\"confirmo\", \"ahi estare\"). El id sale de turnos_del_cliente.",
            """{"type":"object","properties":{"turno_id":{"type":"string"}},"required":["turno_id"],"additionalProperties":false}"""),
        new AiToolSpec(
            "cancelar_turno",
            "Cancela un turno del cliente (libera el cupo). Pide el motivo al cliente. El id sale de turnos_del_cliente.",
            """{"type":"object","properties":{"turno_id":{"type":"string"},"motivo":{"type":"string"}},"required":["turno_id","motivo"],"additionalProperties":false}"""),
    };

    public async Task<AgentToolResult> ExecuteAsync(string toolName, string argumentsJson, Guid actorUserId, bool autonomous, CancellationToken cancellationToken = default)
    {
        JsonElement args;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            args = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return Err("Los argumentos no son un JSON valido.");
        }

        try
        {
            return toolName switch
            {
                "servicios_agendables" => await ServiciosAsync(cancellationToken),
                "consultar_disponibilidad" => await DisponibilidadAsync(args, cancellationToken),
                "reservar_turno" => await ReservarAsync(args, cancellationToken),
                "turnos_del_cliente" => await TurnosClienteAsync(cancellationToken),
                "reprogramar_turno" => await ReprogramarAsync(args, cancellationToken),
                "confirmar_turno" => await ConfirmarAsync(args, cancellationToken),
                "cancelar_turno" => await CancelarAsync(args, cancellationToken),
                _ => Err($"Herramienta desconocida: {toolName}")
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Err($"Error ejecutando '{toolName}': {ex.Message}");
        }
    }

    // ===== servicios / disponibilidad =====

    private async Task<AgentToolResult> ServiciosAsync(CancellationToken ct)
    {
        var servicios = (await _turnos.ListServiciosAsync(ct)).Where(s => s.Activo && s.AgentePuedeReservar)
            .Select(s => new { nombre = s.Nombre, duracion_minutos = s.DuracionMinutos, descripcion = s.Descripcion })
            .ToList();
        return Ok(new
        {
            ok = true,
            servicios,
            mensaje = servicios.Count == 0 ? "No hay servicios habilitados para agendar por este canal." : null
        });
    }

    private async Task<AgentToolResult> DisponibilidadAsync(JsonElement args, CancellationToken ct)
    {
        var servicio = await ServicioAsync(Str(args, "servicio"), ct);
        if (servicio is null)
        {
            return await ServicioNoExisteAsync(Str(args, "servicio"), ct);
        }
        var hoy = await _turnos.HoyAsync(ct);
        // El modelo usa 'fecha' o 'desde' indistintamente: se aceptan ambos.
        var textoFecha = Str(args, "fecha") ?? Str(args, "desde");
        var desde = DateOnly.TryParseExact(textoFecha, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d >= hoy ? d : hoy;
        var dias = args.TryGetProperty("dias", out var dEl) && dEl.TryGetInt32(out var n) ? Math.Clamp(n, 1, 14) : 7;
        var huecos = await _turnos.HuecosAsync(servicio.Id, desde, dias, null, soloRecursosAgente: true, cancellationToken: ct);
        var porDia = PorDia(huecos);

        // Sin huecos en lo pedido: se buscan los proximos dias con cupo para que el agente ofrezca opciones REALES.
        object? siguientes = null;
        if (porDia.Count == 0)
        {
            var luego = await _turnos.HuecosAsync(servicio.Id, desde.AddDays(dias), 14, null, soloRecursosAgente: true, cancellationToken: ct);
            var proximos = PorDia(luego).Take(3).ToList();
            siguientes = proximos.Count == 0 ? null : proximos;
        }
        return Ok(new
        {
            ok = true,
            servicio = servicio.Nombre,
            duracion_minutos = servicio.DuracionMinutos,
            desde = desde.ToString("yyyy-MM-dd"),
            huecos = porDia,
            siguientes,
            mensaje = porDia.Count > 0 ? null
                : siguientes is null
                    ? $"No hay huecos libres para {servicio.Nombre} en las proximas semanas. No ofrezcas horarios; ofrece que un asesor lo contacte."
                    : $"No hay huecos libres para {servicio.Nombre} en esa fecha. Ofrece SOLO las opciones de 'siguientes'."
        });
    }

    // ===== reservar =====

    private async Task<AgentToolResult> ReservarAsync(JsonElement args, CancellationToken ct)
    {
        var servicio = await ServicioAsync(Str(args, "servicio"), ct);
        if (servicio is null)
        {
            return await ServicioNoExisteAsync(Str(args, "servicio"), ct);
        }
        if (!TryFechaHora(args, out var fecha, out var hora))
        {
            return Err("Indica la fecha (yyyy-MM-dd) y la hora (HH:mm) de un hueco que devolvio consultar_disponibilidad.");
        }
        var cliente = await ClienteAsync(ct);
        var nombre = Str(args, "nombre");
        if (string.IsNullOrWhiteSpace(nombre))
        {
            nombre = cliente.Nombre;
        }

        // No duplicar: si el cliente ya tiene ESE servicio en ESA fecha y hora, se devuelve el existente.
        var propios = await _turnos.TurnosDelClienteAsync(cliente.TerceroId, cliente.Telefono, cliente.ConversationId, fecha, ct);
        var igual = propios.FirstOrDefault(t => t.ServicioId == servicio.Id && t.Fecha == fecha && t.HoraInicio == hora
            && AgendaTurnosCalculo.Reprogramable(t.Estado));
        if (igual is not null)
        {
            return Ok(TurnoJson(igual, "El cliente ya tenia este turno (no se duplico)."), cierra: true);
        }

        var res = await _turnos.ReservarAsync(new ReservarTurnoRequest(
            servicio.Id, fecha, hora, nombre ?? "Cliente", cliente.Telefono, Str(args, "nota"),
            AgendaTurnoOrigen.Agente, cliente.TerceroId, cliente.ConversationId, SoloRecursosAgente: true, ActorName: ActorName), ct);
        if (!res.Ok || res.Id is not Guid turnoId)
        {
            return Err((res.Error ?? "No se pudo separar el turno.") + " Consulta de nuevo la disponibilidad y ofrece otro hueco.");
        }
        var turno = await _turnos.GetTurnoAsync(turnoId, ct);
        if (turno?.TaskItemId is Guid taskId)
        {
            // ADR-0163: la alerta de cierre avisa al asignado de la tarea del turno.
            AiToolRunContext.MarkTaskTouched(taskId);
        }
        return Ok(TurnoJson(turno!, "Turno separado."), cierra: true);
    }

    // ===== turnos del cliente =====

    private async Task<AgentToolResult> TurnosClienteAsync(CancellationToken ct)
    {
        var cliente = await ClienteAsync(ct);
        var hoy = await _turnos.HoyAsync(ct);
        var turnos = (await _turnos.TurnosDelClienteAsync(cliente.TerceroId, cliente.Telefono, cliente.ConversationId, hoy, ct))
            .Where(t => t.Estado != AgendaTurnoEstado.Cancelado)
            .Take(10)
            .Select(t => new
            {
                turno_id = t.Id,
                servicio = t.ServicioNombre,
                fecha = t.Fecha.ToString("yyyy-MM-dd"),
                dia = t.Fecha.ToString("dddd d 'de' MMMM", Es),
                hora = t.HoraInicio.ToString("HH:mm"),
                estado = AgendaTurnosService.Estado(t.Estado),
                tarea = t.TaskNumero
            })
            .ToList();
        return Ok(new { ok = true, turnos, mensaje = turnos.Count == 0 ? "El cliente no tiene turnos proximos." : null });
    }

    // ===== reprogramar / cancelar =====

    private async Task<AgentToolResult> ReprogramarAsync(JsonElement args, CancellationToken ct)
    {
        var (turno, error) = await TurnoDelClienteAsync(Str(args, "turno_id"), ct);
        if (turno is null)
        {
            return Err(error!);
        }
        var servicio = (await _turnos.ListServiciosAsync(ct)).FirstOrDefault(s => s.Id == turno.ServicioId);
        if (servicio is null || !servicio.AgentePuedeReservar)
        {
            return Err("Este turno no se puede mover por este canal; un asesor lo gestiona.");
        }
        if (!TryFechaHora(args, out var fecha, out var hora))
        {
            return Err("Indica la nueva fecha (yyyy-MM-dd) y hora (HH:mm) de un hueco libre.");
        }
        var res = await _turnos.ReprogramarAsync(new ReprogramarTurnoRequest(turno.Id, fecha, hora, Str(args, "motivo"), turno.Version,
            SoloRecursosAgente: true, ActorName: ActorName), ct);
        if (!res.Ok)
        {
            return Err((res.Error ?? "No se pudo reprogramar.") + " Consulta la disponibilidad y ofrece otro hueco.");
        }
        var nuevo = (await _turnos.GetTurnoAsync(turno.Id, ct))!;
        if (nuevo.TaskItemId is Guid taskId)
        {
            AiToolRunContext.MarkTaskTouched(taskId);
        }
        return Ok(TurnoJson(nuevo, "Turno reprogramado."), cierra: true);
    }

    private async Task<AgentToolResult> ConfirmarAsync(JsonElement args, CancellationToken ct)
    {
        var (turno, error) = await TurnoDelClienteAsync(Str(args, "turno_id"), ct);
        if (turno is null)
        {
            return Err(error!);
        }
        if (turno.Estado == AgendaTurnoEstado.Confirmado)
        {
            return Ok(TurnoJson(turno, "El turno ya estaba confirmado."), cierra: true);
        }
        var res = await _turnos.CambiarEstadoAsync(new CambiarEstadoTurnoRequest(turno.Id, AgendaTurnoEstado.Confirmado,
            "El cliente confirmo por WhatsApp", turno.Version, ActorName), ct);
        if (!res.Ok)
        {
            return Err(res.Error ?? "No se pudo confirmar el turno.");
        }
        if (turno.TaskItemId is Guid taskId)
        {
            AiToolRunContext.MarkTaskTouched(taskId);
        }
        return Ok(TurnoJson((await _turnos.GetTurnoAsync(turno.Id, ct))!, "Turno confirmado."), cierra: true);
    }

    private async Task<AgentToolResult> CancelarAsync(JsonElement args, CancellationToken ct)
    {
        var (turno, error) = await TurnoDelClienteAsync(Str(args, "turno_id"), ct);
        if (turno is null)
        {
            return Err(error!);
        }
        var motivo = Str(args, "motivo");
        if (string.IsNullOrWhiteSpace(motivo))
        {
            return Err("Pregunta al cliente el motivo de la cancelacion y vuelve a llamar con 'motivo'.");
        }
        var res = await _turnos.CambiarEstadoAsync(new CambiarEstadoTurnoRequest(turno.Id, AgendaTurnoEstado.Cancelado, motivo, turno.Version, ActorName), ct);
        if (!res.Ok)
        {
            return Err(res.Error ?? "No se pudo cancelar.");
        }
        if (turno.TaskItemId is Guid taskId)
        {
            AiToolRunContext.MarkTaskTouched(taskId);
        }
        return Ok(new { ok = true, turno_id = turno.Id, mensaje = $"Turno de {turno.ServicioNombre} del {turno.Fecha:yyyy-MM-dd} {turno.HoraInicio:HH\\:mm} cancelado." }, cierra: true);
    }

    // ===== cliente de la conversacion =====

    private sealed record Cliente(Guid? ConversationId, string? Telefono, Guid? TerceroId, string? Nombre);

    /// <summary>
    /// El cliente es el de la conversacion en curso: telefono real de la conversacion (no el que dicte el modelo) y
    /// su contacto activo del Directorio por los ultimos 10 digitos.
    /// </summary>
    private async Task<Cliente> ClienteAsync(CancellationToken ct)
    {
        var convId = AiToolRunContext.ConversationId;
        string? telefono = null;
        string? nombreConv = null;
        if (convId is Guid cid)
        {
            var conv = await _db.Conversations.AsNoTracking().Where(c => c.Id == cid)
                .Select(c => new { c.ContactPhone, c.ContactName }).FirstOrDefaultAsync(ct);
            telefono = conv?.ContactPhone;
            nombreConv = conv?.ContactName;
        }
        Guid? terceroId = null;
        string? nombre = nombreConv;
        var tel10 = AgendaTurnosService.Ultimos10(telefono);
        if (tel10 is not null)
        {
            var sufijo = tel10[^Math.Min(7, tel10.Length)..];
            var candidatos = await _db.Terceros.AsNoTracking()
                .Where(t => t.Estado != TerceroEstado.Inactivo && t.Telefono != null && EF.Functions.Like(t.Telefono, "%" + sufijo))
                .Select(t => new { t.Id, t.Telefono, t.Nombre })
                .Take(20)
                .ToListAsync(ct);
            var match = candidatos.FirstOrDefault(t => AgendaTurnosService.Ultimos10(t.Telefono) == tel10);
            if (match is not null)
            {
                terceroId = match.Id;
                nombre = match.Nombre;
            }
        }
        return new Cliente(convId, telefono, terceroId, nombre);
    }

    private async Task<(AgendaTurnoDto? Turno, string? Error)> TurnoDelClienteAsync(string? turnoId, CancellationToken ct)
    {
        if (!Guid.TryParse(turnoId, out var id))
        {
            return (null, "Indica el turno_id que devolvio turnos_del_cliente.");
        }
        var cliente = await ClienteAsync(ct);
        var hoy = await _turnos.HoyAsync(ct);
        var propios = await _turnos.TurnosDelClienteAsync(cliente.TerceroId, cliente.Telefono, cliente.ConversationId, hoy, ct);
        var turno = propios.FirstOrDefault(t => t.Id == id);
        if (turno is null)
        {
            // Mismo mensaje exista o no: no se revela si el id es de otro cliente.
            return (null, "Ese turno no es de este cliente o ya paso. Usa turnos_del_cliente.");
        }
        if (!AgendaTurnosCalculo.Reprogramable(turno.Estado))
        {
            return (null, $"El turno esta {AgendaTurnosService.Estado(turno.Estado)}; ya no se puede modificar.");
        }
        return (turno, null);
    }

    // ===== helpers =====

    private async Task<AgendaServicioDto?> ServicioAsync(string? nombre, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            return null;
        }
        var disponibles = (await _turnos.ListServiciosAsync(ct)).Where(s => s.Activo && s.AgentePuedeReservar).ToList();
        // Tolerante a como lo escriba el modelo: sin tildes, sin mayusculas y sin palabras de relleno
        // ("Consulta de Medicina General" == "Consulta medicina general").
        var pedido = Palabras(nombre);
        return disponibles.FirstOrDefault(s => Palabras(s.Nombre).SetEquals(pedido))
            ?? disponibles.FirstOrDefault(s => pedido.Count > 0 && pedido.IsSubsetOf(Palabras(s.Nombre)))
            ?? disponibles.FirstOrDefault(s => Palabras(s.Nombre).Count > 0 && Palabras(s.Nombre).IsSubsetOf(pedido));
    }

    private static readonly HashSet<string> Relleno = new(StringComparer.Ordinal) { "de", "del", "la", "el", "los", "las", "y", "para", "un", "una" };

    private static HashSet<string> Palabras(string texto)
    {
        var sinTildes = new string(texto.Normalize(System.Text.NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray());
        return sinTildes.ToLowerInvariant()
            .Split(new[] { ' ', '-', '_', ',', '.', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !Relleno.Contains(w))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static List<object> PorDia(IReadOnlyList<AgendaHueco> huecos) =>
        huecos.GroupBy(h => h.Fecha).Take(7).Select(g => (object)new
        {
            fecha = g.Key.ToString("yyyy-MM-dd"),
            dia = g.Key.ToString("dddd d 'de' MMMM", Es),
            horas = g.Select(h => h.Inicio.ToString("HH:mm")).Take(60).ToList()
        }).ToList();

    private async Task<AgentToolResult> ServicioNoExisteAsync(string? nombre, CancellationToken ct)
    {
        var nombres = (await _turnos.ListServiciosAsync(ct)).Where(s => s.Activo && s.AgentePuedeReservar).Select(s => s.Nombre).ToList();
        return Err(nombres.Count == 0
            ? "No hay servicios habilitados para agendar por este canal."
            : $"No encontre el servicio '{nombre}'. Servicios que puedes agendar: {string.Join(", ", nombres)}.");
    }

    private static bool TryFechaHora(JsonElement args, out DateOnly fecha, out TimeOnly hora)
    {
        hora = default;
        return DateOnly.TryParseExact(Str(args, "fecha"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha)
            && TimeOnly.TryParseExact(Str(args, "hora"), new[] { "HH:mm", "H:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out hora);
    }

    private static object TurnoJson(AgendaTurnoDto t, string mensaje) => new
    {
        ok = true,
        turno_id = t.Id,
        servicio = t.ServicioNombre,
        fecha = t.Fecha.ToString("yyyy-MM-dd"),
        dia = t.Fecha.ToString("dddd d 'de' MMMM", Es),
        hora = t.HoraInicio.ToString("HH:mm"),
        hasta = t.HoraFin.ToString("HH:mm"),
        estado = AgendaTurnosService.Estado(t.Estado),
        ticket = t.TaskNumero,
        mensaje
    };

    private static string? Str(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static AgentToolResult Ok(object payload, bool cierra = false) => new(JsonSerializer.Serialize(payload, JsonOut), cierra);

    private static AgentToolResult Err(string error) => new(JsonSerializer.Serialize(new { ok = false, error }, JsonOut), false);
}
