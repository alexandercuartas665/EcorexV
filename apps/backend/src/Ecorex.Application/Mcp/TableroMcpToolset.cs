using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Directorio;
using Ecorex.Application.Roles;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Mcp;

/// <summary>Resultado de una herramienta MCP: JSON para el modelo + marca de error.</summary>
public sealed record McpToolResult(string Json, bool IsError)
{
    /// <summary>Estado ANTES del cambio (para la auditoria antes/despues). Null si no aplica.</summary>
    public object? AuditBefore { get; init; }

    /// <summary>Estado DESPUES del cambio (para la auditoria antes/despues). Null si no aplica.</summary>
    public object? AuditAfter { get; init; }
}

/// <summary>
/// Herramientas del servidor MCP de TABLEROS (ADR-0159), pensadas para una IA externa (Claude, ChatGPT,
/// Gemini) que actua COMO un usuario del tenant. A diferencia de los toolsets del agente interno
/// (IAgentToolset, atados a una conversacion de WhatsApp), aqui no hay conversacion: hay un usuario.
/// Capa DELGADA sobre los servicios existentes (ITaskItemService, IActivityBoardService,
/// IWorkflowInboxService): no reimplementa reglas de negocio.
/// </summary>
public interface ITableroMcpToolset
{
    /// <summary>Herramientas que NO modifican nada (no se auditan).</summary>
    IReadOnlySet<string> ReadOnlyTools { get; }

    /// <summary>Herramientas que dan de baja algo (reversible, pero la IA debe confirmar con el usuario).</summary>
    IReadOnlySet<string> DestructiveTools { get; }

    IReadOnlyList<AiToolSpec> GetSpecs();

    Task<McpToolResult> ExecuteAsync(string toolName, string argumentsJson, McpActor actor, CancellationToken cancellationToken = default);
}

public sealed partial class TableroMcpToolset : ITableroMcpToolset
{
    private const int MaxCardsPerColumn = 40;
    private const int MaxSearch = 50;

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly HashSet<string> ReadOnly = new(StringComparer.Ordinal)
    {
        "listar_tableros", "ver_tablero", "buscar_tareas", "ver_tarea", "mis_pasos_pendientes",
        "listar_conceptos", "listar_usuarios", "buscar_contacto", "ver_contacto", "tareas_del_contacto",
        "listar_campos_contacto"
    };

    private static readonly HashSet<string> Destructive = new(StringComparer.Ordinal) { "inactivar_contacto" };

    private readonly IApplicationDbContext _db;
    private readonly IActivityBoardService _boards;
    private readonly ITaskItemService _tasks;
    private readonly IWorkflowInboxService _inbox;
    private readonly ITerceroService? _terceros;
    private readonly IDirectorioToolset? _directorio;
    private readonly ITerceroVinculoService? _vinculos;
    private readonly ITerceroFieldService? _fields;
    private readonly IRolService? _roles;
    private readonly IFormAuthoringToolset? _formAuthoring;
    private readonly IWorkflowDesignService? _flowDesign;
    private readonly IWorkflowEngine? _engine;

    public TableroMcpToolset(IApplicationDbContext db, IActivityBoardService boards, ITaskItemService tasks, IWorkflowInboxService inbox,
        ITerceroService? terceros = null, IDirectorioToolset? directorio = null, ITerceroVinculoService? vinculos = null,
        ITerceroFieldService? fields = null, IRolService? roles = null,
        IFormAuthoringToolset? formAuthoring = null, IWorkflowDesignService? flowDesign = null, IWorkflowEngine? engine = null)
    {
        _vinculos = vinculos;
        _fields = fields;
        _roles = roles;
        _db = db;
        _boards = boards;
        _tasks = tasks;
        _inbox = inbox;
        _terceros = terceros;
        _directorio = directorio;
        _formAuthoring = formAuthoring;
        _flowDesign = flowDesign;
        _engine = engine;
    }

    // Los conjuntos y specs se arman por INSTANCIA: las herramientas de autoria (formularios/flujos) solo
    // existen si sus servicios fueron inyectados (en pruebas pueden faltar), asi que no pueden ser estaticas.
    public IReadOnlySet<string> ReadOnlyTools => _readOnlyAll ??= BuildReadOnly();

    public IReadOnlySet<string> DestructiveTools => _destructiveAll ??= BuildDestructive();

    public IReadOnlyList<AiToolSpec> GetSpecs() => _allSpecs ??= BuildSpecs();

    private HashSet<string>? _readOnlyAll;
    private HashSet<string>? _destructiveAll;
    private AiToolSpec[]? _allSpecs;

    private HashSet<string> BuildReadOnly()
    {
        var set = new HashSet<string>(ReadOnly, StringComparer.Ordinal);
        set.UnionWith(FlowReadOnly);
        if (_formAuthoring is not null) { set.UnionWith(_formAuthoring.ReadOnlyTools); }
        return set;
    }

    private HashSet<string> BuildDestructive()
    {
        var set = new HashSet<string>(Destructive, StringComparer.Ordinal);
        set.UnionWith(FlowDestructive);
        set.UnionWith(FormDestructive);
        return set;
    }

    private AiToolSpec[] BuildSpecs()
    {
        var all = new List<AiToolSpec>(Specs);
        all.AddRange(DirectorySpecs);
        if (_flowDesign is not null) { all.AddRange(FlowSpecs); }
        if (_formAuthoring is not null) { all.AddRange(_formAuthoring.GetSpecs()); }
        return all.ToArray();
    }

