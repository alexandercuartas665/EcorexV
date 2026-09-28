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
/// Matriz dual PostgreSQL / SQL Server del CODIGO AUTOMATICO al crear (iniciales del asignado + consecutivo
/// GLOBAL por tenant), formato {iniciales}-{consecutivo}, ej. RG-0001. Se genera en CreateTaskFormAsync (al
/// crear la respuesta, aunque quede en borrador), es idempotente (no se re-genera en autoguardados) y el
/// consecutivo global no se repite entre vendedores ni bajo concurrencia (CAS de SequenceService).
/// </summary>
public abstract class FormAutoCodeTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected FormAutoCodeTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    // (a) Crear cotizacion con asignado "Richard Gonzalez" -> num_cotizacion = "RG-0001".
    [Fact]
    public async Task Create_WithAssignee_FillsAutoCode_WithInitialsAndConsecutive()
    {
        var seed = await SeedTenantAsync("AutoCode A");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var defId = await SeedAutoCodeFormAsync(ctx, seed);
        var taskId = await SeedTaskAsync(ctx, seed, "T00001", "Richard Gonzalez");

        var responses = BuildResponseService(ctx, seed);
        var created = await responses.CreateTaskFormAsync(taskId, defId);
        Assert.True(created.IsOk, created.Error);

        var read = await responses.GetAsync(created.Value!.ResponseId);
        Assert.NotNull(read);
        Assert.Equal("RG-0001", read!.Data["num_cotizacion"].Value);
    }

    // (b) La siguiente cotizacion (otro vendedor) sube el consecutivo GLOBAL: JA-0002.
    [Fact]
    public async Task Create_SecondSeller_IncrementsGlobalConsecutive()
    {
        var seed = await SeedTenantAsync("AutoCode B");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var defId = await SeedAutoCodeFormAsync(ctx, seed);

        var responses = BuildResponseService(ctx, seed);

        var t1 = await SeedTaskAsync(ctx, seed, "T00001", "Richard Gonzalez");
        var r1 = await responses.CreateTaskFormAsync(t1, defId);
        Assert.True(r1.IsOk, r1.Error);
        var read1 = await responses.GetAsync(r1.Value!.ResponseId);
        Assert.Equal("RG-0001", read1!.Data["num_cotizacion"].Value);

        var t2 = await SeedTaskAsync(ctx, seed, "T00002", "Julian Arango");
        var r2 = await responses.CreateTaskFormAsync(t2, defId);
        Assert.True(r2.IsOk, r2.Error);
        var read2 = await responses.GetAsync(r2.Value!.ResponseId);
        Assert.Equal("JA-0002", read2!.Data["num_cotizacion"].Value);
    }

    // (c) Idempotencia: el codigo NO se re-genera ni se pisa en un autoguardado posterior.
    [Fact]
    public async Task Save_DoesNotRegenerate_AutoCode()
    {
        var seed = await SeedTenantAsync("AutoCode C");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var defId = await SeedAutoCodeFormAsync(ctx, seed);
        var taskId = await SeedTaskAsync(ctx, seed, "T00001", "Richard Gonzalez");

        var responses = BuildResponseService(ctx, seed);
        var created = await responses.CreateTaskFormAsync(taskId, defId);
        Assert.True(created.IsOk, created.Error);
        var responseId = created.Value!.ResponseId;

        // Autoguardado (submit:false) reenviando el mismo valor: NO debe cambiar.
        var data = new Dictionary<string, FormFieldValue>(StringComparer.Ordinal)
        {
            ["num_cotizacion"] = new FormFieldValue("RG-0001", "Text"),
        };
        var saved = await responses.SaveAsync(responseId, data, submit: false, seed.TenantUserId);
        Assert.True(saved.IsOk, saved.Error);

        var read = await responses.GetAsync(responseId);
        Assert.Equal("RG-0001", read!.Data["num_cotizacion"].Value);
    }

    // (d) Concurrencia: dos creaciones en paralelo NO repiten el consecutivo (CAS de SequenceService).
    [Fact]
    public async Task Create_Concurrent_DoesNotRepeatConsecutive()
    {
        var seed = await SeedTenantAsync("AutoCode D");
        Guid defId;
        await using (var setup = _fixture.CreateContext(seed.TenantId))
        {
            defId = await SeedAutoCodeFormAsync(setup, seed);
            // Primera creacion secuencial: crea/inicializa la secuencia "AUTOCOD" (evita el race del Ensure).
            var t0 = await SeedTaskAsync(setup, seed, "T00000", "Richard Gonzalez");
            var r0 = await BuildResponseService(setup, seed).CreateTaskFormAsync(t0, defId);
            Assert.True(r0.IsOk, r0.Error);
        }

        // Dos tareas + dos contextos independientes creando EN PARALELO.
        Guid tA, tB;
        await using (var s = _fixture.CreateContext(seed.TenantId))
        {
            tA = await SeedTaskAsync(s, seed, "T00001", "Ana Perez");
            tB = await SeedTaskAsync(s, seed, "T00002", "Beto Ruiz");
        }

        async Task<string> CreateAsync(Guid taskId)
        {
            await using var c = _fixture.CreateContext(seed.TenantId);
            var res = await BuildResponseService(c, seed).CreateTaskFormAsync(taskId, defId);
            Assert.True(res.IsOk, res.Error);
            var read = await BuildResponseService(c, seed).GetAsync(res.Value!.ResponseId);
            return read!.Data["num_cotizacion"].Value!;
        }

        var results = await Task.WhenAll(CreateAsync(tA), CreateAsync(tB));
        Assert.NotEqual(results[0], results[1]); // consecutivos distintos, sin duplicado.
        Assert.All(results, v => Assert.False(string.IsNullOrWhiteSpace(v)));
    }

    // ---- Helpers ----

    private static FormDefinitionService BuildDefinitionService(EcorexDbContext ctx, SeedData seed)
    {
        var tenant = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        return new(ctx, tenant, new Ecorex.Application.MenuConfig.MenuConfigService(ctx, tenant),
            new SequenceService(ctx, tenant));
    }

    private static FormResponseService BuildResponseService(EcorexDbContext ctx, SeedData seed)
    {
        var tenant = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        var engine = new WorkflowEngine(ctx, tenant, new NoOpWorkflowRuleHook(), new NoOpTaskBroadcaster());
        return new(ctx, engine, new SequenceService(ctx, tenant), tenant, new NoOpFormRecordBroadcaster(),
            new Ecorex.Application.Forms.Lookups.FormLookupService(
                System.Array.Empty<Ecorex.Application.Forms.Lookups.IFormLookupSource>()),
            new NoOpRulesEngine());
    }

    /// <summary>Crea una definicion con un campo Text "num_cotizacion" y habilita el codigo automatico
    /// (target = num_cotizacion, ancho 4). Devuelve el id de la definicion activa.</summary>
    private static async Task<Guid> SeedAutoCodeFormAsync(EcorexDbContext ctx, SeedData seed)
    {
        var definitions = BuildDefinitionService(ctx, seed);
        var created = await definitions.CreateAsync(new CreateFormDefinitionRequest(
            "COT" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), "Registro cotizaciones"));
        var defId = created.Value!.Id;
        var container = (await definitions.AddContainerAsync(defId, new SaveFormContainerRequest("Datos"))).Value!;
        var q = await definitions.AddQuestionAsync(defId, new SaveFormQuestionRequest(
            container.Id, "num_cotizacion", "COD COT", FormControlType.Text));
        Assert.True(q.IsOk, q.Error);
        var activated = await definitions.ActivateAsync(defId);
        Assert.True(activated.IsOk, activated.Error);
        var tx = await definitions.SetTransactionalAsync(defId, new SetFormTransactionalRequest(
            IsTransactional: false, IdentityMode: FormIdentityMode.None, IdentitySourceFieldCode: null,
            AutoCodeEnabled: true, AutoCodeTargetFieldCode: "num_cotizacion", AutoCodePadWidth: 4));
        Assert.True(tx.IsOk, tx.Error);
        return defId;
    }

    /// <summary>Crea una tarea asignada a un usuario con el nombre dado (para las iniciales). Devuelve su id.</summary>
    private static async Task<Guid> SeedTaskAsync(EcorexDbContext ctx, SeedData seed, string number, string assigneeName)
    {
        var platformUser = new PlatformUser
        {
            Email = $"{assigneeName.Replace(' ', '.').ToLowerInvariant()}-{Guid.NewGuid():N}@autocode.test",
            DisplayName = assigneeName,
            EmailVerified = true,
            Status = PlatformUserStatus.Active
        };
        ctx.PlatformUsers.Add(platformUser);
        var tenantUser = new TenantUser
        {
            TenantId = seed.TenantId,
            PlatformUserId = platformUser.Id,
            Email = platformUser.Email
        };
        ctx.TenantUsers.Add(tenantUser);
        var task = new TaskItem
        {
            TenantId = seed.TenantId,
            Number = number,
            Title = "Cotizacion " + number,
            Status = TaskItemStatus.Active,
            AssigneeTenantUserId = tenantUser.Id
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
                Email = $"owner-{tenantId:N}@autocode.test",
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
public sealed class FormAutoCodeTests_Postgres
    : FormAutoCodeTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public FormAutoCodeTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture) { }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class FormAutoCodeTests_SqlServer
    : FormAutoCodeTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public FormAutoCodeTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture) { }
}
