using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.DataContainers;

/// <summary>
/// Implementacion del asistente de tablas derivadas sobre el motor generico <see cref="IProposalChatEngine"/>
/// y el toolset <see cref="IDerivedTableToolset"/>. Proveedor: el marcado como "IA gestion de formularios" en
/// Servidores de IA (mismo criterio que el asistente de formularios; es la IA "de autoria" de la plataforma),
/// con Gemini como respaldo. El <c>contenedor_id</c> se FUERZA en cada llamada a herramienta: el modelo no
/// puede apuntar a otro contenedor (y el filtro global ya impide otro tenant). Consumo en AiUsageLog con
/// fuente "derived-table-chat".
/// </summary>
public sealed class DerivedTableChatService : IDerivedTableChatService
{
    public const string UsageSource = "derived-table-chat";
    private const AiProvider DefaultProvider = AiProvider.Gemini;

    private sealed record Session(ProposalConversation Conversation, Guid ModelId, Guid? SourceTableId, AiProvider Provider, string Model)
    {
        /// <summary>Destino elegido en la UI (null = el contenedor del origen).</summary>
        public Guid? TargetModelId { get; set; }
    }

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ISecretProtector _secrets;
    private readonly IDerivedTableToolset _toolset;
    private readonly IProposalChatEngine _engine;
    // Conversaciones vivas de ESTE scope (circuito Blazor): un panel = una conversacion.
    private readonly Dictionary<Guid, Session> _sessions = new();

    public DerivedTableChatService(IApplicationDbContext db, ITenantContext tenant, ISecretProtector secrets,
        IDerivedTableToolset toolset, IProposalChatEngine engine)
    {
        _db = db;
        _tenant = tenant;
        _secrets = secrets;
        _toolset = toolset;
        _engine = engine;
    }

    public async Task<DerivedChatStartResult> StartAsync(Guid modelId, Guid? sourceTableId, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is null) { return DerivedChatStartResult.Fail("Sin tenant en contexto."); }
        var model = await _db.DataModels.AsNoTracking().FirstOrDefaultAsync(m => m.Id == modelId, ct);
        if (model is null) { return DerivedChatStartResult.Fail("El contenedor no existe."); }

