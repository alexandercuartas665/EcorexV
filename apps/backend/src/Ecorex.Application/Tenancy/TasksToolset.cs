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

    public TasksToolset(IApplicationDbContext db, ITaskItemService tasks, ITenantContext tenant)
    {
        _db = db;
        _tasks = tasks;
        _tenant = tenant;
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
            "imagenes que el cliente haya enviado en la conversacion se adjuntan AUTOMATICAMENTE a la tarea.",
            """{"type":"object","properties":{"tablero":{"type":"string","description":"Nombre exacto del tablero destino (ver listar_tableros)"},"titulo":{"type":"string","description":"Titulo corto de la tarea"},"descripcion":{"type":"string","description":"Detalle de lo que necesita el cliente"},"prioridad":{"type":"string","enum":["baja","media","alta","urgente"],"description":"Prioridad (opcional, por defecto media)"},"vence":{"type":"string","description":"Fecha limite ISO 8601 opcional (ej. 2026-08-10)"}},"required":["tablero","titulo"],"additionalProperties":false}"""),
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
        var boards = await _db.TaskBoards.AsNoTracking()
            .Where(b => !b.IsArchived)
            .OrderBy(b => b.Name)
            .Select(b => new { b.Name, b.Description })
            .ToListAsync(ct);
        return Ok(new { ok = true, tableros = boards.Select(b => new { nombre = b.Name, descripcion = b.Description }) });
    }

    private async Task<AgentToolResult> CreateTaskAsync(JsonElement args, Guid actor, CancellationToken ct)
    {
        var tableroNombre = Str(args, "tablero");
        var titulo = Str(args, "titulo");
        if (string.IsNullOrWhiteSpace(tableroNombre)) { return Err("Falta el nombre del tablero (tablero)."); }
        if (string.IsNullOrWhiteSpace(titulo)) { return Err("Falta el titulo de la tarea (titulo)."); }

        // Tablero por NOMBRE (case-insensitive). Si no existe, devolvemos la lista para que el modelo reintente.
        var board = await _db.TaskBoards.AsNoTracking()
            .Where(b => !b.IsArchived)
            .FirstOrDefaultAsync(b => b.Name.ToLower() == tableroNombre!.Trim().ToLower(), ct);
        if (board is null)
        {
            var nombres = await _db.TaskBoards.AsNoTracking().Where(b => !b.IsArchived)
                .OrderBy(b => b.Name).Select(b => b.Name).ToListAsync(ct);
            return Err($"No existe un tablero llamado '{tableroNombre}'. Tableros disponibles: {string.Join(", ", nombres)}.");
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

        // Reparto round-robin: el sistema asigna la tarea al SIGUIENTE asesor MARCADO como asignable
        // (con usuario vinculado). No todos los asesores entran: solo los que tienen la marca.
        var asignado = await PickNextAssigneeAsync(ct);

        var req = new CreateTaskItemRequest(
            Title: titulo!.Trim(),
            ActivityTypeId: tipoId,
            Description: string.IsNullOrWhiteSpace(descripcion) ? null : descripcion!.Trim(),
            Priority: prioridad,
            DueDate: vence,
            BoardId: board.Id,
            AssigneeTenantUserId: asignado?.UserId);

        var res = await _tasks.CreateAsync(req, actor, ActorName, ct);
        if (!res.IsOk || res.Value is null)
        {
            return Err(res.Error ?? "No se pudo crear la tarea.");
        }
        var taskId = res.Value.Item.Id;

        // Auto-adjuntar los archivos que el cliente ENVIO: media entrante de la conversacion + adjuntos
        // pendientes del contexto (los que sube la herramienta de pruebas del agente).
        var adjuntados = await AttachConversationMediaAsync(taskId, ct);
        adjuntados += await AttachPendingAsync(taskId, ct);

        return new AgentToolResult(JsonSerializer.Serialize(new
        {
            ok = true,
            tarea_id = taskId,
            tablero = board.Name,
            asignado_a = asignado?.Nombre,
            adjuntos = adjuntados,
            mensaje = $"Tarea creada en el tablero '{board.Name}'"
                + (asignado is not null ? $", asignada a {asignado.Nombre}" : "")
                + (adjuntados > 0 ? $" con {adjuntados} archivo(s) adjunto(s)." : ".")
        }, JsonOut), SessionCompleted: true);
    }

    /// <summary>Adjunta a la tarea los archivos entrantes de la conversacion en curso (reusa la URL ya
    /// almacenada del media; no copia bytes). Devuelve cuantos adjunto.</summary>
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

    /// <summary>Elige el SIGUIENTE asesor para el reparto round-robin del agente y marca su ultima
    /// asignacion. Elegibles: activos, con la MARCA AssignableByAgent y con usuario vinculado. Entre
    /// ellos, recibe primero el que hace mas tiempo no recibe (LastAgentAssignmentAt null/mas antiguo).
    /// Devuelve null si ningun asesor esta marcado (la tarea queda sin asignar).</summary>
    private async Task<Assignee?> PickNextAssigneeAsync(CancellationToken ct)
    {
        var next = await _db.Asesores
            .Where(a => a.IsActive && a.AssignableByAgent && a.TenantUserId != null)
            .OrderBy(a => a.LastAgentAssignmentAt == null ? 0 : 1)
            .ThenBy(a => a.LastAgentAssignmentAt)
            .ThenBy(a => a.Nombre)
            .FirstOrDefaultAsync(ct);
        if (next?.TenantUserId is not Guid uid) { return null; }

        next.LastAgentAssignmentAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new Assignee(uid, next.Nombre);
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
