using Ecorex.Application.Common;
using Ecorex.Application.Integraciones.Cerebro;
using Ecorex.Application.Integraciones.Maqueta;
using Ecorex.Application.Mcp;
using Ecorex.Application.Storage;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Cerebros como modulo propio (ADR-0200, ola 1) en matriz dual PostgreSQL / SQL Server:
/// - Migracion de datos: cada maqueta con notas recibe "Cerebro de {nombre}" con el MISMO Id, categoria "Maquetas" y
///   vinculo principal; las notas y enlaces quedan colgando del cerebro (se baja y se vuelve a subir la migracion).
/// - Catalogo: crear/editar/archivar, categorias (archivar deja los cerebros sin categoria), slug unico, permisos.
/// - Vinculo N:M con maquetas: principal = menor orden; un cerebro archivado deja de ser principal.
/// - Aislamiento: otro tenant no ve ni toca los cerebros ni las categorias.
/// </summary>
public abstract class BrainsModuleTestsBase
{
    private const string Migration = "AddBrainsModule";
    private readonly TenantIsolationDbFixture _fixture;

    protected BrainsModuleTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Migracion_CreaCerebroConElMismoId_PorCadaMaquetaConNotas()
    {
        // Se baja la migracion (esquema viejo: notas colgando de la maqueta), se siembran datos y se vuelve a subir.
        string previous;
        await using (var admin = _fixture.CreateContext(tenantId: null))
        {
            var all = admin.Database.GetMigrations().ToList();
            var index = all.FindIndex(m => m.EndsWith("_" + Migration, StringComparison.Ordinal));
            Assert.True(index > 0, "No se encontro la migracion " + Migration);
            previous = all[index - 1];
            await admin.GetService<IMigrator>().MigrateAsync(previous);
        }

        var tenantId = Guid.CreateVersion7();
        var withNotes = Guid.CreateVersion7();
        var withoutNotes = Guid.CreateVersion7();
        var noteA = Guid.CreateVersion7();
        var noteB = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        await using (var admin = _fixture.CreateContext(tenantId: null))
        {
            admin.Tenants.Add(new Tenant { Id = tenantId, Name = "Migracion cerebros" });
            await admin.SaveChangesAsync();
            var db = admin.Database;
            await db.ExecuteSqlAsync($"INSERT INTO mockup_projects (id, tenant_id, slug, name, last_version_number, created_at) VALUES ({withNotes}, {tenantId}, {"visal"}, {"VISAL"}, {0}, {now})");
            await db.ExecuteSqlAsync($"INSERT INTO mockup_projects (id, tenant_id, slug, name, last_version_number, created_at) VALUES ({withoutNotes}, {tenantId}, {"vacia"}, {"Vacia"}, {0}, {now})");
            await db.ExecuteSqlAsync($"INSERT INTO mockup_notes (id, tenant_id, mockup_project_id, title, title_key, body, is_deleted, version, created_at) VALUES ({noteA}, {tenantId}, {withNotes}, {"Inicio"}, {"inicio"}, {"Ver [[Datos]]"}, {false}, {0L}, {now})");
            await db.ExecuteSqlAsync($"INSERT INTO mockup_notes (id, tenant_id, mockup_project_id, title, title_key, body, is_deleted, version, created_at) VALUES ({noteB}, {tenantId}, {withNotes}, {"Datos"}, {"datos"}, {"x"}, {false}, {0L}, {now})");
            await db.ExecuteSqlAsync($"INSERT INTO mockup_note_links (id, tenant_id, mockup_project_id, from_note_id, target_kind, target_key, label, created_at) VALUES ({Guid.CreateVersion7()}, {tenantId}, {withNotes}, {noteA}, {"note"}, {"datos"}, {"Datos"}, {now})");
            await admin.Database.MigrateAsync();
        }

        await using var ctx = _fixture.CreateContext(tenantId);
        var brain = Assert.Single(await ctx.Brains.Include(b => b.Category).ToListAsync());
        Assert.Equal(withNotes, brain.Id);
        Assert.Equal("visal", brain.Slug);
        Assert.Equal("Cerebro de VISAL", brain.Name);
        Assert.Equal("Maquetas", brain.Category?.Name);
        var link = Assert.Single(await ctx.MockupProjectBrains.ToListAsync());
        Assert.Equal((withNotes, withNotes), (link.MockupProjectId, link.BrainId));
        Assert.All(await ctx.MockupNotes.ToListAsync(), n => Assert.Equal(withNotes, n.BrainId));

        // El servicio de notas y el catalogo ya trabajan por el cerebro migrado.
        var notes = new MockupBrainService(ctx, new AuditWriter(ctx), new NoFiles());
        var inicio = (await notes.GetNoteAsync("visal", noteA))!;
        Assert.True(Assert.Single(inicio.Links).Exists);
        var brains = new BrainService(ctx, new AuditWriter(ctx));
        Assert.Equal("visal", (await brains.GetPrimaryForMockupAsync("visal"))!.Slug);
        Assert.Null(await brains.GetPrimaryForMockupAsync("vacia"));
        var card = Assert.Single(await brains.ListAsync());
        Assert.Equal(2, card.Notes);
        Assert.True(Assert.Single(card.Mockups).IsPrimary);
    }