    private static readonly AiToolSpec[] Specs =
    {
        new("listar_tableros",
            "Lista los tableros de tareas que el usuario puede ver: nombre, codigo, columnas y numero de tareas. " +
            "Usala primero para conocer los nombres exactos de tableros y columnas.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("ver_tablero",
            "Muestra un tablero por columnas con sus tareas (numero, titulo, prioridad, encargado, vencimiento y si la " +
            "gobierna un flujo). 'alcance': equipo (todas), mias, sin_asignar o terminadas. Si el tablero limita la " +
            "vista del usuario, el servidor fuerza 'mias'.",
            """{"type":"object","properties":{"tablero":{"type":"string","description":"Nombre, codigo o id del tablero"},"alcance":{"type":"string","enum":["equipo","mias","sin_asignar","terminadas"]},"columna":{"type":"string","description":"Solo esta columna (nombre)"},"texto":{"type":"string","description":"Filtra por numero, titulo, descripcion o solicitante"}},"required":["tablero"],"additionalProperties":false}"""),
        new("buscar_tareas",
            "Busca tareas por texto (numero, titulo, descripcion) en todo el tenant, limitado a lo que el usuario " +
            "puede ver. 'solo_mias' = solo donde es encargado/asignado o tiene un paso de flujo.",
            """{"type":"object","properties":{"texto":{"type":"string"},"estado":{"type":"string","enum":["abiertas","terminadas","todas"]},"solo_mias":{"type":"boolean"},"limite":{"type":"integer","minimum":1,"maximum":50}},"additionalProperties":false}"""),
        new("ver_tarea",
            "Detalle de una tarea por numero (ej. T00123): descripcion, solicitante, checklist, asignados, ultimas " +
            "anotaciones, adjuntos y, si tiene flujo, el PASO ACTUAL con sus opciones/rutas y si el usuario puede atenderlo.",
            """{"type":"object","properties":{"numero":{"type":"string","description":"Numero de la tarea, ej. T00123"}},"required":["numero"],"additionalProperties":false}"""),
        new("mis_pasos_pendientes",
            "Lista los pasos de flujo que esperan al usuario (asignados a el o que puede tomar), con la tarea, el " +
            "proceso, el paso y sus opciones de aprobacion.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("listar_conceptos",
            "Lista los conceptos (tipos de actividad) con los que se puede crear una tarea. Un concepto puede traer su " +
            "propio tablero y ARRANCAR un flujo al crear la tarea.",
            """{"type":"object","properties":{"texto":{"type":"string","description":"Filtro por nombre o codigo"}},"additionalProperties":false}"""),
        new("listar_usuarios",
            "Lista los usuarios activos del tenant (nombre y email) para asignar tareas.",
            """{"type":"object","properties":{"texto":{"type":"string","description":"Filtro por nombre o email"}},"additionalProperties":false}"""),
        new("crear_tarea",
            "Crea una tarea en un tablero. Si indicas 'concepto' la tarea se clasifica por el y, si el concepto tiene " +
            "proceso, ARRANCA su flujo (y puede usar el tablero del concepto si no das 'tablero'). Sin 'columna' cae en " +
            "la primera. Devuelve el numero de la tarea.",
            """{"type":"object","properties":{"tablero":{"type":"string","description":"Nombre, codigo o id del tablero"},"columna":{"type":"string"},"concepto":{"type":"string","description":"Nombre o codigo del concepto (ver listar_conceptos)"},"titulo":{"type":"string"},"descripcion":{"type":"string"},"prioridad":{"type":"string","enum":["baja","media","alta"]},"vence":{"type":"string","description":"Fecha limite ISO 8601 (ej. 2026-11-20)"},"asignado":{"type":"string","description":"Email o nombre del encargado (ver listar_usuarios)"}},"required":["titulo"],"additionalProperties":false}"""),
        new("mover_tarea",
            "Mueve una tarea SIN flujo a otra columna de su tablero. Si la tarea esta gobernada por un flujo en curso NO se " +
            "mueve a mano: usa completar_paso. Si la columna destino es de cierre y el tablero exige motivo, envia 'motivo_cierre'.",
            """{"type":"object","properties":{"numero":{"type":"string"},"columna":{"type":"string","description":"Nombre de la columna destino"},"motivo_cierre":{"type":"string"},"nota":{"type":"string"}},"required":["numero","columna"],"additionalProperties":false}"""),
        new("comentar",
            "Agrega un comentario a la bitacora de la tarea (queda a nombre del usuario, marcado 'via IA').",
            """{"type":"object","properties":{"numero":{"type":"string"},"texto":{"type":"string"}},"required":["numero","texto"],"additionalProperties":false}"""),
        new("asignar_tarea",
            "Cambia el ENCARGADO de la tarea. 'usuario' = email o nombre (ver listar_usuarios); vacio = quitar encargado.",
            """{"type":"object","properties":{"numero":{"type":"string"},"usuario":{"type":"string"}},"required":["numero"],"additionalProperties":false}"""),
        new("checklist",
            "Gestiona el checklist de la tarea. accion=agregar (con 'texto'), marcar o desmarcar (con 'item' = texto o posicion 1..n).",
            """{"type":"object","properties":{"numero":{"type":"string"},"accion":{"type":"string","enum":["agregar","marcar","desmarcar"]},"texto":{"type":"string"},"item":{"type":"string"}},"required":["numero","accion"],"additionalProperties":false}"""),
        new("registrar_tiempo",
            "Registra tiempo trabajado por el usuario en la tarea.",
            """{"type":"object","properties":{"numero":{"type":"string"},"minutos":{"type":"integer","minimum":1,"maximum":1440},"nota":{"type":"string"}},"required":["numero","minutos"],"additionalProperties":false}"""),
        new("tomar_paso",
            "Toma (reclama) el paso actual del flujo de una tarea que esta sin asignar y que el usuario puede atender.",
            """{"type":"object","properties":{"numero":{"type":"string"},"paso":{"type":"string","description":"Nombre del paso si hay varios en curso"}},"required":["numero"],"additionalProperties":false}"""),
        new("completar_paso",
            "Cierra el PASO ACTUAL del flujo de la tarea y el flujo avanza. Si el paso ofrece opciones/rutas (ver ver_tarea) " +
            "indica 'opcion' con una de ellas EXACTA. Si el paso exige un formulario sin diligenciar, el servidor lo " +
            "rechaza: el usuario debe llenarlo en la aplicacion.",
            """{"type":"object","properties":{"numero":{"type":"string"},"opcion":{"type":"string","description":"Opcion/ruta exacta del paso (ver ver_tarea)"},"comentario":{"type":"string"},"paso":{"type":"string","description":"Nombre del paso si hay varios en curso"}},"required":["numero"],"additionalProperties":false}"""),
    };

    public async Task<McpToolResult> ExecuteAsync(string toolName, string argumentsJson, McpActor actor, CancellationToken cancellationToken = default)
    {
        JsonElement args;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            args = doc.RootElement.Clone();
        }
        catch (JsonException) { return Err("Los argumentos no son un JSON valido."); }
        if (args.ValueKind != JsonValueKind.Object) { return Err("Los argumentos deben ser un objeto JSON."); }

