using Ecorex.Application.Common;
using Ecorex.Application.Confidentiality;
using Ecorex.Domain.Entities;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Matriz dual PostgreSQL / SQL Server de la politica de confidencialidad por tenant + link
/// permanente /pc/{token} con constancia de aceptacion (ADR-0160). Cubre: alta/edicion (un registro
/// por tenant, token generado), saneo del HTML, resolucion por token (cross-tenant, el UNICO punto
/// que ignora el filtro global), aceptacion con snapshot de version/hash, regeneracion del link y
/// aislamiento cross-tenant de la resolucion y de las constancias.
/// </summary>
public abstract class ConfidentialityPolicyTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected ConfidentialityPolicyTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Save_FirstTime_CreatesSinglePolicyWithToken()
    {
        var tenantId = await SeedTenantAsync("Policy A");
        await using var ctx = _fixture.CreateContext(tenantId);
        var svc = BuildService(ctx, tenantId);

        var saved = await svc.SaveAsync(new ConfidentialityPolicySaveRequest("Mi politica", "<p>Hola</p>", true));

        Assert.False(string.IsNullOrWhiteSpace(saved.PublicToken));
        Assert.Equal("Mi politica", saved.Titulo);

        var fetched = await svc.GetForTenantAsync();
        Assert.NotNull(fetched);
        Assert.Equal(saved.Id, fetched!.Id);
        Assert.Equal(saved.PublicToken, fetched.PublicToken);

        // Un segundo guardado NO crea otra fila (upsert): mismo Id.
        var again = await svc.SaveAsync(new ConfidentialityPolicySaveRequest("Mi politica v2", "<p>Dos</p>", true));
        Assert.Equal(saved.Id, again.Id);
        Assert.Equal(saved.PublicToken, again.PublicToken);
    }

    [Fact]
    public async Task Save_SanitizesHtml_DropsScript()
    {
        var tenantId = await SeedTenantAsync("Policy sanitize");
        await using var ctx = _fixture.CreateContext(tenantId);
        var svc = BuildService(ctx, tenantId);

        var saved = await svc.SaveAsync(new ConfidentialityPolicySaveRequest(
            "T", "<p>ok</p><script>alert(1)</script><a href=\"javascript:evil()\">x</a>", true));

        Assert.DoesNotContain("<script", saved.Contenido, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", saved.Contenido, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<p>ok</p>", saved.Contenido, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveByToken_ReturnsPolicy_CrossTenant()
    {
        var tenantId = await SeedTenantAsync("Policy resolve");
        string token;
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var svc = BuildService(ctx, tenantId);
            var saved = await svc.SaveAsync(new ConfidentialityPolicySaveRequest("Pub", "<p>texto</p>", true));
            token = saved.PublicToken;
        }

        // Visor anonimo: SIN tenant en contexto.
        await using var anonCtx = _fixture.CreateContext(tenantId: null);
        var anon = BuildAnon(anonCtx);
        var pub = await anon.ResolveByTokenAsync(token);

        Assert.NotNull(pub);
        Assert.Equal(tenantId, pub!.TenantId);
        Assert.Equal("Pub", pub.Titulo);
        Assert.False(string.IsNullOrWhiteSpace(pub.ContenidoHash));
    }

    [Fact]
    public async Task ResolveByToken_Invalid_Or_Inactive_ReturnsNull()
    {
        var tenantId = await SeedTenantAsync("Policy inactive");
        string token;
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var svc = BuildService(ctx, tenantId);
            var saved = await svc.SaveAsync(new ConfidentialityPolicySaveRequest("P", "<p>x</p>", Activa: false));
            token = saved.PublicToken;
        }

        await using var anonCtx = _fixture.CreateContext(tenantId: null);
        var anon = BuildAnon(anonCtx);

        Assert.Null(await anon.ResolveByTokenAsync(token));          // existe pero inactiva
        Assert.Null(await anon.ResolveByTokenAsync("token-inexistente"));
    }

    [Fact]
    public async Task Accept_RecordsConstancia_WithSnapshot()
    {
        var tenantId = await SeedTenantAsync("Policy accept");
        string token;
        long version;
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var svc = BuildService(ctx, tenantId);
            var saved = await svc.SaveAsync(new ConfidentialityPolicySaveRequest("P", "<p>contenido</p>", true));
            token = saved.PublicToken;
            version = saved.Version;
        }

        // La pagina fija el ambient del tenant del token -> aqui el contexto lleva ese tenant.
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var svc = BuildService(ctx, tenantId);
            var status = await svc.AcceptAsync(token,
                new ConfidentialityAcceptRequest("Juan Perez", "123456", "juan@test.com", null, null));
            Assert.Equal(ConfidentialityAcceptStatus.Ok, status);
        }

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var svc = BuildService(ctx, tenantId);
            var list = await svc.ListAcceptancesAsync();
            Assert.Single(list);
            Assert.Equal("Juan Perez", list[0].Nombre);
            Assert.Equal("123456", list[0].Identificacion);
            Assert.Equal(version, list[0].PolicyVersionSnapshot);

            var row = await ctx.ConfidentialityPolicyAcceptances.AsNoTracking().FirstAsync();
            Assert.Equal(64, row.ContenidoHashSnapshot.Length);
        }
    }

    [Fact]
    public async Task Accept_MissingData_ReturnsMissing()
    {
        var tenantId = await SeedTenantAsync("Policy missing");
        string token;
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var svc = BuildService(ctx, tenantId);
            token = (await svc.SaveAsync(new ConfidentialityPolicySaveRequest("P", "<p>x</p>", true))).PublicToken;
        }

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var svc = BuildService(ctx, tenantId);
            var status = await svc.AcceptAsync(token, new ConfidentialityAcceptRequest("  ", "", null, null, null));
            Assert.Equal(ConfidentialityAcceptStatus.MissingData, status);
        }
    }

    [Fact]
    public async Task Regenerate_InvalidatesOldToken()
    {
        var tenantId = await SeedTenantAsync("Policy regen");
        string oldToken, newToken;
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var svc = BuildService(ctx, tenantId);
            oldToken = (await svc.SaveAsync(new ConfidentialityPolicySaveRequest("P", "<p>x</p>", true))).PublicToken;
            var regen = await svc.RegenerateLinkAsync();
            newToken = regen!.PublicToken;
        }

        Assert.NotEqual(oldToken, newToken);

        await using var anonCtx = _fixture.CreateContext(tenantId: null);
        var anon = BuildAnon(anonCtx);
        Assert.Null(await anon.ResolveByTokenAsync(oldToken));
        Assert.NotNull(await anon.ResolveByTokenAsync(newToken));
    }

    [Fact]
    public async Task CrossTenant_IsolationOfResolveAndAcceptances()
    {
        var tenantA = await SeedTenantAsync("Tenant A");
        var tenantB = await SeedTenantAsync("Tenant B");

        string tokenA;
        await using (var ctx = _fixture.CreateContext(tenantA))
        {
            tokenA = (await BuildService(ctx, tenantA)
                .SaveAsync(new ConfidentialityPolicySaveRequest("A", "<p>a</p>", true))).PublicToken;
        }
        await using (var ctx = _fixture.CreateContext(tenantB))
        {
            await BuildService(ctx, tenantB)
                .SaveAsync(new ConfidentialityPolicySaveRequest("B", "<p>b</p>", true));
        }

        // El token de A resuelve al tenant A, nunca a B.
        await using (var anonCtx = _fixture.CreateContext(tenantId: null))
        {
            var pub = await BuildAnon(anonCtx).ResolveByTokenAsync(tokenA);
            Assert.Equal(tenantA, pub!.TenantId);
        }

        // Una aceptacion en A no aparece en las constancias de B.
        await using (var ctx = _fixture.CreateContext(tenantA))
        {
            await BuildService(ctx, tenantA)
                .AcceptAsync(tokenA, new ConfidentialityAcceptRequest("Ana", "999", null, null, null));
        }
        await using (var ctx = _fixture.CreateContext(tenantB))
        {
            Assert.Empty(await BuildService(ctx, tenantB).ListAcceptancesAsync());
        }
    }

    // ---- helpers ----

    private ConfidentialityPolicyService BuildService(EcorexDbContext ctx, Guid tenantId)
        => new(ctx, new TestTenantContext(tenantId), _fixture.CreateFactory(tenantId));

    private ConfidentialityPolicyService BuildAnon(EcorexDbContext ctx)
        => new(ctx, new TestTenantContext(null), _fixture.CreateFactory(null));

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
public sealed class ConfidentialityPolicyTests_Postgres
    : ConfidentialityPolicyTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public ConfidentialityPolicyTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture) { }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class ConfidentialityPolicyTests_SqlServer
    : ConfidentialityPolicyTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public ConfidentialityPolicyTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture) { }
}
