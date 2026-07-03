using System.Globalization;
using System.Text;
using System.Text.Json;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

/// <summary>Resultado de ejecutar una herramienta: el JSON que se devuelve al modelo y si se creo una cita.</summary>
public sealed record AgendaToolResult(string Json, bool BookingCreated);

/// <summary>
/// Catalogo de herramientas (function calling) que el agente de IA puede invocar para operar la agenda
/// del salon: listar asesores de imagen, consultar precios de servicios, consultar disponibilidad real y
/// reservar una cita (incluyendo doble turno / cadena multi-estacion). Toda reserva pasa por
/// IAgendaService.SaveBookingAsync, asi que comparte el ANTI-OVERBOOKING por solapamiento (exclusion
/// constraint + captura de violacion) y el gate de largo de cabello: la IA NO tiene atajos que los salten.
/// </summary>
public interface IAgendaToolset : IAgentToolset
{
    // GetSpecs y ExecuteAsync se heredan de IAgentToolset.
    // autonomous=true permite reservar/cancelar de verdad; en false (modo sugerencia) reservar_cita y
    // cancelar_cita no escriben en la agenda: registran la solicitud como PENDIENTE para que un asesor la confirme.
}

public sealed class AgendaToolset : IAgendaToolset
{
    private readonly IAgendaService _agenda;
    private readonly IResourceService _resources;
    private readonly IServiceCatalogService _services;
    private readonly IClientService _clients;
    private readonly IOnlineBookingService _onlineBooking;
    private readonly TimeProvider _clock;

    // Zona horaria del salon (America/Bogota = UTC-5, sin horario de verano) para filtrar "hoy en adelante".
    private static readonly TimeSpan SalonOffset = TimeSpan.FromHours(-5);

    public AgendaToolset(IAgendaService agenda, IResourceService resources, IServiceCatalogService services, IClientService clients, IOnlineBookingService onlineBooking, TimeProvider clock)
    {
        _agenda = agenda;
        _resources = resources;
        _services = services;
        _clients = clients;
        _onlineBooking = onlineBooking;
        _clock = clock;
    }

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string GroupKey => "agenda";
    public string GroupLabel => "Agenda y citas";

