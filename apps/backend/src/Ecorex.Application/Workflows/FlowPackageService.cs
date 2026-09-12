using System.Text.Json;
using System.Text.Json.Serialization;
using Ecorex.Application.Common;
using Ecorex.Application.Forms;
using Ecorex.Application.Organization;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Workflows;

/// <summary>
/// Implementacion de <see cref="IFlowPackageService"/> (ADR-0097 Ola B1). Reusa piezas VALIDADAS: el import
/// del grafo (<see cref="IWorkflowDesignService.ImportJsonAsync"/> + BpmnXmlWriter/engine por dentro), el
/// import/export de formularios (<see cref="IFormDefinitionService"/>) y los setters de nodo del disenador.
/// No escribe BD "a mano": todo pasa por los servicios de siempre, asi el flujo importado nace consistente.
/// </summary>
public sealed class FlowPackageService : IFlowPackageService
{
    private const int CurrentFormatVersion = 1;

    private readonly IApplicationDbContext _db;
    private readonly IWorkflowDesignService _design;
    private readonly IFormDefinitionService _forms;
    private readonly IWorkflowNodePolicyService _policies;

    private static readonly JsonSerializerOptions PkgJson = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public FlowPackageService(
        IApplicationDbContext db, IWorkflowDesignService design,
        IFormDefinitionService forms, IWorkflowNodePolicyService policies)
    {
        _db = db;
        _design = design;
        _forms = forms;
        _policies = policies;
    }

    // ---- Export ----

    public async Task<WorkflowResult<string>> ExportAsync(Guid definitionId, CancellationToken cancellationToken = default)
    {
        var def = await _db.WorkflowDefinitions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);
        if (def is null) { return WorkflowResult<string>.NotFound("Flujo no encontrado."); }

        var nodes = await _db.WorkflowNodes.AsNoTracking()
            .Where(n => n.DefinitionId == definitionId).OrderBy(n => n.StepNumber).ToListAsync(cancellationToken);
        var nodeById = nodes.ToDictionary(n => n.Id);
        var edges = await _db.WorkflowEdges.AsNoTracking()
            .Where(e => e.DefinitionId == definitionId).OrderBy(e => e.CreatedAt).ToListAsync(cancellationToken);

        var pkgNodes = new List<FlowPackageNode>(nodes.Count);
        foreach (var n in nodes)
        {
            // Formularios del nodo -> export portable embebido.
            var forms = new List<FlowPackageForm>();
            var nodeForms = await _db.WorkflowNodeForms.AsNoTracking()
                .Where(f => f.NodeId == n.Id).OrderBy(f => f.SortOrder).ToListAsync(cancellationToken);
            foreach (var nf in nodeForms)
            {
                var ex = await _forms.ExportAsync(nf.DefinitionId, cancellationToken);
                if (ex.IsOk && ex.Value is not null)
                {
                    forms.Add(new FlowPackageForm(ex.Value, nf.SortOrder, nf.IsRequired, nf.AutoCreateOnArrival));
                }
            }

            // Cargos (policies) por NOMBRE de OrgUnit.
            var cargoNames = await _db.WorkflowNodePolicies.AsNoTracking()
                .Where(p => p.WorkflowNodeId == n.Id).OrderBy(p => p.SortOrder)
                .Join(_db.OrgUnits.AsNoTracking(), p => p.OrgUnitId, o => o.Id, (p, o) => o.Name)
                .ToListAsync(cancellationToken);

            // Agente del nodo por NOMBRE (sin ids de tenant/colmena/whatsapp).
            FlowPackageAgent? agent = null;
            var na = await _db.WorkflowNodeAgents.AsNoTracking().FirstOrDefaultAsync(a => a.NodeId == n.Id, cancellationToken);
            if (na is not null)
            {
                var agName = await _db.AiAgents.AsNoTracking().Where(a => a.Id == na.AiAgentId).Select(a => a.Name).FirstOrDefaultAsync(cancellationToken);
                string? voiceName = na.VoiceAiAgentId is Guid v
                    ? await _db.AiAgents.AsNoTracking().Where(a => a.Id == v).Select(a => a.Name).FirstOrDefaultAsync(cancellationToken)
                    : null;
                if (!string.IsNullOrWhiteSpace(agName))
                {
                    agent = new FlowPackageAgent(agName!, na.Autonomy.ToString(), na.ExtraPrompt, na.CanSendEmail,
                        na.OnFailure.ToString(), na.FailureRetries, na.FailureRoute, voiceName);
                }
            }

            // Reglas del nodo por NOMBRE.
            var rules = await _db.WorkflowNodeRules.AsNoTracking()
                .Where(r => r.WorkflowNodeId == n.Id).OrderBy(r => r.SortOrder)
                .Join(_db.Rules.AsNoTracking(), r => r.RuleId, ru => ru.Id, (r, ru) => new FlowPackageRule(ru.Name, r.IsAutonomous, r.SortOrder))
                .ToListAsync(cancellationToken);

            pkgNodes.Add(new FlowPackageNode(
                n.BpmnElementId, TipoOf(n.NodeType), n.Name, n.X, n.Y, n.W, n.H,
                n.AssigneeSource.ToString(), n.AssigneeFormFieldCode, n.Color, n.Note,
                forms, cargoNames, agent, rules, n.NoteOffsetX, n.NoteOffsetY));
        }

