using System.Text;
using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Forms;

/// <summary>Un campo propuesto por el asistente (field_code -> valor), con su etiqueta para mostrar.</summary>
public sealed record AiComposeProposedField(string Field, string? Label, string Value);

/// <summary>Un bloque de lienzo propuesto (lienzo field_code + blockId -> valor), con etiquetas para mostrar.</summary>
public sealed record AiComposeProposedBlock(string Lienzo, string? LienzoLabel, string Block, string? BlockLabel, string Value);

/// <summary>Resultado del asistente AiCompose (NO escribe: devuelve PROPUESTAS para que el humano confirme).</summary>
public sealed record AiComposeResult(
    bool Ok, string? Error,
    IReadOnlyList<AiComposeProposedField> Fields,
    IReadOnlyList<AiComposeProposedBlock> Blocks,
    string? Notes, int InputTokens, int OutputTokens);

/// <summary>
/// Motor del control "Asistente IA (llenar campos)" (FormControlType.AiCompose). Primo de FormAiIntakeService:
/// en vez de leer imagen/PDF, entrega al agente las respuestas ya capturadas del formulario (FormResponse.Data,
/// sources o todas) y PROPONE valores para los targets (campos simples y/o bloques de un LienzoBloques) con una
/// herramienta ACOTADA al form (FormScopedToolset.set_campo / set_bloque). Reusa agente del tenant, cupo y
/// AiUsageLog. NO escribe: el humano confirma en el renderer. Tenant-scoped.
/// </summary>
public interface IFormAiComposeService
{
    Task<AiComposeResult> ComposeAsync(Guid responseId, string fieldCode, Guid actorUserId, CancellationToken cancellationToken = default);
}

public sealed class FormAiComposeService : IFormAiComposeService
{
    private const int MaxToolRounds = 6;
    private const int MaxContextPairs = 120;

    private readonly IApplicationDbContext _db;
    private readonly IFormDefinitionService _formDefs;
    private readonly IAiProviderClient _client;
    private readonly ISecretProtector _secret;
    private readonly IAiUsageService _usage;

    public FormAiComposeService(IApplicationDbContext db, IFormDefinitionService formDefs, IAiProviderClient client,
        ISecretProtector secret, IAiUsageService usage)
    {
        _db = db;
        _formDefs = formDefs;
        _client = client;
        _secret = secret;
        _usage = usage;
    }

