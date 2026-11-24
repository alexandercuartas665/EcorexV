using Ecorex.Application.Common;
using Ecorex.Application.Reporting;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Configuracion de la tarjeta de la galeria (ADR-0192) en matriz DUAL PostgreSQL / SQL Server:
/// - <c>UpdateCardAsync</c> renombra, categoriza y describe sin tocar el spec de un tablero; en un panel por spec
///   sincroniza el "title" del JSON; vacio = null; reutiliza la escritura de una categoria existente.
/// - <c>ListCategoriesAsync</c> devuelve las categorias distintas del tenant (incluye archivados) y nunca las de
///   otro tenant.
/// - Nombre vacio se rechaza; un reporte de otro tenant no existe (devuelve false).
/// </summary>
public abstract class ReportCardConfigTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected ReportCardConfigTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task UpdateCard_RenombraCategorizaYDescribe_YListaCategoriasDelTenant()
    {
        var t = await SeedTenantAsync();
        var otro = await SeedTenantAsync();
        var tablero = await AddReportAsync(t, "Tablero viejo", "native:taskitem", "{\"title\":\"Tablero viejo\",\"x\":1}");
        var panel = await AddReportAsync(t, "Panel viejo", "panel:spec", "{\"title\":\"Panel viejo\",\"widgets\":[]}");
        var archivado = await AddReportAsync(t, "Archivado", "native:taskitem", "{}", ReportDefinitionStatus.Archived);
        var ajeno = await AddReportAsync(otro, "De otro tenant", "native:taskitem", "{}");

        Assert.True(await Svc(t).UpdateCardAsync(tablero, "  Ventas del mes  ", "  Ventas   comerciales ", " Resumen "));
        // "ventas comerciales" ya existe como "Ventas comerciales": se reutiliza esa escritura.
        Assert.True(await Svc(t).UpdateCardAsync(panel, "Panel nuevo", "ventas comerciales", ""));
        Assert.True(await Svc(t).UpdateCardAsync(archivado, "Archivado", "Operaciones", null));
        Assert.True(await Svc(otro).UpdateCardAsync(ajeno, "De otro tenant", "Secreta", null));

        await using (var ctx = _fixture.CreateContext(t))
        {
            var tb = await ctx.ReportDefinitions.AsNoTracking().SingleAsync(d => d.Id == tablero);
            Assert.Equal("Ventas del mes", tb.Name);
            Assert.Equal("Ventas comerciales", tb.Category);
            Assert.Equal("Resumen", tb.Description);
            var tbSpec = System.Text.Json.Nodes.JsonNode.Parse(tb.SpecJson)!;   // jsonb reordena: se compara por contenido
            Assert.Equal("Tablero viejo", (string?)tbSpec["title"]);              // un tablero no toca su spec
            Assert.Equal(1, (int?)tbSpec["x"]);

            var pn = await ctx.ReportDefinitions.AsNoTracking().SingleAsync(d => d.Id == panel);
            Assert.Equal("Panel nuevo", pn.Name);
            Assert.Equal("Ventas comerciales", pn.Category);
            Assert.Null(pn.Description);
            var pnSpec = System.Text.Json.Nodes.JsonNode.Parse(pn.SpecJson)!;
            Assert.Equal("Panel nuevo", (string?)pnSpec["title"]);               // el panel sincroniza su titulo
            Assert.NotNull(pnSpec["widgets"]);
        }

        var cats = await Svc(t).ListCategoriesAsync();
        // Distintas, ordenadas, sin la de otro tenant ni la del reporte ARCHIVADO ("Operaciones", ADR-0206).
        Assert.Equal(new[] { "Ventas comerciales" }, cats);

        var summary = Assert.Single(await Svc(t).ListAsync(seeAll: true, rolId: null), r => r.Id == tablero);
        Assert.Equal("Ventas comerciales", summary.Category);
        Assert.Equal("Resumen", summary.Description);

        // Quitar la categoria la deja en null.
        Assert.True(await Svc(t).UpdateCardAsync(tablero, "Ventas del mes", "   ", null));
        await using (var ctx = _fixture.CreateContext(t))
        {
            Assert.Null((await ctx.ReportDefinitions.AsNoTracking().SingleAsync(d => d.Id == tablero)).Category);
        }

        // Aislamiento: el reporte de otro tenant no existe para t, y su categoria no se filtra.
        Assert.False(await Svc(t).UpdateCardAsync(ajeno, "Hackeado", null, null));
        Assert.DoesNotContain("Secreta", await Svc(t).ListCategoriesAsync());

        // Nombre vacio se rechaza.
        await Assert.ThrowsAsync<ArgumentException>(() => Svc(t).UpdateCardAsync(tablero, "  ", "X", null));
    }

    private ReportDefinitionService Svc(Guid tenantId) =>
        new(_fixture.CreateContext(tenantId), new TestTenantContext(tenantId), null!);

    private async Task<Guid> SeedTenantAsync()
    {
        var tenantId = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId: null);
        ctx.Tenants.Add(new Tenant { Id = tenantId, Name = "T-" + tenantId.ToString("N")[..8] });
        await ctx.SaveChangesAsync();
        return tenantId;
    }

    private async Task<Guid> AddReportAsync(Guid tenantId, string name, string sourceKey, string specJson,
        ReportDefinitionStatus status = ReportDefinitionStatus.Active)
    {
        var id = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId);
        ctx.ReportDefinitions.Add(new ReportDefinition
        {
            Id = id,
            TenantId = tenantId,
            Name = name,
            Kind = ReportDefinitionKind.Dashboard,
            Status = status,
            SourceKey = sourceKey,
            SpecJson = specJson
        });
        await ctx.SaveChangesAsync();
        return id;
    }

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class ReportCardConfigTests_Postgres
    : ReportCardConfigTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public ReportCardConfigTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class ReportCardConfigTests_SqlServer
    : ReportCardConfigTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public ReportCardConfigTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture)
    {
    }
}
