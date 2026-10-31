using Ecorex.Application.Actividades;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tablero COMPARTIDO por categoria al crear conceptos (subcategorias) sin elegir tablero
/// (decision del usuario 2026-10-27, reemplaza el 1:1 anterior). Matriz dual PG / SQL Server:
/// las subcategorias de una misma categoria reusan un solo tablero (nombrado por la categoria);
/// otra categoria obtiene el suyo; elegir un tablero explicito se respeta.
/// </summary>
public abstract class ActivityCatalogBoardSharingTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected ActivityCatalogBoardSharingTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Subcategories_ShareOneBoardPerCategory_AndRespectExplicitBoard()
    {
        var tenantId = await SeedTenantAsync("Catalogo Tablero Compartido");
        await using var ctx = _fixture.CreateContext(tenantId);
        var svc = new ActividadCatalogoService(ctx, new TestTenantContext(tenantId));

        // Categoria A con DOS conceptos sin tablero -> comparten UN solo tablero.
        var catA = (await svc.CreateCategoriaAsync(new SaveCategoriaRequest("Comercial"))).Value!;
        var a1 = (await svc.CreateSubcategoriaAsync(new SaveSubcategoriaRequest(catA.Id, "Nuevo lead"))).Value!;
        var a2 = (await svc.CreateSubcategoriaAsync(new SaveSubcategoriaRequest(catA.Id, "Cotizacion"))).Value!;

        var boardA1 = await ctx.ActividadSubcategorias.AsNoTracking().Where(s => s.Id == a1.Id).Select(s => s.TaskBoardId).FirstAsync();
        var boardA2 = await ctx.ActividadSubcategorias.AsNoTracking().Where(s => s.Id == a2.Id).Select(s => s.TaskBoardId).FirstAsync();
        Assert.NotNull(boardA1);
        Assert.Equal(boardA1, boardA2); // mismo tablero compartido

        // El tablero compartido se nombra por la categoria y existe una sola vez.
        var boardName = await ctx.TaskBoards.AsNoTracking().Where(b => b.Id == boardA1!.Value).Select(b => b.Name).FirstAsync();
        Assert.Equal("Comercial", boardName);
        Assert.Equal(1, await ctx.TaskBoards.CountAsync());

        // Ambos conceptos quedan con estado de cierre (la columna IsDone del tablero compartido).
        var doneColId = await ctx.TaskBoardColumns.AsNoTracking()
            .Where(c => c.BoardId == boardA1!.Value && c.IsDone).Select(c => (Guid?)c.Id).FirstAsync();
        var colA1 = await ctx.ActividadSubcategorias.AsNoTracking().Where(s => s.Id == a1.Id).Select(s => s.TaskBoardColumnId).FirstAsync();
        var colA2 = await ctx.ActividadSubcategorias.AsNoTracking().Where(s => s.Id == a2.Id).Select(s => s.TaskBoardColumnId).FirstAsync();
        Assert.Equal(doneColId, colA1);
        Assert.Equal(doneColId, colA2);

        // Categoria B con un concepto -> obtiene SU propio tablero (ahora hay 2 tableros).
        var catB = (await svc.CreateCategoriaAsync(new SaveCategoriaRequest("Soporte"))).Value!;
        var b1 = (await svc.CreateSubcategoriaAsync(new SaveSubcategoriaRequest(catB.Id, "Incidencia"))).Value!;
        var boardB1 = await ctx.ActividadSubcategorias.AsNoTracking().Where(s => s.Id == b1.Id).Select(s => s.TaskBoardId).FirstAsync();
        Assert.NotNull(boardB1);
        Assert.NotEqual(boardA1, boardB1);
        Assert.Equal(2, await ctx.TaskBoards.CountAsync());

        // Elegir un tablero explicito se respeta (no crea ni reusa otro): el concepto apunta al elegido.
        var a3 = (await svc.CreateSubcategoriaAsync(
            new SaveSubcategoriaRequest(catA.Id, "Con tablero propio", TaskBoardId: boardB1!.Value))).Value!;
        var boardA3 = await ctx.ActividadSubcategorias.AsNoTracking().Where(s => s.Id == a3.Id).Select(s => s.TaskBoardId).FirstAsync();
        Assert.Equal(boardB1, boardA3);
        Assert.Equal(2, await ctx.TaskBoards.CountAsync()); // sigue habiendo 2, no se creo uno nuevo
    }

    // ---- Helpers ----

    private async Task<Guid> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId: null);
        ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
        await ctx.SaveChangesAsync();
        return tenantId;
    }

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class ActivityCatalogBoardSharingTests_Postgres
    : ActivityCatalogBoardSharingTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public ActivityCatalogBoardSharingTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture) { }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class ActivityCatalogBoardSharingTests_SqlServer
    : ActivityCatalogBoardSharingTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public ActivityCatalogBoardSharingTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture) { }
}