        var pkgEdges = edges
            .Where(e => nodeById.ContainsKey(e.SourceNodeId) && nodeById.ContainsKey(e.TargetNodeId))
            .Select(e => new FlowPackageEdge(
                nodeById[e.SourceNodeId].BpmnElementId, nodeById[e.TargetNodeId].BpmnElementId, e.Name, e.ConditionExpression))
            .ToList();

        var pkg = new FlowPackage(CurrentFormatVersion, def.Name, def.Description, def.Category, pkgNodes, pkgEdges);
        return WorkflowResult<string>.Ok(JsonSerializer.Serialize(pkg, PkgJson));
    }

    // ---- Import ----

    public async Task<WorkflowResult<FlowImportReport>> ImportAsync(string json, FlowImportOptions options, CancellationToken cancellationToken = default)
    {
        FlowPackage? pkg;
        try { pkg = JsonSerializer.Deserialize<FlowPackage>(json, PkgJson); }
        catch (JsonException ex) { return WorkflowResult<FlowImportReport>.Invalid($"Paquete JSON invalido: {ex.Message}"); }
        if (pkg is null || string.IsNullOrWhiteSpace(pkg.Name) || pkg.Nodes.Count == 0)
        {
            return WorkflowResult<FlowImportReport>.Invalid("El paquete no contiene un flujo valido.");
        }

        // 1) ProcessCode UNICO en el tenant (no versiona uno existente: es un flujo nuevo).
        var processCode = await UniqueProcessCodeAsync(cancellationToken);

        // 2) Grafo -> ImportJsonAsync (crea la definicion BORRADOR + nodos + edges).
        var graphJson = BuildGraphJson(pkg, processCode);
        var imported = await _design.ImportJsonAsync(graphJson, cancellationToken);
        if (!imported.IsOk || imported.Value is null)
        {
            return WorkflowResult<FlowImportReport>.Invalid(imported.Error ?? "No se pudo crear el grafo del flujo.");
        }
        var canvas = imported.Value;
        var nodeIdByBpmn = canvas.Nodes.ToDictionary(x => x.BpmnElementId, x => x.Id, StringComparer.Ordinal);

        // Catalogos del tenant destino para el mapeo por NOMBRE (filtro global -> solo este tenant).
        var orgUnits = await _db.OrgUnits.AsNoTracking().ToListAsync(cancellationToken);
        var agents = await _db.AiAgents.AsNoTracking().ToListAsync(cancellationToken);
        var tenantRules = await _db.Rules.AsNoTracking().ToListAsync(cancellationToken);

        var mappedCargos = new List<string>(); var unmappedCargos = new List<string>();
        var mappedAgents = new List<string>(); var unmappedAgents = new List<string>();
        var mappedRules = new List<string>(); var unmappedRules = new List<string>();
        var warnings = new List<string>();
        var formsImported = 0;

        foreach (var pn in pkg.Nodes)
        {
            if (!nodeIdByBpmn.TryGetValue(pn.BpmnElementId, out var nodeId)) { continue; }

            // Config del nodo: origen del asignado + apariencia (el grafo ya trae tipo/label/coords).
            if (Enum.TryParse<WorkflowAssigneeSource>(pn.AssigneeSource, ignoreCase: true, out var src)
                && src != WorkflowAssigneeSource.Policy)
            {
                await _design.SetNodeAssigneeAsync(nodeId, src, pn.AssigneeFormFieldCode, cancellationToken);
            }
            if (!string.IsNullOrWhiteSpace(pn.Color) || !string.IsNullOrWhiteSpace(pn.Note))
            {
                await _design.SetNodeAppearanceAsync(nodeId, pn.Color, pn.Note, cancellationToken);
            }
            if (pn.NoteOffsetX is not null || pn.NoteOffsetY is not null)
            {
                await _design.SetNodeNotePositionAsync(nodeId, pn.NoteOffsetX, pn.NoteOffsetY, cancellationToken);
            }

            // Formularios del nodo (solo si el asistente pidio migrarlos).
            if (options.IncludeNodeForms)
            {
                foreach (var pf in pn.Forms.OrderBy(f => f.SortOrder))
                {
                    var res = await _forms.ImportAsync(pf.ExportJson, cancellationToken);
                    if (res.IsOk && res.Value is not null)
                    {
                        var newDef = res.Value.Id;
                        await _design.SetNodeFormAsync(nodeId, newDef, cancellationToken);
                        await _design.SetNodeFormRequiredAsync(nodeId, newDef, pf.IsRequired, cancellationToken);
                        await _design.SetNodeFormAutoCreateAsync(nodeId, newDef, pf.AutoCreateOnArrival, cancellationToken);
                        formsImported++;
                    }
                    else { warnings.Add($"No se pudo importar un formulario del nodo '{pn.Label ?? pn.BpmnElementId}'."); }
                }
            }

            // Cargos por nombre.
            foreach (var cargo in pn.CargoNames)
            {
                var ou = orgUnits.FirstOrDefault(o => string.Equals(o.Name, cargo, StringComparison.OrdinalIgnoreCase));
                if (ou is not null)
                {
                    await _policies.AddNodePolicyAsync(nodeId, ou.Id, cancellationToken);
                    if (!mappedCargos.Contains(cargo)) { mappedCargos.Add(cargo); }
                }
                else if (!unmappedCargos.Contains(cargo)) { unmappedCargos.Add(cargo); }
            }

            // Agente por nombre + su config (sin colmena/whatsapp: los cablea el tenant).
            if (pn.Agent is FlowPackageAgent ag)
            {
                var aiAgent = agents.FirstOrDefault(a => string.Equals(a.Name, ag.AgentName, StringComparison.OrdinalIgnoreCase));
                if (aiAgent is not null)
                {
                    Enum.TryParse<WorkflowAgentAutonomy>(ag.Autonomy, ignoreCase: true, out var autonomy);
                    await _design.SetNodeAgentAsync(nodeId, aiAgent.Id, autonomy, cancellationToken);
                    Enum.TryParse<WorkflowAgentFailureAction>(ag.OnFailure, ignoreCase: true, out var onFail);
                    Guid? voiceId = string.IsNullOrWhiteSpace(ag.VoiceAgentName) ? null
                        : agents.FirstOrDefault(a => string.Equals(a.Name, ag.VoiceAgentName, StringComparison.OrdinalIgnoreCase))?.Id;
                    await _design.SetNodeAgentResourcesAsync(nodeId, new FlowNodeAgentResourcesInput(
                        ColmenaClientId: null, ColmenaSessionKey: null, VoiceAiAgentId: voiceId,
                        WhatsAppLineId: null, WhatsAppTemplateName: null, WhatsAppTemplateLang: null,
                        ExtraPrompt: ag.ExtraPrompt, CanSendEmail: ag.CanSendEmail,
                        OnFailure: onFail, FailureRetries: ag.FailureRetries, FailureRoute: ag.FailureRoute), cancellationToken);
                    if (!mappedAgents.Contains(ag.AgentName)) { mappedAgents.Add(ag.AgentName); }
                }
                else if (!unmappedAgents.Contains(ag.AgentName)) { unmappedAgents.Add(ag.AgentName); }
            }

            // Reglas por nombre.
            foreach (var pr in pn.Rules.OrderBy(r => r.SortOrder))
            {
                var rule = tenantRules.FirstOrDefault(r => string.Equals(r.Name, pr.RuleName, StringComparison.OrdinalIgnoreCase));
                if (rule is not null)
                {
                    var link = await _design.AddNodeRuleAsync(nodeId, rule.Id, cancellationToken);
                    if (link.IsOk && link.Value is not null && !pr.IsAutonomous)
                    {
                        await _design.SetNodeRuleAutonomousAsync(link.Value.LinkId, false, cancellationToken);
                    }
                    if (!mappedRules.Contains(pr.RuleName)) { mappedRules.Add(pr.RuleName); }
                }
                else if (!unmappedRules.Contains(pr.RuleName)) { unmappedRules.Add(pr.RuleName); }
            }
        }

        var report = new FlowImportReport(
            canvas.DefinitionId, canvas.ProcessCode, formsImported,
            mappedCargos, unmappedCargos, mappedAgents, unmappedAgents, mappedRules, unmappedRules, warnings);
        return WorkflowResult<FlowImportReport>.Ok(report);
    }

    // ---- Helpers ----

    /// <summary>Serializa el paquete al formato de grafo que consume ImportJsonAsync
    /// (id/nombre/categoria/descripcion + nodos + conexiones).</summary>
    private static string BuildGraphJson(FlowPackage pkg, string processCode)
    {
        var graph = new
        {
            id = processCode,
            nombre = pkg.Name,
            categoria = pkg.Category,
            descripcion = pkg.Description,
            nodos = pkg.Nodes.Select(n => new { id = n.BpmnElementId, tipo = n.Tipo, label = n.Label, x = n.X, y = n.Y, w = n.W, h = n.H }),
            conexiones = pkg.Edges.Select(e => new { de = e.From, a = e.To, nombre = e.Name, condicion = e.Condition })
        };
        return JsonSerializer.Serialize(graph);
    }

    /// <summary>ProcessCode nuevo y unico en el tenant (FLW-XXXXXX). El paquete no reusa el ProcessCode de
    /// origen: es un flujo nuevo, no una version del original.</summary>
    private async Task<string> UniqueProcessCodeAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < 20; i++)
        {
            var code = "FLW-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            if (!await _db.WorkflowDefinitions.AnyAsync(d => d.ProcessCode == code, cancellationToken))
            {
                return code;
            }
        }
        return "FLW-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    }

    private static string TipoOf(WorkflowNodeType type) => type switch
    {
        WorkflowNodeType.StartEvent => "startEvent",
        WorkflowNodeType.Task => "task",
        WorkflowNodeType.ExclusiveGateway => "exclusiveGateway",
        _ => "endEvent"
    };
}