        var provider = await _db.AiProviderConfigs.AsNoTracking()
            .Where(c => c.UseForFormBuilder && c.IsEnabled && c.ApiKeyEncrypted != null)
            .Select(c => (AiProvider?)c.Provider)
            .FirstOrDefaultAsync(ct) ?? DefaultProvider;
        var cfg = await _db.AiProviderConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Provider == provider, ct);
        if (cfg is null || !cfg.IsEnabled || string.IsNullOrWhiteSpace(cfg.ApiKeyEncrypted))
        {
            return DerivedChatStartResult.Fail($"El proveedor de IA ({provider}) no esta habilitado en la plataforma. Configuralo en Servidores de IA (marca uno como IA de gestion).");
        }

        var session = new Session(new ProposalConversation(), modelId, sourceTableId, provider, ChooseModel(provider, cfg.Model));
        _sessions[session.Conversation.Id] = session;

        string? sourceName = null;
        if (sourceTableId is Guid sid)
        {
            sourceName = await _db.DataContainers.AsNoTracking().Where(c => c.Id == sid).Select(c => c.Name).FirstOrDefaultAsync(ct);
        }
        var greeting = sourceName is null
            ? $"Hola. Dime que tabla quieres sacar de '{model.Name}' (por ejemplo: \"items por categoria y marca con total de precio\") y te propongo la derivacion con vista previa."
            : $"Hola. Describe la tabla que quieres derivar de '{sourceName}' (por ejemplo: \"agrupa por categoria y marca con total de precio\") y te propongo la derivacion con vista previa.";
        return new DerivedChatStartResult(true, null, session.Conversation.Id, greeting);
    }

    public async Task<DerivedChatTurnResult> SendAsync(Guid conversationId, string userText, Guid actorUserId, CancellationToken ct = default)
    {
        if (!_sessions.TryGetValue(conversationId, out var s)) { return DerivedChatTurnResult.Fail(conversationId, "La conversacion no existe (vuelve a abrir el asistente)."); }
        var settings = await SettingsAsync(s, ct);
        if (settings is null) { return DerivedChatTurnResult.Fail(conversationId, "El proveedor de IA ya no esta disponible. Revisa Servidores de IA."); }
        return Map(conversationId, await _engine.SendAsync(s.Conversation, settings, userText, actorUserId, ct));
    }

    public async Task<DerivedChatTurnResult> ConfirmAsync(Guid conversationId, Guid actorUserId, CancellationToken ct = default)
    {
        if (!_sessions.TryGetValue(conversationId, out var s)) { return DerivedChatTurnResult.Fail(conversationId, "La conversacion no existe (vuelve a abrir el asistente)."); }
        var settings = await SettingsAsync(s, ct);
        if (settings is null) { return DerivedChatTurnResult.Fail(conversationId, "El proveedor de IA ya no esta disponible. Revisa Servidores de IA."); }
        return Map(conversationId, await _engine.ConfirmAsync(s.Conversation, settings, actorUserId, ct));
    }

    public async Task<DerivedChatTurnResult> RejectAsync(Guid conversationId, string? reason, Guid actorUserId, CancellationToken ct = default)
    {
        if (!_sessions.TryGetValue(conversationId, out var s)) { return DerivedChatTurnResult.Fail(conversationId, "La conversacion no existe (vuelve a abrir el asistente)."); }
        var settings = await SettingsAsync(s, ct);
        if (settings is null) { return DerivedChatTurnResult.Fail(conversationId, "El proveedor de IA ya no esta disponible. Revisa Servidores de IA."); }
        return Map(conversationId, await _engine.RejectAsync(s.Conversation, settings, reason, actorUserId, ct));
    }

    public void SetTargetModel(Guid conversationId, Guid? targetModelId)
    {
        if (_sessions.TryGetValue(conversationId, out var s))
        {
            s.TargetModelId = targetModelId == s.ModelId ? null : targetModelId;
        }
    }

    // Resuelve clave/modelo/prompt para ESTE turno. El prompt se rearma cada turno (las tablas del contenedor
    // cambian al crear una derivada), pero su texto es estable entre turnos para aprovechar el prompt caching.
    private async Task<ProposalChatSettings?> SettingsAsync(Session s, CancellationToken ct)
    {
        var cfg = await _db.AiProviderConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Provider == s.Provider, ct);
        if (cfg is null || !cfg.IsEnabled || string.IsNullOrWhiteSpace(cfg.ApiKeyEncrypted)) { return null; }
        string apiKey;
        try { apiKey = _secrets.Unprotect(cfg.ApiKeyEncrypted!); }
        catch { return null; }
        var meta = AiProviderCatalog.For(s.Provider);
        var baseUrl = !string.IsNullOrWhiteSpace(cfg.BaseUrl) ? cfg.BaseUrl : meta.DefaultBaseUrl;

        var prompt = await BuildSystemPromptAsync(s, ct);
        var modelId = s.ModelId;
        var target = s.TargetModelId;
        return new ProposalChatSettings(s.Provider, apiKey, baseUrl, s.Model, prompt, _toolset, _toolset.ReadOnlyTools, UsageSource,
            (_, args) => ForceModelId(args, modelId, target));
    }

    /// <summary>Pisa/agrega <c>contenedor_id</c> en los argumentos de CUALQUIER herramienta y, si la UI eligio
    /// un destino y el modelo no mando <c>contenedor_destino</c>, lo agrega (lo que el modelo diga explicitamente
    /// gana, porque viene de la frase del usuario).</summary>
    internal static string ForceModelId(string argsJson, Guid modelId, Guid? targetModelId = null)
    {
        JsonObject obj;
        try { obj = JsonNode.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { obj = new JsonObject(); }
        obj[DerivedTableToolset.ModelIdArg] = modelId.ToString("D");
        if (targetModelId is Guid t)
        {
            var existing = obj[DerivedTableToolset.TargetModelArg]?.ToString();
            if (string.IsNullOrWhiteSpace(existing)) { obj[DerivedTableToolset.TargetModelArg] = t.ToString("D"); }
        }
        return obj.ToJsonString();
    }

    private static DerivedChatTurnResult Map(Guid conversationId, ProposalChatTurn t)
    {
        DerivationPreviewDto? preview = null;
        DerivedTableCreatedDto? created = null;
        string? createdName = null;
        foreach (var run in t.ToolRuns)
        {
            if (run.ToolName == DerivedTableToolset.ProposeTool)
            {
                preview = DerivedTableToolset.TryParsePreview(run.ResultJson) ?? preview;
            }
            else if (run.ToolName == DerivedTableToolset.CreateTool && DerivedTableToolset.TryParseCreated(run.ResultJson) is { } c)
            {
                created = c.Created;
                createdName = c.Name;
            }
        }
        return new DerivedChatTurnResult(t.Ok, t.Error, conversationId, t.AssistantText, t.Proposals, t.AwaitingConfirmation,
            preview, created, createdName, t.InputTokens, t.OutputTokens, t.CachedInputTokens, t.CostUsd);
    }

    // Modelo: el configurado en Servidores de IA si soporta herramientas; si no, uno rapido con tool-use.
    private static string ChooseModel(AiProvider provider, string? configured)
    {
        var m = configured?.Trim();
        if (!string.IsNullOrWhiteSpace(m) && !IsNonToolModel(m!)) { return m!; }
        return provider switch
        {
            AiProvider.Gemini => "gemini-2.5-flash",
            AiProvider.DeepSeek => "deepseek-chat",
            AiProvider.ChatGpt => "gpt-4o-mini",
            AiProvider.Claude => "claude-haiku-4-5",
            _ => AiProviderCatalog.For(provider).DefaultModel,
        };
    }

    private static bool IsNonToolModel(string model)
    {
        var x = model.ToLowerInvariant();
        return x.Contains("reasoner") || x.Contains("-thinking") || x.Contains("o1-") || x.EndsWith("-o1");
    }

    // ===== Arnes (system prompt) =====

    private async Task<string> BuildSystemPromptAsync(Session s, CancellationToken ct)
    {
        var model = await _db.DataModels.AsNoTracking().Where(m => m.Id == s.ModelId).Select(m => m.Name).FirstOrDefaultAsync(ct) ?? "contenedor";
        var tables = await _db.DataContainers.AsNoTracking()
            .Where(c => c.ModelId == s.ModelId && c.ParentContainerId == null)
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, c.DerivedFromContainerId })
            .ToListAsync(ct);
        var ids = tables.Select(t => t.Id).ToList();
        var cols = await _db.DataContainerColumns.AsNoTracking()
            .Where(c => ids.Contains(c.ContainerId))
            .OrderBy(c => c.SortOrder)
            .Select(c => new { c.ContainerId, c.Name, c.Type })
            .ToListAsync(ct);
        var byTable = cols.GroupBy(c => c.ContainerId).ToDictionary(g => g.Key, g => g.ToList());
        var sourceName = s.SourceTableId is Guid sid ? tables.FirstOrDefault(t => t.Id == sid)?.Name : null;
        var otherModels = await _db.DataModels.AsNoTracking()
            .Where(m => m.Id != s.ModelId)
            .OrderBy(m => m.Name)
            .Select(m => new { m.Id, m.Name })
            .ToListAsync(ct);
        var targetName = s.TargetModelId is Guid tm ? otherModels.FirstOrDefault(m => m.Id == tm)?.Name : null;

        var sb = new StringBuilder();
        sb.Append("Eres el asistente de TABLAS DERIVADAS del Contenedor de datos de ECOREX. Hablas en espanol, breve y concreto.\n");
        sb.Append("Una tabla derivada es una tabla NUEVA del mismo contenedor cuyas filas se calculan agrupando OTRA tabla (el origen) ");
        sb.Append("por una o mas columnas, con agregados por grupo (count siempre; sum/avg solo sobre numericas; min/max sobre numericas o fechas) ");
        sb.Append("y un filtro previo opcional. Es materializada y se refresca (manual, cada N minutos o cron). REGLA: un solo nivel, ");
        sb.Append("una tabla derivada NO puede ser origen de otra.\n\n");
        sb.Append($"Contenedor actual: '{model}' (contenedor_id = {s.ModelId:D}; el sistema lo inyecta en cada herramienta, no lo inventes).\n");
        if (sourceName is not null)
        {
            sb.Append($"El usuario abrio el asistente desde la tabla '{sourceName}' (id {s.SourceTableId:D}): asume que es el ORIGEN salvo que diga otra cosa; en 'tabla_origen' y 'tabla' manda ese id.\n");
        }
        sb.Append("Tablas del contenedor (nombre [id]: columnas con tipo). Los nombres distinguen MAYUSCULAS: 'Items' e 'ITEMS' son tablas distintas; ante la duda usa el id.\n");
        foreach (var t in tables)
        {
            var tcols = byTable.TryGetValue(t.Id, out var l) ? l : new();
            sb.Append("- ").Append(t.Name).Append(" [").Append(t.Id.ToString("D")).Append(']');
            if (t.DerivedFromContainerId is not null) { sb.Append(" [DERIVADA: no sirve como origen]"); }
            sb.Append(": ");
            sb.Append(tcols.Count == 0 ? "(sin columnas)" : string.Join(", ", tcols.Select(c => $"{c.Name} ({TypeLabel(c.Type)})")));
            sb.Append('\n');
        }
        sb.Append('\n');
        if (otherModels.Count > 0)
        {
            sb.Append("La tabla derivada nace por defecto en este mismo contenedor. Puede nacer en OTRO contenedor del tenant si el usuario lo pide: manda 'contenedor_destino' (nombre exacto o id) en proponer_derivacion y crear_tabla_derivada. Otros contenedores: ");
            sb.Append(string.Join(", ", otherModels.Select(m => $"{m.Name} [{m.Id:D}]"))).Append(".\n");
        }
        if (targetName is not null)
        {
            sb.Append($"El usuario eligio en pantalla el contenedor destino '{targetName}' (id {s.TargetModelId:D}): si no dice otra cosa, la tabla se crea ahi (el sistema lo inyecta).\n");
        }
        sb.Append('\n');
        sb.Append("COMO TRABAJAS:\n");
        sb.Append($"1) Entiende la frase del usuario: que tabla es el origen, por que columnas agrupa, que agregados quiere y si hay filtro. Usa los nombres EXACTOS de las columnas de arriba; nunca inventes columnas. Si una columna no te queda clara por el nombre, llama a {DerivedTableToolset.DescribeTool} para ver una muestra de 10 filas.\n");
        sb.Append($"2) Llama SIEMPRE a {DerivedTableToolset.ProposeTool} con la receta: valida y devuelve la vista previa (primeros grupos). Si devuelve error, corrige la receta y vuelve a proponer; no se lo delegues al usuario si lo puedes resolver.\n");
        sb.Append("3) Cuenta en 2-4 lineas que tabla saldria (origen, agrupacion, agregados, filtro, cuantos grupos) y propon un nombre corto (ej. 'Items por categoria y marca'). Pregunta si la crea asi o ajusta algo. No repitas la tabla completa en texto: el usuario ya ve la vista previa.\n");
        sb.Append($"4) Cuando el usuario diga que si (o pida crearla directamente), llama a {DerivedTableToolset.CreateTool} con la MISMA receta validada, el nombre y el refresco (manual si no lo pidio). El sistema le pedira confirmacion al usuario antes de ejecutar; tu no tienes que volver a preguntar.\n");
        sb.Append("5) Tras crearla, dilo en una frase y termina. No crees mas de una tabla por peticion.\n");
        sb.Append("Si la peticion es ambigua (dos tablas posibles, columna que no existe), haz UNA pregunta corta. 'Total de X' = sum de X; 'promedio de X' = avg; 'cuantos' = count; 'ultima fecha' = max de la fecha; 'por X y Y' = agrupar por X, Y.\n");
        return sb.ToString();
    }

    private static string TypeLabel(DataContainerColumnType t) => t switch
    {
        DataContainerColumnType.Text => "texto",
        DataContainerColumnType.Number => "num",
        DataContainerColumnType.Decimal => "dec",
        DataContainerColumnType.Date => "fecha",
        DataContainerColumnType.Boolean => "bool",
        DataContainerColumnType.Submodel => "subtabla, no agrupable",
        _ => t.ToString().ToLowerInvariant()
    };
}
