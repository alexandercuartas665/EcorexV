using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion del modulo de plantillas HSM de WhatsApp (ADR-0029) en matriz dual
/// PostgreSQL / SQL Server: round-trip de plantilla, unicidad de (Name, Language) por tenant,
/// aislamiento cross-tenant y transicion de estado por Submit (stub, sin integracion real con
/// Meta). Reusa las fixtures de aislamiento dual (Testcontainers).
/// </summary>
public abstract class WhatsAppTemplatesTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected WhatsAppTemplatesTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Template_RoundTrips_WithVariablesAndLine()
    {
        var seed = await SeedTenantAsync("WA Round-Trip");

        var created = await RunAsync(seed, s => s.CreateAsync(new SaveWhatsAppTemplateRequest(
            Name: "Bienvenida Cliente",
            Language: "es",
            Category: WhatsAppTemplateCategory.Utility,
            BodyText: "Hola {{cliente}}, gracias por contactar a {{empresa}}.",
            WhatsAppLineId: seed.LineId,
            FooterText: "Equipo demo",
            Variables: new[]
            {
                new WhatsAppTemplateVariable("cliente", "Juan"),
                new WhatsAppTemplateVariable("empresa", "SKY")
            })));
        Assert.True(created.IsOk, created.Error);

        var dto = created.Value!;
        // El nombre se normaliza al formato tecnico de Meta.
        Assert.Equal("bienvenida_cliente", dto.Name);
        Assert.Equal(WhatsAppTemplateStatus.Draft, dto.Status);
        Assert.Equal(2, dto.Variables.Count);
        Assert.Equal(seed.LineId, dto.WhatsAppLineId);
        Assert.Equal("Linea Test", dto.WhatsAppLineName);

        // Se relee coherente desde la BD.
        var reloaded = await RunAsync(seed, s => s.GetAsync(dto.Id));
        Assert.NotNull(reloaded);
        Assert.Equal("bienvenida_cliente", reloaded!.Name);

        // Update en borrador cambia la categoria.
        var updated = await RunAsync(seed, s => s.UpdateAsync(dto.Id, new SaveWhatsAppTemplateRequest(
            Name: "Bienvenida Cliente",
            Language: "es",
            Category: WhatsAppTemplateCategory.Marketing,
            BodyText: "Hola {{cliente}}.",
            WhatsAppLineId: seed.LineId)));
        Assert.True(updated.IsOk, updated.Error);
        Assert.Equal(WhatsAppTemplateCategory.Marketing, updated.Value!.Category);
    }

    [Fact]
    public async Task NameLanguage_MustBeUniquePerTenant()
    {
        var seed = await SeedTenantAsync("WA Unico");

        var first = await RunAsync(seed, s => s.CreateAsync(Req("promo", "es", seed.LineId)));
        Assert.True(first.IsOk, first.Error);

        // Mismo nombre + idioma -> Conflict.
        var dup = await RunAsync(seed, s => s.CreateAsync(Req("promo", "es", seed.LineId)));
        Assert.Equal(WhatsAppTemplateServiceStatus.Conflict, dup.Status);

        // Mismo nombre pero OTRO idioma coexiste.
        var otherLang = await RunAsync(seed, s => s.CreateAsync(Req("promo", "en_US", seed.LineId)));
        Assert.True(otherLang.IsOk, otherLang.Error);
    }

    [Fact]
    public async Task Submit_TransitionsToSubmitted_AndSetsSubmittedAt()
    {
        var seed = await SeedTenantAsync("WA Submit");

        var created = await RunAsync(seed, s => s.CreateAsync(Req("aviso", "es", seed.LineId)));
        Assert.True(created.IsOk, created.Error);

        var submitted = await RunAsync(seed, s => s.SubmitAsync(created.Value!.Id));
        Assert.True(submitted.IsOk, submitted.Error);
        Assert.Equal(WhatsAppTemplateStatus.Submitted, submitted.Value!.Status);
        Assert.NotNull(submitted.Value.SubmittedAt);

        // Ya sometida no se puede editar ni re-someter.
        var reSubmit = await RunAsync(seed, s => s.SubmitAsync(created.Value.Id));
        Assert.Equal(WhatsAppTemplateServiceStatus.Invalid, reSubmit.Status);

        // SyncStatus es un stub NotImplemented (sin integracion real con Meta).
        var sync = await RunAsync(seed, s => s.SyncStatusAsync(created.Value.Id));
        Assert.Equal(WhatsAppTemplateServiceStatus.NotImplemented, sync.Status);
    }

    [Fact]
    public async Task CrossTenant_Templates_AreIsolated()
    {
        var a = await SeedTenantAsync("WA Tenant A");
        var b = await SeedTenantAsync("WA Tenant B");

        var tplA = await RunAsync(a, s => s.CreateAsync(Req("solo_de_a", "es", a.LineId)));
        Assert.True(tplA.IsOk, tplA.Error);

        // El tenant B no ve la plantilla de A (filtro global).
        var bList = await RunAsync(b, s => s.ListAsync(includeInactive: true));
        Assert.DoesNotContain(bList, t => t.Id == tplA.Value!.Id);
        Assert.Null(await RunAsync(b, s => s.GetAsync(tplA.Value!.Id)));

        // Mismo (Name, Language) en dos tenants NO colisiona (unico por tenant).
        var tplB = await RunAsync(b, s => s.CreateAsync(Req("solo_de_a", "es", b.LineId)));
        Assert.True(tplB.IsOk, tplB.Error);
    }

    // ---- Helpers ----

    private static SaveWhatsAppTemplateRequest Req(string name, string lang, Guid lineId)
        => new(name, lang, WhatsAppTemplateCategory.Utility, $"Hola {{{{cliente}}}} ({name}).", lineId);

    private async Task<T> RunAsync<T>(SeedData seed, Func<IWhatsAppTemplateService, Task<T>> action)
    {
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantContext = new TestTenantContext(seed.TenantId, seed.UserId);
        var service = new WhatsAppTemplateService(ctx, tenantContext, new AuditWriter(ctx), TimeProvider.System);
        return await action(service);
    }

    private async Task<SeedData> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
            await ctx.SaveChangesAsync();
        }

        var lineId = Guid.CreateVersion7();
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            ctx.WhatsAppLines.Add(new WhatsAppLine
            {
                Id = lineId,
                TenantId = tenantId,
                InstanceName = "Linea Test",
                Provider = WhatsAppProvider.Cloud,
                CloudBusinessAccountId = "111111111111111"
            });
            await ctx.SaveChangesAsync();
        }

        return new SeedData(tenantId, Guid.CreateVersion7(), lineId);
    }

    private sealed record SeedData(Guid TenantId, Guid UserId, Guid LineId);

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class WhatsAppTemplatesTests_Postgres
    : WhatsAppTemplatesTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public WhatsAppTemplatesTests_Postgres(PostgresTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class WhatsAppTemplatesTests_SqlServer
    : WhatsAppTemplatesTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public WhatsAppTemplatesTests_SqlServer(SqlServerTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}
