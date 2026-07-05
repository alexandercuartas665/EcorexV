using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion del catalogo de conceptos (modulo 000270, /conceptos) en matriz
/// dual PostgreSQL / SQL Server, sobre las operaciones aditivas de IActivityTypeService:
/// proceso vinculado (WorkflowDefinitionId validado contra flujos publicados), RequiresForm,
/// archivar/restaurar concepto y categoria, renombrar categoria (con colisiones), mover
/// orden dentro de la categoria y conteos de uso (analogo CANT_USADO legacy).
/// </summary>
public abstract class ActivityTypeCatalogTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected ActivityTypeCatalogTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CreateAndUpdate_PersistWorkflowLink_AndRejectUnpublished()
    {
        var seed = await SeedTenantAsync("Conceptos Flujo");

        // Crear con flujo publicado + RequiresForm: queda persistido.
        var created = await RunAsync(seed, s => s.CreateAsync(new CreateActivityTypeRequest(
            "Comercial", "Cotizacion E2E", "Con flujo", null, seed.PublishedWorkflowId, RequiresForm: true)));
        Assert.True(created.IsOk, created.Error);
        Assert.Equal(seed.PublishedWorkflowId, created.Value!.WorkflowDefinitionId);
        Assert.True(created.Value.RequiresForm);

        // Un flujo NO publicado (borrador) se rechaza con error tipado.
        var draftRejected = await RunAsync(seed, s => s.CreateAsync(new CreateActivityTypeRequest(
            "Comercial", "Con borrador", null, null, seed.DraftWorkflowId)));
        Assert.Equal(TaskCoreStatus.Invalid, draftRejected.Status);

        // Un flujo inexistente tambien.
        var missingRejected = await RunAsync(seed, s => s.CreateAsync(new CreateActivityTypeRequest(
            "Comercial", "Con fantasma", null, null, Guid.CreateVersion7())));
        Assert.Equal(TaskCoreStatus.Invalid, missingRejected.Status);

        // Update limpia el vinculo y el flag.
        var cleared = await RunAsync(seed, s => s.UpdateAsync(created.Value.Id, new UpdateActivityTypeRequest(
            "Comercial", "Cotizacion E2E", "Sin flujo", created.Value.SortOrder, IsArchived: false)));
        Assert.True(cleared.IsOk, cleared.Error);
        Assert.Null(cleared.Value!.WorkflowDefinitionId);
        Assert.False(cleared.Value.RequiresForm);

        // El select de flujos ofrece SOLO publicados no archivados.
        var options = await RunAsync(seed, s => s.ListWorkflowOptionsAsync());
        Assert.Contains(options, o => o.Id == seed.PublishedWorkflowId);
        Assert.DoesNotContain(options, o => o.Id == seed.DraftWorkflowId);
    }

    [Fact]
    public async Task SetArchived_TogglesVisibility_AndDoubleToggleIsInvalid()
    {
        var seed = await SeedTenantAsync("Conceptos Archivado");
        var created = await RunAsync(seed, s => s.CreateAsync(new CreateActivityTypeRequest("Soporte", "Incidente")));
        Assert.True(created.IsOk, created.Error);
        var id = created.Value!.Id;

        var archived = await RunAsync(seed, s => s.SetArchivedAsync(id, archived: true));
        Assert.True(archived.IsOk, archived.Error);
        Assert.True(archived.Value!.IsArchived);

        // Doble archivado -> Invalid tipado (no excepcion).
        var again = await RunAsync(seed, s => s.SetArchivedAsync(id, archived: true));
        Assert.Equal(TaskCoreStatus.Invalid, again.Status);

        // ListAsync por defecto lo oculta; includeArchived lo muestra.
        var defaultList = await RunAsync(seed, s => s.ListAsync());
        Assert.DoesNotContain(defaultList, t => t.Id == id);
        var withArchived = await RunAsync(seed, s => s.ListAsync(includeArchived: true));
        Assert.Contains(withArchived, t => t.Id == id);

        // Restaurar lo devuelve al listado por defecto.
        var restored = await RunAsync(seed, s => s.SetArchivedAsync(id, archived: false));
        Assert.True(restored.IsOk, restored.Error);
        Assert.Contains(await RunAsync(seed, s => s.ListAsync()), t => t.Id == id);
    }

    [Fact]
    public async Task RenameCategory_MovesAllConcepts_AndDetectsCollisions()
    {
        var seed = await SeedTenantAsync("Conceptos Renombrar");
        Assert.True((await RunAsync(seed, s => s.CreateAsync(new CreateActivityTypeRequest("Vieja", "Alfa")))).IsOk);
        Assert.True((await RunAsync(seed, s => s.CreateAsync(new CreateActivityTypeRequest("Vieja", "Beta")))).IsOk);
        Assert.True((await RunAsync(seed, s => s.CreateAsync(new CreateActivityTypeRequest("Destino", "Alfa")))).IsOk);

        // Colision: "Destino" ya tiene un concepto "Alfa".
        var collision = await RunAsync(seed, s => s.RenameCategoryAsync("Vieja", "Destino"));
        Assert.Equal(TaskCoreStatus.Invalid, collision.Status);

        // Renombrar a un nombre libre mueve TODOS los conceptos.
        var renamed = await RunAsync(seed, s => s.RenameCategoryAsync("Vieja", "Nueva"));
        Assert.True(renamed.IsOk, renamed.Error);
        Assert.Equal(2, renamed.Value);
        var all = await RunAsync(seed, s => s.ListAsync(includeArchived: true));
        Assert.Equal(2, all.Count(t => t.Category == "Nueva"));
        Assert.DoesNotContain(all, t => t.Category == "Vieja");

        // Categoria inexistente -> NotFound tipado.
        var missing = await RunAsync(seed, s => s.RenameCategoryAsync("NoExiste", "Otra"));
        Assert.Equal(TaskCoreStatus.NotFound, missing.Status);
    }

    [Fact]
    public async Task SetCategoryArchived_TogglesAllConcepts()
    {
        var seed = await SeedTenantAsync("Conceptos Categoria FLAG_INA");
        Assert.True((await RunAsync(seed, s => s.CreateAsync(new CreateActivityTypeRequest("Auditoria", "Interna")))).IsOk);
        Assert.True((await RunAsync(seed, s => s.CreateAsync(new CreateActivityTypeRequest("Auditoria", "Externa")))).IsOk);

        var archived = await RunAsync(seed, s => s.SetCategoryArchivedAsync("Auditoria", archived: true));
        Assert.True(archived.IsOk, archived.Error);
        Assert.Equal(2, archived.Value);
        var all = await RunAsync(seed, s => s.ListAsync(includeArchived: true));
        Assert.All(all.Where(t => t.Category == "Auditoria"), t => Assert.True(t.IsArchived));

        // Restaurar; el segundo restore no cambia nada (0 conceptos tocados, sin error).
        var restored = await RunAsync(seed, s => s.SetCategoryArchivedAsync("Auditoria", archived: false));
        Assert.True(restored.IsOk, restored.Error);
        Assert.Equal(2, restored.Value);
        var idempotent = await RunAsync(seed, s => s.SetCategoryArchivedAsync("Auditoria", archived: false));
        Assert.True(idempotent.IsOk, idempotent.Error);
        Assert.Equal(0, idempotent.Value);
    }

    [Fact]
    public async Task Move_SwapsOrderWithinCategory_AndEdgesAreInvalid()
    {
        var seed = await SeedTenantAsync("Conceptos Orden");
        var first = await RunAsync(seed, s => s.CreateAsync(new CreateActivityTypeRequest("Ventas", "Prospeccion")));
        var second = await RunAsync(seed, s => s.CreateAsync(new CreateActivityTypeRequest("Ventas", "Cotizacion")));
        var third = await RunAsync(seed, s => s.CreateAsync(new CreateActivityTypeRequest("Ventas", "Cierre")));
        Assert.True(first.IsOk && second.IsOk && third.IsOk);

        // Subir el segundo: pasa a la posicion 0 y el primero baja.
        var moved = await RunAsync(seed, s => s.MoveAsync(second.Value!.Id, up: true));
        Assert.True(moved.IsOk, moved.Error);
        var ordered = (await RunAsync(seed, s => s.ListAsync()))
            .Where(t => t.Category == "Ventas").OrderBy(t => t.SortOrder).Select(t => t.Name).ToList();
        Assert.Equal(["Cotizacion", "Prospeccion", "Cierre"], ordered);

        // Extremos: subir el primero o bajar el ultimo -> Invalid tipado.
        Assert.Equal(TaskCoreStatus.Invalid, (await RunAsync(seed, s => s.MoveAsync(second.Value!.Id, up: true))).Status);
        Assert.Equal(TaskCoreStatus.Invalid, (await RunAsync(seed, s => s.MoveAsync(third.Value!.Id, up: false))).Status);
    }

    [Fact]
    public async Task GetUsage_CountsTotalAndOpenTasks_PerType()
    {
        var seed = await SeedTenantAsync("Conceptos Uso");
        var used = await RunAsync(seed, s => s.CreateAsync(new CreateActivityTypeRequest("Operacion", "Usado")));
        var unused = await RunAsync(seed, s => s.CreateAsync(new CreateActivityTypeRequest("Operacion", "Sin uso")));
        Assert.True(used.IsOk && unused.IsOk);

        await using (var ctx = _fixture.CreateContext(seed.TenantId))
        {
            ctx.TaskItems.AddRange(
                NewTask(seed.TenantId, used.Value!.Id, "T00001", TaskItemStatus.Pending),
                NewTask(seed.TenantId, used.Value.Id, "T00002", TaskItemStatus.InProgress),
                NewTask(seed.TenantId, used.Value.Id, "T00003", TaskItemStatus.Closed));
            await ctx.SaveChangesAsync();
        }

        var usage = await RunAsync(seed, s => s.GetUsageAsync());
        var stats = Assert.Single(usage, u => u.ActivityTypeId == used.Value!.Id);
        Assert.Equal(3, stats.TotalTasks);
        Assert.Equal(2, stats.OpenTasks); // la Closed no cuenta como abierta
        Assert.DoesNotContain(usage, u => u.ActivityTypeId == unused.Value!.Id);

        // DeleteAsync sobre el tipo EN USO no borra: archiva (regla existente, se conserva).
        var deleted = await RunAsync(seed, s => s.DeleteAsync(used.Value!.Id));
        Assert.True(deleted.IsOk, deleted.Error);
        Assert.False(deleted.Value); // false = archivado, no borrado
        var all = await RunAsync(seed, s => s.ListAsync(includeArchived: true));
        Assert.True(Assert.Single(all, t => t.Id == used.Value!.Id).IsArchived);
    }

    // ---- Helpers ----

    private async Task<T> RunAsync<T>(SeedData seed, Func<IActivityTypeService, Task<T>> action)
    {
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var service = new ActivityTypeService(ctx, new TestTenantContext(seed.TenantId));
        return await action(service);
    }

    private static TaskItem NewTask(Guid tenantId, Guid activityTypeId, string number, TaskItemStatus status) => new()
    {
        TenantId = tenantId,
        Number = number,
        Title = $"Tarea {number}",
        ActivityTypeId = activityTypeId,
        Status = status
    };

    /// <summary>
    /// Siembra un tenant fresco con dos definiciones de flujo (una publicada, una borrador)
    /// para probar el vinculo "proceso" del concepto. GUIDs nuevos: seguro en contenedor compartido.
    /// </summary>
    private async Task<SeedData> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
            await ctx.SaveChangesAsync();
        }

        Guid publishedId;
        Guid draftId;
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var published = new WorkflowDefinition
            {
                TenantId = tenantId,
                ProcessCode = "CAT-PUB",
                Name = "Flujo publicado",
                BpmnXml = "<definitions />",
                IsPublished = true
            };
            var draft = new WorkflowDefinition
            {
                TenantId = tenantId,
                ProcessCode = "CAT-DRAFT",
                Name = "Flujo borrador",
                BpmnXml = "<definitions />",
                IsPublished = false
            };
            ctx.WorkflowDefinitions.AddRange(published, draft);
            await ctx.SaveChangesAsync();
            publishedId = published.Id;
            draftId = draft.Id;
        }
        return new SeedData(tenantId, publishedId, draftId);
    }

    private sealed record SeedData(Guid TenantId, Guid PublishedWorkflowId, Guid DraftWorkflowId);

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class ActivityTypeCatalogTests_Postgres
    : ActivityTypeCatalogTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public ActivityTypeCatalogTests_Postgres(PostgresTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class ActivityTypeCatalogTests_SqlServer
    : ActivityTypeCatalogTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public ActivityTypeCatalogTests_SqlServer(SqlServerTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}
