using Ecorex.Application.Forms.Lookups;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Fuente de lookup "Actividades" (TaskItem): busqueda por titulo/referencia, filtro por cliente (id o
/// nombre, tambien llegado por token {columna} de la fila de una grilla), cascada vacia = sin resultados,
/// archivadas excluidas por defecto, resolver por referencia y aislamiento entre tenants.
/// </summary>
public abstract class ActivityLookupSourceTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected ActivityLookupSourceTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    private sealed record Seed(Guid TenantId, Guid AcmeId, Guid BetaId);

    private async Task<Seed> SeedAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
            await ctx.SaveChangesAsync();
        }

        await using var c = _fixture.CreateContext(tenantId);
        var acme = new Tercero { TenantId = tenantId, Nombre = "ACME Metales", Tipo = TerceroTipo.Empresa };
        var beta = new Tercero { TenantId = tenantId, Nombre = "Beta Ingenieria", Tipo = TerceroTipo.Empresa };
        c.Terceros.AddRange(acme, beta);
        c.TaskItems.AddRange(
            new TaskItem { TenantId = tenantId, Number = "T00001", Title = "OT tanque ACME", Status = TaskItemStatus.Active, Priority = TaskPriority.Medium, TerceroId = acme.Id },
            new TaskItem { TenantId = tenantId, Number = "T00002", Title = "OT soporte ACME", Status = TaskItemStatus.Pending, Priority = TaskPriority.Medium, TerceroId = acme.Id },
            new TaskItem { TenantId = tenantId, Number = "T00003", Title = "OT vieja ACME", Status = TaskItemStatus.Done, Priority = TaskPriority.Medium, TerceroId = acme.Id, IsArchived = true },
            new TaskItem { TenantId = tenantId, Number = "T00004", Title = "OT puente Beta", Status = TaskItemStatus.Active, Priority = TaskPriority.Medium, TerceroId = beta.Id },
            new TaskItem { TenantId = tenantId, Number = "T00005", Title = "Sin cliente", Status = TaskItemStatus.Active, Priority = TaskPriority.Medium });
        await c.SaveChangesAsync();
        return new Seed(tenantId, acme.Id, beta.Id);
    }

    private async Task<FormLookupPage> SearchAsync(Guid tenantId, string? query, string? filterJson)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        var source = new ActivityLookupSource(ctx);
        return await source.SearchAsync(new FormLookupRequest(
            FormSourceKind.Activity, null, query, filterJson, "titulo", new[] { "referencia", "titulo", "cliente" }));
    }

    private static string[] Refs(FormLookupPage p) => p.Items.Select(i => i.Fields["referencia"]!).OrderBy(x => x).ToArray();

    [Fact]
    public async Task Filtra_Por_Cliente_Id_O_Nombre_Y_Excluye_Archivadas()
    {
        var s = await SeedAsync("Act Lookup Cliente");

        Assert.Equal(new[] { "T00001", "T00002" }, Refs(await SearchAsync(s.TenantId, null, $$"""{"cliente":"{{s.AcmeId}}"}""")));
        Assert.Equal(new[] { "T00001", "T00002" }, Refs(await SearchAsync(s.TenantId, null, """{"cliente":"acme"}""")));
        Assert.Equal(new[] { "T00001", "T00002", "T00003" }, Refs(await SearchAsync(s.TenantId, null, $$"""{"cliente":"{{s.AcmeId}}","archivadas":"true"}""")));
        Assert.Equal(new[] { "T00004" }, Refs(await SearchAsync(s.TenantId, null, $$"""{"cliente_id":"{{s.BetaId}}"}""")));
    }

    [Fact]
    public async Task Cascada_Desde_La_Fila_Y_Cliente_Vacio_No_Lista_Nada()
    {
        var s = await SeedAsync("Act Lookup Cascada");
        const string filtro = """{"cliente":"{proyecto_cliente}"}""";

        // La fila ya tiene cliente: el renderer sustituye el token y solo salen sus actividades.
        var conCliente = FormGridFilterTokens.Resolve(filtro, new Dictionary<string, string?> { ["proyecto_cliente"] = s.BetaId.ToString() });
        Assert.Equal(new[] { "T00004" }, Refs(await SearchAsync(s.TenantId, null, conCliente)));

        // La fila aun no tiene cliente: lista vacia (primero se elige el padre).
        var sinCliente = FormGridFilterTokens.Resolve(filtro, new Dictionary<string, string?>());
        Assert.Empty((await SearchAsync(s.TenantId, null, sinCliente)).Items);
    }

    [Fact]
    public async Task Busca_Por_Titulo_O_Referencia_Y_Resuelve_Por_Referencia()
    {
        var s = await SeedAsync("Act Lookup Buscar");

        Assert.Equal(new[] { "T00004" }, Refs(await SearchAsync(s.TenantId, "puente", null)));
        Assert.Equal(new[] { "T00002" }, Refs(await SearchAsync(s.TenantId, "t00002", null)));

        await using var ctx = _fixture.CreateContext(s.TenantId);
        var item = await new ActivityLookupSource(ctx).ResolveAsync("", "T00001", new[] { "titulo", "cliente" });
        Assert.NotNull(item);
        Assert.Equal("OT tanque ACME", item!.Fields["titulo"]);
        Assert.Equal("ACME Metales", item.Fields["cliente"]);
    }

    [Fact]
    public async Task No_Cruza_Tenants()
    {
        var a = await SeedAsync("Act Lookup A");
        var b = await SeedAsync("Act Lookup B");

        // Desde B, el cliente de A no trae nada aunque se pase su id.
        Assert.Empty((await SearchAsync(b.TenantId, null, $$"""{"cliente":"{{a.AcmeId}}"}""")).Items);
        Assert.Equal(4, (await SearchAsync(b.TenantId, null, null)).Total);
    }
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class ActivityLookupSourceTests_Postgres
    : ActivityLookupSourceTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public ActivityLookupSourceTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (corre en el CI).</summary>
public sealed class ActivityLookupSourceTests_SqlServer
    : ActivityLookupSourceTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public ActivityLookupSourceTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture)
    {
    }
}
