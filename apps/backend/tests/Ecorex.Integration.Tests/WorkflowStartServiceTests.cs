using Ecorex.Application.Common;
using Ecorex.Application.Organization;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion de <see cref="WorkflowStartService"/> (Ola A1 del capitulo "Tareas de
/// proceso - Arranque y encargado del flujo") en matriz dual PostgreSQL / SQL Server.
///
/// Verifica que, ANTES de crear la actividad, se pueda saber QUIEN atendera el primer paso:
/// se camina el grafo EN SECO desde el startEvent hasta el primer nodo Task (atravesando
/// compuertas como lo hace el motor), y se devuelve su cargo y sus candidatos. Cubre tambien
/// cada estado de configuracion incompleta (sin flujo, en borrador, sin nodo Task, sin cargo,
/// sin ocupantes) y el aislamiento cross-tenant.
/// </summary>
public abstract class WorkflowStartServiceTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected WorkflowStartServiceTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ResolveFirstStep_LinearFlow_ReturnsFirstTaskWithCargoAndCandidate()
    {
        var seed = await SeedTenantAsync("Start Lineal");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.PlatformUserId);

        // Cargo "Comprador" con un ocupante (el usuario del tenant).
        var cargoId = await SeedCargoAsync(ctx, tenantCtx, "Comprador", seed.TenantUserId);

        // start -> Cotizar -> Aprobar -> end. El primer Task es "Cotizar".
        var flow = await SeedFlowAsync(ctx, seed.TenantId, published: true, withGateway: false);
        await AddPolicyAsync(ctx, tenantCtx, flow.FirstTaskNodeId, cargoId);

        var subId = await SeedConceptoAsync(ctx, seed.TenantId, "Compra", flow.DefinitionId);

        var svc = new WorkflowStartService(ctx, new NodeAssigneeResolver(ctx));
        var result = await svc.ResolveFirstStepAsync(subId);

        Assert.Equal(FirstStepStatus.Ok, result.Status);
        Assert.True(result.EsProceso);
        Assert.Equal(flow.DefinitionId, result.WorkflowDefinitionId);
        Assert.Equal(flow.FirstTaskNodeId, result.NodeId);
        Assert.Equal("Cotizar", result.NodeName);          // el PRIMER Task, no cualquiera
        Assert.Equal("Comprador", result.CargoPrincipal);
        Assert.Equal(cargoId, Assert.Single(result.Cargos).OrgUnitId);
        Assert.Equal(seed.TenantUserId, Assert.Single(result.CandidateUserIds));
        Assert.True(result.TieneCandidatoUnico);           // el wizard lo preselecciona sin preguntar
    }

    [Fact]
    public async Task ResolveFirstStep_GatewayAfterStart_WalksThroughItToTheTask()
    {
        // Una compuerta justo despues del startEvent NO debe frenar la resolucion: el motor la
        // auto-resuelve al arrancar (sin ApprovalResult toma la rama por defecto) y este servicio
        // debe hacer exactamente lo mismo.
        var seed = await SeedTenantAsync("Start Gateway");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.PlatformUserId);

        var cargoId = await SeedCargoAsync(ctx, tenantCtx, "Comprador", seed.TenantUserId);
        var flow = await SeedFlowAsync(ctx, seed.TenantId, published: true, withGateway: true);
        await AddPolicyAsync(ctx, tenantCtx, flow.FirstTaskNodeId, cargoId);
        var subId = await SeedConceptoAsync(ctx, seed.TenantId, "Compra gw", flow.DefinitionId);

        var result = await new WorkflowStartService(ctx, new NodeAssigneeResolver(ctx))
            .ResolveFirstStepAsync(subId);

        Assert.Equal(FirstStepStatus.Ok, result.Status);
        Assert.Equal(flow.FirstTaskNodeId, result.NodeId);
        Assert.Equal("Cotizar", result.NodeName);
        Assert.Equal(seed.TenantUserId, Assert.Single(result.CandidateUserIds));
    }

    [Fact]
    public async Task ResolveFirstStep_SubcategoriaWithoutFlow_IsSinFlujo()
    {
        var seed = await SeedTenantAsync("Start Sin Flujo");
        await using var ctx = _fixture.CreateContext(seed.TenantId);

        var subId = await SeedConceptoAsync(ctx, seed.TenantId, "Nota simple", definitionId: null);

        var result = await new WorkflowStartService(ctx, new NodeAssigneeResolver(ctx))
            .ResolveFirstStepAsync(subId);

        Assert.Equal(FirstStepStatus.SinFlujo, result.Status);
        Assert.False(result.EsProceso);                    // es una actividad simple
        Assert.Null(result.WorkflowDefinitionId);
        Assert.Empty(result.CandidateUserIds);
    }

    [Fact]
    public async Task ResolveFirstStep_DraftFlow_IsFlujoNoPublicado()
    {
        // Decision D3: el concepto igual se ve en el menu, pero el arranque debe AVISAR que la
        // actividad nacera SIN proceso. Este es el estado que dispara ese banner (Ola C1).
        var seed = await SeedTenantAsync("Start Borrador");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.PlatformUserId);

        var cargoId = await SeedCargoAsync(ctx, tenantCtx, "Comprador", seed.TenantUserId);
        var flow = await SeedFlowAsync(ctx, seed.TenantId, published: false, withGateway: false);
        await AddPolicyAsync(ctx, tenantCtx, flow.FirstTaskNodeId, cargoId);
        var subId = await SeedConceptoAsync(ctx, seed.TenantId, "Compra borrador", flow.DefinitionId);

        var result = await new WorkflowStartService(ctx, new NodeAssigneeResolver(ctx))
            .ResolveFirstStepAsync(subId);

        Assert.Equal(FirstStepStatus.FlujoNoPublicado, result.Status);
        Assert.True(result.EsProceso);                     // tiene flujo... pero no arranca
        Assert.Equal(flow.DefinitionId, result.WorkflowDefinitionId);
        Assert.Empty(result.CandidateUserIds);
    }

    [Fact]
    public async Task ResolveFirstStep_TaskWithoutPolicy_IsSinCargo()
    {
        var seed = await SeedTenantAsync("Start Sin Cargo");
        await using var ctx = _fixture.CreateContext(seed.TenantId);

        var flow = await SeedFlowAsync(ctx, seed.TenantId, published: true, withGateway: false);
        // (no se asigna ninguna policy al nodo)
        var subId = await SeedConceptoAsync(ctx, seed.TenantId, "Compra sin cargo", flow.DefinitionId);

        var result = await new WorkflowStartService(ctx, new NodeAssigneeResolver(ctx))
            .ResolveFirstStepAsync(subId);

        Assert.Equal(FirstStepStatus.SinCargo, result.Status);
        Assert.Equal(flow.FirstTaskNodeId, result.NodeId); // el nodo si se encontro
        Assert.Equal("Cotizar", result.NodeName);
        Assert.Empty(result.Cargos);
        Assert.Empty(result.CandidateUserIds);
    }

    [Fact]
    public async Task ResolveFirstStep_CargoWithoutOccupants_IsSinCandidatos()
    {
        // El cargo existe y esta enganchado al nodo, pero nadie lo ocupa: el paso naceria huerfano.
        var seed = await SeedTenantAsync("Start Sin Ocupantes");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.PlatformUserId);

        var cargoId = await SeedCargoAsync(ctx, tenantCtx, "Comprador vacante", tenantUserId: null);
        var flow = await SeedFlowAsync(ctx, seed.TenantId, published: true, withGateway: false);
        await AddPolicyAsync(ctx, tenantCtx, flow.FirstTaskNodeId, cargoId);
        var subId = await SeedConceptoAsync(ctx, seed.TenantId, "Compra vacante", flow.DefinitionId);

        var result = await new WorkflowStartService(ctx, new NodeAssigneeResolver(ctx))
            .ResolveFirstStepAsync(subId);

        Assert.Equal(FirstStepStatus.SinCandidatos, result.Status);
        Assert.Equal("Comprador vacante", result.CargoPrincipal); // el cargo SI se resolvio
        Assert.Empty(result.CandidateUserIds);                    // pero no hay a quien asignar
        Assert.False(result.TieneCandidatoUnico);
    }

    [Fact]
    public async Task ResolveFirstStep_IsTenantIsolated()
    {
        var seedA = await SeedTenantAsync("Start Tenant A");
        var seedB = await SeedTenantAsync("Start Tenant B");

        Guid subA;
        await using (var ctxA = _fixture.CreateContext(seedA.TenantId))
        {
            var tenantCtxA = new TestTenantContext(seedA.TenantId, seedA.PlatformUserId);
            var cargoId = await SeedCargoAsync(ctxA, tenantCtxA, "Comprador A", seedA.TenantUserId);
            var flow = await SeedFlowAsync(ctxA, seedA.TenantId, published: true, withGateway: false);
            await AddPolicyAsync(ctxA, tenantCtxA, flow.FirstTaskNodeId, cargoId);
            subA = await SeedConceptoAsync(ctxA, seedA.TenantId, "Compra A", flow.DefinitionId);
        }

        await using (var ctxB = _fixture.CreateContext(seedB.TenantId))
        {
            // El filtro global oculta la subcategoria de A: para B ni siquiera existe.
            var result = await new WorkflowStartService(ctxB, new NodeAssigneeResolver(ctxB))
                .ResolveFirstStepAsync(subA);

            Assert.Equal(FirstStepStatus.SinFlujo, result.Status);
            Assert.Empty(result.CandidateUserIds);
        }
    }

    // ================== Ola A3: el primer paso NACE ASIGNADO, y D2 se valida en SERVIDOR ==================

    [Fact]
    public async Task CreateTask_ProcessActivity_FirstStepIsBornAssignedToTheCargoCandidate()
    {
        // El corazon de A3: antes, StartInstanceAsync dejaba el primer paso con
        // AssignedToTenantUserId = null y la asignacion se resolvia perezosamente cuando alguien lo
        // "reclamaba". Ahora nace ASIGNADO al encargado (que a su vez es candidato del cargo del nodo).
        var seed = await SeedTenantAsync("A3 Nace Asignado");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.PlatformUserId);

        var cargoId = await SeedCargoAsync(ctx, tenantCtx, "Comprador", seed.TenantUserId);
        var flow = await SeedFlowAsync(ctx, seed.TenantId, published: true, withGateway: false);
        await AddPolicyAsync(ctx, tenantCtx, flow.FirstTaskNodeId, cargoId);
        var subId = await SeedConceptoAsync(ctx, seed.TenantId, "Compra", flow.DefinitionId);

        var tasks = BuildTaskService(ctx, tenantCtx);
        var created = await tasks.CreateAsync(
            new CreateTaskItemRequest("Compra de equipos", ActivityTypeId: null,
                AssigneeTenantUserId: seed.TenantUserId, SubcategoriaId: subId),
            seed.PlatformUserId, "Tester");

        Assert.True(created.IsOk, created.Error);
        var taskId = created.Value!.Item.Id;

        // La instancia arranco y el primer paso quedo Pending/IsCurrent... Y ASIGNADO.
        var instance = await ctx.WorkflowInstances.AsNoTracking().SingleAsync(i => i.TaskItemId == taskId);
        var step = await ctx.WorkflowStepHistories.AsNoTracking()
            .SingleAsync(s => s.InstanceId == instance.Id && s.IsCurrent && s.Status == WorkflowStepStatus.Pending);

        Assert.Equal(flow.FirstTaskNodeId, step.NodeId);
        Assert.Equal(seed.TenantUserId, step.AssignedToTenantUserId); // <-- lo que antes nacia en null

        // Y el encargado quedo notificado al NACER la tarea (no al reclamar el paso).
        Assert.True(await ctx.Notifications.AsNoTracking()
            .AnyAsync(n => n.RecipientTenantUserId == seed.TenantUserId
                && n.Kind == NotificationKind.TaskAssigned
                && n.RelatedTaskItemId == taskId));
    }

    [Fact]
    public async Task CreateTask_AssigneeOutsideTheNodeCargo_IsRejectedByTheServer()
    {
        // D2 en SERVIDOR: restringir el combo del wizard no basta, un API podria saltarselo.
        var seed = await SeedTenantAsync("A3 D2 Servidor");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.PlatformUserId);

        // El cargo del nodo lo ocupa el usuario del seed; el "extrano" NO lo ocupa.
        var cargoId = await SeedCargoAsync(ctx, tenantCtx, "Comprador", seed.TenantUserId);
        var extranoId = await AddTenantUserAsync(ctx, seed.TenantId, "extrano");

        var flow = await SeedFlowAsync(ctx, seed.TenantId, published: true, withGateway: false);
        await AddPolicyAsync(ctx, tenantCtx, flow.FirstTaskNodeId, cargoId);
        var subId = await SeedConceptoAsync(ctx, seed.TenantId, "Compra", flow.DefinitionId);

        var tasks = BuildTaskService(ctx, tenantCtx);
        var created = await tasks.CreateAsync(
            new CreateTaskItemRequest("Compra colada", ActivityTypeId: null,
                AssigneeTenantUserId: extranoId, SubcategoriaId: subId),
            seed.PlatformUserId, "Tester");

        Assert.Equal(TaskCoreStatus.Invalid, created.Status);
        Assert.Contains("cargo", created.Error!, StringComparison.OrdinalIgnoreCase);

        // Rollback total: ni tarea, ni instancia, ni pasos (regla del proyecto: todo o nada).
        Assert.Empty(await ctx.TaskItems.AsNoTracking().ToListAsync());
        Assert.Empty(await ctx.WorkflowInstances.AsNoTracking().ToListAsync());
        Assert.Empty(await ctx.WorkflowStepHistories.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CreateTask_SimpleActivity_KeepsAcceptingAnyAssignee()
    {
        // Regresion: una actividad SIN flujo no tiene cargo que dictar nada -> cualquier usuario
        // del tenant sigue siendo un encargado valido.
        var seed = await SeedTenantAsync("A3 Sin Flujo");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.PlatformUserId);

        var extranoId = await AddTenantUserAsync(ctx, seed.TenantId, "cualquiera");
        var subId = await SeedConceptoAsync(ctx, seed.TenantId, "Nota simple", definitionId: null);

        var tasks = BuildTaskService(ctx, tenantCtx);
        var created = await tasks.CreateAsync(
            new CreateTaskItemRequest("Tarea simple", ActivityTypeId: null,
                AssigneeTenantUserId: extranoId, SubcategoriaId: subId),
            seed.PlatformUserId, "Tester");

        Assert.True(created.IsOk, created.Error);
        Assert.Empty(await ctx.WorkflowInstances.AsNoTracking().ToListAsync()); // no hay flujo
    }

    // ---- Helpers ----

    private sealed record FlowSeed(Guid DefinitionId, Guid FirstTaskNodeId);

    private static TaskItemService BuildTaskService(EcorexDbContext ctx, ITenantContext tenantCtx)
        => new(ctx, tenantCtx, new SequenceService(ctx, tenantCtx),
            new WorkflowEngine(ctx, tenantCtx, new NoOpWorkflowRuleHook(), new NoOpTaskBroadcaster()),
            new NoOpEmailSender(), new NodeAssigneeResolver(ctx));

    /// <summary>Alta de un usuario del tenant (PlatformUser + TenantUser). Devuelve el TenantUserId.</summary>
    private static async Task<Guid> AddTenantUserAsync(IApplicationDbContext ctx, Guid tenantId, string tag)
    {
        var platformUser = new PlatformUser
        {
            Email = $"{tag}-{Guid.NewGuid():N}@start.test",
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
        return tenantUser.Id;
    }

    /// <summary>
    /// Siembra un flujo con nodos y aristas reales (el servicio los lee de esas tablas).
    /// Sin compuerta:  start -> Cotizar -> Aprobar -> end
    /// Con compuerta:  start -> Gw -> Cotizar -> Aprobar -> end   (arista Gw->Cotizar por defecto)
    /// En ambos casos el PRIMER Task alcanzable es "Cotizar".
    /// </summary>
    private static async Task<FlowSeed> SeedFlowAsync(
        IApplicationDbContext ctx, Guid tenantId, bool published, bool withGateway)
    {
        var definition = new WorkflowDefinition
        {
            TenantId = tenantId,
            ProcessCode = $"ST-{Guid.NewGuid():N}"[..12],
            Name = "Flujo de arranque",
            BpmnXml = "<xml/>",
            Version = 1,
            IsPublished = published
        };
        ctx.WorkflowDefinitions.Add(definition);

        var step = 0;
        WorkflowNode Node(string elementId, string name, WorkflowNodeType type) =>
            new()
            {
                TenantId = tenantId,
                DefinitionId = definition.Id,
                BpmnElementId = elementId,
                Name = name,
                NodeType = type,
                StepNumber = step++,
                AllowsAssignment = type == WorkflowNodeType.Task
            };

        var start = Node("Start_1", "Inicio", WorkflowNodeType.StartEvent);
        var gateway = Node("Gw_1", "Compuerta", WorkflowNodeType.ExclusiveGateway);
        var cotizar = Node("Task_Cot", "Cotizar", WorkflowNodeType.Task);
        var aprobar = Node("Task_Apr", "Aprobar", WorkflowNodeType.Task);
        var end = Node("End_1", "Fin", WorkflowNodeType.EndEvent);

        ctx.WorkflowNodes.AddRange(withGateway
            ? [start, gateway, cotizar, aprobar, end]
            : [start, cotizar, aprobar, end]);

        WorkflowEdge Edge(WorkflowNode from, WorkflowNode to, string? condition = null) =>
            new()
            {
                TenantId = tenantId,
                DefinitionId = definition.Id,
                SourceNodeId = from.Id,
                TargetNodeId = to.Id,
                ConditionExpression = condition
            };

        if (withGateway)
        {
            // La compuerta sale por la rama POR DEFECTO (sin condicion), que es la que el motor
            // toma al arrancar (todavia no hay ApprovalResult).
            ctx.WorkflowEdges.AddRange([
                Edge(start, gateway),
                Edge(gateway, cotizar),
                Edge(cotizar, aprobar),
                Edge(aprobar, end)
            ]);
        }
        else
        {
            ctx.WorkflowEdges.AddRange([
                Edge(start, cotizar),
                Edge(cotizar, aprobar),
                Edge(aprobar, end)
            ]);
        }

        await ctx.SaveChangesAsync();
        return new FlowSeed(definition.Id, cotizar.Id);
    }

    /// <summary>Categoria + subcategoria (el "concepto"), opcionalmente ligada a un flujo.</summary>
    private static async Task<Guid> SeedConceptoAsync(
        IApplicationDbContext ctx, Guid tenantId, string nombre, Guid? definitionId)
    {
        var categoria = new ActividadCategoria
        {
            TenantId = tenantId,
            Codigo = $"CAT-{Guid.NewGuid():N}"[..8],
            Nombre = "Comercial"
        };
        ctx.ActividadCategorias.Add(categoria);

        var sub = new ActividadSubcategoria
        {
            TenantId = tenantId,
            CategoriaId = categoria.Id,
            Codigo = $"SUB-{Guid.NewGuid():N}"[..8],
            Nombre = nombre,
            WorkflowDefinitionId = definitionId
        };
        ctx.ActividadSubcategorias.Add(sub);
        await ctx.SaveChangesAsync();
        return sub.Id;
    }

    /// <summary>Dependencia -> Cargo (-> Funcionario si se pasa un usuario). Devuelve el cargo.</summary>
    private static async Task<Guid> SeedCargoAsync(
        IApplicationDbContext ctx, ITenantContext tenantCtx, string cargoNombre, Guid? tenantUserId)
    {
        var org = new OrgUnitService(ctx, tenantCtx);
        var dep = (await org.CreateAsync(new SaveOrgUnitRequest(
            $"Dep {cargoNombre}", Classifier: OrgUnitClassifier.Dependencia))).Value!;
        var cargo = (await org.CreateAsync(new SaveOrgUnitRequest(
            cargoNombre, ParentId: dep.Id, Classifier: OrgUnitClassifier.Cargo))).Value!;

        if (tenantUserId is Guid userId)
        {
            var funcionario = await org.CreateAsync(new SaveOrgUnitRequest(
                $"Ocupante {cargoNombre}", ParentId: cargo.Id,
                Classifier: OrgUnitClassifier.Funcionario, TenantUserId: userId));
            Assert.True(funcionario.IsOk, funcionario.Error);
        }

        return cargo.Id;
    }

    private static async Task AddPolicyAsync(
        IApplicationDbContext ctx, ITenantContext tenantCtx, Guid nodeId, Guid cargoId)
    {
        var policySvc = new WorkflowNodePolicyService(ctx, tenantCtx);
        var added = await policySvc.AddNodePolicyAsync(nodeId, cargoId);
        Assert.True(added.IsOk, added.Error);
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
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var platformUser = new PlatformUser
            {
                Email = $"user-{tenantId:N}@start.test",
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

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class WorkflowStartServiceTests_Postgres
    : WorkflowStartServiceTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public WorkflowStartServiceTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class WorkflowStartServiceTests_SqlServer
    : WorkflowStartServiceTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public WorkflowStartServiceTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture)
    {
    }
}