    public IReadOnlyList<AiToolSpec> GetSpecs() => new[]
    {
        new AiToolSpec(
            "listar_asesores",
            "Lista los asesores de imagen (profesionales y estaciones) activos del salon, con el tipo y los servicios que atiende cada uno. Usala al inicio para saber con quien puede agendar el cliente.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),

        new AiToolSpec(
            "consultar_servicios_precios",
            "Consulta el catalogo de servicios con su precio y duracion en minutos. Parametro opcional 'asesor' (nombre o id) para ver los precios y servicios habilitados de un asesor especifico. NO inventes precios: usa solo los que devuelve esta herramienta.",
            """{"type":"object","properties":{"asesor":{"type":"string","description":"Nombre o id del asesor (opcional)"}},"additionalProperties":false}"""),

        new AiToolSpec(
            "consultar_disponibilidad",
            "Devuelve los horarios LIBRES de un asesor para una fecha concreta. Si pasas 'servicios' (y 'largo' cuando el servicio varia por largo), devuelve solo los inicios donde cabe el servicio COMPLETO segun su duracion (asesores que agendan por duracion) respetando el margen entre citas. Ofrece al cliente UNICAMENTE los horarios que esta herramienta reporta; nunca inventes horarios.",
            """{"type":"object","properties":{"asesor":{"type":"string","description":"Nombre o id del asesor"},"fecha":{"type":"string","description":"Fecha en formato AAAA-MM-DD"},"servicios":{"type":"array","items":{"type":"string"},"description":"Servicios a realizar (opcional; mejora la disponibilidad segun la duracion real)"},"largo":{"type":"string","description":"Largo de cabello (corto/medio/largo/muy largo) si el servicio varia por largo"}},"required":["asesor","fecha"],"additionalProperties":false}"""),

        new AiToolSpec(
            "reservar_cita",
            "Separa (reserva) una cita. Usala SOLO cuando el cliente haya confirmado asesor, fecha, hora y servicio. " +
            "Si el servicio cambia de precio/duracion segun el largo del cabello, primero determina el largo con " +
            "clasificar_largo_cabello y pasa ese valor en 'largo' (corto/medio/largo/muy largo); sin el, la reserva se " +
            "rechaza. Para DOBLE TURNO (el cliente pasa por dos estaciones el mismo dia, ej. lavado y luego corte con " +
            "otro asesor) agrega el segundo paso en 'cadena'. El sistema valida que el horario siga libre y no se cruce con otra cita.",
            """
            {
              "type":"object",
              "properties":{
                "asesor":{"type":"string","description":"Nombre o id del asesor principal"},
                "fecha":{"type":"string","description":"AAAA-MM-DD"},
                "hora":{"type":"string","description":"Hora de inicio HH:mm en formato 24h"},
                "cliente_nombre":{"type":"string","description":"Nombre del cliente"},
                "cliente_telefono":{"type":"string","description":"Telefono del cliente (opcional)"},
                "servicios":{"type":"array","items":{"type":"string"},"description":"Nombres o ids de los servicios a realizar"},
                "largo":{"type":"string","description":"Largo de cabello detectado (corto/medio/largo/muy largo). Obligatorio si el servicio varia por largo."},
                "notas":{"type":"string","description":"Notas u observaciones (opcional)"},
                "cadena":{"type":"array","description":"Pasos adicionales para doble turno; cada elemento es un asesor y la hora en que continua","items":{"type":"object","properties":{"asesor":{"type":"string"},"hora":{"type":"string","description":"HH:mm 24h"}},"required":["asesor","hora"]}}
              },
              "required":["asesor","fecha","hora","cliente_nombre","servicios"],
              "additionalProperties":false
            }
            """),

        new AiToolSpec(
            "consultar_citas_cliente",
            "Busca las citas FUTURAS y vigentes (programadas o confirmadas) de un cliente por su nombre o telefono. Usala cuando el cliente quiera cancelar, reprogramar o consultar su cita: devuelve cada cita con su cita_id, fecha, hora y asesor (incluye datos de cadena si es doble turno).",
            """{"type":"object","properties":{"cliente":{"type":"string","description":"Nombre o telefono del cliente"}},"required":["cliente"],"additionalProperties":false}"""),

        new AiToolSpec(
            "obtener_link_reserva",
            "Devuelve el LINK publico para que el cliente reserve su cita el mismo (elige asesor, servicio y horario en linea). Usalo cuando el cliente quiera agendar y prefieras que reserve por el link, o cuando te lo pidan. Si el salon no tiene reservas online activas, devuelve disponible=false y debes agendar tu por el flujo normal.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),

        new AiToolSpec(
            "cancelar_cita",
            "Cancela una cita por su cita_id (la obtienes de consultar_citas_cliente). Usala SOLO despues de que el cliente confirme que quiere cancelar. Si la cita es un DOBLE TURNO (cadena), cancela cada paso llamando esta herramienta una vez por cada cita_id de la cadena.",
            """{"type":"object","properties":{"cita_id":{"type":"string","description":"Identificador (uuid) de la cita a cancelar"}},"required":["cita_id"],"additionalProperties":false}""")
    };

    public async Task<AgendaToolResult> ExecuteAsync(string toolName, string argumentsJson, Guid actorUserId, bool autonomous, CancellationToken cancellationToken = default)
    {
        JsonElement args;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            args = doc.RootElement.Clone();
        }
        catch
        {
            return Err("Los argumentos no son un JSON valido.");
        }

        try
        {
            return toolName switch
            {
                "listar_asesores" => await ListAdvisorsAsync(cancellationToken),
                "consultar_servicios_precios" => await ServicePricesAsync(args, cancellationToken),
                "consultar_disponibilidad" => await AvailabilityAsync(args, cancellationToken),
                "reservar_cita" => await BookAsync(args, actorUserId, autonomous, cancellationToken),
                "consultar_citas_cliente" => await ClientAppointmentsAsync(args, cancellationToken),
                "obtener_link_reserva" => await BookingLinkAsync(cancellationToken),
                "cancelar_cita" => await CancelAsync(args, actorUserId, autonomous, cancellationToken),
                _ => Err($"Herramienta desconocida: {toolName}")
            };
        }
        catch (Exception ex)
        {
            return Err($"Error ejecutando '{toolName}': {ex.Message}");
        }
    }

    // ===== Herramientas =====

    private async Task<AgendaToolResult> ListAdvisorsAsync(CancellationToken ct)
    {
        var list = (await _resources.ListAsync(ct)).Where(r => r.IsActive).ToList();
        var data = list.Select(r => new
        {
            id = r.Id,
            nombre = r.Name,
            tipo = KindLabel(r.Kind),
            servicios = r.Services.Select(s => s.ServiceName).ToArray()
        }).ToArray();
        return Ok(new { asesores = data, total = data.Length });
    }

    private async Task<AgendaToolResult> ServicePricesAsync(JsonElement args, CancellationToken ct)
    {
        var asesor = Str(args, "asesor");
        if (!string.IsNullOrWhiteSpace(asesor))
        {
            var res = await ResolveResourceAsync(asesor!, ct);
            if (res is null) { return Err($"No encontre el asesor '{asesor}'. Usa listar_asesores para ver los nombres exactos."); }
            var opts = await _agenda.GetServiceOptionsAsync(res.Id, ct);
            return Ok(new
            {
                asesor = res.Name,
                servicios = opts.Select(o => new { id = o.Id, nombre = o.Name, precio = o.Price, duracion_min = o.DurationMinutes }).ToArray()
            });
        }

        var all = (await _services.ListAsync(false, ct)).ToList();
        return Ok(new
        {
            servicios = all.Select(s => new
            {
                id = s.Id,
                nombre = s.Name,
                precio = s.Price,
                duracion_min = s.DurationMinutes,
                categoria = s.Category,
                descripcion = s.Description,
                // Tarifas por LARGO de cabello (si el servicio varia). Cuando aplique, usa el precio/duracion
                // del largo detectado en vez del precio base. El largo se obtiene con clasificar_largo_cabello.
                precios_por_largo = (s.PriceTiers is { Count: > 0 })
                    ? s.PriceTiers.Select(t => new { largo = t.Length.ToString(), precio = t.Price, duracion_min = t.DurationMinutes }).ToArray()
                    : null
            }).ToArray()
        });
    }

    private async Task<AgendaToolResult> AvailabilityAsync(JsonElement args, CancellationToken ct)
    {
        var asesor = Str(args, "asesor");
        if (string.IsNullOrWhiteSpace(asesor)) { return Err("Falta el parametro 'asesor'."); }
        if (!TryDate(Str(args, "fecha"), out var date)) { return Err("La fecha es invalida; usa el formato AAAA-MM-DD."); }

        var res = await ResolveResourceAsync(asesor!, ct);
        if (res is null) { return Err($"No encontre el asesor '{asesor}'. Usa listar_asesores para ver los nombres exactos."); }

        // Si el agente indica servicios, ofrecer inicios donde cabe el servicio COMPLETO (duracion por largo),
        // respetando modo de agenda y buffer. Si no, caer a la grilla de cupos (consciente de solapamiento).
        var serviciosTokens = StrArray(args, "servicios");
        if (serviciosTokens.Count > 0)
        {
            var (serviceIds, unresolved) = await ResolveServicesAsync(serviciosTokens, ct);
            if (serviceIds.Count > 0)
            {
                var hair = HairLengthFromString(Str(args, "largo"));
                var starts = await _agenda.GetAvailableStartsAsync(res.Id, date, serviceIds, hair, ct);
                return Ok(new
                {
                    asesor = res.Name,
                    fecha = date.ToString("yyyy-MM-dd"),
                    libres = starts.Select(t => t.ToString("HH\\:mm")).ToArray(),
                    mensaje = starts.Count == 0
                        ? "No hay un hueco libre que alcance para ese servicio completo ese dia; ofrece otra fecha."
                        : $"Hay {starts.Count} horarios donde cabe el servicio completo."
                });
            }
        }

        var slots = await _agenda.GetDaySlotsAsync(res.Id, date, ct);
        var libres = slots.Where(s => !s.Occupied).Select(s => s.Time.ToString("HH\\:mm")).ToArray();
        var ocupados = slots.Where(s => s.Occupied).Select(s => s.Time.ToString("HH\\:mm")).ToArray();

        return Ok(new
        {
            asesor = res.Name,
            fecha = date.ToString("yyyy-MM-dd"),
            atiende = slots.Count > 0,
            libres,
            ocupados,
            mensaje = slots.Count == 0
                ? "El asesor no tiene turnos configurados ese dia (no atiende)."
                : (libres.Length == 0 ? "El asesor esta totalmente ocupado ese dia." : $"Hay {libres.Length} cupos libres.")
        });
    }

    private async Task<AgendaToolResult> BookAsync(JsonElement args, Guid actorUserId, bool autonomous, CancellationToken ct)
    {
        var asesor = Str(args, "asesor");
        var clienteNombre = Str(args, "cliente_nombre");
        if (string.IsNullOrWhiteSpace(asesor)) { return Err("Falta el asesor."); }
        if (string.IsNullOrWhiteSpace(clienteNombre)) { return Err("Falta el nombre del cliente."); }
        if (!TryDate(Str(args, "fecha"), out var date)) { return Err("La fecha es invalida; usa AAAA-MM-DD."); }
        if (!TryTime(Str(args, "hora"), out var start)) { return Err("La hora es invalida; usa HH:mm en formato 24h."); }

        var res = await ResolveResourceAsync(asesor!, ct);
        if (res is null) { return Err($"No encontre el asesor '{asesor}'. Usa listar_asesores."); }

        var (serviceIds, unresolved) = await ResolveServicesAsync(StrArray(args, "servicios"), ct);
        if (serviceIds.Count == 0)
        {
            return Err(unresolved.Count > 0
                ? $"No reconoci estos servicios: {string.Join(", ", unresolved)}. Usa consultar_servicios_precios para ver los nombres exactos."
                : "Debes indicar al menos un servicio.");
        }

        // Doble turno / cadena multi-estacion: cada paso adicional es otro asesor + hora.
        var chainSteps = new List<BookingChainStep>();
        if (args.TryGetProperty("cadena", out var chain) && chain.ValueKind == JsonValueKind.Array)
        {
            foreach (var step in chain.EnumerateArray())
            {
                var stepAsesor = Str(step, "asesor");
                if (string.IsNullOrWhiteSpace(stepAsesor)) { continue; }
                if (!TryTime(Str(step, "hora"), out var stepStart)) { return Err($"La hora del paso de cadena '{stepAsesor}' es invalida (usa HH:mm)."); }
                var stepRes = await ResolveResourceAsync(stepAsesor!, ct);
                if (stepRes is null) { return Err($"No encontre el asesor del segundo turno: '{stepAsesor}'."); }
                chainSteps.Add(new BookingChainStep(stepRes.Id, stepStart));
            }
        }

        var phone = Str(args, "cliente_telefono");
        var notas = Str(args, "notas");
        var hairLength = HairLengthFromString(Str(args, "largo"));
        var doble = chainSteps.Count > 0;

        // Modo sugerencia: no se escribe en la agenda; se registra la solicitud para que un asesor la confirme.
        if (!autonomous)
        {
            var pending = new
            {
                ok = true,
                pendiente = true,
                asesor = res.Name,
                fecha = date.ToString("yyyy-MM-dd"),
                hora = start.ToString("HH\\:mm"),
                cliente = clienteNombre,
                doble_turno = doble,
                mensaje = "Solicitud registrada como PENDIENTE. Un asesor del salon confirmara la cita y avisara al cliente. NO afirmes que ya quedo confirmada."
            };
            return new AgendaToolResult(JsonSerializer.Serialize(pending, JsonOut), BookingCreated: false);
        }

        var request = new BookingRequest(
            AppointmentId: null,
            ResourceId: res.Id,
            Date: date,
            StartTime: start,
            ClientName: clienteNombre!.Trim(),
            ClientPhone: string.IsNullOrWhiteSpace(phone) ? null : phone!.Trim(),
            ClientId: null,
            ServiceIds: serviceIds,
            Status: AppointmentStatus.Scheduled,
            Punctuality: Punctuality.Unknown,
            Notes: string.IsNullOrWhiteSpace(notas) ? null : notas!.Trim(),
            ChainSteps: chainSteps,
            Chat: Array.Empty<BookingChatLine>(),
            RescheduledFromId: null,
            HairLength: hairLength);

        var result = await _agenda.SaveBookingAsync(request, actorUserId, ct);
        if (!result.Success)
        {
            return Err(result.Error ?? "No se pudo reservar la cita.");
        }

        var payload = new
        {
            ok = true,
            cita_id = result.AppointmentId,
            asesor = res.Name,
            fecha = date.ToString("yyyy-MM-dd"),
            hora = start.ToString("HH\\:mm"),
            cliente = clienteNombre,
            doble_turno = doble,
            pasos = doble ? chainSteps.Count + 1 : 1,
            mensaje = doble
                ? "Cita reservada con doble turno (cadena). El cliente pasa por las dos estaciones el mismo dia."
                : "Cita reservada correctamente."
        };
        return new AgendaToolResult(JsonSerializer.Serialize(payload, JsonOut), BookingCreated: true);
    }

    private async Task<AgendaToolResult> ClientAppointmentsAsync(JsonElement args, CancellationToken ct)
    {
        var cliente = Str(args, "cliente");
        if (string.IsNullOrWhiteSpace(cliente)) { return Err("Falta el nombre o telefono del cliente."); }

        var matches = await _clients.ListAsync(cliente, ct);
        if (matches.Count == 0) { return Err($"No encontre clientes con '{cliente}'. Confirma el nombre o el telefono con el cliente."); }

        var today = DateOnly.FromDateTime(_clock.GetUtcNow().ToOffset(SalonOffset).DateTime);
        var resultClients = new List<object>();
        var totalCitas = 0;
        foreach (var m in matches.Take(5))
        {
            var detail = await _clients.GetDetailAsync(m.Id, ct);
            if (detail is null) { continue; }
            // Solo citas vigentes (programadas/confirmadas) de hoy en adelante: las cancelables/reprogramables.
            var citas = detail.History
                .Where(h => h.Date >= today && (h.Status == AppointmentStatus.Scheduled || h.Status == AppointmentStatus.Confirmed))
                .OrderBy(h => h.Date).ThenBy(h => h.StartTime)
                .Select(h => new
                {
                    cita_id = h.AppointmentId,
                    fecha = h.Date.ToString("yyyy-MM-dd"),
                    hora = h.StartTime.ToString("HH\\:mm"),
                    asesor = h.ResourceName,
                    servicios = h.ServicesText,
                    estado = StatusLabel(h.Status)
                })
                .ToArray();
            totalCitas += citas.Length;
            resultClients.Add(new { cliente = m.FullName, telefono = m.Phone, citas });
        }

        return Ok(new
        {
            coincidencias = resultClients,
            mensaje = totalCitas == 0
                ? "El cliente no tiene citas futuras vigentes (programadas o confirmadas) para cancelar o reprogramar."
                : $"Se encontraron {totalCitas} cita(s) vigente(s)."
        });
    }

    private async Task<AgendaToolResult> BookingLinkAsync(CancellationToken ct)
    {
        var s = await _onlineBooking.GetAsync(ct);
        if (!s.Enabled || string.IsNullOrWhiteSpace(s.Link))
        {
            return Ok(new { disponible = false, mensaje = "El salon no tiene reservas online por link activas. Agenda tu la cita por el flujo normal." });
        }
        return Ok(new { disponible = true, link = s.Link, mensaje = "Comparte este link con el cliente para que reserve su cita (asesor, servicio y horario)." });
    }

    private async Task<AgendaToolResult> CancelAsync(JsonElement args, Guid actorUserId, bool autonomous, CancellationToken ct)
    {
        var raw = Str(args, "cita_id");
        if (string.IsNullOrWhiteSpace(raw) || !Guid.TryParse(raw, out var id))
        {
            return Err("Falta un cita_id valido. Primero usa consultar_citas_cliente para obtenerlo.");
        }

        // Modo sugerencia: no cancela; registra la solicitud para que un asesor la confirme.
        if (!autonomous)
        {
            return Ok(new { ok = true, pendiente = true, cita_id = id, mensaje = "Solicitud de cancelacion registrada como PENDIENTE. Un asesor la confirmara. NO afirmes que ya quedo cancelada." });
        }

        var ok = await _agenda.CancelAppointmentAsync(id, actorUserId, ct);
        if (!ok) { return Err("No encontre esa cita (ya no existe o el id es incorrecto)."); }

        return Ok(new { ok = true, cita_id = id, mensaje = "Cita cancelada. Quedo registrada para reprogramacion si el cliente desea otra fecha." });
    }

    // ===== Resolucion nombre|id =====

    private async Task<ResourceDto?> ResolveResourceAsync(string nameOrId, CancellationToken ct)
    {
        var all = await _resources.ListAsync(ct);
        if (Guid.TryParse(nameOrId, out var id))
        {
            var byId = all.FirstOrDefault(r => r.Id == id);
            if (byId is not null) { return byId; }
        }
        var key = Normalize(nameOrId);
        return all.FirstOrDefault(r => Normalize(r.Name) == key)
            ?? all.FirstOrDefault(r => Normalize(r.Name).Contains(key));
    }

    private async Task<(IReadOnlyList<Guid> Ids, IReadOnlyList<string> Unresolved)> ResolveServicesAsync(IReadOnlyList<string> tokens, CancellationToken ct)
    {
        var ids = new List<Guid>();
        var unresolved = new List<string>();
        if (tokens.Count == 0) { return (ids, unresolved); }

        var all = (await _services.ListAsync(false, ct)).ToList();
        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token)) { continue; }
            if (Guid.TryParse(token, out var gid) && all.Any(s => s.Id == gid)) { ids.Add(gid); continue; }
            var key = Normalize(token);
            var match = all.FirstOrDefault(s => Normalize(s.Name) == key)
                ?? all.FirstOrDefault(s => Normalize(s.Name).Contains(key) || key.Contains(Normalize(s.Name)));
            if (match is not null) { ids.Add(match.Id); }
            else { unresolved.Add(token); }
        }
        return (ids.Distinct().ToList(), unresolved);
    }

    // ===== Helpers de JSON / parsing =====

    private static AgendaToolResult Ok(object payload) => new(JsonSerializer.Serialize(payload, JsonOut), BookingCreated: false);
    private static AgendaToolResult Err(string message) => new(JsonSerializer.Serialize(new { ok = false, error = message }, JsonOut), BookingCreated: false);

    private static string? Str(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v)
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() : (v.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False ? v.GetRawText() : null))
            : null;

    private static IReadOnlyList<string> StrArray(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(prop, out var v)) { return Array.Empty<string>(); }
        if (v.ValueKind == JsonValueKind.String) { return new[] { v.GetString() ?? "" }; }
        if (v.ValueKind != JsonValueKind.Array) { return Array.Empty<string>(); }
        return v.EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .ToList();
    }

    private static bool TryDate(string? s, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(s)) { return false; }
        s = s.Trim();
        if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) { return true; }
        return DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static bool TryTime(string? s, out TimeOnly time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(s)) { return false; }
        s = s.Trim();
        string[] formats = { "HH:mm", "H:mm", "HH:mm:ss", "h:mm tt", "h:mmtt", "hh:mm tt" };
        if (TimeOnly.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time)) { return true; }
        return TimeOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }

    private static string KindLabel(ResourceKind k) => k switch
    {
        ResourceKind.Image => "Asesor de imagen",
        ResourceKind.Station => "Estacion / cabina",
        _ => k.ToString()
    };

    // Mapea el largo de cabello en texto (corto/medio/largo/muy largo) al enum del dominio.
    private static HairLength? HairLengthFromString(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) { return null; }
        var k = Normalize(s);
        return k switch
        {
            "corto" => HairLength.Corto,
            "medio" or "mediano" => HairLength.Medio,
            "largo" => HairLength.Largo,
            "muy largo" or "muylargo" or "extra largo" or "extralargo" => HairLength.MuyLargo,
            _ => Enum.TryParse<HairLength>(s, true, out var e) ? e : null
        };
    }

    private static string StatusLabel(AppointmentStatus s) => s switch
    {
        AppointmentStatus.Scheduled => "Programada",
        AppointmentStatus.Confirmed => "Confirmada",
        AppointmentStatus.Completed => "Completada",
        AppointmentStatus.NoShow => "No-show",
        AppointmentStatus.Cancelled => "Cancelada",
        AppointmentStatus.Rescheduled => "Reprogramada",
        _ => s.ToString()
    };

    // Normaliza para comparar: minusculas y sin acentos.
    private static string Normalize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) { return string.Empty; }
        var n = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in n)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) { sb.Append(c); }
        }
        return sb.ToString();
    }
}