    [Fact]
    public async Task Catalogo_Categorias_Vinculos_YPermisos()
    {
        var tenantId = await SeedTenantAsync("Cerebros catalogo");
        await using var ctx = _fixture.CreateContext(tenantId);
        var svc = new BrainService(ctx, new AuditWriter(ctx));
        var owner = Actor(tenantId, "Owner", admin: true);
        var user = Actor(tenantId, "Usuario", admin: false);

        // Solo Owner/Admin crea y administra.
        Assert.Equal(TaskCoreStatus.Forbidden, (await svc.CreateAsync("X", null, null, user)).Status);
        Assert.Equal(TaskCoreStatus.Forbidden, (await svc.SaveCategoryAsync(null, "Y", null, user)).Status);

        var cat = await svc.SaveCategoryAsync(null, "Procesos internos", "#0ea5e9", owner);
        Assert.True(cat.IsOk, cat.Error);
        Assert.Equal(TaskCoreStatus.Conflict, (await svc.SaveCategoryAsync(null, "procesos  INTERNOS", null, owner)).Status);
        Assert.Equal(TaskCoreStatus.Invalid, (await svc.SaveCategoryAsync(null, "Mal color", "azul", owner)).Status);

        var a = await svc.CreateAsync("Base de conocimiento", "Notas generales", cat.Value!.Id, owner);
        Assert.True(a.IsOk, a.Error);
        Assert.Equal("base-de-conocimiento", a.Value!.Slug);
        var b = await svc.CreateAsync("Base de conocimiento", null, null, owner);
        Assert.Equal("base-de-conocimiento-2", b.Value!.Slug);

        // Editar con concurrencia optimista.
        var edited = await svc.UpdateAsync(a.Value.Id, "Base clinica", "desc", cat.Value.Id, a.Value.Version, owner);
        Assert.True(edited.IsOk, edited.Error);
        Assert.Equal(TaskCoreStatus.Conflict, (await svc.UpdateAsync(a.Value.Id, "Otra", null, null, a.Value.Version, owner)).Status);
        Assert.Equal("Base clinica", Assert.Single(await svc.ListAsync(categoryId: cat.Value.Id)).Name);
        Assert.Single(await svc.ListAsync(search: "clinica"));

        // Vinculo con maquetas: el primero es el principal; vincular es idempotente.
        var mockups = new MockupService(ctx, new TestTenantContext(tenantId), new NoFiles(), new AuditWriter(ctx));
        Assert.True((await mockups.CreateProjectAsync("TRONOX SGDEA", "tronox", null, isOwnerOrAdmin: true)).IsOk);
        Assert.Null(await svc.GetPrimaryForMockupAsync("tronox"));
        var own = await svc.CreateForMockupAsync("tronox", owner);
        Assert.True(own.IsOk, own.Error);
        Assert.Equal(("tronox", "Cerebro de TRONOX SGDEA"), (own.Value!.Slug, own.Value.Name));
        Assert.Equal(own.Value.Id, (await svc.CreateForMockupAsync("tronox", owner)).Value!.Id);
        Assert.True((await svc.LinkMockupAsync(a.Value.Slug, "tronox", owner)).IsOk);
        Assert.True((await svc.LinkMockupAsync(a.Value.Slug, "tronox", owner)).IsOk);
        Assert.Equal(2, await ctx.MockupProjectBrains.CountAsync());
        Assert.Equal("tronox", (await svc.GetPrimaryForMockupAsync("tronox"))!.Slug);
        Assert.Equal(TaskCoreStatus.Forbidden, (await svc.LinkMockupAsync(b.Value.Slug, "tronox", user)).Status);

        // Ola 2: tarjetas vinculadas (principal primero) y cambiar el principal sin perder el orden del resto.
        Assert.True((await svc.LinkMockupAsync(b.Value.Slug, "tronox", owner)).IsOk);
        var linked = await svc.ListForMockupAsync("tronox");
        Assert.Equal(new[] { "tronox", a.Value.Slug, b.Value.Slug }, linked.Select(l => l.Slug));
        Assert.Equal(new[] { true, false, false }, linked.Select(l => l.IsPrimary));
        Assert.Equal(TaskCoreStatus.Forbidden, (await svc.SetPrimaryAsync(b.Value.Slug, "tronox", user)).Status);
        Assert.True((await svc.SetPrimaryAsync(b.Value.Slug, "tronox", owner)).IsOk);
        Assert.Equal(new[] { b.Value.Slug, "tronox", a.Value.Slug }, (await svc.ListForMockupAsync("tronox")).Select(l => l.Slug));
        Assert.Equal(b.Value.Slug, (await svc.GetPrimaryForMockupAsync("tronox"))!.Slug);
        Assert.Equal(TaskCoreStatus.NotFound, (await svc.SetPrimaryAsync("no-existe", "tronox", owner)).Status);
        Assert.True((await svc.SetPrimaryAsync("tronox", "tronox", owner)).IsOk);
        Assert.True((await svc.UnlinkMockupAsync(b.Value.Slug, "tronox", owner)).IsOk);
        Assert.Equal(TaskCoreStatus.Invalid, (await svc.SetPrimaryAsync(b.Value.Slug, "tronox", owner)).Status);

        // Archivar el principal: pasa a serlo el siguiente vinculado.
        Assert.True((await svc.ArchiveAsync(own.Value.Id, owner)).IsOk);
        Assert.Equal(a.Value.Slug, (await svc.GetPrimaryForMockupAsync("tronox"))!.Slug);
        Assert.Null(await svc.GetAsync("tronox"));
        Assert.True((await svc.UnlinkMockupAsync(a.Value.Slug, "tronox", owner)).IsOk);
        Assert.Null(await svc.GetPrimaryForMockupAsync("tronox"));

        // Archivar la categoria deja a sus cerebros sin categoria.
        Assert.True((await svc.ArchiveCategoryAsync(cat.Value.Id, owner)).IsOk);
        Assert.Empty(await svc.ListCategoriesAsync());
        Assert.All(await svc.ListAsync(), x => Assert.Null(x.CategoryId));
    }