        try
        {
            return toolName switch
            {
                "listar_tableros" => await ListBoardsAsync(actor, cancellationToken),
                "ver_tablero" => await ViewBoardAsync(args, actor, cancellationToken),
                "buscar_tareas" => await SearchAsync(args, actor, cancellationToken),
                "ver_tarea" => await ViewTaskAsync(args, actor, cancellationToken),
                "mis_pasos_pendientes" => await MyStepsAsync(actor, cancellationToken),
                "listar_conceptos" => await ListConceptsAsync(args, cancellationToken),
                "listar_usuarios" => await ListUsersAsync(args, cancellationToken),
                "crear_tarea" => await CreateAsync(args, actor, cancellationToken),
                "mover_tarea" => await MoveAsync(args, actor, cancellationToken),
                "comentar" => await CommentAsync(args, actor, cancellationToken),
                "asignar_tarea" => await AssignAsync(args, actor, cancellationToken),
                "checklist" => await ChecklistAsync(args, actor, cancellationToken),
                "registrar_tiempo" => await WorkLogAsync(args, actor, cancellationToken),
                "tomar_paso" => await ClaimAsync(args, actor, cancellationToken),
                "completar_paso" => await CompleteStepAsync(args, actor, cancellationToken),
                _ when DirectoryToolNames.Contains(toolName) => await ExecuteDirectoryAsync(toolName, args, argumentsJson, actor, cancellationToken),
                _ when _flowDesign is not null && FlowToolNames.Contains(toolName) => await ExecuteFlowAsync(toolName, args, actor, cancellationToken),
                _ when _formAuthoring is not null && FormToolNames.Contains(toolName) => await ExecuteFormAsync(toolName, argumentsJson, actor, cancellationToken),
                _ => Err($"Herramienta desconocida: {toolName}")
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return Err($"Error ejecutando '{toolName}': {ex.Message}");
        }
    }

    // ===================== Lectura =====================

    private async Task<McpToolResult> ListBoardsAsync(McpActor actor, CancellationToken ct)
    {
        var index = await _boards.ListBoardsAsync(VisibleFilter(actor), ct);
        return Ok(new
        {
            tableros = index.Boards.Select(b => new
            {
                id = b.Id,
                codigo = b.Code,
                nombre = b.Name,
                descripcion = b.Description,
                columnas = b.ColumnNames,
                tareas = b.TaskCount,
                avance_pct = b.ProgressPct
            })
        });
    }

    private async Task<McpToolResult> ViewBoardAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var board = await ResolveBoardAsync(Str(args, "tablero"), actor, ct);
        if (board is null) { return Err("Tablero no encontrado o sin acceso. Usa listar_tableros."); }

        var scope = Str(args, "alcance") switch
        {
            "mias" => ActivityBoardScope.Mine,
            "sin_asignar" => ActivityBoardScope.Unassigned,
            "terminadas" => ActivityBoardScope.Done,
            _ => ActivityBoardScope.Team
        };
        IReadOnlyList<Guid>? columnIds = null;
        var colName = Str(args, "columna");
        if (colName is not null)
        {
            var col = await ResolveColumnAsync(board.Id, colName, ct);
            if (col is null) { return Err($"La columna '{colName}' no existe en el tablero '{board.Name}'."); }
            columnIds = new[] { col.Id };
        }

        var res = await _boards.GetBoardDetailAsync(board.Id, new ActivityBoardDetailFilter(
            ColumnIds: columnIds, Scope: scope, CurrentTenantUserId: actor.TenantUserId,
            CanSeeRestricted: actor.IsOwnerOrAdmin, Text: Str(args, "texto")), ct);
        if (!res.IsOk || res.Value is null) { return Err(res.Error ?? "No se pudo abrir el tablero."); }
        var d = res.Value;

