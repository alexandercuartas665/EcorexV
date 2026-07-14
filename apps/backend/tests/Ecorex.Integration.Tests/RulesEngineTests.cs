using Ecorex.Application.Common;
using Ecorex.Application.Organization;
using Ecorex.Application.Forms;
using Ecorex.Application.Rules;
using Ecorex.Application.Rules.Verbs;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion del RulesEngine (FASE 4 ola 3, ADR-0016) en matriz dual
/// PostgreSQL / SQL Server, reutilizando los fixtures de TenantIsolation. Cubre:
/// (1) ExecuteRule escribe SIEMPRE historial (exito, fallo y verbo no registrado -> error
/// tipado) con TTL de 90 dias, (2) regla de campo PASAR_CAMPOS end-to-end: el dispatcher
/// aplica las acciones y el guardado de la respuesta persiste el FormData cambiado,
/// (3) regla autonoma con AutoCompleteStep hace avanzar el flujo al activar el nodo,
/// (4) aislamiento cross-tenant de documentos/reglas/historial, y (5) el worker TTL borra
/// SOLO los logs expirados (en todos los tenants).
/// </summary>
public abstract class RulesEngineTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected RulesEngineTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    // ---- (1) Historial SIEMPRE (exito y fallo) con TTL ----

    [Fact]
    public async Task ExecuteRule_AlwaysWritesHistory_OnSuccessFailureAndUnknownVerb()
    {
        var seed = await SeedTenantAsync("Rules Historial");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var engine = BuildEngine(ctx, seed);
        var documents = new RuleDocumentService(ctx, new TestTenantContext(seed.TenantId, seed.PlatformUserId), engine);

        var document = (await documents.CreateDocumentAsync(new SaveRuleDocumentRequest(
            "RUL-T01", "Documento de prueba", "FORMULARIOS", Status: RuleStatus.Active))).Value!;

        // Regla valida (NOTIFICAR) -> Success en historial.
        var okRule = (await documents.CreateRuleAsync(document.Id, new SaveRuleRequest(
            "Notifica", "NOTIFICAR", ParamsJson: """{"message":"hola"}""", Status: RuleStatus.Active))).Value!;
        var okResult = await engine.ExecuteRuleAsync(okRule.Id, RuleInvocation.Manual(seed.TenantUserId));
        Assert.True(okResult.IsOk, okResult.Error);
        Assert.Equal(RuleExecutionStatus.Success, okResult.Value!.Status);

        // Regla con params invalidos (PASAR_CAMPOS sin mappings pasa la validacion del
        // descriptor? no: mappings es obligatorio, asi que se crea directo en la base
        // para simular una configuracion rota) -> Failed en historial.
        var badRule = new Rule
        {
            TenantId = seed.TenantId,
            DocumentId = document.Id,
            Name = "Copia rota",
            VerbName = "PASAR_CAMPOS",
            ParamsJson = """{"mappings":"no-es-arreglo"}""",
            Status = RuleStatus.Active,
            SortOrder = 1
        };
        ctx.Rules.Add(badRule);
        await ctx.SaveChangesAsync();
        var badResult = await engine.ExecuteRuleAsync(badRule.Id, RuleInvocation.Manual());
        Assert.True(badResult.IsOk, badResult.Error);
        Assert.Equal(RuleExecutionStatus.Failed, badResult.Value!.Status);
        Assert.NotNull(badResult.Value.ErrorMessage);

        // Verbo NO registrado (el ejecutor legacy hacia Activator.CreateInstance; aqui es
        // un error TIPADO) -> Invalid + Failed en historial.
        var unknownRule = new Rule
        {
            TenantId = seed.TenantId,
            DocumentId = document.Id,
            Name = "Verbo fantasma",
            VerbName = "EXECUTE_SQL",
            Status = RuleStatus.Active,
            SortOrder = 2
        };
        ctx.Rules.Add(unknownRule);
        await ctx.SaveChangesAsync();
        var unknownResult = await engine.ExecuteRuleAsync(unknownRule.Id, RuleInvocation.Manual());
        Assert.Equal(RuleServiceStatus.Invalid, unknownResult.Status);
        Assert.Contains("Verbo no registrado", unknownResult.Error);
        Assert.Equal(RuleExecutionStatus.Failed, unknownResult.Value!.Status);

        // Historial: 3 filas (exito + fallo + verbo desconocido), todas con TTL ~90 dias.
        var logs = await ctx.RuleExecutionLogs.AsNoTracking()
            .OrderBy(l => l.CreatedAt).ToListAsync();
        Assert.Equal(3, logs.Count);
        Assert.Equal(RuleExecutionStatus.Success, logs[0].Status);
        Assert.Equal(seed.TenantUserId, logs[0].ExecutedByTenantUserId);
        Assert.Equal(RuleExecutionStatus.Failed, logs[1].Status);
        Assert.Equal(RuleExecutionStatus.Failed, logs[2].Status);
        Assert.Equal("Verbo fantasma", logs[2].RuleNameSnapshot);
        Assert.All(logs, l =>
        {
            Assert.Equal(RuleTriggerKind.Manual, l.TriggerKind);
            var ttl = l.ExpiresAt - l.CreatedAt;
            Assert.InRange(ttl.TotalDays, 89, 91);
        });

        // La regla con historial NO se puede borrar (append-only): error tipado.
        var deleteBlocked = await documents.DeleteRuleAsync(okRule.Id);
        Assert.Equal(RuleServiceStatus.Invalid, deleteBlocked.Status);
    }

    // ---- (2) Regla de campo aplicada al guardar cambia el FormData (end-to-end) ----

    [Fact]
    public async Task FieldRule_PasarCampos_ChangesFormDataOnSave()
    {
        var seed = await SeedTenantAsync("Rules Campo");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantContext = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        var engine = BuildEngine(ctx, seed);
        var documents = new RuleDocumentService(ctx, tenantContext, engine);
        var definitions = new FormDefinitionService(ctx, tenantContext, new Ecorex.Application.MenuConfig.MenuConfigService(ctx, tenantContext));
        var responses = new FormResponseService(ctx, BuildWorkflowEngine(ctx, seed, engine), new SequenceService(ctx, tenantContext), tenantContext, new NoOpFormRecordBroadcaster());
        var dispatcher = new FormRuleDispatcher(ctx, engine);

        // Formulario con 2 campos texto y regla PASAR_CAMPOS origen -> destino, vinculada
        // a la pregunta 'origen' (FormFieldRule).
        var form = (await definitions.CreateAsync(new CreateFormDefinitionRequest("FRM-RUL", "Con reglas"))).Value!;
        var origen = (await definitions.AddQuestionAsync(form.Id, new SaveFormQuestionRequest(
            null, "origen", "Origen", FormControlType.Text, Required: true))).Value!;
        Assert.True((await definitions.AddQuestionAsync(form.Id, new SaveFormQuestionRequest(
            null, "destino", "Destino", FormControlType.Text))).IsOk);
        Assert.True((await definitions.ActivateAsync(form.Id)).IsOk);

        var document = (await documents.CreateDocumentAsync(new SaveRuleDocumentRequest(
            "RUL-T02", "Reglas de campo", "FORMULARIOS", Status: RuleStatus.Active))).Value!;
        var rule = (await documents.CreateRuleAsync(document.Id, new SaveRuleRequest(
            "Copiar origen a destino", "PASAR_CAMPOS",
            ParamsJson: """{"mappings":[{"source":"origen","target":"destino"}]}""",
            Status: RuleStatus.Active))).Value!;
        Assert.True((await documents.LinkToQuestionAsync(rule.Id, origen.Id)).IsOk);

        // El dispatcher reporta el campo disparador al renderer.
        var triggers = await dispatcher.GetTriggerFieldCodesAsync(form.Id);
        Assert.Contains("origen", triggers);

        // Cambio del campo -> ejecutar reglas -> aplicar acciones (mismo camino del
        // renderer via FormRuleUiState) -> guardar la respuesta.
        var draft = (await responses.GetOrCreateDraftAsync(form.Id, "REF-RUL")).Value!;
        var values = new Dictionary<string, string?>(StringComparer.Ordinal) { ["origen"] = "Cliente Alfa" };
        var outcome = await dispatcher.OnFieldChangedAsync(form.Id, "origen", values, draft.Id, seed.TenantUserId);
        Assert.Single(outcome.Executions);
        Assert.Equal(RuleExecutionStatus.Success, outcome.Executions[0].Status);
        var uiState = new FormRuleUiState();
        uiState.Apply(outcome.Actions, values);
        Assert.Equal("Cliente Alfa", values["destino"]);

        var document2 = values.ToDictionary(kv => kv.Key, kv => new FormFieldValue(kv.Value, "Text"), StringComparer.Ordinal);
        var saved = await responses.SaveAsync(draft.Id, document2, submit: true, seed.TenantUserId);
        Assert.True(saved.IsOk, saved.Error);

        // El FormData persistido quedo con el valor copiado por la regla.
        var read = await responses.GetAsync(draft.Id);
        Assert.Equal("Cliente Alfa", read!.Data["destino"].Value);

        // Y la ejecucion quedo en el historial con disparador FormField.
        var log = await ctx.RuleExecutionLogs.AsNoTracking().SingleAsync(l => l.RuleId == rule.Id);
        Assert.Equal(RuleTriggerKind.FormField, log.TriggerKind);
        Assert.Equal(RuleExecutionStatus.Success, log.Status);
    }

    // ---- (3) Regla autonoma con AutoCompleteStep avanza el flujo ----

    [Fact]
    public async Task AutonomousNodeRule_WithAutoCompleteStep_AdvancesWorkflow()
    {
        var seed = await SeedTenantAsync("Rules Flujo");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantContext = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        var engine = BuildEngine(ctx, seed);
        var documents = new RuleDocumentService(ctx, tenantContext, engine);
        // WorkflowEngine con el hook REAL (WorkflowRuleHook), como en produccion.
        var workflowEngine = BuildWorkflowEngine(ctx, seed, engine);

        var workflow = (await workflowEngine.ImportBpmnAsync(new ImportBpmnRequest(
            "RUL-FLOW", "Flujo con regla autonoma", LinearXml))).Value!;
        Assert.True((await workflowEngine.PublishAsync(workflow.Id)).IsOk);
        var nodeA = workflow.Nodes.Single(n => n.BpmnElementId == "Task_A");

        var document = (await documents.CreateDocumentAsync(new SaveRuleDocumentRequest(
            "RUL-T03", "Reglas de flujo", "PROCESOS", Status: RuleStatus.Active))).Value!;
        var rule = (await documents.CreateRuleAsync(document.Id, new SaveRuleRequest(
            "Consecutivo y avanza", "ASIGNAR_CONSECUTIVO",
            ParamsJson: """{"sequenceCode":"RULT","prefix":"R-","padding":4,"autoComplete":true}""",
            Status: RuleStatus.Active))).Value!;
        Assert.True((await documents.LinkToNodeAsync(rule.Id, nodeA.Id, isAutonomous: true)).IsOk,
            "el vinculo nodo->regla debe crearse");

        // Arrancar la instancia: al activar Task_A la regla autonoma corre, pide
        // AutoCompleteStep y el motor avanza en cascada hasta Task_B.
        var started = await workflowEngine.StartInstanceAsync(workflow.Id);
        Assert.True(started.IsOk, started.Error);
        var current = Assert.Single(started.Value!.CurrentSteps);
        Assert.Equal("Task_B", current.BpmnElementId);

        // El paso de Task_A quedo Completed por la regla (comentario del hook).
        var stepA = await ctx.WorkflowStepHistories.AsNoTracking()
            .SingleAsync(s => s.InstanceId == started.Value.Id && s.NodeId == nodeA.Id);
        Assert.Equal(WorkflowStepStatus.Completed, stepA.Status);
        Assert.Contains("reglas autonomas", stepA.ApprovalComment ?? "");

        // Historial con disparador WorkflowNode + el consecutivo emitido existe.
        var log = await ctx.RuleExecutionLogs.AsNoTracking().SingleAsync(l => l.RuleId == rule.Id);
        Assert.Equal(RuleTriggerKind.WorkflowNode, log.TriggerKind);
        Assert.Equal(RuleExecutionStatus.Success, log.Status);
        var sequence = await ctx.TenantSequences.AsNoTracking().SingleAsync(s => s.Code == "RULT");
        Assert.True(sequence.NextValue > 1);
    }

    // ---- (4) Aislamiento cross-tenant ----

    [Fact]
    public async Task DocumentsRulesAndHistory_AreIsolatedBetweenTenants()
    {
        var seedA = await SeedTenantAsync("Rules Tenant A");
        var seedB = await SeedTenantAsync("Rules Tenant B");

        Guid ruleId;
        await using (var ctxA = _fixture.CreateContext(seedA.TenantId))
        {
            var engineA = BuildEngine(ctxA, seedA);
            var documentsA = new RuleDocumentService(ctxA, new TestTenantContext(seedA.TenantId, seedA.PlatformUserId), engineA);
            var document = (await documentsA.CreateDocumentAsync(new SaveRuleDocumentRequest(
                "RUL-A", "Documento A", "FORMULARIOS", Status: RuleStatus.Active))).Value!;
            var rule = (await documentsA.CreateRuleAsync(document.Id, new SaveRuleRequest(
                "Regla A", "NOTIFICAR", ParamsJson: """{"message":"aislada"}""", Status: RuleStatus.Active))).Value!;
            ruleId = rule.Id;
            Assert.True((await engineA.ExecuteRuleAsync(ruleId, RuleInvocation.Manual())).IsOk);
        }

        await using (var ctxB = _fixture.CreateContext(seedB.TenantId))
        {
            // El filtro global oculta documentos, reglas, vinculos e historial de A.
            Assert.Empty(await ctxB.RuleDocuments.ToListAsync());
            Assert.Empty(await ctxB.Rules.ToListAsync());
            Assert.Empty(await ctxB.RuleExecutionLogs.ToListAsync());

            var engineB = BuildEngine(ctxB, seedB);
            var documentsB = new RuleDocumentService(ctxB, new TestTenantContext(seedB.TenantId, seedB.PlatformUserId), engineB);
            Assert.Empty(await documentsB.ListDocumentsAsync(includeArchived: true));
            Assert.Empty(await documentsB.ListExecutionLogsAsync());

            // Ejecutar la regla de A desde B: NotFound tipado, sin historial fantasma.
            var crossExecution = await engineB.ExecuteRuleAsync(ruleId, RuleInvocation.Manual());
            Assert.Equal(RuleServiceStatus.NotFound, crossExecution.Status);
            Assert.Empty(await ctxB.RuleExecutionLogs.ToListAsync());
        }
    }

    // ---- (5) Worker TTL: borra SOLO expirados ----

    [Fact]
    public async Task TtlCleaner_DeletesOnlyExpiredLogs_AcrossTenants()
    {
        var seedA = await SeedTenantAsync("Rules TTL A");
        var seedB = await SeedTenantAsync("Rules TTL B");

        // Historial real en A (2 ejecuciones) y B (1): una de A y la de B se expiran a mano.
        Guid expiredA;
        Guid aliveA;
        await using (var ctxA = _fixture.CreateContext(seedA.TenantId))
        {
            var engineA = BuildEngine(ctxA, seedA);
            var documentsA = new RuleDocumentService(ctxA, new TestTenantContext(seedA.TenantId, seedA.PlatformUserId), engineA);
            var document = (await documentsA.CreateDocumentAsync(new SaveRuleDocumentRequest(
                "RUL-TTL", "TTL", "FORMULARIOS", Status: RuleStatus.Active))).Value!;
            var rule = (await documentsA.CreateRuleAsync(document.Id, new SaveRuleRequest(
                "TTL", "NOTIFICAR", ParamsJson: """{"message":"ttl"}""", Status: RuleStatus.Active))).Value!;
            await engineA.ExecuteRuleAsync(rule.Id, RuleInvocation.Manual());
            await engineA.ExecuteRuleAsync(rule.Id, RuleInvocation.Manual());

            var logs = await ctxA.RuleExecutionLogs.OrderBy(l => l.CreatedAt).ToListAsync();
            Assert.Equal(2, logs.Count);
            logs[0].ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
            expiredA = logs[0].Id;
            aliveA = logs[1].Id;
            await ctxA.SaveChangesAsync();
        }
        Guid expiredB;
        await using (var ctxB = _fixture.CreateContext(seedB.TenantId))
        {
            var engineB = BuildEngine(ctxB, seedB);
            var documentsB = new RuleDocumentService(ctxB, new TestTenantContext(seedB.TenantId, seedB.PlatformUserId), engineB);
            var document = (await documentsB.CreateDocumentAsync(new SaveRuleDocumentRequest(
                "RUL-TTL", "TTL B", "FORMULARIOS", Status: RuleStatus.Active))).Value!;
            var rule = (await documentsB.CreateRuleAsync(document.Id, new SaveRuleRequest(
                "TTL B", "NOTIFICAR", ParamsJson: """{"message":"ttl"}""", Status: RuleStatus.Active))).Value!;
            await engineB.ExecuteRuleAsync(rule.Id, RuleInvocation.Manual());
            var log = await ctxB.RuleExecutionLogs.SingleAsync();
            log.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            expiredB = log.Id;
            await ctxB.SaveChangesAsync();
        }

        // El worker corre SIN tenant (SystemTenantContext): borra expirados de TODOS los
        // tenants y respeta los vigentes.
        await using (var ctxSystem = _fixture.CreateContext(tenantId: null))
        {
            var cleaner = new RuleExecutionLogCleaner(ctxSystem);
            var deleted = await cleaner.CleanupExpiredAsync(DateTimeOffset.UtcNow);
            Assert.Equal(2, deleted);

            // Solo los tenants de ESTE test (el contenedor se comparte con los otros
            // tests de la clase y sus logs vigentes no son de este escenario).
            var remaining = await ctxSystem.RuleExecutionLogs.IgnoreQueryFilters()
                .Where(l => l.TenantId == seedA.TenantId || l.TenantId == seedB.TenantId)
                .ToListAsync();
            var survivor = Assert.Single(remaining);
            Assert.Equal(aliveA, survivor.Id);
            Assert.NotEqual(expiredA, survivor.Id);
            Assert.NotEqual(expiredB, survivor.Id);

            // Segunda pasada: idempotente, nada que borrar.
            Assert.Equal(0, await cleaner.CleanupExpiredAsync(DateTimeOffset.UtcNow));
        }
    }

    // ---- (6) Metricas 30d + lista plana (modulo /reglas, ADR-0023) ----

    [Fact]
    public async Task TenantStatsAndRuleMetrics_UseThirtyDayWindow_AndFlatListBringsDocument()
    {
        var seed = await SeedTenantAsync("Rules Metricas");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var engine = BuildEngine(ctx, seed);
        var documents = new RuleDocumentService(ctx, new TestTenantContext(seed.TenantId, seed.PlatformUserId), engine);

        var document = (await documents.CreateDocumentAsync(new SaveRuleDocumentRequest(
            "RUL-MET", "Metricas", "FORMULARIOS", Status: RuleStatus.Active))).Value!;
        var okRule = (await documents.CreateRuleAsync(document.Id, new SaveRuleRequest(
            "Notifica bien", "NOTIFICAR", ParamsJson: """{"message":"ok"}""", Status: RuleStatus.Active))).Value!;
        var badRule = new Rule
        {
            TenantId = seed.TenantId,
            DocumentId = document.Id,
            Name = "Copia rota",
            VerbName = "PASAR_CAMPOS",
            ParamsJson = """{"mappings":"no-es-arreglo"}""",
            Status = RuleStatus.Active,
            SortOrder = 1
        };
        ctx.Rules.Add(badRule);
        await ctx.SaveChangesAsync();

        // 2 exitos + 1 fallo dentro de la ventana.
        Assert.True((await engine.ExecuteRuleAsync(okRule.Id, RuleInvocation.Manual())).IsOk);
        Assert.True((await engine.ExecuteRuleAsync(okRule.Id, RuleInvocation.Manual())).IsOk);
        Assert.True((await engine.ExecuteRuleAsync(badRule.Id, RuleInvocation.Manual())).IsOk);

        // Y una ejecucion VIEJA (41 dias) de la regla buena: queda fuera de la ventana de 30.
        var old = await ctx.RuleExecutionLogs
            .Where(l => l.RuleId == okRule.Id)
            .OrderBy(l => l.CreatedAt).FirstAsync();
        old.CreatedAt = DateTimeOffset.UtcNow.AddDays(-41);
        await ctx.SaveChangesAsync();

        var stats = await documents.GetTenantStatsAsync();
        Assert.Equal(1, stats.Documents);
        Assert.Equal(2, stats.Rules);
        Assert.Equal(2, stats.Executions30d);
        Assert.NotNull(stats.SuccessRate30d);
        Assert.Equal(0.5, stats.SuccessRate30d!.Value, precision: 3); // 1 exito / (1 exito + 1 fallo)
        Assert.NotNull(stats.AvgDurationMs30d);

        var okMetrics = await documents.GetRuleMetricsAsync(okRule.Id);
        Assert.Equal(1, okMetrics.Executions30d); // la otra quedo fuera de la ventana
        Assert.Equal(1, okMetrics.Success30d);
        Assert.Equal(0, okMetrics.Failed30d);
        Assert.Equal(1.0, okMetrics.SuccessRate30d!.Value, precision: 3);

        var badMetrics = await documents.GetRuleMetricsAsync(badRule.Id);
        Assert.Equal(1, badMetrics.Executions30d);
        Assert.Equal(0.0, badMetrics.SuccessRate30d!.Value, precision: 3);

        // Lista plana con el documento como categoria (panel izquierdo de /reglas).
        var flat = await documents.ListAllRulesAsync();
        Assert.Equal(2, flat.Count);
        Assert.All(flat, r =>
        {
            Assert.Equal("RUL-MET", r.DocumentCode);
            Assert.Equal("FORMULARIOS", r.DocumentCategory);
        });

        // Documento archivado: sale de la lista plana salvo que se pida.
        Assert.True((await documents.SetDocumentArchivedAsync(document.Id, true)).IsOk);
        Assert.Empty(await documents.ListAllRulesAsync());
        Assert.Equal(2, (await documents.ListAllRulesAsync(includeArchivedDocuments: true)).Count);

        // El historial ahora resuelve el nombre del ejecutor (null => "Sistema" en la UI).
        var logs = await documents.ListExecutionLogsAsync(ruleId: okRule.Id);
        Assert.All(logs, l => Assert.Null(l.ExecutedByName));
    }

    // ---- (7) Duplicar regla: mismo documento, sin vinculos, en Development ----

    [Fact]
    public async Task DuplicateRule_ClonesInSameDocument_WithoutLinksAndInDevelopment()
    {
        var seed = await SeedTenantAsync("Rules Duplicar");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantContext = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        var engine = BuildEngine(ctx, seed);
        var documents = new RuleDocumentService(ctx, tenantContext, engine);
        var definitions = new FormDefinitionService(ctx, tenantContext, new Ecorex.Application.MenuConfig.MenuConfigService(ctx, tenantContext));

        var document = (await documents.CreateDocumentAsync(new SaveRuleDocumentRequest(
            "RUL-DUP", "Duplicados", "FORMULARIOS", Status: RuleStatus.Active))).Value!;
        var rule = (await documents.CreateRuleAsync(document.Id, new SaveRuleRequest(
            "Original", "PASAR_CAMPOS",
            ParamsJson: """{"mappings":[{"source":"a","target":"b"}]}""",
            SortOrder: 3, Status: RuleStatus.Active))).Value!;

        // La original tiene un vinculo a pregunta: el clon NO debe heredarlo.
        var form = (await definitions.CreateAsync(new CreateFormDefinitionRequest("FRM-DUP", "Para duplicar"))).Value!;
        var question = (await definitions.AddQuestionAsync(form.Id, new SaveFormQuestionRequest(
            null, "a", "Campo A", FormControlType.Text))).Value!;
        Assert.True((await documents.LinkToQuestionAsync(rule.Id, question.Id)).IsOk);

        var copy = (await documents.DuplicateRuleAsync(rule.Id)).Value!;
        Assert.Equal(document.Id, copy.DocumentId);
        Assert.Equal("Original (copia)", copy.Name);
        // Comparacion SEMANTICA: el jsonb de Postgres normaliza los espacios del texto.
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
            System.Text.Json.Nodes.JsonNode.Parse(rule.ParamsJson!),
            System.Text.Json.Nodes.JsonNode.Parse(copy.ParamsJson!)));
        Assert.Equal(rule.SortOrder + 1, copy.SortOrder);
        Assert.Equal(RuleStatus.Development, copy.Status);
        Assert.Empty(await documents.ListFormLinksAsync(copy.Id));
        Assert.Single(await documents.ListFormLinksAsync(rule.Id));

        // Duplicar una regla inexistente: NotFound tipado.
        Assert.Equal(RuleServiceStatus.NotFound,
            (await documents.DuplicateRuleAsync(Guid.CreateVersion7())).Status);

        // Mover de documento via UpdateRule (select Documento del editor).
        var target = (await documents.CreateDocumentAsync(new SaveRuleDocumentRequest(
            "RUL-DUP2", "Destino", "PROCESOS", Status: RuleStatus.Active))).Value!;
        var moved = await documents.UpdateRuleAsync(copy.Id, new SaveRuleRequest(
            copy.Name, copy.VerbName, copy.Description, copy.SortOrder, copy.ParamsJson,
            copy.Status, DocumentId: target.Id));
        Assert.True(moved.IsOk, moved.Error);
        Assert.Equal(target.Id, moved.Value!.DocumentId);

        var badMove = await documents.UpdateRuleAsync(copy.Id, new SaveRuleRequest(
            copy.Name, copy.VerbName, copy.Description, copy.SortOrder, copy.ParamsJson,
            copy.Status, DocumentId: Guid.CreateVersion7()));
        Assert.Equal(RuleServiceStatus.NotFound, badMove.Status);
    }

    // ---- Helpers ----

    /// <summary>start -> Task_A -> Task_B -> end (mismo fixture sintetico del motor).</summary>
    private const string LinearXml = """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="rul" targetNamespace="http://ecorex.local/bpmn">
          <bpmn:process id="P_Rules">
            <bpmn:startEvent id="Start_1" name="Inicio" />
            <bpmn:task id="Task_A" name="Paso con regla" />
            <bpmn:task id="Task_B" name="Paso humano" />
            <bpmn:endEvent id="End_1" name="Fin" />
            <bpmn:sequenceFlow id="F1" sourceRef="Start_1" targetRef="Task_A" />
            <bpmn:sequenceFlow id="F2" sourceRef="Task_A" targetRef="Task_B" />
            <bpmn:sequenceFlow id="F3" sourceRef="Task_B" targetRef="End_1" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>
    /// Motor de reglas con el REGISTRO TIPADO real de los 5 verbos, cableado a mano contra
    /// el contexto EF del fixture (espeja DependencyInjection). Los verbos que dependen de
    /// ITaskItemService/IWorkflowEngine se registran con factory diferida, igual que en el
    /// scope real (el motor resuelve al ejecutar, no al construirse).
    /// </summary>
    private static RulesEngine BuildEngine(EcorexDbContext ctx, SeedData seed)
    {
        var tenantContext = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        RulesEngine engine = null!;
        var services = new ServiceCollection();
        services.AddSingleton<IRuleVerb>(new PasarCamposVerb());
        services.AddSingleton<IRuleVerb>(new BloquearCampoPorCondicionVerb());
        services.AddSingleton<IRuleVerb>(_ => new AsignarConsecutivoVerb(new SequenceService(ctx, tenantContext), ctx));
        services.AddSingleton<IRuleVerb>(_ => new GenerarTareasDesdeTablaVerb(
            new TaskItemService(ctx, tenantContext, new SequenceService(ctx, tenantContext),
                new WorkflowEngine(ctx, tenantContext, new WorkflowRuleHook(engine), new NoOpTaskBroadcaster()), new NoOpEmailSender(), new NodeAssigneeResolver(ctx))));
        services.AddSingleton<IRuleVerb>(_ => new NotificarVerb(ctx));
        var provider = services.BuildServiceProvider();
        engine = new RulesEngine(ctx, tenantContext, provider);
        return engine;
    }

    /// <summary>WorkflowEngine con el hook REAL del RulesEngine (como en produccion).</summary>
    private static WorkflowEngine BuildWorkflowEngine(EcorexDbContext ctx, SeedData seed, IRulesEngine engine)
        => new(ctx, new TestTenantContext(seed.TenantId, seed.PlatformUserId),
            new WorkflowRuleHook(engine), new NoOpTaskBroadcaster());

    private async Task<SeedData> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();

        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
            await ctx.SaveChangesAsync();
        }

        Guid tenantUserId;
        Guid platformUserId;
        Guid activityTypeId;
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var platformUser = new PlatformUser
            {
                Email = $"user-{tenantId:N}@rules.test",
                EmailVerified = true,
                Status = PlatformUserStatus.Active
            };
            ctx.PlatformUsers.Add(platformUser);
            var tenantUser = new TenantUser
            {
                TenantId = tenantId,
                PlatformUserId = platformUser.Id,
                Email = platformUser.Email
            };
            ctx.TenantUsers.Add(tenantUser);
            var activityType = new ActivityType
            {
                TenantId = tenantId,
                Category = "General",
                Name = "Con reglas"
            };
            ctx.ActivityTypes.Add(activityType);
            await ctx.SaveChangesAsync();
            tenantUserId = tenantUser.Id;
            platformUserId = platformUser.Id;
            activityTypeId = activityType.Id;
        }

        return new SeedData(tenantId, tenantUserId, platformUserId, activityTypeId);
    }

    private sealed record SeedData(Guid TenantId, Guid TenantUserId, Guid PlatformUserId, Guid ActivityTypeId);

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class RulesEngineTests_Postgres
    : RulesEngineTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public RulesEngineTests_Postgres(PostgresTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class RulesEngineTests_SqlServer
    : RulesEngineTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public RulesEngineTests_SqlServer(SqlServerTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}
