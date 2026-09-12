using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Forms;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Herramienta (function calling / "MCP") de ACTIVIDADES POR CONCEPTO (ADR-0098, Opcion A): el agente crea
/// una ACTIVIDAD de un CONCEPTO (ActividadSubcategoria) -que ya trae tablero/columna/formulario/flujo- y de
/// paso LLENA y ENVIA el formulario del concepto con los datos de la charla. NO crea contacto en el
/// Directorio (Tercero): solo la actividad + su formulario.
///
/// Es un toolset SEPARADO de TasksToolset (crear_tarea): coexisten y cada agente elige el suyo por
/// disabled_tools_json. NO modifica crear_tarea. Reusa ITaskItemService (misma alta que el wizard, via
/// SubcategoriaId) e IFormResponseService (crear + enviar la respuesta del concepto, que si el nodo del
/// flujo tiene FormFlowLink pendiente completa el paso en la misma transaccion del servicio).
/// </summary>
public interface IActividadesToolset : IAgentToolset { }

public sealed class ActividadesToolset : IActividadesToolset
{
    private readonly IApplicationDbContext _db;
    private readonly ITaskItemService _tasks;
    private readonly IFormResponseService _forms;
    private readonly IFormDefinitionService _formDefs;
    private readonly ITenantContext _tenant;

    public ActividadesToolset(IApplicationDbContext db, ITaskItemService tasks, IFormResponseService forms,
        IFormDefinitionService formDefs, ITenantContext tenant)
    {
        _db = db;
        _tasks = tasks;
        _forms = forms;
        _formDefs = formDefs;
        _tenant = tenant;
    }

    public string GroupKey => "actividades";
    public string GroupLabel => "Actividades por concepto";

    private const string ActorName = "Agente IA";

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions OptionsJson = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<AiToolSpec> GetSpecs() => new[]
    {
        new AiToolSpec(
            "ver_formulario_concepto",
            "Describe el FORMULARIO de un CONCEPTO (tipo de actividad) del tenant: sus campos (field_code, " +
            "label, tipo, requerido) y las OPCIONES validas de los campos de seleccion. Usala ANTES de " +
            "crear_actividad para saber QUE datos capturar y con que valores validos llenarlos.",
            """{"type":"object","properties":{"concepto":{"type":"string","description":"Nombre o codigo del concepto / tipo de actividad"}},"required":["concepto"],"additionalProperties":false}"""),
        new AiToolSpec(
            "crear_actividad",
            "Crea (CIERRA) una ACTIVIDAD del CONCEPTO indicado y LLENA+ENVIA su formulario con los datos " +
            "capturados en la conversacion. La actividad hereda tablero/columna/flujo del concepto. NO crea " +
            "contacto en el directorio. 'datos' es un objeto { field_code: valor } (usa ver_formulario_concepto " +
            "para conocer los field_code y, en los campos de seleccion, las opciones validas). Los archivos que " +
            "el cliente haya enviado en la conversacion se adjuntan AUTOMATICAMENTE. Devuelve un 'ticket' " +
            "(numero de la actividad) que DEBES entregarle al cliente como comprobante.",
            """{"type":"object","properties":{"concepto":{"type":"string","description":"Nombre o codigo del concepto / tipo de actividad"},"titulo":{"type":"string","description":"Titulo corto de la actividad (opcional; si falta se genera uno)"},"datos":{"type":"object","description":"Valores del formulario por field_code (ver ver_formulario_concepto). En campos de seleccion usa una opcion valida.","additionalProperties":true}},"required":["concepto","datos"],"additionalProperties":false}"""),
    };

    public async Task<AgentToolResult> ExecuteAsync(string toolName, string argumentsJson, Guid actorUserId, bool autonomous, CancellationToken cancellationToken = default)
    {
        JsonElement args;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            args = doc.RootElement.Clone();
        }
        catch { return Err("Los argumentos no son un JSON valido."); }