    public async Task<AiComposeResult> ComposeAsync(Guid responseId, string fieldCode, Guid actorUserId, CancellationToken ct = default)
    {
        // 1) Registro -> definicion + su Data (contexto).
        var resp = await _db.FormResponses.AsNoTracking()
            .Where(r => r.Id == responseId)
            .Select(r => new { r.DefinitionId, r.Data })
            .FirstOrDefaultAsync(ct);
        if (resp is null) { return Fail("El registro del formulario no existe."); }

        var def = await _formDefs.GetAsync(resp.DefinitionId, ct);
        if (def is null) { return Fail("No se pudo cargar la definicion del formulario."); }

        var control = def.Questions.FirstOrDefault(q => q.ControlType == FormControlType.AiCompose
            && string.Equals(q.FieldCode, fieldCode, StringComparison.OrdinalIgnoreCase));
        if (control is null) { return Fail("El control de asistente no existe en este formulario."); }

        var cfg = AiComposeConfig.Parse(control.OptionsJson);
        if (cfg.AgentId is not Guid agentId) { return Fail("El asistente no tiene un agente de IA configurado. Eligelo en el disenador."); }
        if (cfg.Targets.Count == 0) { return Fail("El asistente no tiene destinos (campos o bloques) configurados."); }

        // 2) Validar los targets contra la definicion actual (y recordar sus etiquetas).
        var labelOf = def.Questions.ToDictionary(q => q.FieldCode, q => q.Label, StringComparer.OrdinalIgnoreCase);
        var scalarFields = def.Questions.Where(q => IsScalarInput(q.ControlType))
            .Select(q => q.FieldCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lienzos = def.Questions.Where(q => q.ControlType == FormControlType.LienzoBloques)
            .ToDictionary(q => q.FieldCode, q => LienzoBloquesConfig.Parse(q.OptionsJson), StringComparer.OrdinalIgnoreCase);

        var fieldTargets = cfg.Targets.Where(t => t.IsField && scalarFields.Contains(t.Field!))
            .Select(t => t.Field!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var blockTargets = cfg.Targets.Where(t => t.IsLienzo && lienzos.ContainsKey(t.Lienzo!)
                && lienzos[t.Lienzo!].Blocks.Any(b => string.Equals(b.Id, t.Block, StringComparison.OrdinalIgnoreCase)))
            .Select(t => (Lienzo: t.Lienzo!, Block: t.Block!)).ToList();
        if (fieldTargets.Count == 0 && blockTargets.Count == 0) { return Fail("Los destinos configurados ya no existen en el formulario."); }

        // 3) Agente -> proveedor/modelo/apiKey.
        var agent = await _db.AiAgents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId, ct);
        if (agent is null) { return Fail("El agente configurado ya no existe."); }

        var providerCfg = await _db.AiProviderConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Provider == agent.Provider, ct);
        if (providerCfg is null || !providerCfg.IsEnabled || string.IsNullOrWhiteSpace(providerCfg.ApiKeyEncrypted))
        {
            return Fail($"El proveedor {agent.Provider} no esta habilitado en la plataforma.");
        }
        string apiKey;
        try { apiKey = _secret.Unprotect(providerCfg.ApiKeyEncrypted!); }
        catch { return Fail("La API key del proveedor esta cifrada con una version anterior; vuelvela a guardar en Servidores de IA."); }

        var meta = AiProviderCatalog.For(agent.Provider);
        var model = !string.IsNullOrWhiteSpace(cfg.Model) ? cfg.Model!
            : !string.IsNullOrWhiteSpace(agent.Model) ? agent.Model!
            : !string.IsNullOrWhiteSpace(providerCfg.Model) ? providerCfg.Model!
            : meta.DefaultModel;

        // 4) Cupo del plan.
        var quota = await _usage.GetQuotaAsync(ct);
        if (quota.Exceeded && quota.Hard)
        {
            return Fail($"Alcanzaste el limite de tokens de IA de tu plan este mes ({quota.MonthlyLimitTokens:N0}).");
        }

        // 5) Contexto: respuestas capturadas (sources o todas) como pares "etiqueta: valor".
        var document = FormResponseService.ParseDocument(resp.Data);
        var context = BuildContext(def, document, cfg.Sources, labelOf);
        if (string.IsNullOrWhiteSpace(context)) { return Fail("No hay respuestas capturadas para que el asistente trabaje. Llena el formulario primero."); }

        // 6) Prompt + toolset acotado a ESTA definicion.
        var systemPrompt = BuildSystemPrompt(agent.SystemPrompt, cfg, def, fieldTargets, blockTargets, labelOf, lienzos);
        var toolset = new FormScopedToolset(def, 1);
        var messages = new List<AiToolMessage>
        {
            new("user", context)
        };

        var totalIn = 0;
        var totalOut = 0;
        string? notes = null;
        var ok = false;
        string? error = null;

        for (var round = 1; round <= MaxToolRounds; round++)
        {
            var completion = await _client.CompleteWithToolsAsync(agent.Provider, apiKey, providerCfg.BaseUrl, model, systemPrompt, messages, toolset.GetSpecs(), ct);
            totalIn += completion.InputTokens;
            totalOut += completion.OutputTokens;

            if (!completion.Ok) { error = completion.Error ?? "El proveedor de IA no respondio."; break; }

            if (completion.ToolCalls.Count == 0)
            {
                notes = completion.Text;
                ok = true;
                break;
            }

            messages.Add(new AiToolMessage("assistant", completion.Text, completion.ToolCalls));
            foreach (var call in completion.ToolCalls)
            {
                var exec = toolset.Execute(call.Name, call.ArgumentsJson);
                messages.Add(new AiToolMessage("tool", exec.Json, ToolCallId: call.Id, ToolName: call.Name));
            }
            if (round == MaxToolRounds) { ok = true; }   // tope de rondas: usamos lo propuesto hasta aqui
        }

        await _usage.RecordAsync(agentId, agent.Provider, model, totalIn, totalOut, "form-ai-compose", error is null, ct);

        if (error is not null) { return new AiComposeResult(false, error, Array.Empty<AiComposeProposedField>(), Array.Empty<AiComposeProposedBlock>(), null, totalIn, totalOut); }

        // 7) Recoger lo propuesto, ACOTADO a los targets configurados.
        var fieldSet = fieldTargets.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var proposedFields = toolset.ProposedFields
            .Where(kv => fieldSet.Contains(kv.Key))
            .Select(kv => new AiComposeProposedField(kv.Key, labelOf.GetValueOrDefault(kv.Key), kv.Value))
            .ToList();

        var blockSet = blockTargets.Select(t => t.Lienzo + "\u0001" + t.Block).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var proposedBlocks = new List<AiComposeProposedBlock>();
        foreach (var (lienzo, map) in toolset.ProposedBlocks)
        {
            lienzos.TryGetValue(lienzo, out var lzCfg);
            foreach (var (block, value) in map)
            {
                if (!blockSet.Contains(lienzo + "\u0001" + block)) { continue; }
                var blockLabel = lzCfg?.Blocks.FirstOrDefault(b => string.Equals(b.Id, block, StringComparison.OrdinalIgnoreCase))?.Label;
                proposedBlocks.Add(new AiComposeProposedBlock(lienzo, labelOf.GetValueOrDefault(lienzo), block, blockLabel, value));
            }
        }

        if (proposedFields.Count == 0 && proposedBlocks.Count == 0)
        {
            return new AiComposeResult(false, "El asistente no propuso valores.", proposedFields, proposedBlocks, notes, totalIn, totalOut);
        }
        return new AiComposeResult(true, null, proposedFields, proposedBlocks, notes, totalIn, totalOut);
    }

