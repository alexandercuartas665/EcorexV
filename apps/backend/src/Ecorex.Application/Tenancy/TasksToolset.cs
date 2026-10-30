using System.Globalization;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Herramienta (function calling / "MCP") de TAREAS: permite al agente de IA CREAR una tarea en un
/// tablero del tenant y adjuntarle automaticamente los archivos que el cliente envio en la
/// conversacion. Es la accion de CIERRE del agente (SessionCompleted): carga la solicitud en el
/// modulo de Tareas para que un humano la atienda. Reusa <see cref="ITaskItemService"/> (misma alta
/// que el wizard) y el aislamiento por tenant del filtro global.
/// </summary>
public interface ITasksToolset : IAgentToolset { }

public sealed class TasksToolset : ITasksToolset
{
    private readonly IApplicationDbContext _db;
    private readonly ITaskItemService _tasks;
    private readonly ITenantContext _tenant;
    private readonly Forms.IQuoteDocumentRenderer _quoteDoc;
    private readonly Notifications.INotificationChannelSender _sender;
    private readonly IAgentAvisoTareaService _aviso;

    public TasksToolset(
        IApplicationDbContext db, ITaskItemService tasks, ITenantContext tenant,
        Forms.IQuoteDocumentRenderer quoteDoc, Notifications.INotificationChannelSender sender,
        IAgentAvisoTareaService aviso)
    {
        _db = db;
        _tasks = tasks;
        _tenant = tenant;
        _quoteDoc = quoteDoc;
        _sender = sender;
        _aviso = aviso;
    }

    public string GroupKey => "tareas";
    public string GroupLabel => "Tareas y tableros";