        try
        {
            return toolName switch
            {
                "ver_formulario_concepto" => await VerFormularioAsync(args, cancellationToken),
                "crear_actividad" => await CrearActividadAsync(args, actorUserId, cancellationToken),
                _ => Err($"Herramienta desconocida: {toolName}")
            };
        }
        catch (Exception ex)
        {
            return Err($"Error ejecutando '{toolName}': {ex.Message}");
        }
    }

    // ===== ver_formulario_concepto =====

    private async Task<AgentToolResult> VerFormularioAsync(JsonElement args, CancellationToken ct)
    {
        var concepto = Str(args, "concepto");
        var sub = await ResolveConceptoAsync(concepto, ct);
        if (sub is null) { return await ConceptoNoExisteAsync(concepto, ct); }
        if (sub.FormDefinitionId is not Guid defId)
        {
            return Err($"El concepto '{sub.Nombre}' no tiene un formulario asociado.");
        }

        var def = await _formDefs.GetAsync(defId, ct);
        if (def is null) { return Err($"El formulario del concepto '{sub.Nombre}' no se pudo cargar."); }

        var campos = def.Questions
            .Where(q => !IsNonInput(q.ControlType))
            .OrderBy(q => q.SortOrder)
            .Select(q => new
            {
                field_code = q.FieldCode,
                label = q.Label,
                tipo = q.ControlType.ToString(),
                requerido = q.Required,
                opciones = HasOptions(q.ControlType)
                    ? ParseOptions(q.OptionsJson).Select(o => new { id = o.Value ?? o.Id, o.Label }).ToList()
                    : null
            })
            .ToList();

        return Ok(new
        {
            ok = true,
            concepto = sub.Nombre,
            codigo = sub.Codigo,
            formulario = def.Title,
            campos
        });
    }

    // ===== crear_actividad =====

    private async Task<AgentToolResult> CrearActividadAsync(JsonElement args, Guid actor, CancellationToken ct)
    {
        var concepto = Str(args, "concepto");
        var sub = await ResolveConceptoAsync(concepto, ct);
        if (sub is null) { return await ConceptoNoExisteAsync(concepto, ct); }
        if (sub.FormDefinitionId is not Guid defId)
        {
            return Err($"El concepto '{sub.Nombre}' no tiene un formulario asociado; no se puede crear la actividad por esta via.");
        }

        var def = await _formDefs.GetAsync(defId, ct);
        if (def is null) { return Err($"El formulario del concepto '{sub.Nombre}' no se pudo cargar."); }

        // datos { field_code: valor } -> mapa string. Valores escalares a texto; arreglos/objetos como JSON.
        var datos = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("datos", out var datosEl)
            && datosEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in datosEl.EnumerateObject()) { datos[p.Name] = ValueToString(p.Value); }
        }

        // PRE-VALIDACION contra la definicion (evita crear una actividad huerfana): requeridos presentes y
        // valores de seleccion (Radio/Select) que sean una opcion valida. SaveAsync es la autoridad final.
        var errores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var q in def.Questions.Where(q => !IsNonInput(q.ControlType)))
        {
            datos.TryGetValue(q.FieldCode, out var val);
            var vacio = string.IsNullOrWhiteSpace(val);
            if (q.Required && vacio) { errores[q.FieldCode] = $"'{q.Label}' es obligatorio."; continue; }
            if (!vacio && IsSingleChoice(q.ControlType))
            {
                var opts = ParseOptions(q.OptionsJson);
                if (opts.Count > 0 && !OptionMatches(opts, val!))
                {
                    var validas = string.Join(", ", opts.Select(o => o.Value ?? o.Id));
                    errores[q.FieldCode] = $"'{val}' no es una opcion valida de '{q.Label}'. Opciones: {validas}.";
                }
            }
        }
        if (errores.Count > 0)
        {
            // No se creo nada: el modelo debe reintentar con los datos corregidos.
            return new AgentToolResult(JsonSerializer.Serialize(new
            {
                ok = false,
                error = "Los datos del formulario no son validos. Corrige los campos indicados y reintenta.",
                campos = errores
            }, JsonOut), SessionCompleted: false);
        }

        // Datos del solicitante (para el RESUMEN de la actividad): heuristica por field_code. El detalle real
        // queda en la respuesta del formulario; esto solo alimenta Contacto/Telefono/Email de la tarea.
        var clienteNombre = FindByKey(datos, "nombre", "contacto", "cliente");
        var clienteTelefono = FindByKey(datos, "movil", "whatsapp", "telefono", "celular", "phone");
        var clienteEmail = FindByKey(datos, "correo", "email", "mail");

        // Respaldo del telefono con el de la conversacion en curso (en WhatsApp el cliente no lo dicta).
        if (clienteTelefono is null && AiToolRunContext.ConversationId is Guid convId)
        {
            var convPhone = await _db.Conversations.AsNoTracking()
                .Where(c => c.Id == convId).Select(c => c.ContactPhone).FirstOrDefaultAsync(ct);
            clienteTelefono = string.IsNullOrWhiteSpace(convPhone) ? null : convPhone!.Trim();
        }

        var titulo = Str(args, "titulo");
        if (string.IsNullOrWhiteSpace(titulo)) { titulo = clienteNombre ?? sub.Nombre; }

        // 1) Alta de la actividad tipada por el concepto (BoardId null -> hereda tablero/columna/flujo).
        var req = new CreateTaskItemRequest(
            Title: titulo!.Trim(),
            ActivityTypeId: null,
            SubcategoriaId: sub.Id,
            BoardId: null,
            RequesterName: clienteNombre,
            RequesterEmail: clienteEmail,
            RequesterPhone: clienteTelefono);

        var created = await _tasks.CreateAsync(req, actor, ActorName, ct);
        if (!created.IsOk || created.Value is null) { return Err(created.Error ?? "No se pudo crear la actividad."); }
        var taskId = created.Value.Item.Id;
        var ticket = created.Value.Item.Number;

        // 2) Crear la respuesta del formulario del concepto para la actividad.
        var form = await _forms.CreateTaskConceptFormAsync(taskId, ct);
        if (!form.IsOk || form.Value is null)
        {
            await SafeArchiveAsync(taskId, actor, ct); // no dejar la actividad a medias
            return Err(form.Error ?? "No se pudo crear el formulario de la actividad.");
        }
        var responseId = form.Value.ResponseId;

        // 3) Llenar + ENVIAR el formulario con los valores (Type = tipo del campo de la definicion).
        var typeByCode = def.Questions.ToDictionary(q => q.FieldCode, q => q.ControlType.ToString(), StringComparer.OrdinalIgnoreCase);
        var data = new Dictionary<string, FormFieldValue>(StringComparer.Ordinal);
        foreach (var kv in datos)
        {
            if (kv.Value is null) { continue; }
            var type = typeByCode.TryGetValue(kv.Key, out var t) ? t : "Text";
            data[kv.Key] = new FormFieldValue(kv.Value, type);
        }

        var saved = await _forms.SaveAsync(responseId, data, submit: true,
            submittedByTenantUserId: null, executedByAiAgentId: AiToolRunContext.AgentId, cancellationToken: ct);
        if (!saved.IsOk)
        {
            // Caso borde (una regla que la pre-validacion no cubrio): archivar la actividad para no dejarla
            // huerfana y devolver los errores por campo para que el modelo reintente.
            await SafeArchiveAsync(taskId, actor, ct);
            return new AgentToolResult(JsonSerializer.Serialize(new
            {
                ok = false,
                error = saved.Error ?? "El formulario no paso la validacion.",
                campos = saved.FieldErrors ?? new Dictionary<string, string>()
            }, JsonOut), SessionCompleted: false);
        }

        // 4) Adjuntar automaticamente la media entrante de la conversacion.
        var adjuntados = await AttachConversationMediaAsync(taskId, ct);

        // Nombre del tablero del concepto (best-effort, para el mensaje).
        string? tablero = null;
        if (sub.TaskBoardId is Guid boardId)
        {
            tablero = await _db.TaskBoards.AsNoTracking().Where(b => b.Id == boardId).Select(b => b.Name).FirstOrDefaultAsync(ct);
        }

        return new AgentToolResult(JsonSerializer.Serialize(new
        {
            ok = true,
            ticket,
            tarea_id = taskId,
            concepto = sub.Nombre,
            tablero,
            adjuntos = adjuntados,
            mensaje = $"Actividad creada con ticket {ticket} ({sub.Nombre})"
                + (tablero is not null ? $" en el tablero '{tablero}'" : "")
                + (adjuntados > 0 ? $" con {adjuntados} archivo(s) adjunto(s)." : ".")
        }, JsonOut), SessionCompleted: true);
    }

    // ===== Helpers =====

    private async Task<ActividadSubcategoria?> ResolveConceptoAsync(string? input, CancellationToken ct)
    {
        var t = input?.Trim();
        if (string.IsNullOrWhiteSpace(t)) { return null; }
        var lower = t.ToLower();
        return await _db.ActividadSubcategorias.AsNoTracking()
            .Where(s => !s.IsArchived)
            .FirstOrDefaultAsync(s => s.Codigo.ToLower() == lower || s.Nombre.ToLower() == lower, ct);
    }

    private async Task<AgentToolResult> ConceptoNoExisteAsync(string? concepto, CancellationToken ct)
    {
        var nombres = await _db.ActividadSubcategorias.AsNoTracking()
            .Where(s => !s.IsArchived).OrderBy(s => s.Nombre).Select(s => s.Nombre).Take(50).ToListAsync(ct);
        return Err($"No existe un concepto llamado '{concepto}'. Conceptos disponibles: {string.Join(", ", nombres)}.");
    }

    /// <summary>Adjunta a la actividad los archivos entrantes de la conversacion (reusa la URL almacenada).</summary>
    private async Task<int> AttachConversationMediaAsync(Guid taskId, CancellationToken ct)
    {
        if (AiToolRunContext.ConversationId is not Guid convId) { return 0; }
        if (_tenant.TenantId is not Guid tenantId) { return 0; }

        var media = await _db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == convId
                && m.Direction == MessageDirection.Inbound
                && m.MediaType != MessageMediaType.None
                && m.MediaUrl != null)
            .OrderBy(m => m.Id)
            .Select(m => new { m.MediaUrl, m.MediaMimeType })
            .ToListAsync(ct);
        if (media.Count == 0) { return 0; }

        foreach (var m in media)
        {
            _db.TaskItemAttachments.Add(new TaskItemAttachment
            {
                TenantId = tenantId,
                TaskItemId = taskId,
                FileName = FileNameFromUrl(m.MediaUrl!),
                Url = m.MediaUrl!,
                MimeType = m.MediaMimeType,
                SizeBytes = 0,
                UploadedByName = ActorName
            });
        }
        await _db.SaveChangesAsync(ct);
        return media.Count;
    }

    private async Task SafeArchiveAsync(Guid taskId, Guid actor, CancellationToken ct)
    {
        try { await _tasks.ArchiveAsync(taskId, actor, ActorName, ct); } catch { /* limpieza best-effort */ }
    }

    private static string? FindByKey(Dictionary<string, string?> datos, params string[] needles)
    {
        foreach (var kv in datos)
        {
            if (string.IsNullOrWhiteSpace(kv.Value)) { continue; }
            if (needles.Any(n => kv.Key.Contains(n, StringComparison.OrdinalIgnoreCase))) { return kv.Value!.Trim(); }
        }
        return null;
    }

    private static List<FormOption> ParseOptions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return new(); }
        try { return JsonSerializer.Deserialize<List<FormOption>>(json, OptionsJson) ?? new(); }
        catch { return new(); }
    }

    private static bool OptionMatches(List<FormOption> opts, string value)
    {
        var v = value.Trim();
        return opts.Any(o =>
            string.Equals(o.Value, v, StringComparison.OrdinalIgnoreCase)
            || string.Equals(o.Id, v, StringComparison.OrdinalIgnoreCase)
            || string.Equals(o.Label, v, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ValueToString(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => v.GetRawText()
    };

    private static string FileNameFromUrl(string url)
    {
        var clean = url.Split('?', '#')[0].TrimEnd('/');
        var name = clean[(clean.LastIndexOf('/') + 1)..];
        return string.IsNullOrWhiteSpace(name) ? "adjunto" : name;
    }

    private static bool HasOptions(FormControlType t) => t is FormControlType.Select or FormControlType.MultiCheck or FormControlType.Radio;
    private static bool IsSingleChoice(FormControlType t) => t is FormControlType.Select or FormControlType.Radio;

    // Controles que NO capturan valor (estructura/visuales): no se validan ni se envian como dato.
    private static bool IsNonInput(FormControlType t) => t is FormControlType.Heading or FormControlType.Literal
        or FormControlType.Divider or FormControlType.Spacer or FormControlType.Paragraph or FormControlType.Html
        or FormControlType.Button or FormControlType.Chart or FormControlType.Subform or FormControlType.Image
        or FormControlType.Photo;

    private static AgentToolResult Ok(object payload) => new(JsonSerializer.Serialize(payload, JsonOut), SessionCompleted: false);
    private static AgentToolResult Err(string message) => new(JsonSerializer.Serialize(new { ok = false, error = message }, JsonOut), SessionCompleted: false);

    private static string? Str(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