    private static bool IsScalarInput(FormControlType t) => t is
        FormControlType.Text or FormControlType.TextArea or FormControlType.Select or FormControlType.MultiCheck
        or FormControlType.Radio or FormControlType.Toggle or FormControlType.Number or FormControlType.Date
        or FormControlType.Time or FormControlType.DateTime;

    private static string BuildContext(FormDefinitionDetailDto def, IReadOnlyDictionary<string, FormFieldValue> doc,
        IReadOnlyList<string> sources, IReadOnlyDictionary<string, string> labelOf)
    {
        // Orden: el del formulario. Si hay sources, solo esos; si no, todos los campos con valor (no estructura).
        var sourceSet = sources.Count > 0 ? sources.ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
        var sb = new StringBuilder();
        sb.AppendLine("[CONTEXTO] Respuestas capturadas en el formulario (pregunta: respuesta):");
        var count = 0;
        foreach (var q in def.Questions.OrderBy(q => q.SortOrder))
        {
            if (sourceSet is not null && !sourceSet.Contains(q.FieldCode)) { continue; }
            if (FormFieldValidator.IsNonInput(q.ControlType)) { continue; }
            if (!doc.TryGetValue(q.FieldCode, out var fv)) { continue; }
            var val = (fv.Value ?? "").Trim();
            if (val.Length == 0) { continue; }
            if (val.Length > 2000) { val = val[..2000] + "..."; }
            var label = labelOf.GetValueOrDefault(q.FieldCode) ?? q.FieldCode;
            sb.Append("- ").Append(label).Append(": ").AppendLine(val);
            if (++count >= MaxContextPairs) { break; }
        }
        return count == 0 ? "" : sb.ToString().TrimEnd();
    }

    private static string BuildSystemPrompt(string? agentBase, AiComposeConfig cfg, FormDefinitionDetailDto def,
        IReadOnlyList<string> fieldTargets, IReadOnlyList<(string Lienzo, string Block)> blockTargets,
        IReadOnlyDictionary<string, string> labelOf, IReadOnlyDictionary<string, LienzoBloquesConfig> lienzos)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(agentBase)) { sb.AppendLine(agentBase!.Trim()).AppendLine(); }
        if (!string.IsNullOrWhiteSpace(cfg.Prompt)) { sb.AppendLine(cfg.Prompt!.Trim()).AppendLine(); }

        sb.AppendLine("[TAREA] Con la informacion del CONTEXTO (las respuestas del formulario), PROPON el contenido de los siguientes destinos de ESTE formulario, llamando una herramienta por destino:");
        if (fieldTargets.Count > 0)
        {
            sb.AppendLine("- Campos (usa set_campo(field, valor)):");
            foreach (var f in fieldTargets) { sb.Append("    - ").Append(f).Append(" (").Append(labelOf.GetValueOrDefault(f) ?? f).AppendLine(")"); }
        }
        if (blockTargets.Count > 0)
        {
            sb.AppendLine("- Bloques de lienzo (usa set_bloque(lienzo, block, valor)):");
            foreach (var (lienzo, block) in blockTargets)
            {
                var blockLabel = lienzos.TryGetValue(lienzo, out var lz)
                    ? lz.Blocks.FirstOrDefault(b => string.Equals(b.Id, block, StringComparison.OrdinalIgnoreCase))?.Label ?? block
                    : block;
                sb.Append("    - lienzo='").Append(lienzo).Append("' block='").Append(block).Append("' (").Append(blockLabel).AppendLine(")");
            }
        }
        sb.AppendLine("[REGLAS] Usa SOLO la informacion del contexto; NO inventes datos (cifras, nombres). Si un destino no tiene soporte suficiente, dejalo breve o indica que falta. No escribas texto fuera de las herramientas salvo un resumen corto al final.");
        return sb.ToString().TrimEnd();
    }

    private static AiComposeResult Fail(string error) =>
        new(false, error, Array.Empty<AiComposeProposedField>(), Array.Empty<AiComposeProposedBlock>(), null, 0, 0);
}