        return Ok(new
        {
            tablero = new { id = d.Id, codigo = d.Code, nombre = d.Name },
            alcance_aplicado = scope == ActivityBoardScope.Team && !d.CanSeeAllTasks ? "mias (forzado por el tablero)" : Str(args, "alcance") ?? "equipo",
            contadores = new { equipo = d.ScopeCounters.Team, mias = d.ScopeCounters.Mine, sin_asignar = d.ScopeCounters.Unassigned, terminadas = d.ScopeCounters.Done },
            motivos_cierre = d.CloseReasons is { Count: > 0 } ? d.CloseReasons : null,
            columnas = d.Columns.Select(c => new
            {
                nombre = c.Name,
                es_cierre = c.IsDone,
                total = c.Cards.Count,
                tareas = c.Cards.Take(MaxCardsPerColumn).Select(Card),
                truncado = c.Cards.Count > MaxCardsPerColumn ? true : (bool?)null
            })
        });
    }

    private async Task<McpToolResult> SearchAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var limit = Math.Clamp(Int(args, "limite") ?? 20, 1, MaxSearch);
        IReadOnlyList<TaskItemStatus>? statuses = Str(args, "estado") switch
        {
            "terminadas" => new[] { TaskItemStatus.Done, TaskItemStatus.Closed },
            "todas" => null,
            _ => new[] { TaskItemStatus.Pending, TaskItemStatus.Active, TaskItemStatus.InProgress, TaskItemStatus.Suspended }
        };
        var onlyMine = Bool(args, "solo_mias") ?? false;
        var ctx = await VisibilityContext.LoadAsync(_db, _inbox, actor, ct);

        // Se piden paginas al servicio y se filtra por visibilidad hasta llenar el limite (tope de 5 paginas).
        var found = new List<TaskItemSummaryDto>();
        for (var page = 1; page <= 5 && found.Count < limit; page++)
        {
            var res = await _tasks.ListAsync(new TaskItemListFilter(Statuses: statuses, Text: Str(args, "texto"), Page: page, PageSize: 100), ct);
            foreach (var t in res.Items)
            {
                if (found.Count >= limit) { break; }
                var mine = await ctx.IsMineAsync(t.Id, t.AssigneeTenantUserId, ct);
                if (onlyMine && !mine) { continue; }
                if (mine || await ctx.CanSeeAsync(t.BoardId, ct)) { found.Add(t); }
            }
            if (res.Items.Count < 100) { break; }
        }

        var boardNames = await BoardNamesAsync(found.Select(f => f.BoardId), ct);
        var userNames = await UserNamesAsync(found.Select(f => f.AssigneeTenantUserId), ct);
        return Ok(new
        {
            total = found.Count,
            tareas = found.Select(t => new
            {
                numero = t.Number,
                titulo = t.Title,
                estado = StatusEs(t.Status),
                prioridad = PriorityEs(t.Priority),
                tablero = t.BoardId is Guid b && boardNames.TryGetValue(b, out var bn) ? bn : null,
                encargado = t.AssigneeTenantUserId is Guid u && userNames.TryGetValue(u, out var un) ? un : null,
                concepto = t.SubcategoriaName ?? t.ActivityTypeName,
                vence = t.DueDate
            })
        });
    }

    private async Task<McpToolResult> ViewTaskAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (task, error) = await ResolveVisibleTaskAsync(Str(args, "numero"), actor, ct);
        if (task is null) { return Err(error!); }

        var detail = await _tasks.GetDetailAsync(task.Id, ct);
        if (detail is null) { return Err("Tarea no encontrada."); }
        var it = detail.Item;
        var boardNames = await BoardNamesAsync(new[] { it.BoardId }, ct);
        var colName = it.ColumnId is Guid cid
            ? await _db.TaskBoardColumns.AsNoTracking().Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(ct)
            : null;
        var userNames = await UserNamesAsync(new[] { it.AssigneeTenantUserId }, ct);

        object? flow = null;
        if (task.WorkflowInstanceId is not null)
        {
            var diagram = await _inbox.GetTaskFlowDiagramAsync(task.Id, actor.TenantUserId, ct);
            if (diagram is not null)
            {
                flow = new
                {
                    proceso = diagram.FlowName,
                    pasos_actuales = diagram.Nodes.Where(n => n.IsCurrent).Select(n => new
                    {
                        paso = n.Name,
                        encargado = n.AssigneeLabel,
                        es_mio = n.IsMine,
                        puedo_tomarlo = n.IsClaimable,
                        lo_atiende_un_agente = n.IsAuto ? true : (bool?)null,
                        tiene_formulario = n.HasForm,
                        opciones = RouteOptions(n),
                        esperando_desde = n.WaitingSince
                    }),
                    pasos_cerrados = diagram.Nodes.Count(n => n.State == TaskFlowNodeState.Completed)
                };
            }
        }

        return Ok(new
        {
            numero = it.Number,
            titulo = it.Title,
            descripcion = detail.Description,
            estado = StatusEs(it.Status),
            prioridad = PriorityEs(it.Priority),
            tablero = it.BoardId is Guid b && boardNames.TryGetValue(b, out var bn) ? bn : null,
            columna = colName,
            concepto = it.SubcategoriaName ?? it.ActivityTypeName,
            encargado = it.AssigneeTenantUserId is Guid u && userNames.TryGetValue(u, out var un) ? un : null,
            asignados = detail.Assignees.Select(a => a.DisplayName),
            vence = it.DueDate,
            creada = it.CreatedAt,
            solicitante = detail.RequesterName is null && detail.RequesterEmail is null ? null : new
            {
                nombre = detail.RequesterName,
                email = detail.RequesterEmail,
                telefono = detail.RequesterPhone,
                documento = detail.RequesterDocument
            },
            checklist = detail.Checklist.OrderBy(c => c.SortOrder).Select((c, i) => new { n = i + 1, texto = c.Text, hecho = c.IsCompleted }),
            subtareas = detail.Subtasks?.Select(s => new { numero = s.Number, titulo = s.Title, estado = StatusEs(s.Status) }),
            adjuntos = detail.Attachments.Select(a => new { nombre = a.FileName, url = a.Url }),
            tiempo_total_min = detail.TotalWorkSeconds / 60,
            bitacora = detail.RecentActivity.Take(15).Select(a => new { fecha = a.CreatedAt, autor = a.ActorName, texto = a.Text }),
            flujo = flow
        });
    }

    private async Task<McpToolResult> MyStepsAsync(McpActor actor, CancellationToken ct)
    {
        var steps = await _inbox.GetMyPendingStepsAsync(actor.TenantUserId, ct);
        return Ok(new
        {
            total = steps.Count,
            pasos = steps.Select(s => new
            {
                tarea = s.TaskNumber,
                titulo = s.TaskTitle,
                proceso = s.ProcessName,
                paso = s.NodeName,
                es_mio = s.IsMine,
                puedo_tomarlo = s.IsClaimable,
                tiene_formulario = s.HasForm,
                opciones = s.ApprovalOptions.Count > 0 ? s.ApprovalOptions : null,
                desde = s.CreatedAt
            })
        });
    }

    private async Task<McpToolResult> ListConceptsAsync(JsonElement args, CancellationToken ct)
    {
        var text = Str(args, "texto");
        var q = _db.ActividadSubcategorias.AsNoTracking().Where(s => !s.IsArchived);
        if (text is not null)
        {
            var low = text.ToLower();
            q = q.Where(s => s.Nombre.ToLower().Contains(low) || s.Codigo.ToLower().Contains(low));
        }
        var rows = await q.OrderBy(s => s.SortOrder).ThenBy(s => s.Nombre).Take(100)
            .Select(s => new
            {
                codigo = s.Codigo,
                nombre = s.Nombre,
                descripcion = s.Descripcion,
                tablero = s.TaskBoard != null ? s.TaskBoard.Name : null,
                arranca_flujo = s.WorkflowDefinitionId != null,
                tiene_formulario = s.FormDefinitionId != null
            })
            .ToListAsync(ct);
        return Ok(new { conceptos = rows });
    }

    private async Task<McpToolResult> ListUsersAsync(JsonElement args, CancellationToken ct)
    {
        var text = Str(args, "texto")?.ToLower();
        var rows = await _db.TenantUsers.AsNoTracking()
            .Where(u => u.Status == PlatformUserStatus.Active)
            .Select(u => new { u.Email, Name = u.PlatformUser != null ? u.PlatformUser.DisplayName : null })
            .ToListAsync(ct);
        var list = rows
            .Where(u => text is null || u.Email.ToLower().Contains(text) || (u.Name ?? "").ToLower().Contains(text))
            .OrderBy(u => u.Name ?? u.Email)
            .Take(200)
            .Select(u => new { nombre = u.Name ?? u.Email, email = u.Email });
        return Ok(new { usuarios = list });
    }

    // ===================== Escritura =====================

    private async Task<McpToolResult> CreateAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var title = Str(args, "titulo");
        if (title is null) { return Err("'titulo' es obligatorio."); }

        ActividadSubcategoria? concept = null;
        var conceptArg = Str(args, "concepto");
        if (conceptArg is not null)
        {
            var low = conceptArg.ToLower();
            concept = await _db.ActividadSubcategorias.AsNoTracking()
                .Where(s => !s.IsArchived && (s.Codigo.ToLower() == low || s.Nombre.ToLower() == low))
                .FirstOrDefaultAsync(ct);
            if (concept is null) { return Err($"Concepto '{conceptArg}' no encontrado. Usa listar_conceptos."); }
        }

        TaskBoard? board;
        var boardArg = Str(args, "tablero");
        if (boardArg is not null)
        {
            board = await ResolveBoardAsync(boardArg, actor, ct);
            if (board is null) { return Err("Tablero no encontrado o sin acceso. Usa listar_tableros."); }
        }
        else if (concept?.TaskBoardId is Guid cb)
        {
            board = await ResolveBoardAsync(cb.ToString(), actor, ct);
            if (board is null) { return Err("El tablero del concepto no es visible para el usuario."); }
        }
        else
        {
            return Err("Indica 'tablero' (o un 'concepto' que tenga tablero).");
        }

        // Tenants que clasifican SOLO por concepto (sin tipos de actividad activos, ej. BITCODE): sin
        // 'concepto' se usa el UNICO concepto ligado al tablero; si no hay uno claro, se pide elegir.
        if (concept is null && !await _db.ActivityTypes.AsNoTracking().AnyAsync(t => !t.IsArchived, ct))
        {
            var boardConcepts = await _db.ActividadSubcategorias.AsNoTracking()
                .Where(s => !s.IsArchived && s.TaskBoardId == board.Id).Take(2).ToListAsync(ct);
            if (boardConcepts.Count == 1)
            {
                concept = boardConcepts[0];
            }
            else
            {
                var names = await _db.ActividadSubcategorias.AsNoTracking().Where(s => !s.IsArchived)
                    .OrderBy(s => s.SortOrder).ThenBy(s => s.Nombre).Select(s => s.Nombre).Take(30).ToListAsync(ct);
                return Err("Esta empresa clasifica las tareas por CONCEPTO: indica 'concepto'. Opciones: " + string.Join(" | ", names) + ".");
            }
        }

        TaskBoardColumn? column;
        var colArg = Str(args, "columna");
        if (colArg is not null)
        {
            column = await ResolveColumnAsync(board.Id, colArg, ct);
            if (column is null) { return Err($"La columna '{colArg}' no existe en '{board.Name}'."); }
        }
        else if (concept?.TaskBoardColumnId is Guid ccol && concept.TaskBoardId == board.Id)
        {
            column = await _db.TaskBoardColumns.AsNoTracking().FirstOrDefaultAsync(c => c.Id == ccol, ct);
        }
        else
        {
            column = await _db.TaskBoardColumns.AsNoTracking()
                .Where(c => c.BoardId == board.Id).OrderBy(c => c.SortOrder).FirstOrDefaultAsync(ct);
        }
        if (column is null) { return Err($"El tablero '{board.Name}' no tiene columnas."); }

        Guid? assignee = null;
        var assigneeArg = Str(args, "asignado");
        if (assigneeArg is not null)
        {
            assignee = await ResolveUserAsync(assigneeArg, ct);
            if (assignee is null) { return Err($"Usuario '{assigneeArg}' no encontrado. Usa listar_usuarios."); }
        }

        DateTimeOffset? due = null;
        var dueArg = Str(args, "vence");
        if (dueArg is not null)
        {
            if (!DateTimeOffset.TryParse(dueArg, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return Err("'vence' debe ser una fecha ISO 8601 (ej. 2026-11-20).");
            }
            due = parsed.ToUniversalTime();
        }

        var res = await _boards.QuickCreateTaskAsync(new QuickCreateTaskRequest(
            BoardId: board.Id, ColumnId: column.Id, Title: Clip(title, 300), Description: Str(args, "descripcion"),
            Priority: ParsePriority(Str(args, "prioridad")), AssigneeTenantUserId: assignee, DueDate: due,
            SubcategoriaId: concept?.Id), actor.PlatformUserId, ActorName(actor), ct);
        if (!res.IsOk || res.Value is null) { return Err(res.Error ?? "No se pudo crear la tarea."); }

        var created = res.Value.Item;
        var hasFlow = await _db.TaskItems.AsNoTracking().Where(t => t.Id == created.Id).Select(t => t.WorkflowInstanceId != null).FirstOrDefaultAsync(ct);
        return Ok(new
        {
            ok = true,
            numero = created.Number,
            titulo = created.Title,
            tablero = board.Name,
            columna = column.Name,
            flujo_iniciado = hasFlow,
            aviso = concept?.FormDefinitionId is not null ? "El concepto tiene formulario: el usuario debe diligenciarlo en la aplicacion." : null
        });
    }

    private async Task<McpToolResult> MoveAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (task, error) = await ResolveVisibleTaskAsync(Str(args, "numero"), actor, ct);
        if (task is null) { return Err(error!); }
        if (task.BoardId is not Guid boardId) { return Err("La tarea no esta en un tablero."); }

        // Regla clave (ADR-0159): una tarea gobernada por un flujo EN CURSO no se mueve a mano; avanza el flujo.
        if (await HasRunningFlowAsync(task, ct))
        {
            return Err($"La tarea {task.Number} la gobierna un flujo en curso: no se mueve de columna a mano. " +
                       "Usa ver_tarea para ver el paso actual y completar_paso para avanzarlo.");
        }

        var colArg = Str(args, "columna");
        if (colArg is null) { return Err("'columna' es obligatoria."); }
        var column = await ResolveColumnAsync(boardId, colArg, ct);
        if (column is null) { return Err($"La columna '{colArg}' no existe en el tablero de la tarea."); }

        // Al final de la columna destino.
        var sort = await _db.TaskItems.AsNoTracking().Where(t => t.ColumnId == column.Id).Select(t => (int?)t.BoardSortOrder).MaxAsync(ct) ?? 0;
        var res = await _boards.MoveTaskAsync(task.Id, column.Id, sort + 1, actor.PlatformUserId, ActorName(actor),
            Str(args, "motivo_cierre"), Str(args, "nota"), ct);
        if (!res.IsOk || res.Value is null) { return Err(res.Error ?? "No se pudo mover la tarea."); }
        return Ok(new { ok = true, numero = task.Number, columna = column.Name, estado = StatusEs(res.Value.Status), nota = res.Value.StatusNote });
    }

    private async Task<McpToolResult> CommentAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (task, error) = await ResolveVisibleTaskAsync(Str(args, "numero"), actor, ct);
        if (task is null) { return Err(error!); }
        var text = Str(args, "texto");
        if (text is null) { return Err("'texto' es obligatorio."); }
        var res = await _tasks.AddCommentAsync(task.Id, Clip(text, 4000), actor.PlatformUserId, ActorName(actor), ct);
        return res.IsOk ? Ok(new { ok = true, numero = task.Number }) : Err(res.Error ?? "No se pudo comentar.");
    }

    private async Task<McpToolResult> AssignAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (task, error) = await ResolveVisibleTaskAsync(Str(args, "numero"), actor, ct);
        if (task is null) { return Err(error!); }
        var userArg = Str(args, "usuario");
        if (userArg is null)
        {
            var un = await _tasks.UnassignAsync(task.Id, actor.PlatformUserId, ActorName(actor), ct);
            return un.IsOk ? Ok(new { ok = true, numero = task.Number, encargado = (string?)null }) : Err(un.Error ?? "No se pudo quitar el encargado.");
        }
        var uid = await ResolveUserAsync(userArg, ct);
        if (uid is null) { return Err($"Usuario '{userArg}' no encontrado. Usa listar_usuarios."); }
        var res = await _tasks.AssignAsync(task.Id, uid.Value, actor.PlatformUserId, ActorName(actor), ct);
        return res.IsOk ? Ok(new { ok = true, numero = task.Number, encargado = userArg }) : Err(res.Error ?? "No se pudo asignar.");
    }

    private async Task<McpToolResult> ChecklistAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (task, error) = await ResolveVisibleTaskAsync(Str(args, "numero"), actor, ct);
        if (task is null) { return Err(error!); }
        var action = Str(args, "accion");
        if (action == "agregar")
        {
            var text = Str(args, "texto");
            if (text is null) { return Err("'texto' es obligatorio para agregar."); }
            var add = await _tasks.AddChecklistItemAsync(task.Id, Clip(text, 500), actor.PlatformUserId, ActorName(actor), ct);
            return add.IsOk ? Ok(new { ok = true, numero = task.Number, item = text }) : Err(add.Error ?? "No se pudo agregar.");
        }
        if (action is not ("marcar" or "desmarcar")) { return Err("'accion' debe ser agregar, marcar o desmarcar."); }

        var itemArg = Str(args, "item");
        if (itemArg is null) { return Err("'item' (texto o posicion) es obligatorio."); }
        var detail = await _tasks.GetDetailAsync(task.Id, ct);
        var items = detail?.Checklist.OrderBy(c => c.SortOrder).ToList() ?? new List<TaskItemChecklistItemDto>();
        TaskItemChecklistItemDto? target = int.TryParse(itemArg, out var pos) && pos >= 1 && pos <= items.Count
            ? items[pos - 1]
            : items.FirstOrDefault(c => string.Equals(c.Text.Trim(), itemArg, StringComparison.OrdinalIgnoreCase))
              ?? items.FirstOrDefault(c => c.Text.Contains(itemArg, StringComparison.OrdinalIgnoreCase));
        if (target is null) { return Err($"No hay un item '{itemArg}' en el checklist de {task.Number}."); }
        var done = action == "marcar";
        var res = await _tasks.ToggleChecklistItemAsync(target.Id, done, done ? actor.TenantUserId : null, actor.PlatformUserId, ActorName(actor), ct);
        return res.IsOk ? Ok(new { ok = true, numero = task.Number, item = target.Text, hecho = done }) : Err(res.Error ?? "No se pudo actualizar.");
    }

    private async Task<McpToolResult> WorkLogAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (task, error) = await ResolveVisibleTaskAsync(Str(args, "numero"), actor, ct);
        if (task is null) { return Err(error!); }
        var minutes = Int(args, "minutos");
        if (minutes is null or < 1 or > 1440) { return Err("'minutos' debe estar entre 1 y 1440."); }
        var res = await _tasks.AddWorkLogAsync(new AddTaskWorkLogRequest(task.Id, actor.TenantUserId, minutes.Value * 60,
            Str(args, "nota"), WorkLogKind.Manual, LogActivity: true), actor.PlatformUserId, ActorName(actor), ct);
        return res.IsOk ? Ok(new { ok = true, numero = task.Number, minutos = minutes }) : Err(res.Error ?? "No se pudo registrar el tiempo.");
    }

    private async Task<McpToolResult> ClaimAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (task, error) = await ResolveVisibleTaskAsync(Str(args, "numero"), actor, ct);
        if (task is null) { return Err(error!); }
        var (node, nodeErr) = await ResolveCurrentNodeAsync(task, Str(args, "paso"), actor, n => n.IsClaimable, ct);
        if (node is null) { return Err(nodeErr!); }
        var res = await _inbox.ClaimStepAsync(node.StepId!.Value, actor.TenantUserId, ct);
        return res.IsOk ? Ok(new { ok = true, numero = task.Number, paso = node.Name }) : Err(res.Error ?? "No se pudo tomar el paso.");
    }

    private async Task<McpToolResult> CompleteStepAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (task, error) = await ResolveVisibleTaskAsync(Str(args, "numero"), actor, ct);
        if (task is null) { return Err(error!); }
        var (node, nodeErr) = await ResolveCurrentNodeAsync(task, Str(args, "paso"), actor, n => n.IsMine || actor.IsOwnerOrAdmin, ct);
        if (node is null) { return Err(nodeErr!); }
        if (node.IsAuto) { return Err($"El paso '{node.Name}' lo esta atendiendo un agente de IA del sistema; no se cierra desde aqui."); }

        var stepId = node.StepId!.Value;
        var option = Str(args, "opcion");
        var comment = Str(args, "comentario");
        var options = RouteOptions(node);
        if (options is { Count: > 0 })
        {
            if (option is null) { return Err($"El paso '{node.Name}' exige elegir una opcion: {string.Join(" | ", options)}."); }
            var route = node.Routes?.FirstOrDefault(r => Same(r.Option, option) || Same(r.TargetName, option));
            if (route is null && !node.ApprovalOptions.Any(o => Same(o, option)))
            {
                return Err($"Opcion '{option}' no valida. Opciones: {string.Join(" | ", options)}.");
            }
            // Compuerta ATENDIDA: la rama se sigue por su destino (ADR-0072), igual que en el detalle de la tarea.
            if (node.NodeType == WorkflowNodeType.ExclusiveGateway && route?.TargetNodeId is Guid target)
            {
                var gw = await _inbox.CompleteGatewayChoiceAsync(stepId, target, actor.TenantUserId, comment, ct);
                return gw.IsOk ? Ok(new { ok = true, numero = task.Number, paso = node.Name, ruta = route.Option }) : Err(gw.Error ?? "No se pudo tomar la ruta.");
            }
            option = route?.Option ?? node.ApprovalOptions.First(o => Same(o, option));
        }

        var res = await _inbox.CompletePendingStepAsync(stepId, actor.TenantUserId, option, comment, ct);
        if (!res.IsOk) { return Err(res.Error ?? "No se pudo cerrar el paso."); }

        var after = await _inbox.GetTaskFlowDiagramAsync(task.Id, actor.TenantUserId, ct);
        return Ok(new
        {
            ok = true,
            numero = task.Number,
            paso_cerrado = node.Name,
            opcion = option,
            siguientes_pasos = after?.Nodes.Where(n => n.IsCurrent).Select(n => new { paso = n.Name, encargado = n.AssigneeLabel })
        });
    }

    // ===================== Resolucion + visibilidad =====================

    private static ActivityBoardIndexFilter VisibleFilter(McpActor actor)
        => new(CurrentTenantUserId: actor.TenantUserId, CanSeeRestricted: actor.IsOwnerOrAdmin);

    /// <summary>Tablero visible para el actor por id, codigo o nombre (sin distinguir mayusculas).</summary>
    private async Task<TaskBoard?> ResolveBoardAsync(string? key, McpActor actor, CancellationToken ct)
    {
        if (key is null) { return null; }
        var index = await _boards.ListBoardsAsync(VisibleFilter(actor), ct);
        var match = Guid.TryParse(key, out var gid)
            ? index.Boards.FirstOrDefault(b => b.Id == gid)
            : index.Boards.FirstOrDefault(b => Same(b.Code, key)) ?? index.Boards.FirstOrDefault(b => Same(b.Name, key))
              ?? SingleOrNull(index.Boards.Where(b => b.Name.Contains(key, StringComparison.OrdinalIgnoreCase)));
        if (match is null) { return null; }
        return await _db.TaskBoards.AsNoTracking().FirstOrDefaultAsync(b => b.Id == match.Id, ct);
    }

    private async Task<TaskBoardColumn?> ResolveColumnAsync(Guid boardId, string name, CancellationToken ct)
    {
        var cols = await _db.TaskBoardColumns.AsNoTracking().Where(c => c.BoardId == boardId).ToListAsync(ct);
        return cols.FirstOrDefault(c => Same(c.Name, name))
               ?? SingleOrNull(cols.Where(c => c.Name.Contains(name, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<Guid?> ResolveUserAsync(string key, CancellationToken ct)
    {
        var rows = await _db.TenantUsers.AsNoTracking()
            .Where(u => u.Status == PlatformUserStatus.Active)
            .Select(u => new { u.Id, u.Email, Name = u.PlatformUser != null ? u.PlatformUser.DisplayName : null })
            .ToListAsync(ct);
        var hit = rows.FirstOrDefault(u => Same(u.Email, key)) ?? rows.FirstOrDefault(u => Same(u.Name, key))
                  ?? SingleOrNull(rows.Where(u => (u.Name ?? "").Contains(key, StringComparison.OrdinalIgnoreCase)));
        return hit?.Id;
    }

    /// <summary>
    /// Tarea por numero (o id) que el actor PUEDE VER, con la misma regla del tablero: Owner/Admin todo; si no,
    /// lo suyo (encargado, asignado o paso de flujo ruteado) o tareas de un tablero visible cuyo alcance no esta
    /// limitado para el. Sin acceso responde igual que "no existe" (no revela tareas ajenas).
    /// </summary>
    private async Task<(TaskItem? Task, string? Error)> ResolveVisibleTaskAsync(string? numero, McpActor actor, CancellationToken ct)
    {
        if (numero is null) { return (null, "'numero' es obligatorio (ej. T00123)."); }
        var key = numero.Trim();
        var task = Guid.TryParse(key, out var gid)
            ? await _db.TaskItems.AsNoTracking().FirstOrDefaultAsync(t => t.Id == gid, ct)
            : await _db.TaskItems.AsNoTracking().FirstOrDefaultAsync(t => t.Number == key.ToUpper(), ct);
        var notFound = $"No encontre la tarea '{key}' (o no tienes acceso).";
        if (task is null) { return (null, notFound); }
        if (actor.IsOwnerOrAdmin) { return (task, null); }

        var ctx = await VisibilityContext.LoadAsync(_db, _inbox, actor, ct);
        if (await ctx.IsMineAsync(task.Id, task.AssigneeTenantUserId, ct)) { return (task, null); }
        return await ctx.CanSeeAsync(task.BoardId, ct) ? (task, null) : (null, notFound);
    }

    private async Task<(TaskFlowNodeDto? Node, string? Error)> ResolveCurrentNodeAsync(
        TaskItem task, string? stepName, McpActor actor, Func<TaskFlowNodeDto, bool> canAct, CancellationToken ct)
    {
        if (task.WorkflowInstanceId is null) { return (null, $"La tarea {task.Number} no tiene flujo."); }
        var diagram = await _inbox.GetTaskFlowDiagramAsync(task.Id, actor.TenantUserId, ct);
        var current = diagram?.Nodes.Where(n => n.IsCurrent && n.StepId is not null).ToList() ?? new List<TaskFlowNodeDto>();
        if (current.Count == 0) { return (null, $"La tarea {task.Number} no tiene pasos en curso."); }

        if (stepName is not null)
        {
            var named = current.FirstOrDefault(n => Same(n.Name, stepName));
            if (named is null) { return (null, $"No hay un paso en curso llamado '{stepName}'. En curso: {string.Join(", ", current.Select(n => n.Name))}."); }
            return canAct(named) ? (named, null) : (null, $"No puedes atender el paso '{named.Name}' (encargado: {named.AssigneeLabel ?? "sin asignar"}).");
        }
        var actionable = current.Where(canAct).ToList();
        return actionable.Count switch
        {
            1 => (actionable[0], null),
            0 => (null, $"No tienes pasos que atender en {task.Number}. En curso: " +
                        string.Join(", ", current.Select(n => $"{n.Name} ({n.AssigneeLabel ?? "sin asignar"})")) + "."),
            _ => (null, $"Hay varios pasos en curso; indica 'paso': {string.Join(", ", actionable.Select(n => n.Name))}.")
        };
    }

    private async Task<bool> HasRunningFlowAsync(TaskItem task, CancellationToken ct)
        => task.WorkflowInstanceId is Guid wid
           && await _db.WorkflowInstances.AsNoTracking().AnyAsync(w => w.Id == wid && w.Status == WorkflowInstanceStatus.Running, ct);

    /// <summary>
    /// Cache de visibilidad por llamada: tableros visibles (con su restriccion de alcance) y tareas "mias".
    /// Replica la regla del tablero (ADR-0117 tableros restringidos + ADR-0136 alcance por usuario + ADR-0038).
    /// </summary>
    private sealed class VisibilityContext
    {
        private readonly IApplicationDbContext _db;
        private readonly McpActor _actor;
        private readonly Dictionary<Guid, bool> _boardFullAccess;
        private readonly HashSet<Guid> _routed;

        private VisibilityContext(IApplicationDbContext db, McpActor actor, Dictionary<Guid, bool> boards, HashSet<Guid> routed)
        {
            _db = db; _actor = actor; _boardFullAccess = boards; _routed = routed;
        }

        public static async Task<VisibilityContext> LoadAsync(IApplicationDbContext db, IWorkflowInboxService inbox, McpActor actor, CancellationToken ct)
        {
            var rows = await db.TaskBoards.AsNoTracking()
                .Where(b => b.Kind == TaskBoardKind.Activities)
                .Select(b => new { b.Id, b.AllowedUserIdsJson, b.SeeAllUserIdsJson })
                .ToListAsync(ct);
            var map = new Dictionary<Guid, bool>();
            foreach (var b in rows)
            {
                var allowed = ParseIds(b.AllowedUserIdsJson);
                if (!actor.IsOwnerOrAdmin && allowed.Count > 0 && !allowed.Contains(actor.TenantUserId)) { continue; }
                var seeAll = ParseIds(b.SeeAllUserIdsJson);
                map[b.Id] = actor.IsOwnerOrAdmin || seeAll.Count == 0 || seeAll.Contains(actor.TenantUserId);
            }
            var routed = (await inbox.GetMyPendingStepsAsync(actor.TenantUserId, ct))
                .Where(s => s.TaskItemId is not null).Select(s => s.TaskItemId!.Value).ToHashSet();
            return new VisibilityContext(db, actor, map, routed);
        }

        /// <summary>El tablero es visible Y el actor puede ver TODAS sus tareas (no solo las suyas).</summary>
        public Task<bool> CanSeeAsync(Guid? boardId, CancellationToken ct)
            => Task.FromResult(_actor.IsOwnerOrAdmin
                || (boardId is Guid b && _boardFullAccess.TryGetValue(b, out var full) && full));

        public async Task<bool> IsMineAsync(Guid taskId, Guid? assignee, CancellationToken ct)
            => assignee == _actor.TenantUserId
               || _routed.Contains(taskId)
               || await _db.TaskItemAssignments.AsNoTracking().AnyAsync(a => a.TaskItemId == taskId && a.TenantUserId == _actor.TenantUserId, ct);

        private static HashSet<Guid> ParseIds(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) { return new HashSet<Guid>(); }
            try { return (JsonSerializer.Deserialize<List<Guid>>(json) ?? new List<Guid>()).Where(g => g != Guid.Empty).ToHashSet(); }
            catch (JsonException) { return new HashSet<Guid>(); }
        }
    }

    // ===================== Helpers =====================

    private static object Card(ActivityCardDto c) => new
    {
        numero = c.Number,
        titulo = c.Title,
        prioridad = PriorityEs(c.Priority),
        estado = StatusEs(c.Status),
        encargado = c.Assignee?.DisplayName,
        vence = c.DueDate,
        checklist = c.ChecklistTotal > 0 ? $"{c.ChecklistDone}/{c.ChecklistTotal}" : null,
        con_flujo = c.HasFlow ? true : (bool?)null,
        solicitante = c.RequesterName
    };

    private static IReadOnlyList<string>? RouteOptions(TaskFlowNodeDto n)
    {
        var list = n.Routes is { Count: > 0 } r ? r.Select(x => x.Option).ToList() : n.ApprovalOptions.ToList();
        return list.Count > 0 ? list : null;
    }

    private async Task<Dictionary<Guid, string>> BoardNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var set = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (set.Count == 0) { return new Dictionary<Guid, string>(); }
        return await _db.TaskBoards.AsNoTracking().Where(b => set.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, ct);
    }

    private async Task<Dictionary<Guid, string>> UserNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var set = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (set.Count == 0) { return new Dictionary<Guid, string>(); }
        var rows = await _db.TenantUsers.AsNoTracking().Where(u => set.Contains(u.Id))
            .Select(u => new { u.Id, u.Email, Name = u.PlatformUser != null ? u.PlatformUser.DisplayName : null })
            .ToListAsync(ct);
        return rows.ToDictionary(u => u.Id, u => string.IsNullOrWhiteSpace(u.Name) ? u.Email : u.Name!);
    }

    /// <summary>Nombre en bitacora: el usuario + marca de que la accion vino de una IA externa.</summary>
    public static string ActorName(McpActor actor) => $"{actor.DisplayName} (via IA)";

    private static TaskPriority ParsePriority(string? p) => p switch
    {
        "baja" => TaskPriority.Low,
        "alta" or "urgente" => TaskPriority.High,
        _ => TaskPriority.Medium
    };

    private static string PriorityEs(TaskPriority p) => p switch
    {
        TaskPriority.Low => "baja",
        TaskPriority.High => "alta",
        _ => "media"
    };

    private static string StatusEs(TaskItemStatus s) => s switch
    {
        TaskItemStatus.Pending => "pendiente",
        TaskItemStatus.Active => "activa",
        TaskItemStatus.InProgress => "en_progreso",
        TaskItemStatus.Done => "terminada",
        TaskItemStatus.Suspended => "suspendida",
        TaskItemStatus.Closed => "cerrada",
        _ => s.ToString()
    };

    private static bool Same(string? a, string? b)
        => a is not null && b is not null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static T? SingleOrNull<T>(IEnumerable<T> source) where T : class
    {
        var list = source.Take(2).ToList();
        return list.Count == 1 ? list[0] : null;
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    private static string? Str(JsonElement a, string name)
    {
        if (!a.TryGetProperty(name, out var v)) { return null; }
        var s = v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null
        };
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private static int? Int(JsonElement a, string name)
    {
        if (!a.TryGetProperty(name, out var v)) { return null; }
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) { return i; }
        return v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var j) ? j : null;
    }

    private static bool? Bool(JsonElement a, string name)
    {
        if (!a.TryGetProperty(name, out var v)) { return null; }
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(v.GetString(), out var b) ? b : null,
            _ => null
        };
    }

    private static McpToolResult Ok(object payload) => new(JsonSerializer.Serialize(payload, JsonOut), false);

    private static McpToolResult Err(string message) => new(JsonSerializer.Serialize(new { ok = false, error = message }, JsonOut), true);
}
