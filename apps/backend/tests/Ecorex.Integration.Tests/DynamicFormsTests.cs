using Ecorex.Application.Common;
using Ecorex.Application.Organization;
using Ecorex.Application.Forms;
using Ecorex.Application.Storage;
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

        // GetOrCreateDraft con la misma referencia REABRE la respuesta ya ENVIADA (ADR-0132:
        // la enviada tiene prioridad), en vez de crear un draft nuevo VACIO que la tape. Esto es
        // el guard anti-reemplazo que arreglo el caso T00521 (una cotizacion enviada quedaba
        // tapada por un draft en blanco al reabrir la tarea). Candado anti-regresion de esa ADR.
        var second = await responses.GetOrCreateDraftAsync(definition.Id, "REF-001");
        Assert.True(second.IsOk);
        Assert.Equal(draft.Value.Id, second.Value!.Id);
        Assert.Equal(FormResponseStatus.Submitted, second.Value.Status);
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

    // ---- Galeria/Marketplace: el import (traer) conserva el diseno de cabecera (fix 8c64e82f) ----

    [Fact]
    public async Task Export_Then_Import_CarriesCustomCssAndHeaderDesign()
    {
        var seed = await SeedTenantAsync("Forms Export Import CSS");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var definitions = BuildDefinitionService(ctx, seed);

        // Formulario origen con diseno de cabecera: es justo lo que la galeria debe conservar.
        var created = await definitions.CreateAsync(new CreateFormDefinitionRequest(
            "FRM-CSS" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), "Formulario con diseno"));
        Assert.True(created.IsOk, created.Error);
        var srcId = created.Value!.Id;

        const string css = "@scope{ .dfr-heading{ background:#1f3b73 !important; color:#fff; } }";
        const string theme = """{"skin":"dian"}""";
        const string ladder = """["Borrador","Enviado","Aprobado"]""";
        const string closeRule = """{"on":"Aprobado"}""";
        Assert.True((await definitions.SetCustomCssAsync(srcId, new SetFormCssRequest(css))).IsOk);
        Assert.True((await definitions.SetThemeAsync(srcId, theme)).IsOk);
        Assert.True((await definitions.SetStatusLadderAsync(srcId, ladder)).IsOk);
        Assert.True((await definitions.SetCloseRuleAsync(srcId, closeRule)).IsOk);

        // Una pregunta con calc/format para confirmar que el contenido tambien viaja.
        Assert.True((await definitions.AddQuestionAsync(srcId, new SaveFormQuestionRequest(
            null, "total", "Total", FormControlType.Number, Format: "currency",
            CalcExpression: "{a}+{b}"))).IsOk);

        // Export -> Import crea un formulario NUEVO, igual que "traer de la galeria".
        var export = await definitions.ExportAsync(srcId);
        Assert.True(export.IsOk, export.Error);
        var imported = await definitions.ImportAsync(export.Value!);
        Assert.True(imported.IsOk, imported.Error);
        var newId = imported.Value!.Id;
        Assert.NotEqual(srcId, newId);

        // Regresion del fix: el importado conserva CSS, tema, escalon de estados y regla de cierre.
        var detail = await definitions.GetAsync(newId);
        Assert.NotNull(detail);
        Assert.Equal(css, detail!.CustomCss);
        Assert.False(string.IsNullOrWhiteSpace(detail.ThemeJson));
        Assert.False(string.IsNullOrWhiteSpace(detail.StatusLadderJson));
        Assert.False(string.IsNullOrWhiteSpace(detail.CloseRuleJson));
        Assert.Equal(
            new[] { "Borrador", "Enviado", "Aprobado" },
            System.Text.Json.JsonSerializer.Deserialize<List<string>>(detail.StatusLadderJson!));

        // El contenido (pregunta con calc/format) tambien se trajo.
        var total = detail.Questions.Single(q => q.FieldCode == "total");
        Assert.Equal("currency", total.Format);
        Assert.Equal("{a}+{b}", total.CalcExpression);
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

    /// <summary>
    /// Motor de calculo del SIMULADOR DE COTIZACIONES de punta a punta y en AMBOS motores: la
    /// formula de columna usa funciones (SI / REDONDEAR.SUPERIOR), lee el ENCABEZADO con
    /// <c>{#iva_pct}</c> (C3) y el roll-up EXCLUYE las filas marcadas por <c>aggWhen</c> (C4).
    /// Lo que se verifica es que el SERVIDOR recalcula y persiste (el cliente no es fuente de
    /// verdad para montos): se mandan valores calculados falsos y deben quedar sobrescritos.
    /// </summary>
    [Fact]
    public async Task GridCalc_FunctionsHeaderRefAndConditionalAggregate_AreRecomputedOnServer()
    {
        var seed = await SeedTenantAsync("Forms Calculo");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var definitions = BuildDefinitionService(ctx, seed);
        var responses = BuildResponseService(ctx, seed);

        var created = await definitions.CreateAsync(new CreateFormDefinitionRequest(
            "FRM-C" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), "Simulador de cotizaciones"));
        var definition = created.Value!;

        // Encabezado: el % de IVA es editable POR COTIZACION, no una constante de la formula.
        var iva = await definitions.AddQuestionAsync(definition.Id, new SaveFormQuestionRequest(
            null, "iva_pct", "IVA %", FormControlType.Number));
        Assert.True(iva.IsOk, iva.Error);

        // Roll-ups del encabezado.
        foreach (var (code, label) in new[] { ("total_linea", "Total lineas"), ("total_iva", "Total IVA") })
        {
            var rollupField = await definitions.AddQuestionAsync(definition.Id, new SaveFormQuestionRequest(
                null, code, label, FormControlType.Number));
            Assert.True(rollupField.IsOk, rollupField.Error);
        }

        const string columns =
            """
            [{"id":"cantidad","label":"Cantidad"},
             {"id":"stock","label":"Stock"},
             {"id":"precio_base","label":"Precio base"},
             {"id":"mano_obra","label":"Mano de obra"},
             {"id":"p_unitario","label":"P. unitario","calc":"REDONDEAR.SUPERIOR({precio_base}+{mano_obra}; 1000)"},
             {"id":"sin_stock","label":"Sin stock","calc":"SI({cantidad} > {stock}; 1; 0)"},
             {"id":"subt_desc","label":"Subtotal","calc":"{p_unitario} * {cantidad}","agg":"Sum","rollup":"total_linea","aggWhen":"{sin_stock}=0"},
             {"id":"iva","label":"IVA","calc":"SI({exento_iva}=1; 0; {subt_desc}*{#iva_pct}/100)","agg":"Sum","rollup":"total_iva","aggWhen":"{sin_stock}=0"},
             {"id":"exento_iva","label":"Exento"}]
            """;
        var grid = await definitions.AddQuestionAsync(definition.Id, new SaveFormQuestionRequest(
            null, "lineas", "Lineas", FormControlType.GridDetail, OptionsJson: columns));
        Assert.True(grid.IsOk, grid.Error);

        var activated = await definitions.ActivateAsync(definition.Id);
        Assert.True(activated.IsOk, activated.Error);

        // Fila 1: hay stock, gravada. Fila 2: pide 5 con 3 en stock -> se marca y NO suma.
        // Fila 3: hay stock pero es EXENTA de IVA. Los valores calculados que manda el cliente
        // van adrede en 999999 para comprobar que el servidor los descarta.
        const string rows =
            """
            [{"cantidad":"2","stock":"10","precio_base":"1200000","mano_obra":"150000","exento_iva":"0","p_unitario":"999999","sin_stock":"999999","subt_desc":"999999","iva":"999999"},
             {"cantidad":"5","stock":"3","precio_base":"1000000","mano_obra":"1","exento_iva":"0"},
             {"cantidad":"1","stock":"9","precio_base":"500000","mano_obra":"0","exento_iva":"1"}]
            """;
        var draft = await responses.GetOrCreateDraftAsync(definition.Id, "REF-CALC");
        var saved = await responses.SaveAsync(draft.Value!.Id, new Dictionary<string, FormFieldValue>
        {
            ["iva_pct"] = new("19", "Number"),
            ["lineas"] = new(rows, "GridDetail"),
        }, submit: true, seed.TenantUserId);
        Assert.True(saved.IsOk, saved.Error);

        var read = await responses.GetAsync(draft.Value.Id);
        var parsed = FormFieldValidator.ParseGridRows(read!.Data["lineas"].Value);
        Assert.Equal(3, parsed.Count);

        // REDONDEAR.SUPERIOR con el multiplo COMO PARAMETRO: 1.350.000 sube al siguiente mil.
        Assert.Equal("1350000", parsed[0]["p_unitario"]);
        Assert.Equal("1001000", parsed[1]["p_unitario"]);   // 1.000.001 sube al siguiente mil
        Assert.Equal("500000", parsed[2]["p_unitario"]);

        // SI() + comparador: solo la fila 2 queda sin stock.
        Assert.Equal("0", parsed[0]["sin_stock"]);
        Assert.Equal("1", parsed[1]["sin_stock"]);
        Assert.Equal("0", parsed[2]["sin_stock"]);

        // {#iva_pct} viene del ENCABEZADO; la fila exenta no paga IVA.
        Assert.Equal("2700000", parsed[0]["subt_desc"]);
        Assert.Equal("513000", parsed[0]["iva"]);           // 2.700.000 * 19 / 100
        Assert.Equal("0", parsed[2]["iva"]);

        // Roll-ups al encabezado EXCLUYENDO la fila sin stock (aggWhen).
        Assert.Equal("3200000", read.Data["total_linea"].Value);   // 2.700.000 + 500.000
        Assert.Equal("513000", read.Data["total_iva"].Value);
    }

    [Fact]
    public async Task GridMaxFromAndOptionsFromGrid_BlocksOverLimit_AndAcceptsCrossGridOption()
    {
        // FEATURE A: una columna select con 'optionsFromGrid' toma su valor de otra grilla -> la validacion de
        // servidor NO debe rechazarlo como "opcion invalida" (no tiene opciones estaticas). FEATURE B: una columna
        // con 'maxFrom' no puede superar el valor resuelto de la otra grilla (ej. cantidad_entregada <= items.cantidad
        // del mismo item) -> al enviar un valor que lo excede, el servidor BLOQUEA (ValidationFailed); uno valido pasa.
        var seed = await SeedTenantAsync("Forms MaxFrom");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var definitions = BuildDefinitionService(ctx, seed);
        var responses = BuildResponseService(ctx, seed);

        var created = await definitions.CreateAsync(new CreateFormDefinitionRequest(
            "FRM-M" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), "Entregas"));
        var definition = created.Value!;

        var items = await definitions.AddQuestionAsync(definition.Id, new SaveFormQuestionRequest(
            null, "items", "Items", FormControlType.GridDetail,
            OptionsJson: """[{"id":"item","label":"Item"},{"id":"cantidad","label":"Cantidad"}]"""));
        Assert.True(items.IsOk, items.Error);

        const string entregasCols =
            """
            [{"id":"item","type":"select","label":"Item","optionsFromGrid":{"grid":"items","valueField":"item","labelField":"item"}},
             {"id":"entregar","label":"Entregar","maxFrom":{"grid":"items","field":"cantidad","match":{"item":"{item}"}}}]
            """;
        var entregas = await definitions.AddQuestionAsync(definition.Id, new SaveFormQuestionRequest(
            null, "entregas", "Entregas", FormControlType.GridDetail, OptionsJson: entregasCols));
        Assert.True(entregas.IsOk, entregas.Error);

        var activated = await definitions.ActivateAsync(definition.Id);
        Assert.True(activated.IsOk, activated.Error);

        const string itemsRows = """[{"item":"A","cantidad":"3"}]""";

        // Sobre-entrega: entregar=5 > items.cantidad(A)=3 -> BLOQUEA (y el item 'A' NO se rechaza por opcion invalida).
        var draft = await responses.GetOrCreateDraftAsync(definition.Id, "REF-MAX");
        var over = await responses.SaveAsync(draft.Value!.Id, new Dictionary<string, FormFieldValue>
        {
            ["items"] = new(itemsRows, "GridDetail"),
            ["entregas"] = new("""[{"item":"A","entregar":"5"}]""", "GridDetail"),
        }, submit: true, seed.TenantUserId);
        Assert.Equal(FormServiceStatus.ValidationFailed, over.Status);
        Assert.NotNull(over.FieldErrors);
        Assert.True(over.FieldErrors!.ContainsKey("entregas"), "el error de tope debe ir en la grilla 'entregas'");
        Assert.Contains("supera el maximo", over.FieldErrors!["entregas"]);
        // FEATURE A: el valor 'A' (de la otra grilla) NO fue rechazado como opcion invalida.
        Assert.DoesNotContain("opcion valida", string.Join(" | ", over.FieldErrors!.Values));

        // Entrega valida: entregar=2 <= 3 -> guarda OK (y confirma que 'A' se acepta).
        var okSave = await responses.SaveAsync(draft.Value.Id, new Dictionary<string, FormFieldValue>
        {
            ["items"] = new(itemsRows, "GridDetail"),
            ["entregas"] = new("""[{"item":"A","entregar":"2"}]""", "GridDetail"),
        }, submit: true, seed.TenantUserId);
        Assert.True(okSave.IsOk, okSave.Error);
    }

    [Fact]
    public async Task SignatureByLink_EmitSignVoid_RespectsInvariants()
    {
        // FEATURE E (firma por link, por fila): emitir -> validar -> firmar escribe /archivo/{id} en la celda;
        // el token queda un-solo-uso; que un token venza NO borra una firma ya hecha; anular limpia la celda,
        // borra la imagen y revoca los tokens vivos.
        var seed = await SeedTenantAsync("Forms Firma");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var definitions = BuildDefinitionService(ctx, seed);
        var responses = BuildResponseService(ctx, seed);
        var files = new FakeFileStorage();
        var sign = BuildSignatureService(ctx, seed, files);

        var created = await definitions.CreateAsync(new CreateFormDefinitionRequest(
            "FRM-S" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), "Entregas firma"));
        var def = created.Value!;
        const string entregasCols =
            """[{"id":"item","label":"Item"},{"id":"firma","label":"Firma","signature":{"linkHours":72,"requireName":false}}]""";
        var entregas = await definitions.AddQuestionAsync(def.Id, new SaveFormQuestionRequest(
            null, "entregas", "Entregas", FormControlType.GridDetail, OptionsJson: entregasCols));
        Assert.True(entregas.IsOk, entregas.Error);
        Assert.True((await definitions.ActivateAsync(def.Id)).IsOk);

        const string rowId = "row0001";
        var draft = await responses.GetOrCreateDraftAsync(def.Id, "REF-FIRMA");
        var responseId = draft.Value!.Id;
        var save = await responses.SaveAsync(responseId, new Dictionary<string, FormFieldValue>
        {
            ["entregas"] = new($"[{{\"__rowId\":\"{rowId}\",\"item\":\"A\"}}]", "GridDetail"),
        }, submit: false, seed.TenantUserId);
        Assert.True(save.IsOk, save.Error);

        // EMIT + VALIDATE
        var emit = await sign.RequestAsync(new RequestSignatureInput(
            responseId, "entregas", rowId, "firma", 72, RequireName: false,
            SendWhatsApp: false, Phone: null, Message: null, ActorUserId: seed.TenantUserId));
        Assert.True(emit.IsOk, emit.Error);
        var token = emit.Value!.ClearToken;
        var val = await sign.ValidateAsync(token);
        Assert.True(val.IsValid);
        Assert.Equal(responseId, val.ResponseId);
        Assert.Equal("firma", val.ColumnId);

        // SIGN -> la celda recibe /archivo/{id}
        var png = "data:image/png;base64," + Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 });
        Assert.True((await sign.SignAsync(token, png, null)).IsOk);
        Assert.StartsWith("/archivo/", FirmaCell(ctx, responseId, rowId));

        // Un-solo-uso: el mismo token ya no vale.
        Assert.False((await sign.ValidateAsync(token)).IsValid);

        // No-borrar-si-firmada-y-vencida: un token NUEVO que vence no toca la firma ya hecha.
        var emit2 = await sign.RequestAsync(new RequestSignatureInput(
            responseId, "entregas", rowId, "firma", 72, RequireName: false,
            SendWhatsApp: false, Phone: null, Message: null, ActorUserId: seed.TenantUserId));
        var tok2 = await ctx.FormSignatureTokens.SingleAsync(t => t.Id == emit2.Value!.TokenId);
        tok2.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await ctx.SaveChangesAsync();
        Assert.False((await sign.ValidateAsync(emit2.Value!.ClearToken)).IsValid);
        Assert.StartsWith("/archivo/", FirmaCell(ctx, responseId, rowId)); // firma intacta

        // Anular: limpia la celda, borra la imagen y revoca los tokens vivos.
        Assert.True((await sign.VoidAsync(responseId, "entregas", rowId, "firma")).IsOk);
        Assert.True(string.IsNullOrEmpty(FirmaCell(ctx, responseId, rowId)));
        Assert.NotEmpty(files.Deleted);
        var anyLive = await ctx.FormSignatureTokens
            .AnyAsync(t => t.ResponseId == responseId && t.RevokedAt == null && t.SignedAt == null);
        Assert.False(anyLive);

        // Vigencia en MINUTOS manda sobre horas (vencimientos cortos).
        var emitMin = await sign.RequestAsync(new RequestSignatureInput(
            responseId, "entregas", rowId, "firma", LinkHours: 72, RequireName: false,
            SendWhatsApp: false, Phone: null, Message: null, ActorUserId: seed.TenantUserId, LinkMinutes: 30));
        var tokMin = await ctx.FormSignatureTokens.SingleAsync(t => t.Id == emitMin.Value!.TokenId);
        Assert.True(tokMin.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(31),
            "con linkMinutes=30 el vencimiento debe ser por minutos, no por las 72 h");
    }

    private static string? FirmaCell(EcorexDbContext ctx, Guid responseId, string rowId)
    {
        var resp = ctx.FormResponses.AsNoTracking().Single(r => r.Id == responseId);
        var rows = FormFieldValidator.ParseGridRows(FormResponseService.ParseDocument(resp.Data)["entregas"].Value);
        var row = rows.FirstOrDefault(r => string.Equals(r.GetValueOrDefault("__rowId"), rowId, StringComparison.Ordinal));
        return row?.GetValueOrDefault("firma");
    }

    private static FormSignatureService BuildSignatureService(EcorexDbContext ctx, SeedData seed, FakeFileStorage files)
        => new(ctx, new TestTenantContext(seed.TenantId, seed.PlatformUserId), files, new FakeLinkBuilder(), new ThrowingWhatsApp());

    private static FormDefinitionService BuildDefinitionService(EcorexDbContext ctx, SeedData seed)
    {
        var tenant = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        return new(ctx, tenant, new Ecorex.Application.MenuConfig.MenuConfigService(ctx, tenant), new Ecorex.Application.Tenancy.SequenceService(ctx, tenant));
    }

    private static FormResponseService BuildResponseService(EcorexDbContext ctx, SeedData seed, IWorkflowEngine? engine = null)
    {
        var tenant = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        return new(ctx, engine ?? BuildEngine(ctx, seed),
            new SequenceService(ctx, tenant), tenant, new NoOpFormRecordBroadcaster(),
            new Ecorex.Application.Forms.Lookups.FormLookupService(System.Array.Empty<Ecorex.Application.Forms.Lookups.IFormLookupSource>()),
            new NoOpRulesEngine());
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

    // ---- Fakes para FEATURE E (firma por link) ----

    private sealed class FakeFileStorage : IFileStorageService
    {
        public readonly List<Guid> Deleted = new();
        private readonly Dictionary<Guid, byte[]> _blobs = new();

        public Task<StoredFileDto> SaveAsync(FileStorageSaveRequest request, CancellationToken ct = default)
        {
            var id = Guid.NewGuid();
            _blobs[id] = request.Content;
            return Task.FromResult(new StoredFileDto(
                id, request.Module, request.Ref1, request.Ref2, request.FileName,
                request.ContentType, request.Content.LongLength, DateTimeOffset.UtcNow, "Local"));
        }

        public Task<bool> DeleteAsync(Guid fileId, CancellationToken ct = default)
        {
            Deleted.Add(fileId);
            return Task.FromResult(_blobs.Remove(fileId));
        }

        public Task<IReadOnlyList<StoredFileDto>> ListAsync(string module, string? ref1 = null, string? ref2 = null, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<StoredFileDto>)Array.Empty<StoredFileDto>());
        public Task<StoredFileDto?> GetAsync(Guid fileId, CancellationToken ct = default) => Task.FromResult<StoredFileDto?>(null);
        public Task<FileStorageContent?> ReadAsync(Guid fileId, CancellationToken ct = default) => Task.FromResult<FileStorageContent?>(null);
        public Task<FileStorageContent?> ReadForTenantAsync(Guid fileId, Guid tenantId, CancellationToken ct = default) => Task.FromResult<FileStorageContent?>(null);
        public Task<StoredFileDto?> FindAsync(string module, string? ref1, string? ref2, string fileName, CancellationToken ct = default) => Task.FromResult<StoredFileDto?>(null);
        public Task<IReadOnlyCollection<string>> ListRefsWithFilesAsync(string module, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyCollection<string>)Array.Empty<string>());
    }

    private sealed class FakeLinkBuilder : INotifyLinkBuilder
    {
        public string? BuildTaskLink(Guid taskId) => null;
        public string? BuildDecisionLink(string token) => null;
        public string? BuildSignatureLink(string token) => $"https://test.local/firmar/{token}";
    }

    /// <summary>Nunca se invoca en estos tests (SendWhatsApp=false); solo cumple el contrato del constructor.</summary>
    private sealed class ThrowingWhatsApp : IWhatsAppConnectorService
    {
        private static T No<T>() => throw new NotSupportedException();
        public Task<EvolutionServerSettingDto> GetServerAsync(CancellationToken cancellationToken = default) => No<Task<EvolutionServerSettingDto>>();
        public Task<EvolutionServerSettingDto?> SetServerAsync(SetEvolutionServerRequest request, Guid actorUserId, CancellationToken cancellationToken = default) => No<Task<EvolutionServerSettingDto?>>();
        public Task<LineConnectResult> ConnectLineAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default) => No<Task<LineConnectResult>>();
        public Task<WhatsAppLineDto?> RefreshAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default) => No<Task<WhatsAppLineDto?>>();
        public Task<bool> DisconnectAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default) => No<Task<bool>>();
        public Task<bool> DeleteLineAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default) => No<Task<bool>>();
        public Task<int> ApplyWebhookToConnectedLinesAsync(Guid actorUserId, CancellationToken cancellationToken = default) => No<Task<int>>();
        public Task<LineSendResult> SendTestAsync(Guid lineId, string phone, string text, Guid actorUserId, string? remoteJid = null, CancellationToken cancellationToken = default) => No<Task<LineSendResult>>();
        public Task<LineSendResult> SendTemplateAsync(Guid lineId, string phone, string templateName, string language, IReadOnlyList<string> bodyParams, Guid actorUserId, string? headerMediaType = null, string? headerMediaUrl = null, string? headerMediaFileName = null, string? attachmentBase64 = null, string? attachmentMime = null, string? attachmentFileName = null, IReadOnlyList<WhatsAppUrlButtonParam>? urlButtons = null, CancellationToken cancellationToken = default) => No<Task<LineSendResult>>();
        public Task<LineSendResult> SendMediaAsync(Guid lineId, string phone, Ecorex.Domain.Enums.MessageMediaType mediaType, string base64, string? mimeType, string? fileName, string? caption, Guid actorUserId, string? remoteJid = null, CancellationToken cancellationToken = default) => No<Task<LineSendResult>>();
        public Task<LineSendResult> SendLocationAsync(Guid lineId, string phone, double latitude, double longitude, string? name, Guid actorUserId, string? remoteJid = null, CancellationToken cancellationToken = default) => No<Task<LineSendResult>>();
        public Task<LineSendResult> DeleteMessageForEveryoneAsync(Guid lineId, string phone, string messageId, string? remoteJid = null, CancellationToken cancellationToken = default) => No<Task<LineSendResult>>();
        public Task<LineSendResult> SendReactionAsync(Guid lineId, string phone, string externalMessageId, string emoji, string? remoteJid = null, CancellationToken cancellationToken = default) => No<Task<LineSendResult>>();
        public Task<Ecorex.Application.Admin.EvolutionMediaResult> FetchInboundMediaAsync(Guid lineId, string messageKeyId, CancellationToken cancellationToken = default) => No<Task<Ecorex.Application.Admin.EvolutionMediaResult>>();
        public Task<Ecorex.Application.Tenancy.WhatsAppDeliveryProbeResult> ProbeDeliveryStatusAsync(Guid lineId, string externalId, CancellationToken cancellationToken = default) => No<Task<Ecorex.Application.Tenancy.WhatsAppDeliveryProbeResult>>();
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
