using Ecorex.Application.Common;
using Ecorex.Application.Forms;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Matriz dual PostgreSQL / SQL Server de los formularios de la tarea PADRE en un salto de flujo (ADR-0076 v2):
/// (a) "Traer a esta tarea" copia el formulario del padre a la hija como borrador editable anclado al numero de
/// la hija; (b) solo se pueden traer formularios del padre (no de otra tarea) y solo en tareas con padre; (c) al
/// saltar, la hija recibe la copia de los formularios del padre que el flujo DESTINO exige (y no los demas).
/// </summary>
public abstract class ParentFormCopyTestsBase
{
    private const string TargetFlowXml = """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          id="salto" targetNamespace="http://ecorex.local/bpmn">
          <bpmn:process id="P_Salto">
            <bpmn:startEvent id="Start_1" name="Inicio" />
            <bpmn:task id="Task_Cot" name="Se prepara la cotizacion" />
            <bpmn:endEvent id="End_1" name="Fin" />
            <bpmn:sequenceFlow id="F1" sourceRef="Start_1" targetRef="Task_Cot" />
            <bpmn:sequenceFlow id="F2" sourceRef="Task_Cot" targetRef="End_1" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    private readonly TenantIsolationDbFixture _fixture;

    protected ParentFormCopyTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CopyParentForm_CreatesEditableDraftOfChild_WithParentData()
    {
        var seed = await SeedTenantAsync("Padre copia A");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var defId = await SeedFormAsync(ctx, seed, "COT");
        var parentId = await SeedTaskAsync(ctx, seed, "T00886", parentId: null);
        var childId = await SeedTaskAsync(ctx, seed, "T00894", parentId);
        var responses = BuildResponseService(ctx, seed);
        var parentForm = await SeedFilledFormAsync(responses, parentId, defId, seed, "Doblez 40 perfiles HR 2.5mm");

        var copied = await responses.CopyParentFormToTaskAsync(parentForm, childId);
        Assert.True(copied.IsOk, copied.Error);
        Assert.Equal("T00894-1", copied.Value!.Reference);
        Assert.Equal(FormResponseStatus.Draft, copied.Value.Status);

        var read = await responses.GetAsync(copied.Value.ResponseId);
        Assert.Equal("Doblez 40 perfiles HR 2.5mm", read!.Data["detalle"].Value);

        // El original del padre queda intacto (sigue siendo suyo).
        var original = await ctx.FormResponses.AsNoTracking().SingleAsync(r => r.Id == parentForm);
        Assert.Equal("T00886-1", original.Reference);

        // La hija ya tiene SU formulario (no heredado) y el subtitulo "cliente" no toma la casilla si/no.
        var generos = await responses.GetTaskFormGenerosAsync(childId);
        var own = Assert.Single(generos, g => !g.Inherited && g.DefinitionId == defId);
        var item = Assert.Single(own.Items);
        Assert.Equal(copied.Value.ResponseId, item.ResponseId);
        Assert.Null(item.Cliente);
        Assert.Contains(generos, g => g.Inherited && g.DefinitionId == defId);
    }

    [Fact]
    public async Task CopyParentForm_RejectsFormsOutsideTheParent_AndTasksWithoutParent()
    {
        var seed = await SeedTenantAsync("Padre copia B");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var defId = await SeedFormAsync(ctx, seed, "COT");
        var parentId = await SeedTaskAsync(ctx, seed, "T00100", parentId: null);
        var childId = await SeedTaskAsync(ctx, seed, "T00101", parentId);
        var strangerId = await SeedTaskAsync(ctx, seed, "T00200", parentId: null);
        var responses = BuildResponseService(ctx, seed);
        var strangerForm = await SeedFilledFormAsync(responses, strangerId, defId, seed, "otra tarea");
        var parentForm = await SeedFilledFormAsync(responses, parentId, defId, seed, "del padre");

        // Un formulario de OTRA tarea (no del padre) no se puede traer.
        var foreign = await responses.CopyParentFormToTaskAsync(strangerForm, childId);
        Assert.False(foreign.IsOk);

        // Una tarea sin padre no trae nada.
        var orphan = await responses.CopyParentFormToTaskAsync(parentForm, strangerId);
        Assert.False(orphan.IsOk);

        Assert.False(await ctx.FormResponses.AnyAsync(r => r.Reference != null && r.Reference.StartsWith("T00101")));
    }

    [Fact]
    public async Task Jump_CopiesOnlyTheParentFormsRequiredByTheTargetFlow()
    {
        var seed = await SeedTenantAsync("Padre salto C");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenant = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        var cotDefId = await SeedFormAsync(ctx, seed, "COT");
        var otrDefId = await SeedFormAsync(ctx, seed, "OTR");
        var engine = BuildEngine(ctx, seed);
        var responses = BuildResponseService(ctx, seed);

        // Flujo destino publicado: el paso "Se prepara la cotizacion" EXIGE COT; OTR no esta en el flujo.
        var imported = (await engine.ImportBpmnAsync(new ImportBpmnRequest("SAL-01", "Flujo destino", TargetFlowXml))).Value!;
        var cotNode = imported.Nodes.Single(n => n.BpmnElementId == "Task_Cot");
        ctx.WorkflowNodeForms.Add(new WorkflowNodeForm
        {
            TenantId = seed.TenantId,
            NodeId = cotNode.Id,
            DefinitionId = cotDefId,
            IsRequired = true
        });
        await ctx.SaveChangesAsync();
        Assert.True((await engine.PublishAsync(imported.Id)).IsOk);

        var parentId = await SeedTaskAsync(ctx, seed, "T00300", parentId: null);
        await SeedFilledFormAsync(responses, parentId, cotDefId, seed, "cotizacion del padre");
        await SeedFilledFormAsync(responses, parentId, otrDefId, seed, "otro formulario");

        var starter = new ChildTaskStarter(ctx, new SequenceService(ctx, tenant), engine, responses);
        var childId = await starter.StartChildFromJumpAsync(parentId, imported.Id);
        Assert.NotNull(childId);

        var child = await ctx.TaskItems.AsNoTracking().SingleAsync(t => t.Id == childId);
        var childForms = await ctx.FormResponses.AsNoTracking()
            .Where(r => r.Reference != null && r.Reference.StartsWith(child.Number + "-"))
            .ToListAsync();
        var cot = Assert.Single(childForms);
        Assert.Equal(cotDefId, cot.DefinitionId);
        Assert.Equal(child.Number + "-1", cot.Reference);
        Assert.Equal(FormResponseStatus.Draft, cot.Status);
        var read = await responses.GetAsync(cot.Id);
        Assert.Equal("cotizacion del padre", read!.Data["detalle"].Value);
    }

    // ---- Helpers ----

    private static WorkflowEngine BuildEngine(EcorexDbContext ctx, SeedData seed)
        => new(ctx, new TestTenantContext(seed.TenantId, seed.PlatformUserId),
            new NoOpWorkflowRuleHook(), new NoOpTaskBroadcaster());

    private FormResponseService BuildResponseService(EcorexDbContext ctx, SeedData seed)
    {
        var tenant = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        return new(ctx, BuildEngine(ctx, seed), new SequenceService(ctx, tenant), tenant, new NoOpFormRecordBroadcaster(),
            new Ecorex.Application.Forms.Lookups.FormLookupService(
                System.Array.Empty<Ecorex.Application.Forms.Lookups.IFormLookupSource>()),
            new NoOpRulesEngine(),
            dbFactory: _fixture.CreateFactory(seed.TenantId));
    }

    /// <summary>Definicion activa con un texto "detalle" y una casilla si/no cuya etiqueta menciona al cliente.</summary>
    private static async Task<Guid> SeedFormAsync(EcorexDbContext ctx, SeedData seed, string codePrefix)
    {
        var tenant = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        var definitions = new FormDefinitionService(ctx, tenant,
            new Ecorex.Application.MenuConfig.MenuConfigService(ctx, tenant), new SequenceService(ctx, tenant));
        var created = await definitions.CreateAsync(new CreateFormDefinitionRequest(
            codePrefix + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), "Registro " + codePrefix));
        var defId = created.Value!.Id;
        var container = (await definitions.AddContainerAsync(defId, new SaveFormContainerRequest("Datos"))).Value!;
        Assert.True((await definitions.AddQuestionAsync(defId, new SaveFormQuestionRequest(
            container.Id, "detalle", "Detalle", FormControlType.Text))).IsOk);
        Assert.True((await definitions.AddQuestionAsync(defId, new SaveFormQuestionRequest(
            container.Id, "material_cliente", "El cliente suministra el material", FormControlType.Toggle))).IsOk);
        Assert.True((await definitions.ActivateAsync(defId)).IsOk);
        return defId;
    }

    private static async Task<Guid> SeedFilledFormAsync(
        FormResponseService responses, Guid taskId, Guid defId, SeedData seed, string detalle)
    {
        var created = await responses.CreateTaskFormAsync(taskId, defId);
        Assert.True(created.IsOk, created.Error);
        var data = new Dictionary<string, FormFieldValue>(StringComparer.Ordinal)
        {
            ["detalle"] = new FormFieldValue(detalle, "Text"),
            ["material_cliente"] = new FormFieldValue("true", "Toggle"),
        };
        var saved = await responses.SaveAsync(created.Value!.ResponseId, data, submit: false, seed.TenantUserId);
        Assert.True(saved.IsOk, saved.Error);
        return created.Value.ResponseId;
    }

    private static async Task<Guid> SeedTaskAsync(EcorexDbContext ctx, SeedData seed, string number, Guid? parentId)
    {
        var task = new TaskItem
        {
            TenantId = seed.TenantId,
            Number = number,
            Title = "Cotizacion " + number,
            Status = TaskItemStatus.Active,
            ParentId = parentId
        };
        ctx.TaskItems.Add(task);
        await ctx.SaveChangesAsync();
        return task.Id;
    }

    private async Task<SeedData> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
            await ctx.SaveChangesAsync();
        }

        Guid tenantUserId, platformUserId;
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var platformUser = new PlatformUser
            {
                Email = $"owner-{tenantId:N}@padre.test",
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
            await ctx.SaveChangesAsync();
            tenantUserId = tenantUser.Id;
            platformUserId = platformUser.Id;
        }
        return new SeedData(tenantId, tenantUserId, platformUserId);
    }

    private sealed record SeedData(Guid TenantId, Guid TenantUserId, Guid PlatformUserId);

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class ParentFormCopyTests_Postgres
    : ParentFormCopyTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public ParentFormCopyTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture) { }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class ParentFormCopyTests_SqlServer
    : ParentFormCopyTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public ParentFormCopyTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture) { }
}
