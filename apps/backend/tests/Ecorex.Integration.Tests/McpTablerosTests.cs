using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Mcp;
using Ecorex.Application.Organization;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Servidor MCP de tableros (ADR-0159) en matriz dual PostgreSQL / SQL Server:
/// - Token PERSONAL: resuelve tenant+usuario; revocado, de otro alcance (admin) o de usuario inactivo NO resuelve.
/// - Visibilidad: la IA de un usuario NO ve tableros restringidos a otros ni sus tareas (responde "no encontre").
/// - Flujo: una tarea con flujo en curso NO se mueve de columna a mano; se avanza con tomar_paso/completar_paso,
///   y la bitacora queda a nombre del usuario "(via IA)".
/// </summary>
public abstract class McpTablerosTestsBase
{
    private const string FlowXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          id="mcp" targetNamespace="http://ecorex.local/bpmn">
          <bpmn:process id="P_Mcp">
            <bpmn:startEvent id="Start_1" name="Inicio" />
            <bpmn:task id="Task_Rev" name="Revision" />
            <bpmn:task id="Task_Cierre" name="Cierre" />
            <bpmn:endEvent id="End_1" name="Fin" />
            <bpmn:sequenceFlow id="F1" sourceRef="Start_1" targetRef="Task_Rev" />
            <bpmn:sequenceFlow id="F2" sourceRef="Task_Rev" targetRef="Task_Cierre" />
            <bpmn:sequenceFlow id="F3" sourceRef="Task_Cierre" targetRef="End_1" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    private readonly TenantIsolationDbFixture _fixture;

    protected McpTablerosTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task PersonalToken_ResolvesOwnerUser_AndRejectsRevokedAdminScopeAndInactive()
    {
        var seed = await SeedTenantAsync("Mcp Tokens");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var svc = new PersonalAccessTokenService(ctx, new AuditWriter(ctx));

        var issued = await svc.IssueAsync(seed.MemberUserId, seed.MemberPlatformId, "Claude portatil");
        Assert.StartsWith(ApiTokenHasher.TokenPrefix, issued.PlainToken);
        Assert.Equal(TenantApiToken.ScopeMcpTableros, issued.Token.Scope);

        var actor = await svc.ResolveAsync(issued.PlainToken, TenantApiToken.ScopeMcpTableros);
        Assert.NotNull(actor);
        Assert.Equal(seed.TenantId, actor!.TenantId);
        Assert.Equal(seed.MemberUserId, actor.TenantUserId);
        Assert.Equal(seed.MemberPlatformId, actor.PlatformUserId);
        Assert.False(actor.IsOwnerOrAdmin);

        // El hash, no el valor, es lo que se persiste.
        var row = await ctx.TenantApiTokens.AsNoTracking().SingleAsync(t => t.Id == issued.Token.Id);
        Assert.NotEqual(issued.PlainToken, row.TokenHash);
        Assert.NotNull(row.LastUsedAt);

        // Un token personal no sirve para otro alcance (p.ej. la API de configuracion "admin").
        Assert.Null(await svc.ResolveAsync(issued.PlainToken, TenantApiToken.ScopeAdmin));

        // Un token de tenant (admin, sin dueno) no abre el MCP.
        var adminPlain = ApiTokenHasher.Generate();
        ctx.TenantApiTokens.Add(new TenantApiToken { TenantId = seed.TenantId, Name = "script", TokenHash = ApiTokenHasher.Hash(adminPlain) });
        await ctx.SaveChangesAsync();
        Assert.Null(await svc.ResolveAsync(adminPlain, TenantApiToken.ScopeMcpTableros));

        // Otro usuario no puede revocar el token ajeno; el dueno si.
        Assert.False(await svc.RevokeAsync(seed.OwnerUserId, seed.OwnerPlatformId, issued.Token.Id));
        Assert.True(await svc.RevokeAsync(seed.MemberUserId, seed.MemberPlatformId, issued.Token.Id));
        Assert.Null(await svc.ResolveAsync(issued.PlainToken, TenantApiToken.ScopeMcpTableros));

        // Usuario desactivado: sus tokens dejan de resolver.
        var second = await svc.IssueAsync(seed.MemberUserId, seed.MemberPlatformId, "Gemini");
        var member = await ctx.TenantUsers.SingleAsync(u => u.Id == seed.MemberUserId);
        member.Status = PlatformUserStatus.Suspended;
        await ctx.SaveChangesAsync();
        Assert.Null(await svc.ResolveAsync(second.PlainToken, TenantApiToken.ScopeMcpTableros));

        Assert.Equal(2, (await svc.ListMineAsync(seed.MemberUserId)).Count);
    }