    private const string ActorName = "Agente IA";

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public IReadOnlyList<AiToolSpec> GetSpecs() => new[]
    {
        new AiToolSpec(
            "listar_tableros",
            "Lista los tableros de tareas disponibles del tenant (nombre y para que sirven). Usala ANTES de " +
            "crear una tarea si no sabes el nombre exacto del tablero destino.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new AiToolSpec(
            "crear_tarea",
            "Crea (CIERRA) una tarea en un tablero del modulo de Tareas para que un humano la atienda. Usala al " +
            "final, cuando ya tengas claro que necesita el cliente. Indica 'tablero' con el nombre EXACTO de un " +
            "tablero (ver listar_tableros), un 'titulo' corto y una 'descripcion' con el detalle. Los archivos/" +
            "imagenes que el cliente haya enviado en la conversacion se adjuntan AUTOMATICAMENTE a la tarea. " +
            "Incluye SIEMPRE los datos del cliente que ya tienes (cliente_nombre, cliente_telefono, cliente_email, " +
            "cliente_identificacion) para que la tarea quede ligada al contacto y el asesor lo pueda contactar. " +
            "Devuelve un 'ticket' (numero de la solicitud) que DEBES entregarle al cliente como comprobante.",
            """{"type":"object","properties":{"tablero":{"type":"string","description":"Nombre exacto del tablero destino (ver listar_tableros)"},"titulo":{"type":"string","description":"Titulo corto de la tarea"},"descripcion":{"type":"string","description":"Detalle de lo que necesita el cliente"},"prioridad":{"type":"string","enum":["baja","media","alta","urgente"],"description":"Prioridad (opcional, por defecto media)"},"vence":{"type":"string","description":"Fecha limite ISO 8601 opcional (ej. 2026-08-10)"},"cliente_nombre":{"type":"string","description":"Nombre del cliente/contacto que solicita (opcional pero recomendado)"},"cliente_telefono":{"type":"string","description":"Telefono del cliente (opcional pero recomendado)"},"cliente_email":{"type":"string","description":"Email del cliente (opcional)"},"cliente_identificacion":{"type":"string","description":"Identificacion/NIT del cliente (opcional)"}},"required":["tablero","titulo"],"additionalProperties":false}"""),
        new AiToolSpec(
            "consultar_tarea",
            "Consulta (SOLO LECTURA) los datos de una tarea/caso EXISTENTE: estado, tablero, solicitante, archivos " +
            "adjuntos (como la cotizacion que ya se envio) y las ultimas anotaciones de la bitacora. Usala cuando el " +
            "cliente pregunte por el estado de su solicitud, por su cotizacion ya enviada, o por el detalle de un caso. " +
            "Indica 'numero' (ej. T00303) si lo conoces (suele venir en las notas internas del flujo); si lo omites, se " +
            "consulta la tarea ligada a esta conversacion. NO crea ni modifica nada.",
            """{"type":"object","properties":{"numero":{"type":"string","description":"Numero de la tarea/caso, ej. T00303 (opcional; si se omite se usa la tarea ligada a la conversacion actual)"}},"additionalProperties":false}"""),
        new AiToolSpec(
            "reenviar_documento",
            "REENVIA al cliente, por WhatsApp, el PDF de un documento YA generado del caso (tipicamente la " +
            "COTIZACION). Usala cuando el cliente pida de nuevo la cotizacion o un documento del caso (ej. 'no me " +
            "llego', 'se me borro', 'mandamela otra vez'): en ese caso NO crees una tarea, REENVIA el documento con " +
            "esta herramienta. Indica 'numero' del caso si lo conoces (ej. T00303); si lo omites, se usa el caso " +
            "ligado a esta conversacion. 'documento' es opcional (nombre o codigo del formulario; por defecto se " +
            "reenvia la cotizacion del caso). Entrega el documento OFICIAL ya generado: no transcribas ni menciones " +
            "valores en el chat. Solo entrega si la ventana de conversacion esta abierta (el cliente escribio hace poco).",
            """{"type":"object","properties":{"numero":{"type":"string","description":"Numero del caso, ej. T00303 (opcional; por defecto el ligado a la conversacion)"},"documento":{"type":"string","description":"Nombre o codigo del formulario a reenviar (opcional; por defecto la cotizacion)"},"mensaje":{"type":"string","description":"Texto breve que acompana el documento (opcional)"}},"additionalProperties":false}"""),
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
                "listar_tableros" => await ListBoardsAsync(cancellationToken),
                "crear_tarea" => await CreateTaskAsync(args, actorUserId, cancellationToken),
                "consultar_tarea" => await ConsultTaskAsync(args, cancellationToken),
                "reenviar_documento" => await ResendDocumentAsync(args, cancellationToken),
                _ => Err($"Herramienta desconocida: {toolName}")
            };
        }
        catch (Exception ex)
        {
            return Err($"Error ejecutando '{toolName}': {ex.Message}");
        }
    }

    private async Task<AgentToolResult> ListBoardsAsync(CancellationToken ct)
    {
        // Whitelist dura del agente (null/vacio = sin restriccion = todos los tableros del tenant).
        var allowed = AllowedBoards();
        var q = _db.TaskBoards.AsNoTracking().Where(b => !b.IsArchived);
        if (allowed is not null) { q = q.Where(b => allowed.Contains(b.Id)); }
        var boards = await q
            .OrderBy(b => b.Name)
            .Select(b => new { b.Name, b.Description })
            .ToListAsync(ct);
        return Ok(new { ok = true, tableros = boards.Select(b => new { nombre = b.Name, descripcion = b.Description }) });
    }

    /// <summary>Whitelist de tableros permitidos para este agente (del contexto ambiental). Devuelve null si
    /// no hay restriccion (lista vacia o null) para preservar el comportamiento de "todos los tableros".</summary>
    private static List<Guid>? AllowedBoards()
    {
        var allowed = AiToolRunContext.AllowedBoardIds;
        return allowed is { Count: > 0 } ? allowed.ToList() : null;
    }

    private async Task<AgentToolResult> CreateTaskAsync(JsonElement args, Guid actor, CancellationToken ct)
    {
        // Capa 1 (ADR-0101): si crear_tarea ya cerro OK en ESTE turno, devuelve el mismo ticket sin duplicar.
        var priorTurn = AgentTaskIdempotency.TryGetTurnResult("crear_tarea");
        if (priorTurn is not null) { return new AgentToolResult(priorTurn, SessionCompleted: true); }

        // Candado de cierre-por-flujo: si un flujo ya cerro este caso (conversacion marcada), SARA NO crea una
        // tarea (el flujo es el dueno; evita duplicados aunque el modelo lo pida). NO-OP, sin cerrar sesion.
        if (AiToolRunContext.ConversationId is Guid flowConvId
            && await AgentTaskIdempotency.IsFlowHandledCloseAsync(_db, flowConvId, DateTimeOffset.UtcNow, ct))
        {
            return Ok(new { ok = true, idempotente = true, mensaje = "El caso ya fue cerrado y gestionado por un flujo; no se crea una tarea nueva." });
        }

        var tableroNombre = Str(args, "tablero");
        var titulo = Str(args, "titulo");
        if (string.IsNullOrWhiteSpace(titulo)) { return Err("Falta el titulo de la tarea (titulo)."); }

        // Whitelist dura del agente (null = sin restriccion). Con restriccion, el nombre se resuelve SOLO
        // entre los tableros permitidos: un nombre que calce con un tablero fuera de la whitelist NO vale.
        var allowed = AllowedBoards();

        TaskBoard? board = null;
        if (!string.IsNullOrWhiteSpace(tableroNombre))
        {
            var q = _db.TaskBoards.AsNoTracking().Where(b => !b.IsArchived);
            if (allowed is not null) { q = q.Where(b => allowed.Contains(b.Id)); }
            board = await q.FirstOrDefaultAsync(b => b.Name.ToLower() == tableroNombre!.Trim().ToLower(), ct);
        }

        // Comodidad: con EXACTAMENTE 1 tablero permitido, si no vino nombre o no calzo, se usa ese por defecto.
        if (board is null && allowed is { Count: 1 })
        {
            board = await _db.TaskBoards.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == allowed[0] && !b.IsArchived, ct);
        }

        if (board is null)
        {
            // Nombres a sugerir: SOLO los permitidos si hay whitelist; si no, todos los del tenant.
            var nombresQ = _db.TaskBoards.AsNoTracking().Where(b => !b.IsArchived);
            if (allowed is not null) { nombresQ = nombresQ.Where(b => allowed.Contains(b.Id)); }
            var nombres = await nombresQ.OrderBy(b => b.Name).Select(b => b.Name).ToListAsync(ct);
            if (string.IsNullOrWhiteSpace(tableroNombre))
            {
                return Err($"Falta el nombre del tablero (tablero). Tableros disponibles: {string.Join(", ", nombres)}.");
            }
            var restringido = allowed is not null ? " permitido" : "";
            return Err($"No existe un tablero{restringido} llamado '{tableroNombre}'. Tableros disponibles: {string.Join(", ", nombres)}.");
        }

        // Tipo de actividad por defecto (el mas basico del tenant): CreateAsync exige tipo o concepto.
        // Si el tenant aun no tiene ninguno, se auto-provisiona uno "General" para no bloquear el alta.
        var tipoId = await _db.ActivityTypes.AsNoTracking()
            .Where(t => !t.IsArchived).OrderBy(t => t.Name).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        if (tipoId is null)
        {
            if (_tenant.TenantId is not Guid tid) { return Err("Sin tenant activo."); }
            var tipo = new ActivityType { TenantId = tid, Category = "General", Name = "Solicitud" };
            _db.ActivityTypes.Add(tipo);
            await _db.SaveChangesAsync(ct);
            tipoId = tipo.Id;
        }

        var descripcion = Str(args, "descripcion");
        var prioridad = ParsePriority(Str(args, "prioridad"));
        var vence = ParseDate(Str(args, "vence"));

        // Datos del contacto que el agente ya conoce: se guardan como solicitante de la tarea para que el
        // RESUMEN muestre Contacto/Telefono/Email/Identificacion y el asesor pueda contactar al cliente.
        static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s!.Trim();
        var clienteNombre = Clean(Str(args, "cliente_nombre"));
        var clienteTelefono = Clean(Str(args, "cliente_telefono"));
        var clienteEmail = Clean(Str(args, "cliente_email"));
        var clienteIdentificacion = Clean(Str(args, "cliente_identificacion"));

        // Telefono REAL (ADR-0101 rev.2): gana el ContactPhone de la conversacion en curso; cliente_telefono
        // solo se usa como RESPALDO cuando NO hay conversacion (el modelo suele ALUCINAR el numero dictado).
        var conversationId = AiToolRunContext.ConversationId;
        if (conversationId is Guid convPhoneId)
        {
            var convPhone = await _db.Conversations.AsNoTracking()
                .Where(c => c.Id == convPhoneId)
                .Select(c => c.ContactPhone)
                .FirstOrDefaultAsync(ct);
            clienteTelefono = Clean(convPhone) ?? clienteTelefono;
        }

        // Capa 2 (ADR-0101 rev.3): dedup por CONVERSACION + TABLERO, SIN ventana de tiempo. Si esta
        // conversacion ya tiene una tarea NO archivada en ESTE tablero, se devuelve ese ticket (aunque cambien
        // telefono/resumen o pasen horas). Se comprueba ANTES de repartir asesor para no avanzar el round-robin
        // en un re-cierre. Otro tablero -> tarea nueva. Sin conversacion -> no aplica.
        if (conversationId is Guid convDupId)
        {
            var dup = await AgentTaskIdempotency.FindByConversationAndBoardAsync(_db, convDupId, board.Id, ct);
            if (dup is { } ex)
            {
                var dupJson = JsonSerializer.Serialize(new
                {
                    ok = true,
                    ticket = ex.Number,
                    tarea_id = ex.Id,
                    idempotente = true,
                    mensaje = $"Ya existe la solicitud con ticket {ex.Number} (no se duplico)."
                }, JsonOut);
                AgentTaskIdempotency.RememberTurnResult("crear_tarea", dupJson);
                return new AgentToolResult(dupJson, SessionCompleted: true);
            }
        }

        // Reparto round-robin: el sistema asigna la tarea al SIGUIENTE asesor MARCADO como asignable
        // (con usuario vinculado). No todos los asesores entran: solo los que tienen la marca.
        var asignado = await PickNextAssigneeAsync(ct);

        // Concepto "Fijo por agente": si el agente tiene un concepto por defecto y valida (mismo tenant via filtro
        // global + no archivado), se adjunta como SubcategoriaId para que CreateAsync siembre el checklist del
        // concepto (ActividadSubcategoria.Chequeo). Null o no valido = comportamiento actual (sin concepto). El
        // tablero sigue siendo el que eligio el agente (BoardId explicito gana sobre el del concepto).
        Guid? defaultSubcatId = null;
        if (AiToolRunContext.AgentId is Guid agentCtxId)
        {
            var defSub = await _db.AiAgents.AsNoTracking()
                .Where(a => a.Id == agentCtxId).Select(a => a.DefaultSubcategoriaId).FirstOrDefaultAsync(ct);
            if (defSub is Guid subId
                && await _db.ActividadSubcategorias.AsNoTracking().AnyAsync(s => s.Id == subId && !s.IsArchived, ct))
            {
                defaultSubcatId = subId;
            }
        }

        var req = new CreateTaskItemRequest(
            Title: titulo!.Trim(),
            ActivityTypeId: tipoId,
            Description: string.IsNullOrWhiteSpace(descripcion) ? null : descripcion!.Trim(),
            Priority: prioridad,
            DueDate: vence,
            BoardId: board.Id,
            AssigneeTenantUserId: asignado?.UserId,
            RequesterName: clienteNombre,
            RequesterEmail: clienteEmail,
            RequesterPhone: clienteTelefono,
            RequesterDocument: clienteIdentificacion,
            SubcategoriaId: defaultSubcatId,
            ConversationId: conversationId);

        var res = await _tasks.CreateAsync(req, actor, ActorName, ct);
        if (!res.IsOk || res.Value is null)
        {
            return Err(res.Error ?? "No se pudo crear la tarea.");
        }
        var taskId = res.Value.Item.Id;
        var ticket = res.Value.Item.Number;   // numero legible de la tarea = ticket para el cliente

        // Auto-adjuntar los archivos que el cliente ENVIO: media entrante de la conversacion + adjuntos
        // pendientes del contexto (los que sube la herramienta de pruebas del agente).
        var adjuntados = await AttachConversationMediaAsync(taskId, ct);
        adjuntados += await AttachPendingAsync(taskId, ct);

        // Aviso de "tarea asignada" al usuario ASIGNADO (ADR-0140): best-effort, solo en la creacion REAL
        // (los caminos idempotentes de arriba ya devolvieron). Un fallo de envio no rompe la respuesta.
        if (AiToolRunContext.AgentId is Guid avisoAgentId)
        {
            await _aviso.NotifyTaskAssignedAsync(avisoAgentId, taskId, conversationId, actor, ct);
        }

        var okJson = JsonSerializer.Serialize(new
        {
            ok = true,
            ticket,
            tarea_id = taskId,
            tablero = board.Name,
            asignado_a = asignado?.Nombre,
            adjuntos = adjuntados,
            mensaje = $"Tarea creada con ticket {ticket} en el tablero '{board.Name}'"
                + (asignado is not null ? $", asignada a {asignado.Nombre}" : "")
                + (adjuntados > 0 ? $" con {adjuntados} archivo(s) adjunto(s)." : ".")
        }, JsonOut);
        // Capa 1: recuerda el cierre para que una 2a llamada de crear_tarea en este turno reuse el ticket.
        AgentTaskIdempotency.RememberTurnResult("crear_tarea", okJson);
        return new AgentToolResult(okJson, SessionCompleted: true);
    }

    /// <summary>
    /// SOLO LECTURA: consulta una tarea existente por numero (ej. T00303) o, si no se da, la ligada a la
    /// conversacion en curso. Devuelve estado, tablero, solicitante, adjuntos (ej. la cotizacion enviada) y
    /// las ultimas anotaciones de la bitacora, para que el agente responda al cliente sin crear nada. El
    /// aislamiento por tenant lo garantiza el filtro global; no se aplica la whitelist de tableros (esa rige
    /// DONDE se crean tareas, no que puede LEER el agente del caso que el cliente consulta).
    /// </summary>
    private async Task<AgentToolResult> ConsultTaskAsync(JsonElement args, CancellationToken ct)
    {
        var numero = Str(args, "numero");

        TaskItem? task = null;
        if (!string.IsNullOrWhiteSpace(numero))
        {
            var num = numero!.Trim();
            task = await _db.TaskItems.AsNoTracking().FirstOrDefaultAsync(t => t.Number == num, ct);
        }
        else if (AiToolRunContext.ConversationId is Guid convId)
        {
            task = await _db.TaskItems.AsNoTracking()
                .Where(t => t.ConversationId == convId)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync(ct);
        }

        if (task is null)
        {
            return Err(string.IsNullOrWhiteSpace(numero)
                ? "No hay una tarea ligada a esta conversacion. Pide al cliente el numero de caso (ej. T00303) para consultarla."
                : $"No se encontro la tarea '{numero}'.");
        }

        var tablero = task.BoardId is Guid boardId
            ? await _db.TaskBoards.AsNoTracking().Where(b => b.Id == boardId).Select(b => b.Name).FirstOrDefaultAsync(ct)
            : null;

        var adjuntos = await _db.TaskItemAttachments.AsNoTracking()
            .Where(a => a.TaskItemId == task.Id)
            .OrderBy(a => a.Id)
            .Select(a => a.FileName)
            .Take(20)
            .ToListAsync(ct);

        var bitacoraRows = await _db.TaskItemActivities.AsNoTracking()
            .Where(a => a.TaskItemId == task.Id)
            .OrderByDescending(a => a.CreatedAt)
            .Take(10)
            .Select(a => new { a.CreatedAt, a.ActorName, a.Text })
            .ToListAsync(ct);

        var payload = new
        {
            ok = true,
            numero = task.Number,
            titulo = task.Title,
            estado = task.Status.ToString(),
            tablero,
            prioridad = task.Priority.ToString(),
            vence = task.DueDate,
            creada = task.CreatedAt,
            solicitante = new { nombre = task.RequesterName, telefono = task.RequesterPhone, email = task.RequesterEmail },
            adjuntos,
            bitacora = bitacoraRows
                .OrderBy(b => b.CreatedAt)
                .Select(b => new { fecha = b.CreatedAt, actor = b.ActorName, texto = b.Text })
        };
        return Ok(payload);
    }

    /// <summary>
    /// REENVIA al cliente el PDF de un documento YA generado del caso (tipicamente la cotizacion). Gemelo para SARA
    /// del 'enviar_impresion' del agente del flujo: resuelve la linea y el telefono por la conversacion actual, el
    /// caso por numero (o el ligado a la conversacion), elige el documento del caso (preferiendo la cotizacion),
    /// renderiza el PDF y lo envia por la linea; deja el documento como SALIENTE en el hilo. NO cierra la sesion ni
    /// crea tareas: es la respuesta correcta cuando el cliente pide de nuevo su cotizacion.
    /// </summary>
    private async Task<AgentToolResult> ResendDocumentAsync(JsonElement args, CancellationToken ct)
    {
        if (AiToolRunContext.ConversationId is not Guid convId)
        {
            return Err("No hay una conversacion activa para reenviar el documento.");
        }
        var conv = await _db.Conversations.AsNoTracking()
            .Where(c => c.Id == convId)
            .Select(c => new { c.WhatsAppLineId, c.ContactPhone, c.TenantId })
            .FirstOrDefaultAsync(ct);
        if (conv is null || conv.WhatsAppLineId is not Guid lineId || string.IsNullOrWhiteSpace(conv.ContactPhone))
        {
            return Err("Esta conversacion no tiene una linea de WhatsApp y telefono para enviar el documento.");
        }

        // Caso: por numero explicito, o el ligado a la conversacion (igual que consultar_tarea).
        var numero = Str(args, "numero");
        TaskItem? task;
        if (!string.IsNullOrWhiteSpace(numero))
        {
            var num = numero!.Trim();
            task = await _db.TaskItems.AsNoTracking().FirstOrDefaultAsync(t => t.Number == num, ct);
        }
        else
        {
            task = await _db.TaskItems.AsNoTracking()
                .Where(t => t.ConversationId == convId)
                .OrderByDescending(t => t.CreatedAt).FirstOrDefaultAsync(ct);
        }
        if (task is null)
        {
            return Err(string.IsNullOrWhiteSpace(numero)
                ? "No hay un caso ligado a esta conversacion. Pide al cliente el numero (ej. T00303) para reenviar su documento."
                : $"No se encontro el caso '{numero}'.");
        }
        var caseNum = task.Number;

        // Documentos (respuestas diligenciadas) anclados al caso (Reference == num o "num-n"). Si el nombre pedido
        // calza con una definicion, se prefiere esa; si no, CUALQUIER documento del caso, prefiriendo la cotizacion.
        var formName = Str(args, "documento");
        var fn = formName?.Trim().ToLower();
        var matchIds = string.IsNullOrWhiteSpace(fn)
            ? new List<Guid>()
            : await _db.FormDefinitions.AsNoTracking()
                .Where(d => !d.IsArchived && (d.Title.ToLower() == fn || d.Code.ToLower() == fn))
                .Select(d => d.Id).ToListAsync(ct);

        var caseDocs = await (
            from r in _db.FormResponses.AsNoTracking()
            where r.Reference != null && (r.Reference == caseNum || r.Reference.StartsWith(caseNum + "-"))
            join d in _db.FormDefinitions.AsNoTracking() on r.DefinitionId equals d.Id
            where !d.IsArchived
            select new { ResponseId = r.Id, DefId = d.Id, d.Title, d.Code, r.IsActive, r.CreatedAt }
        ).ToListAsync(ct);
        if (caseDocs.Count == 0)
        {
            return Err($"El caso {caseNum} no tiene ningun documento (formulario diligenciado) para reenviar.");
        }

        var chosen =
            (matchIds.Count > 0
                ? caseDocs.Where(c => matchIds.Contains(c.DefId))
                    .OrderByDescending(c => c.IsActive).ThenBy(c => c.CreatedAt).FirstOrDefault()
                : null)
            ?? caseDocs
                .OrderByDescending(c => ((c.Title ?? "") + " " + (c.Code ?? "")).ToLower().Contains("cotiz"))
                .ThenByDescending(c => c.IsActive).ThenBy(c => c.CreatedAt).First();

        Forms.QuoteDocument? pdf;
        try { pdf = await _quoteDoc.RenderResponsePdfAsync(chosen.ResponseId, null, ct); }
        catch (Exception ex) { return Err($"No se pudo generar el PDF: {ex.Message}"); }
        if (pdf is null)
        {
            return Err("No se pudo generar el PDF (plantilla vacia o render fallido).");
        }

        var caption = Str(args, "mensaje");
        var captionClean = string.IsNullOrWhiteSpace(caption) ? null : caption!.Trim();
        var b64 = Convert.ToBase64String(pdf.Bytes);
        bool sent;
        try
        {
            sent = await _sender.SendWhatsAppDocumentAsync(
                lineId, conv.ContactPhone!, b64, pdf.MimeType, pdf.FileName, captionClean, Guid.Empty, ct);
        }
        catch (Exception ex) { return Err($"Error enviando el documento: {ex.Message}"); }
        if (!sent)
        {
            return Err("No se pudo enviar el documento por esta linea. Puede ser que la ventana de conversacion de 24h " +
                "este cerrada (el cliente debe haber escrito hace poco) o un problema de la linea. Si no se puede, crea " +
                "una tarea para que un asesor lo reenvie.");
        }

        // Traza: el documento queda como SALIENTE en el hilo (se ve en la conversacion y la bitacora). Best-effort.
        try
        {
            if (_tenant.TenantId is Guid tenantId)
            {
                var sentAt = DateTimeOffset.UtcNow;
                _db.Messages.Add(new Message
                {
                    TenantId = tenantId,
                    ConversationId = convId,
                    Direction = MessageDirection.Outbound,
                    Body = captionClean ?? $"Documento reenviado: {pdf.FileName}",
                    MessageType = "document",
                    MediaType = MessageMediaType.Document,
                    MediaMimeType = pdf.MimeType,
                    MediaFileName = pdf.FileName,
                    SentByName = ActorName,
                    SentAt = sentAt
                });
                var convRow = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == convId, ct);
                if (convRow is not null) { convRow.LastMessageAt = sentAt; }
                await _db.SaveChangesAsync(ct);
            }
        }
        catch { /* best-effort: la traza no debe invalidar un envio ya hecho */ }

        return Ok(new { ok = true, caso = caseNum, mensaje = $"Documento '{pdf.FileName}' reenviado al cliente por WhatsApp." });
    }

    /// <summary>Adjunta a la tarea los archivos entrantes de la conversacion en curso (reusa la URL ya
    /// almacenada del media; no copia bytes). Devuelve cuantos adjunto.</summary>
    private async Task<int> AttachConversationMediaAsync(Guid taskId, CancellationToken ct)
    {
        if (AiToolRunContext.ConversationId is not Guid convId) { return 0; }
        if (_tenant.TenantId is not Guid tenantId) { return 0; }

        // Acotar la media al ENGAGEMENT ACTUAL: una conversacion de un contacto recurrente es UNA sola
        // fila que vive meses, asi que adjuntar toda su media entrante arrastraba archivos de leads
        // anteriores (p.ej. una imagen de hace un mes en una tarea de hoy). El corte (cutoff) es el MAS
        // reciente de: (1) agent_context_reset_at (el motor de cierre lo setea al "olvidar" al cliente =
        // inicio del engagement actual) y (2) la fecha de la tarea ANTERIOR creada desde esta misma
        // conversacion (para que cada tarea se lleve solo SU media). Si no hay ninguno (primer lead sin
        // reset), un fallback de recencia de 72h evita traer lo viejo. Se usa SentAt (fecha real del
        // mensaje), no CreatedAt de la fila.
        var resetAt = await _db.Conversations.AsNoTracking()
            .Where(c => c.Id == convId)
            .Select(c => c.AgentContextResetAt)
            .FirstOrDefaultAsync(ct);
        var prevTaskAt = await _db.TaskItems.AsNoTracking()
            .Where(t => t.ConversationId == convId && t.Id != taskId)
            .MaxAsync(t => (DateTimeOffset?)t.CreatedAt, ct);

        var bounds = new List<DateTimeOffset>();
        if (resetAt.HasValue) { bounds.Add(resetAt.Value); }
        if (prevTaskAt.HasValue) { bounds.Add(prevTaskAt.Value); }
        var cutoff = bounds.Count > 0 ? bounds.Max() : DateTimeOffset.UtcNow.AddHours(-72);

        var media = await _db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == convId
                && m.Direction == MessageDirection.Inbound
                && m.MediaType != MessageMediaType.None
                && m.MediaUrl != null
                && m.SentAt > cutoff)
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

    /// <summary>Adjunta a la tarea los archivos pendientes del contexto (subidos en la herramienta de
    /// pruebas del agente). Reusa la URL ya almacenada. Devuelve cuantos adjunto.</summary>
    private async Task<int> AttachPendingAsync(Guid taskId, CancellationToken ct)
    {
        var pend = AiToolRunContext.PendingAttachments;
        if (pend is null || pend.Count == 0) { return 0; }
        if (_tenant.TenantId is not Guid tenantId) { return 0; }

        foreach (var p in pend)
        {
            _db.TaskItemAttachments.Add(new TaskItemAttachment
            {
                TenantId = tenantId,
                TaskItemId = taskId,
                FileName = p.FileName,
                Url = p.Url,
                MimeType = p.MimeType,
                SizeBytes = 0,
                UploadedByName = ActorName
            });
        }
        await _db.SaveChangesAsync(ct);
        return pend.Count;
    }

    // ===== Reparto de asesores =====

    private sealed record Assignee(Guid UserId, string Nombre);

    /// <summary>Elige el SIGUIENTE usuario para el reparto round-robin del agente (ADR-0128): si el agente tiene
    /// una lista propia de usuarios (AssigneeUserIdsJson) rota sobre ella; si no, cae al pool de asesores del
    /// tenant marcados AssignableByAgent (menos-recientemente-asignado). Null = nadie a quien asignar.</summary>
    private async Task<Assignee?> PickNextAssigneeAsync(CancellationToken ct)
    {
        var (uid, name) = await AgentAssigneePicker.PickAsync(_db, AiToolRunContext.AgentId, ct);
        return uid is Guid u ? new Assignee(u, name ?? "") : null;
    }

    // ===== Helpers =====

    private static string FileNameFromUrl(string url)
    {
        var clean = url.Split('?', '#')[0].TrimEnd('/');
        var name = clean[(clean.LastIndexOf('/') + 1)..];
        return string.IsNullOrWhiteSpace(name) ? "adjunto" : name;
    }

    private static TaskPriority ParsePriority(string? p) => (p?.Trim().ToLowerInvariant()) switch
    {
        "baja" or "low" => TaskPriority.Low,
        "alta" or "high" or "urgente" or "critica" or "critical" => TaskPriority.High,
        _ => TaskPriority.Medium
    };

    private static DateTimeOffset? ParseDate(string? s)
        => DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    private static AgentToolResult Ok(object payload) => new(JsonSerializer.Serialize(payload, JsonOut), SessionCompleted: false);
    private static AgentToolResult Err(string message) => new(JsonSerializer.Serialize(new { ok = false, error = message }, JsonOut), SessionCompleted: false);

    private static string? Str(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
