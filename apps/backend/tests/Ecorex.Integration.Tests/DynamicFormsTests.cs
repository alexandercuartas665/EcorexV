using Ecorex.Application.Common;
using Ecorex.Application.Organization;
using Ecorex.Application.Forms;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion de los formularios dinamicos (FASE 4 ola 2, ADR-0015) en matriz
/// dual PostgreSQL / SQL Server, reutilizando los fixtures de TenantIsolation. Cubre:
/// (1) CRUD de definicion + preguntas y round-trip del documento de respuesta (JSON
/// identico al leer), (2) validacion servidor que rechaza submits invalidos con errores
/// por fieldCode, (3) ciclo de vida del token (emitir/validar/usar/reusar/expirado/
/// revocado) y su scoping cross-tenant acotado, (4) submit del formulario vinculado que
/// completa el paso del workflow y el motor avanza, y (5) aislamiento multi-tenant de
/// definiciones y respuestas.
/// </summary>
public abstract class DynamicFormsTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected DynamicFormsTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    // ---- (1) CRUD + round-trip del documento ----

    [Fact]
    public async Task DefinitionCrud_AndResponseRoundTrip_DataJsonIsIdentical()
    {
        var seed = await SeedTenantAsync("Forms RoundTrip");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var definitions = BuildDefinitionService(ctx, seed);
        var responses = BuildResponseService(ctx, seed);

        var definition = await BuildDemoDefinitionAsync(definitions);
        Assert.Equal(FormStatus.Active, definition.Status);
        Assert.Equal(2, definition.Containers.Count);
        Assert.Equal(6, definition.Questions.Count);

        // FieldCode duplicado se rechaza (clave del documento JSON).
        var duplicated = await definitions.AddQuestionAsync(definition.Id, new SaveFormQuestionRequest(
            null, "nombre", "Duplicada", FormControlType.Text));
        Assert.Equal(FormServiceStatus.Invalid, duplicated.Status);

        // Select sin opciones se rechaza.
        var withoutOptions = await definitions.AddQuestionAsync(definition.Id, new SaveFormQuestionRequest(
            null, "sin_opciones", "Sin opciones", FormControlType.Select));
        Assert.Equal(FormServiceStatus.Invalid, withoutOptions.Status);

        // Pattern no compilable se rechaza al guardar la pregunta.
        var badPattern = await definitions.AddQuestionAsync(definition.Id, new SaveFormQuestionRequest(
            null, "regex_rota", "Regex rota", FormControlType.Text,
            ValidationJson: """{"pattern":"(["}"""));
        Assert.Equal(FormServiceStatus.Invalid, badPattern.Status);

        // Round-trip: draft -> submit valido -> leer y comparar documento campo a campo.
        var draft = await responses.GetOrCreateDraftAsync(definition.Id, "REF-001");
        Assert.True(draft.IsOk, draft.Error);
        var data = ValidDocument();
        var submitted = await responses.SaveAsync(draft.Value!.Id, data, submit: true, seed.TenantUserId);
        Assert.True(submitted.IsOk, submitted.Error);
        Assert.Equal(FormResponseStatus.Submitted, submitted.Value!.Status);
        Assert.NotNull(submitted.Value.SubmittedAt);
        Assert.Equal(seed.TenantUserId, submitted.Value.SubmittedByTenantUserId);

        var read = await responses.GetAsync(draft.Value.Id);
        Assert.NotNull(read);
        Assert.Equal(data.Count, read!.Data.Count);
        foreach (var (fieldCode, expected) in data)
        {
            var actual = read.Data[fieldCode];
            Assert.Equal(expected.Value, actual.Value);
        }
        // El tipo persiste junto al valor ({ value, type }).
        Assert.Equal("Number", read.Data["cantidad"].Type);
        Assert.Equal("MultiCheck", read.Data["canales"].Type);

        // GetOrCreateDraft con la misma referencia NO reabre la respuesta enviada.
        var second = await responses.GetOrCreateDraftAsync(definition.Id, "REF-001");
        Assert.True(second.IsOk);
        Assert.NotEqual(draft.Value.Id, second.Value!.Id);
    }

    // ---- (2) Validacion servidor en el submit ----

    [Fact]
    public async Task Submit_WithInvalidData_ReturnsValidationErrorsPerField()
    {
        var seed = await SeedTenantAsync("Forms Validacion");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var definitions = BuildDefinitionService(ctx, seed);
        var responses = BuildResponseService(ctx, seed);
        var definition = await BuildDemoDefinitionAsync(definitions);

        var draft = await responses.GetOrCreateDraftAsync(definition.Id, "REF-BAD");
        var invalid = new Dictionary<string, FormFieldValue>
        {
            // nombre: required ausente (no se manda)
            ["email"] = new("no-es-un-correo", "Text"),      // pattern
            ["cantidad"] = new("99999", "Number"),           // fuera de rango (max 1000)
            ["prioridad"] = new("urgentisima", "Radio"),     // opcion inexistente
            ["canales"] = new("""["whatsapp","fax"]""", "MultiCheck"), // opcion invalida
            ["fecha"] = new("31-99-2026", "Date")            // fecha invalida
        };
        var result = await responses.SaveAsync(draft.Value!.Id, invalid, submit: true);

        Assert.Equal(FormServiceStatus.ValidationFailed, result.Status);
        Assert.NotNull(result.FieldErrors);
        var errors = result.FieldErrors!;
        Assert.Equal("Este campo es obligatorio.", errors["nombre"]);
        Assert.Equal("El valor no tiene el formato esperado.", errors["email"]);
        Assert.Equal("El valor maximo es 1000.", errors["cantidad"]);
        Assert.Equal("Selecciona una opcion valida.", errors["prioridad"]);
        Assert.Equal("Hay opciones seleccionadas que no son validas.", errors["canales"]);
        Assert.Equal("Ingresa una fecha valida.", errors["fecha"]);

        // La respuesta sigue Draft y sin datos persistidos del intento fallido.
        var entity = await ctx.FormResponses.AsNoTracking().SingleAsync(r => r.Id == draft.Value.Id);
        Assert.Equal(FormResponseStatus.Draft, entity.Status);
        Assert.Equal("{}", entity.Data);

        // El autosave (submit=false) NO valida: persiste el borrador tal cual.
        var autosaved = await responses.SaveAsync(draft.Value.Id, invalid, submit: false);
        Assert.True(autosaved.IsOk, autosaved.Error);
        Assert.Equal(FormResponseStatus.Draft, autosaved.Value!.Status);
    }

    // ---- (3) Ciclo de vida del token + scoping cross-tenant ----

    [Fact]
    public async Task TokenLifecycle_EmitValidateUseExpireRevoke_AndTenantScoping()
    {
        var seedA = await SeedTenantAsync("Forms Token A");
        var seedB = await SeedTenantAsync("Forms Token B");

        Guid definitionId;
        string clearToken;
        Guid tokenId;
        await using (var ctxA = _fixture.CreateContext(seedA.TenantId))
        {
            var definitions = BuildDefinitionService(ctxA, seedA);
            var tokens = BuildTokenService(ctxA, seedA);
            var definition = await BuildDemoDefinitionAsync(definitions);
            definitionId = definition.Id;

            // Emitir: el token viaja en claro UNA vez; en la base solo queda el hash.
            var emitted = await tokens.EmitAsync(definition.Id, new EmitFormTokenRequest(
                Reference: "REF-TOKEN", ExpirationHours: 24, SingleUse: true));
            Assert.True(emitted.IsOk, emitted.Error);
            clearToken = emitted.Value!.Token;
            tokenId = emitted.Value.TokenId;
            var stored = await ctxA.FormTokens.AsNoTracking().SingleAsync(t => t.Id == tokenId);
            Assert.Equal(FormTokenService.HashToken(clearToken), stored.TokenHash);
            Assert.DoesNotContain(clearToken, stored.TokenHash);

            // Validar OK con las 4 verificaciones.
            var validation = await tokens.ValidateAsync(clearToken);
            Assert.True(validation.IsValid);
            Assert.Equal(seedA.TenantId, validation.TenantId);
            Assert.Equal(definition.Id, validation.DefinitionId);
            Assert.Equal("REF-TOKEN", validation.Reference);

            // Usar (single-use) -> revalidar falla.
            await tokens.MarkUsedAsync(tokenId);
            Assert.False((await tokens.ValidateAsync(clearToken)).IsValid);

            // Token expirado falla.
            var expired = await tokens.EmitAsync(definition.Id, new EmitFormTokenRequest(ExpirationHours: 1));
            var expiredEntity = await ctxA.FormTokens.SingleAsync(t => t.Id == expired.Value!.TokenId);
            expiredEntity.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            await ctxA.SaveChangesAsync();
            Assert.False((await tokens.ValidateAsync(expired.Value!.Token)).IsValid);

            // Token revocado falla.
            var revoked = await tokens.EmitAsync(definition.Id, new EmitFormTokenRequest());
            Assert.True((await tokens.ValidateAsync(revoked.Value!.Token)).IsValid);
            Assert.True((await tokens.RevokeAsync(revoked.Value.TokenId)).IsOk);
            Assert.False((await tokens.ValidateAsync(revoked.Value.Token)).IsValid);

            // Token garabateado falla.
            Assert.False((await tokens.ValidateAsync("token-inexistente")).IsValid);
        }

        // Scoping: en el contexto del tenant B, el DbSet NO ve los tokens de A (filtro
        // global intacto); la resolucion anonima por hash es la UNICA via cross-tenant y
        // devuelve el tenant DUENO del token (A), nunca el del contexto.
        await using (var ctxB = _fixture.CreateContext(seedB.TenantId))
        {
            Assert.Empty(await ctxB.FormTokens.ToListAsync());
            var tokensB = BuildTokenService(ctxB, seedB);
            // Un token nuevo de A (no usado) emitido arriba seria valido; el single-use ya
            // usado sigue reportando invalido tambien desde B (mismas 4 verificaciones).
            Assert.False((await tokensB.ValidateAsync(clearToken)).IsValid);
            // RevokeAsync desde B no alcanza el token de A (tenant-scoped).
            var revokeCross = await tokensB.RevokeAsync(tokenId);
            Assert.Equal(FormServiceStatus.NotFound, revokeCross.Status);
        }

        // Un token vigente de A resuelto SIN tenant B de por medio: emitimos otro en A y lo
        // validamos desde el contexto de B para verificar que devuelve TenantId de A.
        string freshToken;
        await using (var ctxA2 = _fixture.CreateContext(seedA.TenantId))
        {
            var tokensA = BuildTokenService(ctxA2, seedA);
            freshToken = (await tokensA.EmitAsync(definitionId, new EmitFormTokenRequest())).Value!.Token;
        }
        await using (var ctxB2 = _fixture.CreateContext(seedB.TenantId))
        {
            var tokensB = BuildTokenService(ctxB2, seedB);
            var crossValidation = await tokensB.ValidateAsync(freshToken);
            Assert.True(crossValidation.IsValid);
            Assert.Equal(seedA.TenantId, crossValidation.TenantId); // tenant del TOKEN, no del contexto
        }
    }

    // ---- (4) Submit del formulario vinculado completa el paso del flujo ----

    [Fact]
    public async Task SubmittingLinkedForm_CompletesWorkflowStep_AndEngineAdvances()
    {
        var seed = await SeedTenantAsync("Forms Flujo");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var engine = BuildEngine(ctx, seed);
        var definitions = BuildDefinitionService(ctx, seed);
        var responses = BuildResponseService(ctx, seed, engine);

        // Flujo lineal publicado y anclado al ActivityType + formulario asignado a Task_A.
        var workflow = (await engine.ImportBpmnAsync(new ImportBpmnRequest("FRM-FLOW", "Flujo con formulario", LinearXml))).Value!;
        Assert.True((await engine.PublishAsync(workflow.Id)).IsOk);
        var activityType = await ctx.ActivityTypes.SingleAsync(t => t.Id == seed.ActivityTypeId);
        activityType.WorkflowDefinitionId = workflow.Id;
        await ctx.SaveChangesAsync();

        var form = await BuildDemoDefinitionAsync(definitions);
        var nodeA = workflow.Nodes.Single(n => n.BpmnElementId == "Task_A");
        Assert.True((await definitions.AssignToWorkflowNodeAsync(nodeA.Id, form.Id)).IsOk);
        Assert.Equal(form.Id, await definitions.GetWorkflowNodeFormAsync(nodeA.Id));

        // Crear la tarea via TaskItemService: el flujo arranca y Task_A queda current.
        var taskService = BuildTaskService(ctx, seed, engine);
        var created = await taskService.CreateAsync(
            new CreateTaskItemRequest("Tarea con formulario", seed.ActivityTypeId), seed.PlatformUserId, "Tester");
        Assert.True(created.IsOk, created.Error);
        var taskId = created.Value!.Item.Id;
        var instance = await ctx.WorkflowInstances.AsNoTracking().SingleAsync(i => i.TaskItemId == taskId);

        // GetTaskStepFormsAsync asegura borrador + link Pending (idempotente).
        var stepForms = await responses.GetTaskStepFormsAsync(taskId);
        var stepForm = Assert.Single(stepForms);
        Assert.Equal(form.Id, stepForm.DefinitionId);
        Assert.Equal(FormFlowLinkStatus.Pending, stepForm.LinkStatus);
        Assert.Equal(created.Value.Item.Number, stepForm.Reference);
        var again = await responses.GetTaskStepFormsAsync(taskId);
        Assert.Equal(stepForm.ResponseId, Assert.Single(again).ResponseId);
        Assert.Equal(1, await ctx.FormFlowLinks.CountAsync());

        // Submit del formulario -> link Completed y el motor avanza a Task_B.
        var submitted = await responses.SaveAsync(stepForm.ResponseId, ValidDocument(), submit: true, seed.TenantUserId);
        Assert.True(submitted.IsOk, submitted.Error);

        var link = await ctx.FormFlowLinks.AsNoTracking().SingleAsync();
        Assert.Equal(FormFlowLinkStatus.Completed, link.Status);
        var current = Assert.Single(await engine.GetCurrentStepsAsync(instance.Id));
        Assert.Equal("Task_B", current.BpmnElementId);

        // El paso de Task_A quedo Completed con el ejecutor del submit.
        var stepA = await ctx.WorkflowStepHistories.AsNoTracking()
            .SingleAsync(s => s.InstanceId == instance.Id && s.NodeId == nodeA.Id);
        Assert.Equal(WorkflowStepStatus.Completed, stepA.Status);
        Assert.Equal(seed.TenantUserId, stepA.ExecutedByTenantUserId);
    }

    [Fact]
    public async Task SubmittingFormWithGatewayAhead_PropagatesDecision_AndEngineResolvesGateway()
    {
        var seed = await SeedTenantAsync("Forms Gateway");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var engine = BuildEngine(ctx, seed);
        var definitions = BuildDefinitionService(ctx, seed);
        var responses = BuildResponseService(ctx, seed, engine);

        // Flujo con compuerta publicado + formulario asignado a Task_Cot (nodo con gateway adelante).
        var workflow = (await engine.ImportBpmnAsync(new ImportBpmnRequest("FRM-GW", "Flujo con compuerta y formulario", GatewayFormXml))).Value!;
        var restartEnd = workflow.Nodes.Single(n => n.BpmnElementId == "End_Re");
        var restartTarget = workflow.Nodes.Single(n => n.BpmnElementId == "Task_Cot");
        Assert.True((await engine.SetRestartTargetAsync(restartEnd.Id, restartTarget.Id)).IsOk);
        Assert.True((await engine.PublishAsync(workflow.Id)).IsOk);
        var activityType = await ctx.ActivityTypes.SingleAsync(t => t.Id == seed.ActivityTypeId);
        activityType.WorkflowDefinitionId = workflow.Id;
        await ctx.SaveChangesAsync();

        var form = await BuildDemoDefinitionAsync(definitions);
        var cotNode = workflow.Nodes.Single(n => n.BpmnElementId == "Task_Cot");
        Assert.True((await definitions.AssignToWorkflowNodeAsync(cotNode.Id, form.Id)).IsOk);

        var taskService = BuildTaskService(ctx, seed, engine);
        var created = await taskService.CreateAsync(
            new CreateTaskItemRequest("Tarea form+gateway", seed.ActivityTypeId), seed.PlatformUserId, "Tester");
        Assert.True(created.IsOk, created.Error);
        var taskId = created.Value!.Item.Id;
        var instance = await ctx.WorkflowInstances.AsNoTracking().SingleAsync(i => i.TaskItemId == taskId);

        // Requerimiento no tiene formulario: se completa con el motor para llegar a Cotizacion.
        var reqStep = Assert.Single(await engine.GetCurrentStepsAsync(instance.Id));
        Assert.Equal("Task_Req", reqStep.BpmnElementId);
        Assert.True((await engine.CompleteStepAsync(instance.Id, reqStep.Id, seed.TenantUserId)).IsOk);

        // El formulario del paso Cotizacion trae la compuerta adelante y sus opciones (ADR-0037).
        var stepForm = Assert.Single(await responses.GetTaskStepFormsAsync(taskId));
        Assert.Equal("Cotizacion", stepForm.NodeName);
        Assert.True(stepForm.IsGatewayAhead);
        Assert.Equal(new[] { "Aprobada", "Rechazada" }, stepForm.ApprovalOptions!.OrderBy(o => o, StringComparer.Ordinal));

        // Enviar el formulario CON decision Aprobada: el paso lleva el ApprovalResult, el motor
        // resuelve la compuerta en su cascada y enruta a Facturacion (gateway NO queda pendiente).
        var submitted = await responses.SaveAsync(
            stepForm.ResponseId, ValidDocument(), submit: true, seed.TenantUserId, approvalResult: "Aprobada");
        Assert.True(submitted.IsOk, submitted.Error);

        var current = Assert.Single(await engine.GetCurrentStepsAsync(instance.Id));
        Assert.Equal("Task_Fac", current.BpmnElementId);

        // El gateway quedo Completed (no Pending-current) heredando la decision.
        var gwStep = await ctx.WorkflowStepHistories.AsNoTracking()
            .Join(ctx.WorkflowNodes.AsNoTracking(), s => s.NodeId, n => n.Id, (s, n) => new { s, n })
            .Where(x => x.s.InstanceId == instance.Id && x.n.NodeType == WorkflowNodeType.ExclusiveGateway)
            .Select(x => x.s).SingleAsync();
        Assert.Equal(WorkflowStepStatus.Completed, gwStep.Status);
        Assert.False(gwStep.IsCurrent);
        Assert.Equal("Aprobada", gwStep.ApprovalResult);
    }

    // ---- (5) Aislamiento multi-tenant ----

    [Fact]
    public async Task DefinitionsAndResponses_AreIsolatedBetweenTenants()
    {
        var seedA = await SeedTenantAsync("Forms Tenant A");
        var seedB = await SeedTenantAsync("Forms Tenant B");

        Guid definitionId;
        Guid responseId;
        await using (var ctxA = _fixture.CreateContext(seedA.TenantId))
        {
            var definitions = BuildDefinitionService(ctxA, seedA);
            var responses = BuildResponseService(ctxA, seedA);
            var definition = await BuildDemoDefinitionAsync(definitions);
            definitionId = definition.Id;
            responseId = (await responses.GetOrCreateDraftAsync(definition.Id, "AISLADA")).Value!.Id;
        }

        await using (var ctxB = _fixture.CreateContext(seedB.TenantId))
        {
            Assert.Empty(await ctxB.FormDefinitions.ToListAsync());
            Assert.Empty(await ctxB.FormQuestions.ToListAsync());
            Assert.Empty(await ctxB.FormResponses.ToListAsync());

            var definitionsB = BuildDefinitionService(ctxB, seedB);
            var responsesB = BuildResponseService(ctxB, seedB);
            Assert.Empty(await definitionsB.ListAsync(includeArchived: true));
            Assert.Null(await definitionsB.GetAsync(definitionId));
            Assert.Null(await responsesB.GetAsync(responseId));
            var draft = await responsesB.GetOrCreateDraftAsync(definitionId, "X");
            Assert.Equal(FormServiceStatus.NotFound, draft.Status);
            var save = await responsesB.SaveAsync(responseId, ValidDocument(), submit: true);
            Assert.Equal(FormServiceStatus.NotFound, save.Status);
        }
    }

    // ---- Helpers ----

    /// <summary>start -> Task_A -> Task_B -> end (mismo fixture sintetico del motor).</summary>
    private const string LinearXml = """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="frm" targetNamespace="http://ecorex.local/bpmn">
          <bpmn:process id="P_Forms">
            <bpmn:startEvent id="Start_1" name="Inicio" />
            <bpmn:task id="Task_A" name="Diligenciar formulario" />
            <bpmn:task id="Task_B" name="Revisar" />
            <bpmn:endEvent id="End_1" name="Fin" />
            <bpmn:sequenceFlow id="F1" sourceRef="Start_1" targetRef="Task_A" />
            <bpmn:sequenceFlow id="F2" sourceRef="Task_A" targetRef="Task_B" />
            <bpmn:sequenceFlow id="F3" sourceRef="Task_B" targetRef="End_1" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>
    /// start -> Requerimiento -> Cotizacion (con formulario) -> gateway Aprobacion;
    /// Aprobada -> Facturacion -> end; Rechazada -> End_Re (reinicio hacia Cotizacion).
    /// </summary>
    private const string GatewayFormXml = """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                          id="frmgw" targetNamespace="http://ecorex.local/bpmn">
          <bpmn:process id="P_FormsGw">
            <bpmn:startEvent id="Start_1" name="Inicio" />
            <bpmn:task id="Task_Req" name="Requerimiento" />
            <bpmn:task id="Task_Cot" name="Cotizacion" />
            <bpmn:exclusiveGateway id="Gw_Ap" name="Aprobacion" />
            <bpmn:task id="Task_Fac" name="Facturacion" />
            <bpmn:endEvent id="End_1" name="Fin" />
            <bpmn:endEvent id="End_Re" name="Rechazada" />
            <bpmn:sequenceFlow id="F1" sourceRef="Start_1" targetRef="Task_Req" />
            <bpmn:sequenceFlow id="F2" sourceRef="Task_Req" targetRef="Task_Cot" />
            <bpmn:sequenceFlow id="F3" sourceRef="Task_Cot" targetRef="Gw_Ap" />
            <bpmn:sequenceFlow id="F4" name="Aprobada" sourceRef="Gw_Ap" targetRef="Task_Fac">
              <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">approval == 'Aprobada'</bpmn:conditionExpression>
            </bpmn:sequenceFlow>
            <bpmn:sequenceFlow id="F5" name="Rechazada" sourceRef="Gw_Ap" targetRef="End_Re">
              <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">approval == 'Rechazada'</bpmn:conditionExpression>
            </bpmn:sequenceFlow>
            <bpmn:sequenceFlow id="F6" sourceRef="Task_Fac" targetRef="End_1" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>
    /// Definicion demo ACTIVA con 2 contenedores y 6 preguntas Tier 1 variadas:
    /// nombre (Text req), email (Text pattern), prioridad (Radio), canales (MultiCheck),
    /// cantidad (Number 1..1000) y fecha (Date opcional).
    /// </summary>
    private static async Task<FormDefinitionDetailDto> BuildDemoDefinitionAsync(IFormDefinitionService definitions)
    {
        var created = await definitions.CreateAsync(new CreateFormDefinitionRequest(
            "FRM-T" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), "Formulario de prueba"));
        Assert.True(created.IsOk, created.Error);
        var definition = created.Value!;

        var c1 = (await definitions.AddContainerAsync(definition.Id, new SaveFormContainerRequest("Datos"))).Value!;
        var c2 = (await definitions.AddContainerAsync(definition.Id, new SaveFormContainerRequest("Detalle"))).Value!;

        async Task AddAsync(SaveFormQuestionRequest request)
        {
            var result = await definitions.AddQuestionAsync(definition.Id, request);
            Assert.True(result.IsOk, result.Error);
        }

        await AddAsync(new SaveFormQuestionRequest(c1.Id, "nombre", "Nombre", FormControlType.Text,
            Required: true, GridCol: "col-md-6", ValidationJson: """{"minLength":3,"maxLength":100}"""));
        await AddAsync(new SaveFormQuestionRequest(c1.Id, "email", "Correo", FormControlType.Text,
            Required: false, GridCol: "col-md-6",
            ValidationJson: """{"pattern":"^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}$"}"""));
        await AddAsync(new SaveFormQuestionRequest(c2.Id, "prioridad", "Prioridad", FormControlType.Radio,
            Required: true,
            OptionsJson: """[{"id":"alta","label":"Alta"},{"id":"media","label":"Media"},{"id":"baja","label":"Baja"}]"""));
        await AddAsync(new SaveFormQuestionRequest(c2.Id, "canales", "Canales", FormControlType.MultiCheck,
            Required: false,
            OptionsJson: """[{"id":"email","label":"Email"},{"id":"whatsapp","label":"WhatsApp"},{"id":"llamada","label":"Llamada"}]"""));
        await AddAsync(new SaveFormQuestionRequest(c2.Id, "cantidad", "Cantidad", FormControlType.Number,
            Required: true, GridCol: "col-md-4", ValidationJson: """{"minValue":1,"maxValue":1000}"""));
        await AddAsync(new SaveFormQuestionRequest(c2.Id, "fecha", "Fecha", FormControlType.Date,
            Required: false, GridCol: "col-md-4"));

        var activated = await definitions.ActivateAsync(definition.Id);
        Assert.True(activated.IsOk, activated.Error);
        return activated.Value!;
    }

    private static Dictionary<string, FormFieldValue> ValidDocument() => new()
    {
        ["nombre"] = new("Cliente Alfa SAS", "Text"),
        ["email"] = new("compras@cliente-alfa.example", "Text"),
        ["prioridad"] = new("alta", "Radio"),
        ["canales"] = new("""["email","whatsapp"]""", "MultiCheck"),
        ["cantidad"] = new("25", "Number"),
        ["fecha"] = new("2026-07-15", "Date")
    };

    // ---- (6) Constructor del prototipo (ADR-0021): campos nuevos + tabla funcional ----

    [Fact]
    public async Task BuilderFields_RoundTrip_WidthSyncAndContainers()
    {
        var seed = await SeedTenantAsync("Forms Builder RoundTrip");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var definitions = BuildDefinitionService(ctx, seed);

        var created = await definitions.CreateAsync(new CreateFormDefinitionRequest(
            "FRM-B" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), "Constructor"));
        Assert.True(created.IsOk, created.Error);
        var definition = created.Value!;

        // Contenedor Row con Width y Tabs con TabsJson: round-trip completo.
        var row = await definitions.AddContainerAsync(definition.Id, new SaveFormContainerRequest(
            "Datos del cliente", FormContainerType.Row, Width: 12));
        Assert.True(row.IsOk, row.Error);
        var tabs = await definitions.AddContainerAsync(definition.Id, new SaveFormContainerRequest(
            "Pestanas", FormContainerType.Tabs, TabsJson: """["General","Detalle"]""", IsHidden: true));
        Assert.True(tabs.IsOk, tabs.Error);

        var detail = await definitions.GetAsync(definition.Id);
        var rowDto = detail!.Containers.Single(c => c.Id == row.Value!.Id);
        Assert.Equal(FormContainerType.Row, rowDto.ContainerType);
        Assert.Equal(12, rowDto.Width);
        var tabsDto = detail.Containers.Single(c => c.Id == tabs.Value!.Id);
        Assert.Equal(FormContainerType.Tabs, tabsDto.ContainerType);
        // jsonb (PG) normaliza el formato del JSON: comparar el contenido parseado.
        var tabNames = System.Text.Json.JsonSerializer.Deserialize<List<string>>(tabsDto.TabsJson!);
        Assert.Equal(new[] { "General", "Detalle" }, tabNames);
        Assert.True(tabsDto.IsHidden);

        // Width manda y GridCol queda sincronizado (col-md-5).
        var q1 = await definitions.AddQuestionAsync(definition.Id, new SaveFormQuestionRequest(
            row.Value!.Id, "cc", "CC", FormControlType.Text,
            Width: 5, PlaceholderText: "Ingrese documento", DefaultValue: "SIN-CC", IsLocked: true));
        Assert.True(q1.IsOk, q1.Error);
        Assert.Equal(5, q1.Value!.Width);
        Assert.Equal("col-md-5", q1.Value.GridCol);
        Assert.Equal("Ingrese documento", q1.Value.PlaceholderText);
        Assert.Equal("SIN-CC", q1.Value.DefaultValue);
        Assert.True(q1.Value.IsLocked);

        // Compatibilidad: Width default (12) + GridCol legacy parseable -> Width derivado.
        var q2 = await definitions.AddQuestionAsync(definition.Id, new SaveFormQuestionRequest(
            row.Value.Id, "legacy", "Legacy", FormControlType.Text, GridCol: "col-md-4"));
        Assert.True(q2.IsOk, q2.Error);
        Assert.Equal(4, q2.Value!.Width);
        Assert.Equal("col-md-4", q2.Value.GridCol);

        // Requerido en multimedia placeholder se apaga al guardar (ADR-0021).
        var firma = await definitions.AddQuestionAsync(definition.Id, new SaveFormQuestionRequest(
            null, "firma", "Firma", FormControlType.Signature, Required: true));
        Assert.True(firma.IsOk, firma.Error);
        Assert.False(firma.Value!.Required);

        // MoveQuestionToAsync: mueve 'legacy' a la raiz en el indice 0 y renumera.
        var moved = await definitions.MoveQuestionToAsync(q2.Value.Id, null, 0);
        Assert.True(moved.IsOk, moved.Error);
        var afterMove = await definitions.GetAsync(definition.Id);
        var movedDto = afterMove!.Questions.Single(q => q.Id == q2.Value.Id);
        Assert.Null(movedDto.ContainerId);
        Assert.Equal(0, movedDto.SortOrder);

        // MoveContainerToAsync: tabs pasa a ser hijo de row.
        var movedContainer = await definitions.MoveContainerToAsync(tabs.Value!.Id, row.Value.Id, 0);
        Assert.True(movedContainer.IsOk, movedContainer.Error);
        var afterContainerMove = await definitions.GetAsync(definition.Id);
        Assert.Equal(row.Value.Id, afterContainerMove!.Containers.Single(c => c.Id == tabs.Value.Id).ParentId);

        // Ciclo prohibido: row no puede colgar de su descendiente tabs.
        var cycle = await definitions.MoveContainerToAsync(row.Value.Id, tabs.Value.Id, 0);
        Assert.Equal(FormServiceStatus.Invalid, cycle.Status);
    }

    [Fact]
    public async Task GridDetail_SubmitRoundTrip_AndHiddenRequiredIsSkipped()
    {
        var seed = await SeedTenantAsync("Forms Tabla");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var definitions = BuildDefinitionService(ctx, seed);
        var responses = BuildResponseService(ctx, seed);

        var created = await definitions.CreateAsync(new CreateFormDefinitionRequest(
            "FRM-G" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), "Tabla funcional"));
        var definition = created.Value!;

        // Tabla sin columnas se rechaza (estructura, ADR-0021).
        var noColumns = await definitions.AddQuestionAsync(definition.Id, new SaveFormQuestionRequest(
            null, "sin_columnas", "Sin columnas", FormControlType.GridDetail));
        Assert.Equal(FormServiceStatus.Invalid, noColumns.Status);

        var grid = await definitions.AddQuestionAsync(definition.Id, new SaveFormQuestionRequest(
            null, "equipos", "Equipos", FormControlType.GridDetail, Required: true,
            OptionsJson: """[{"id":"equipo","label":"Equipo"},{"id":"serial","label":"Serial"}]"""));
        Assert.True(grid.IsOk, grid.Error);

        // Campo requerido pero OCULTO por el disenador: la validacion lo salta.
        var hidden = await definitions.AddQuestionAsync(definition.Id, new SaveFormQuestionRequest(
            null, "oculto_req", "Oculto requerido", FormControlType.Text, Required: true, IsHidden: true));
        Assert.True(hidden.IsOk, hidden.Error);

        var activated = await definitions.ActivateAsync(definition.Id);
        Assert.True(activated.IsOk, activated.Error);

        // Submit sin filas: la tabla requerida bloquea.
        var draft = await responses.GetOrCreateDraftAsync(definition.Id, "REF-GRID");
        var empty = await responses.SaveAsync(draft.Value!.Id,
            new Dictionary<string, FormFieldValue>(), submit: true, seed.TenantUserId);
        Assert.Equal(FormServiceStatus.ValidationFailed, empty.Status);
        Assert.Equal("Este campo es obligatorio.", empty.FieldErrors!["equipos"]);
        Assert.False(empty.FieldErrors.ContainsKey("oculto_req"));

        // Submit con filas: round-trip identico del arreglo JSON de filas.
        const string rows = """[{"equipo":"Switch 24p","serial":"SN-001"},{"equipo":"AP Wifi","serial":"SN-002"}]""";
        var ok = await responses.SaveAsync(draft.Value.Id,
            new Dictionary<string, FormFieldValue> { ["equipos"] = new(rows, "GridDetail") },
            submit: true, seed.TenantUserId);
        Assert.True(ok.IsOk, ok.Error);

        var read = await responses.GetAsync(draft.Value.Id);
        Assert.Equal(rows, read!.Data["equipos"].Value);
        Assert.Equal("GridDetail", read.Data["equipos"].Type);
        var parsed = FormFieldValidator.ParseGridRows(read.Data["equipos"].Value);
        Assert.Equal(2, parsed.Count);
        Assert.Equal("SN-002", parsed[1]["serial"]);
    }

    private static FormDefinitionService BuildDefinitionService(EcorexDbContext ctx, SeedData seed)
    {
        var tenant = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        return new(ctx, tenant, new Ecorex.Application.MenuConfig.MenuConfigService(ctx, tenant));
    }

    private static FormResponseService BuildResponseService(EcorexDbContext ctx, SeedData seed, IWorkflowEngine? engine = null)
    {
        var tenant = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        return new(ctx, engine ?? BuildEngine(ctx, seed),
            new SequenceService(ctx, tenant), tenant, new NoOpFormRecordBroadcaster());
    }

    private static FormTokenService BuildTokenService(EcorexDbContext ctx, SeedData seed)
        => new(ctx, new TestTenantContext(seed.TenantId, seed.PlatformUserId));

    private static WorkflowEngine BuildEngine(EcorexDbContext ctx, SeedData seed)
        => new(ctx, new TestTenantContext(seed.TenantId, seed.PlatformUserId),
            new NoOpWorkflowRuleHook(), new NoOpTaskBroadcaster());

    private static TaskItemService BuildTaskService(EcorexDbContext ctx, SeedData seed, IWorkflowEngine engine)
    {
        var tenantContext = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        return new TaskItemService(ctx, tenantContext, new SequenceService(ctx, tenantContext), engine, new NoOpEmailSender(), new NodeAssigneeResolver(ctx));
    }

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
                Email = $"user-{tenantId:N}@forms.test",
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
                Name = "Con formulario"
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
public sealed class DynamicFormsTests_Postgres
    : DynamicFormsTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public DynamicFormsTests_Postgres(PostgresTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class DynamicFormsTests_SqlServer
    : DynamicFormsTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public DynamicFormsTests_SqlServer(SqlServerTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}
