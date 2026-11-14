using Ecorex.Application.Common;
using Ecorex.Application.Organization;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Correccion de registros de tiempo en matriz dual PostgreSQL / SQL Server: el dueno corrige y anula lo suyo, el
/// Owner/Admin corrige lo de cualquiera, otro miembro no puede, la tarea cerrada no deja tocar tiempos, la Bitacora
/// guarda cada correccion y AISLAMIENTO cross-tenant.
/// </summary>
public abstract class TaskWorkLogCorreccionTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected TaskWorkLogCorreccionTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    private sealed record Base(Guid Tenant, Guid Tarea, Guid Admin, Guid Ana, Guid Luis, Guid AnaTu, Guid LuisTu);

    private async Task<Base> SeedAsync(string nombre)
    {
        var tenantId = Guid.CreateVersion7();
        await using (var root = _fixture.CreateContext(tenantId: null))
        {
            root.Tenants.Add(new Tenant { Id = tenantId, Name = nombre, TimeZoneId = "America/Bogota" });
            await root.SaveChangesAsync();
        }
        await using var ctx = _fixture.CreateContext(tenantId);
        (Guid P, Guid T) Usuario(string n, TenantRole rol)
        {
            var p = new PlatformUser { Email = $"{n}-{Guid.NewGuid():N}@worklog.test", DisplayName = n, EmailVerified = true, Status = PlatformUserStatus.Active };
            ctx.PlatformUsers.Add(p);
            var tu = new TenantUser { TenantId = tenantId, PlatformUserId = p.Id, Email = p.Email, TenantRole = rol, Status = PlatformUserStatus.Active };
            ctx.TenantUsers.Add(tu);
            return (p.Id, tu.Id);
        }
        var admin = Usuario("admin", TenantRole.Admin);
        var ana = Usuario("ana", TenantRole.Advisor);
        var luis = Usuario("luis", TenantRole.Advisor);
        var tarea = new TaskItem { TenantId = tenantId, Number = "WL-1", Title = "Tarea con tiempos", Status = TaskItemStatus.Pending, Priority = TaskPriority.Medium };
        ctx.TaskItems.Add(tarea);
        await ctx.SaveChangesAsync();
        return new Base(tenantId, tarea.Id, admin.P, ana.P, luis.P, ana.T, luis.T);
    }

    [Fact]
    public async Task DuenoCorrigeYAnula_AdminCorrigeTodo_OtroNo_YQuedaEnLaBitacora()
    {
        var b = await SeedAsync("Worklog correccion");
        var svc = Tareas(b.Tenant, out var ctx);

        // Ana se equivoca: carga 5 horas en vez de 30 minutos. Luis carga 1 hora.
        var malo = await svc.AddWorkLogAsync(new AddTaskWorkLogRequest(b.Tarea, b.AnaTu, 5 * 3600, "revision", LogActivity: true), b.Ana, "Ana");
        Assert.True(malo.IsOk, malo.Error);
        var deLuis = await svc.AddWorkLogAsync(new AddTaskWorkLogRequest(b.Tarea, b.LuisTu, 3600, "visita"), b.Luis, "Luis");
        Assert.True(deLuis.IsOk, deLuis.Error);
        Assert.Equal(6 * 3600, await svc.TotalSecondsAsync(b.Tarea));

        // Ana corrige lo suyo a 30 minutos.
        var corr = await svc.UpdateWorkLogAsync(new UpdateTaskWorkLogRequest(malo.Value!.Id, 30 * 60, "revision"), b.Ana, "Ana");
        Assert.True(corr.IsOk, corr.Error);
        Assert.Equal(30 * 60 + 3600, await svc.TotalSecondsAsync(b.Tarea));
        Assert.True(await ctx.TaskItemActivities.AnyAsync(a => a.TaskItemId == b.Tarea && a.Text.Contains("de 5h 00m a 30m")));

        // Ana NO puede tocar el registro de Luis.
        var ajeno = await svc.UpdateWorkLogAsync(new UpdateTaskWorkLogRequest(deLuis.Value!.Id, 60, null), b.Ana, "Ana");
        Assert.Equal(TaskCoreStatus.Forbidden, ajeno.Status);
        Assert.Contains("administrador", ajeno.Error);
        Assert.False((await svc.DeleteWorkLogAsync(deLuis.Value.Id, b.Ana, "Ana")).IsOk);

        // El admin si corrige el de Luis; duracion invalida se rechaza.
        Assert.False((await svc.UpdateWorkLogAsync(new UpdateTaskWorkLogRequest(deLuis.Value.Id, 0, null), b.Admin, "Admin")).IsOk);
        Assert.True((await svc.UpdateWorkLogAsync(new UpdateTaskWorkLogRequest(deLuis.Value.Id, 45 * 60, "visita corta"), b.Admin, "Admin")).IsOk);

        // Ana anula el suyo: queda solo el de Luis y la Bitacora conserva lo anulado.
        Assert.True((await svc.DeleteWorkLogAsync(malo.Value.Id, b.Ana, "Ana")).IsOk);
        var quedan = await svc.ListWorkLogsAsync(b.Tarea);
        var unico = Assert.Single(quedan);
        Assert.Equal(45 * 60, unico.Seconds);
        Assert.Equal("visita corta", unico.Note);
        Assert.True(await ctx.TaskItemActivities.AnyAsync(a => a.TaskItemId == b.Tarea && a.Text.Contains("anulo un registro de 30m")));
        Assert.Equal(TaskCoreStatus.NotFound, (await svc.DeleteWorkLogAsync(malo.Value.Id, b.Ana, "Ana")).Status);

        // Tarea cerrada: ya no se corrigen tiempos.
        var tarea = await ctx.TaskItems.SingleAsync(t => t.Id == b.Tarea);
        tarea.Status = TaskItemStatus.Closed;
        await ctx.SaveChangesAsync();
        var cerrada = await svc.UpdateWorkLogAsync(new UpdateTaskWorkLogRequest(unico.Id, 60, null), b.Admin, "Admin");
        Assert.False(cerrada.IsOk);
        Assert.Contains("cerrada", cerrada.Error);
    }

    [Fact]
    public async Task OtroTenant_NoVeNiCorrigeElRegistro()
    {
        var a = await SeedAsync("Worklog tenant A");
        var b = await SeedAsync("Worklog tenant B");
        var svcA = Tareas(a.Tenant, out _);
        var reg = await svcA.AddWorkLogAsync(new AddTaskWorkLogRequest(a.Tarea, a.AnaTu, 600), a.Ana, "Ana");
        Assert.True(reg.IsOk, reg.Error);

        // El admin del tenant B no lo encuentra (filtro global): ni corrige ni anula.
        var svcB = Tareas(b.Tenant, out _);
        Assert.False((await svcB.UpdateWorkLogAsync(new UpdateTaskWorkLogRequest(reg.Value!.Id, 60, null), b.Admin, "Admin")).IsOk);
        Assert.False((await svcB.DeleteWorkLogAsync(reg.Value.Id, b.Admin, "Admin")).IsOk);
        Assert.Equal(600, await svcA.TotalSecondsAsync(a.Tarea));
    }

    private TaskItemService Tareas(Guid tenantId, out Ecorex.Infrastructure.Persistence.EcorexDbContext ctx)
    {
        ctx = _fixture.CreateContext(tenantId);
        var tenantCtx = new FixedTenant(tenantId);
        return new TaskItemService(ctx, tenantCtx, new SequenceService(ctx, tenantCtx),
            new WorkflowEngine(ctx, tenantCtx, new NoOpWorkflowRuleHook(), new NoOpTaskBroadcaster()), new NoOpEmailSender(), new NodeAssigneeResolver(ctx));
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;
        public Guid? UserId => null;
    }
}

public sealed class TaskWorkLogCorreccionTests_Postgres
    : TaskWorkLogCorreccionTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public TaskWorkLogCorreccionTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture)
    {
    }
}

public sealed class TaskWorkLogCorreccionTests_SqlServer
    : TaskWorkLogCorreccionTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public TaskWorkLogCorreccionTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture)
    {
    }
}
