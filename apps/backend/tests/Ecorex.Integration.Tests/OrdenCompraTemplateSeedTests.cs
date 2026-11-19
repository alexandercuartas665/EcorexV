using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Auth;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Siembra de la plantilla "Orden de compra" (grupo "Compras") en todos los tenants (ADR-0189), matriz dual:
/// - Un tenant sin nada recibe el grupo con membrete y la plantilla con los tokens {oc.*}.
/// - Un tenant que ya tenia su grupo "Compras" no recibe otro (solo queda marcado).
/// - Los tenants archivados no se tocan.
/// - Es idempotente y UNA sola vez por tenant: si el tenant borra la plantilla, el siguiente arranque no la resucita.
/// </summary>
public abstract class OrdenCompraTemplateSeedTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected OrdenCompraTemplateSeedTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Siembra_UnaVezPorTenant_RespetaExistentesYArchivados_YNoResucita()
    {
        var vacio = Guid.CreateVersion7();
        var conGrupo = Guid.CreateVersion7();
        var archivado = Guid.CreateVersion7();
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.AddRange(
                new Tenant { Id = vacio, Name = "OC Vacio", Status = TenantStatus.Active },
                new Tenant { Id = conGrupo, Name = "OC Con grupo", Status = TenantStatus.Active },
                new Tenant { Id = archivado, Name = "OC Archivado", Status = TenantStatus.Archived });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _fixture.CreateContext(conGrupo))
        {
            ctx.DocumentTemplateGroups.Add(new DocumentTemplateGroup { TenantId = conGrupo, Name = "Compras", SortOrder = 1 });
            await ctx.SaveChangesAsync();
        }

        // Dos arranques sucesivos: el segundo no duplica.
        await RunSeederAsync();
        await RunSeederAsync();

        await using (var ctx = _fixture.CreateContext(vacio))
        {
            var group = Assert.Single(await ctx.DocumentTemplateGroups.AsNoTracking().ToListAsync());
            Assert.Equal("Compras", group.Name);
            Assert.Contains("{empresa.razonsocial}", group.HeaderHtml);
            var template = Assert.Single(await ctx.DocumentTemplates.AsNoTracking().ToListAsync());
            Assert.Equal("Orden de compra", template.Name);
            Assert.Equal(group.Id, template.GroupId);
            Assert.True(template.IsActive);
            Assert.Contains("{oc.tabla_html}", template.HtmlContent);
            Assert.Contains("{oc.numero}", template.HtmlContent);
            Assert.Single(await ctx.TenantConfigurations.AsNoTracking()
                .Where(c => c.ConfigKey == DatabaseSeeder.OrdenCompraTemplateSeedKey).ToListAsync());
        }

        await using (var ctx = _fixture.CreateContext(conGrupo))
        {
            Assert.Single(await ctx.DocumentTemplateGroups.AsNoTracking().ToListAsync());
            Assert.Empty(await ctx.DocumentTemplates.AsNoTracking().ToListAsync());
            Assert.Single(await ctx.TenantConfigurations.AsNoTracking()
                .Where(c => c.ConfigKey == DatabaseSeeder.OrdenCompraTemplateSeedKey).ToListAsync());
        }

        await using (var ctx = _fixture.CreateContext(archivado))
        {
            Assert.Empty(await ctx.DocumentTemplateGroups.AsNoTracking().ToListAsync());
            Assert.Empty(await ctx.TenantConfigurations.AsNoTracking().ToListAsync());
        }

        // El tenant borra la plantilla y el grupo: el siguiente arranque NO los resucita.
        await using (var ctx = _fixture.CreateContext(vacio))
        {
            ctx.DocumentTemplates.RemoveRange(await ctx.DocumentTemplates.ToListAsync());
            ctx.DocumentTemplateGroups.RemoveRange(await ctx.DocumentTemplateGroups.ToListAsync());
            await ctx.SaveChangesAsync();
        }

        await RunSeederAsync();

        await using (var ctx = _fixture.CreateContext(vacio))
        {
            Assert.Empty(await ctx.DocumentTemplateGroups.AsNoTracking().ToListAsync());
            Assert.Empty(await ctx.DocumentTemplates.AsNoTracking().ToListAsync());
        }
    }

    private async Task RunSeederAsync()
    {
        await using var ctx = _fixture.CreateContext(tenantId: null);
        var seeder = new DatabaseSeeder(ctx, new Pbkdf2PasswordHasher(), NullLogger<DatabaseSeeder>.Instance);
        await seeder.EnsureOrdenCompraTemplateAsync();
    }
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class OrdenCompraTemplateSeedTests_Postgres : OrdenCompraTemplateSeedTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public OrdenCompraTemplateSeedTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture) { }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class OrdenCompraTemplateSeedTests_SqlServer : OrdenCompraTemplateSeedTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public OrdenCompraTemplateSeedTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture) { }
}