    [Fact]
    public async Task RestrictedBoard_IsInvisibleToOtherUsersAi_ButVisibleToOwner()
    {
        var seed = await SeedTenantAsync("Mcp Visibilidad");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var (boards, _, toolset) = Build(ctx, tenantCtx);

        var privado = (await boards.CreateBoardAsync(new CreateActivityBoardRequest("Gerencia privada",
            AllowedUserIds: new[] { seed.OwnerUserId }), seed.OwnerPlatformId, "Owner")).Value!;
        var publico = (await boards.CreateBoardAsync(new CreateActivityBoardRequest("Soporte"), seed.OwnerPlatformId, "Owner")).Value!;
        var colPriv = await FirstColumnAsync(ctx, privado.Id);
        var secreta = (await boards.QuickCreateTaskAsync(new QuickCreateTaskRequest(privado.Id, colPriv, "Presupuesto confidencial"),
            seed.OwnerPlatformId, "Owner")).Value!.Item.Number;

        var member = Actor(seed, owner: false);
        var owner = Actor(seed, owner: true);

        // listar_tableros: el miembro solo ve "Soporte"; el owner ve ambos.
        var memberBoards = Payload(await toolset.ExecuteAsync("listar_tableros", "{}", member));
        var names = memberBoards.GetProperty("tableros").EnumerateArray().Select(b => b.GetProperty("nombre").GetString()).ToList();
        Assert.Contains("Soporte", names);
        Assert.DoesNotContain("Gerencia privada", names);
        var ownerBoards = Payload(await toolset.ExecuteAsync("listar_tableros", "{}", owner));
        Assert.Equal(2, ownerBoards.GetProperty("tableros").GetArrayLength());

        // ver_tarea / comentar de una tarea del tablero privado: "no encontre" (no revela que existe).
        var view = await toolset.ExecuteAsync("ver_tarea", Args(new { numero = secreta }), member);
        Assert.True(view.IsError);
        Assert.Contains("No encontre", view.Json);
        var comment = await toolset.ExecuteAsync("comentar", Args(new { numero = secreta, texto = "hola" }), member);
        Assert.True(comment.IsError);
        var board = await toolset.ExecuteAsync("ver_tablero", Args(new { tablero = "Gerencia privada" }), member);
        Assert.True(board.IsError);

        // buscar_tareas tampoco la devuelve al miembro, si al owner.
        var memberSearch = Payload(await toolset.ExecuteAsync("buscar_tareas", Args(new { texto = "confidencial" }), member));
        Assert.Equal(0, memberSearch.GetProperty("total").GetInt32());
        var ownerSearch = Payload(await toolset.ExecuteAsync("buscar_tareas", Args(new { texto = "confidencial" }), owner));
        Assert.Equal(1, ownerSearch.GetProperty("total").GetInt32());

        // El owner si la ve.
        var ownerView = await toolset.ExecuteAsync("ver_tarea", Args(new { numero = secreta }), owner);
        Assert.False(ownerView.IsError, ownerView.Json);

        // El miembro crea, comenta y gestiona checklist en el tablero publico (queda a su nombre "via IA").
        var created = Payload(await toolset.ExecuteAsync("crear_tarea",
            Args(new { tablero = "Soporte", titulo = "Revisar impresora", prioridad = "alta", asignado = seed.MemberEmail }), member));
        var numero = created.GetProperty("numero").GetString()!;
        Assert.Equal("Por hacer", created.GetProperty("columna").GetString());
        Assert.False((await toolset.ExecuteAsync("comentar", Args(new { numero, texto = "Ya llame al proveedor" }), member)).IsError);
        Assert.False((await toolset.ExecuteAsync("checklist", Args(new { numero, accion = "agregar", texto = "Cambiar toner" }), member)).IsError);
        Assert.False((await toolset.ExecuteAsync("checklist", Args(new { numero, accion = "marcar", item = "1" }), member)).IsError);
        var moved = await toolset.ExecuteAsync("mover_tarea", Args(new { numero, columna = "En progreso" }), member);
        Assert.False(moved.IsError, moved.Json);

        var task = await ctx.TaskItems.AsNoTracking().SingleAsync(t => t.Number == numero);
        Assert.Equal(seed.MemberUserId, task.AssigneeTenantUserId);
        Assert.Equal(publico.Id, task.BoardId);
        var detail = Payload(await toolset.ExecuteAsync("ver_tarea", Args(new { numero }), member));
        Assert.Equal("En progreso", detail.GetProperty("columna").GetString());
        Assert.True(detail.GetProperty("checklist")[0].GetProperty("hecho").GetBoolean());
        Assert.Contains(detail.GetProperty("bitacora").EnumerateArray(),
            a => (a.GetProperty("autor").GetString() ?? "").EndsWith("(via IA)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TaskWithRunningFlow_CannotBeMovedByHand_AndAdvancesWithCompletarPaso()
    {
        var seed = await SeedTenantAsync("Mcp Flujo");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var (boards, engine, toolset) = Build(ctx, tenantCtx);

        // Flujo Revision -> Cierre; el miembro es candidato (por cargo) de ambos pasos.
        var imported = (await engine.ImportBpmnAsync(new ImportBpmnRequest("MCP-01", "Flujo MCP", FlowXml))).Value!;
        Assert.True((await engine.PublishAsync(imported.Id)).IsOk);
        var type = await ctx.ActivityTypes.SingleAsync(t => t.Id == seed.ActivityTypeId);
        type.WorkflowDefinitionId = imported.Id;
        await ctx.SaveChangesAsync();

        var org = new OrgUnitService(ctx, tenantCtx);
        var dep = (await org.CreateAsync(new SaveOrgUnitRequest("Operaciones", Classifier: OrgUnitClassifier.Dependencia))).Value!;
        var cargo = (await org.CreateAsync(new SaveOrgUnitRequest("Analista", ParentId: dep.Id, Classifier: OrgUnitClassifier.Cargo))).Value!;
        Assert.True((await org.CreateAsync(new SaveOrgUnitRequest("Analista Persona", ParentId: cargo.Id,
            Classifier: OrgUnitClassifier.Funcionario, TenantUserId: seed.MemberUserId))).IsOk);
        var policies = new WorkflowNodePolicyService(ctx, tenantCtx);
        foreach (var n in imported.Nodes.Where(n => n.BpmnElementId is "Task_Rev" or "Task_Cierre"))
        {
            Assert.True((await policies.AddNodePolicyAsync(n.Id, cargo.Id)).IsOk);
        }

        await boards.CreateBoardAsync(new CreateActivityBoardRequest("Procesos"), seed.OwnerPlatformId, "Owner");
        var member = Actor(seed, owner: false);

        var created = Payload(await toolset.ExecuteAsync("crear_tarea", Args(new { tablero = "Procesos", titulo = "Solicitud con flujo" }), member));
        Assert.True(created.GetProperty("flujo_iniciado").GetBoolean());
        var numero = created.GetProperty("numero").GetString()!;

        // Regla clave: no se mueve de columna a mano.
        var move = await toolset.ExecuteAsync("mover_tarea", Args(new { numero, columna = "Completado" }), member);
        Assert.True(move.IsError);
        Assert.Contains("completar_paso", move.Json);

        // El paso aparece en sus pendientes y en ver_tarea.
        var pending = Payload(await toolset.ExecuteAsync("mis_pasos_pendientes", "{}", member));
        Assert.Equal(1, pending.GetProperty("total").GetInt32());
        var flow = Payload(await toolset.ExecuteAsync("ver_tarea", Args(new { numero }), member)).GetProperty("flujo");
        Assert.Equal("Revision", flow.GetProperty("pasos_actuales")[0].GetProperty("paso").GetString());

        // Tomar y cerrar: avanza a Cierre.
        var claim = await toolset.ExecuteAsync("tomar_paso", Args(new { numero }), member);
        Assert.False(claim.IsError, claim.Json);
        var done = Payload(await toolset.ExecuteAsync("completar_paso", Args(new { numero, comentario = "Revisado por la IA" }), member));
        Assert.Equal("Revision", done.GetProperty("paso_cerrado").GetString());
        Assert.Equal("Cierre", done.GetProperty("siguientes_pasos")[0].GetProperty("paso").GetString());
    }

    /// <summary>
    /// Regresion del iniciador (ADR-0159): los llamadores pasan el id de PLATAFORMA al crear la tarea; el motor
    /// debe normalizarlo a TenantUser para que un paso "hereda al iniciador" quede asignado a quien la creo
    /// (antes quedaba sin encargado y nadie podia atenderlo). Tenant que clasifica solo por CONCEPTO.
    /// </summary>
    [Fact]
    public async Task InheritStartFlow_AssignsCreator_AndAiClosesItByConcept()
    {
        var seed = await SeedTenantAsync("Mcp Iniciador");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var (boards, engine, toolset) = Build(ctx, tenantCtx);

        var imported = (await engine.ImportBpmnAsync(new ImportBpmnRequest("MCP-02", "Flujo iniciador", FlowXml))).Value!;
        var design = new WorkflowDesignService(ctx, engine);
        foreach (var n in imported.Nodes.Where(n => n.BpmnElementId is "Task_Rev" or "Task_Cierre"))
        {
            Assert.True((await design.SetNodeAssigneeAsync(n.Id, WorkflowAssigneeSource.InheritStart, null)).IsOk);
        }
        Assert.True((await engine.PublishAsync(imported.Id)).IsOk);

        // Sin tipos de actividad activos: se clasifica por concepto (ligado al flujo y al tablero).
        var type = await ctx.ActivityTypes.SingleAsync(t => t.Id == seed.ActivityTypeId);
        type.IsArchived = true;
        var board = (await boards.CreateBoardAsync(new CreateActivityBoardRequest("Ventas"), seed.OwnerPlatformId, "Owner")).Value!;
        var cat = new ActividadCategoria { TenantId = seed.TenantId, Codigo = "CAT-90", Nombre = "Pruebas" };
        ctx.ActividadCategorias.Add(cat);
        ctx.ActividadSubcategorias.Add(new ActividadSubcategoria
        {
            TenantId = seed.TenantId,
            CategoriaId = cat.Id,
            Codigo = "CAT-90-01",
            Nombre = "Cotizacion IA",
            WorkflowDefinitionId = imported.Id,
            TaskBoardId = board.Id
        });
        await ctx.SaveChangesAsync();

        var member = Actor(seed, owner: false);
        var noConcept = await toolset.ExecuteAsync("crear_tarea", Args(new { tablero = "Ventas", titulo = "x" }), member);
        Assert.False(noConcept.IsError, noConcept.Json); // un solo concepto en el tablero -> se usa ese

        var created = Payload(await toolset.ExecuteAsync("crear_tarea", Args(new { concepto = "Cotizacion IA", titulo = "Cotizar portatiles" }), member));
        Assert.True(created.GetProperty("flujo_iniciado").GetBoolean());
        var numero = created.GetProperty("numero").GetString()!;

        // El paso quedo asignado al creador: es "mio" sin tener que tomarlo, y lo cierra hasta el final.
        var pending = Payload(await toolset.ExecuteAsync("mis_pasos_pendientes", "{}", member)).GetProperty("pasos");
        Assert.Contains(pending.EnumerateArray(), p => p.GetProperty("tarea").GetString() == numero && p.GetProperty("es_mio").GetBoolean());
        Assert.False((await toolset.ExecuteAsync("completar_paso", Args(new { numero }), member)).IsError);
        var last = Payload(await toolset.ExecuteAsync("completar_paso", Args(new { numero }), member));
        Assert.Equal("Cierre", last.GetProperty("paso_cerrado").GetString());
    }

    // ---- Helpers ----

    private (ActivityBoardService Boards, WorkflowEngine Engine, TableroMcpToolset Toolset) Build(EcorexDbContext ctx, ITenantContext tenantCtx)
    {
        var engine = new WorkflowEngine(ctx, tenantCtx, new NoOpWorkflowRuleHook(), new NoOpTaskBroadcaster());
        var tasks = new TaskItemService(ctx, tenantCtx, new SequenceService(ctx, tenantCtx), engine, new NoOpEmailSender(), new NodeAssigneeResolver(ctx));
        var boards = new ActivityBoardService(ctx, tenantCtx, new SequenceService(ctx, tenantCtx), tasks, new AuditWriter(ctx), new NodeAssigneeResolver(ctx),
            _fixture.CreateFactory(tenantCtx.TenantId));
        var inbox = new WorkflowInboxService(ctx, tenantCtx, new NodeAssigneeResolver(ctx), engine, new WorkflowDesignService(ctx, engine), new NoOpAgentStepRunner());
        return (boards, engine, new TableroMcpToolset(ctx, boards, tasks, inbox));
    }

    private static McpActor Actor(SeedData s, bool owner) => owner
        ? new McpActor(s.TenantId, s.OwnerUserId, s.OwnerPlatformId, "Owner Mcp", true, Guid.NewGuid())
        : new McpActor(s.TenantId, s.MemberUserId, s.MemberPlatformId, "Miembro Mcp", false, Guid.NewGuid());

    private static async Task<Guid> FirstColumnAsync(EcorexDbContext ctx, Guid boardId)
        => await ctx.TaskBoardColumns.AsNoTracking().Where(c => c.BoardId == boardId).OrderBy(c => c.SortOrder).Select(c => c.Id).FirstAsync();

    private static string Args(object o) => JsonSerializer.Serialize(o);

    private static JsonElement Payload(McpToolResult r)
    {
        Assert.False(r.IsError, r.Json);
        using var doc = JsonDocument.Parse(r.Json);
        return doc.RootElement.Clone();
    }

    private async Task<SeedData> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var ownerP = new PlatformUser { Email = $"owner-{tenantId:N}@mcp.test", DisplayName = "Owner Mcp", EmailVerified = true, Status = PlatformUserStatus.Active };
            var memberP = new PlatformUser { Email = $"member-{tenantId:N}@mcp.test", DisplayName = "Miembro Mcp", EmailVerified = true, Status = PlatformUserStatus.Active };
            ctx.PlatformUsers.AddRange(ownerP, memberP);
            var owner = new TenantUser { TenantId = tenantId, PlatformUserId = ownerP.Id, Email = ownerP.Email, TenantRole = TenantRole.Owner, Status = PlatformUserStatus.Active };
            var member = new TenantUser { TenantId = tenantId, PlatformUserId = memberP.Id, Email = memberP.Email, TenantRole = TenantRole.Advisor, Status = PlatformUserStatus.Active };
            ctx.TenantUsers.AddRange(owner, member);
            var type = new ActivityType { TenantId = tenantId, Category = "General", Name = "Mcp" };
            ctx.ActivityTypes.Add(type);
            await ctx.SaveChangesAsync();
            return new SeedData(tenantId, owner.Id, ownerP.Id, member.Id, memberP.Id, memberP.Email, type.Id);
        }
    }

    private sealed record SeedData(Guid TenantId, Guid OwnerUserId, Guid OwnerPlatformId, Guid MemberUserId, Guid MemberPlatformId, string MemberEmail, Guid ActivityTypeId);

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class McpTablerosTests_Postgres : McpTablerosTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public McpTablerosTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture) { }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class McpTablerosTests_SqlServer : McpTablerosTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public McpTablerosTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture) { }
}