    [Fact]
    public async Task OtroTenant_NoVeNiTocaLosCerebros()
    {
        var a = await SeedTenantAsync("Cerebros TA");
        var b = await SeedTenantAsync("Cerebros TB");
        Guid brainId;
        Guid catId;
        await using (var ctxA = _fixture.CreateContext(a))
        {
            var svcA = new BrainService(ctxA, new AuditWriter(ctxA));
            var owner = Actor(a, "Ana", admin: true);
            catId = (await svcA.SaveCategoryAsync(null, "Secreta", null, owner)).Value!.Id;
            brainId = (await svcA.CreateAsync("Cerebro secreto", null, catId, owner)).Value!.Id;
        }

        await using var ctxB = _fixture.CreateContext(b);
        var svcB = new BrainService(ctxB, new AuditWriter(ctxB));
        var intruso = Actor(b, "Intruso", admin: true);
        Assert.Empty(await svcB.ListAsync());
        Assert.Empty(await svcB.ListCategoriesAsync());
        Assert.Null(await svcB.GetAsync("cerebro-secreto"));
        Assert.Equal(TaskCoreStatus.NotFound, (await svcB.UpdateAsync(brainId, "X", null, null, 0, intruso)).Status);
        Assert.Equal(TaskCoreStatus.NotFound, (await svcB.ArchiveAsync(brainId, intruso)).Status);
        Assert.Equal(TaskCoreStatus.NotFound, (await svcB.ArchiveCategoryAsync(catId, intruso)).Status);
        Assert.Equal(TaskCoreStatus.Invalid, (await svcB.CreateAsync("Mio", null, catId, intruso)).Status);
        // Mismo slug en otra empresa: no choca.
        Assert.Equal("cerebro-secreto", (await svcB.CreateAsync("Cerebro secreto", null, null, intruso)).Value!.Slug);
        Assert.Single(await ctxB.Brains.ToListAsync());
    }

    // ---- Helpers ----

    private static McpActor Actor(Guid tenantId, string name, bool admin)
        => new(tenantId, Guid.CreateVersion7(), Guid.CreateVersion7(), name, admin, Guid.NewGuid());

    private async Task<Guid> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId: null);
        ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
        await ctx.SaveChangesAsync();
        return tenantId;
    }

    private sealed class NoFiles : IFileStorageService
    {
        public Task<StoredFileDto> SaveAsync(FileStorageSaveRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<StoredFileDto>> ListAsync(string module, string? ref1 = null, string? ref2 = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StoredFileDto>>(Array.Empty<StoredFileDto>());
        public Task<bool> DeleteAsync(Guid fileId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<StoredFileDto?> GetAsync(Guid fileId, CancellationToken ct = default) => Task.FromResult<StoredFileDto?>(null);
        public Task<FileStorageContent?> ReadAsync(Guid fileId, CancellationToken ct = default) => Task.FromResult<FileStorageContent?>(null);
        public Task<FileStorageContent?> ReadForTenantAsync(Guid fileId, Guid tenantId, CancellationToken ct = default) => Task.FromResult<FileStorageContent?>(null);
        public Task<StoredFileDto?> FindAsync(string module, string? ref1, string? ref2, string fileName, CancellationToken ct = default) => Task.FromResult<StoredFileDto?>(null);
        public Task<IReadOnlyCollection<string>> ListRefsWithFilesAsync(string module, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>());
    }

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class BrainsModuleTests_Postgres : BrainsModuleTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public BrainsModuleTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture) { }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class BrainsModuleTests_SqlServer : BrainsModuleTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public BrainsModuleTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture) { }
}
