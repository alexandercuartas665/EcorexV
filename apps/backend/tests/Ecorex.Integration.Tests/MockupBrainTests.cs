using System.IO.Compression;
using System.Text;
using Ecorex.Application.Common;
using Ecorex.Application.Integraciones.Maqueta;
using Ecorex.Application.Mcp;
using Ecorex.Application.Storage;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Cerebro digital de la maqueta (ADR-0183) en matriz dual PostgreSQL / SQL Server:
/// - Notas enlazadas: enlaces derivados, backlinks, nodos fantasma y grafo (notas, pantallas, tarjetas, etiquetas).
/// - Renombrar reescribe los [[enlaces]] de las demas notas; concurrencia optimista por Version.
/// - Archivar (soft-delete) solo autor u Owner/Admin; el titulo vuelve a quedar libre (indice unico filtrado).
/// - Importar un vault de Obsidian en una transaccion; omitir o sobrescribir existentes.
/// - Aislamiento: otro tenant no ve ni escribe en el cerebro aunque use el mismo slug.
/// </summary>
public abstract class MockupBrainTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected MockupBrainTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Notas_Backlinks_Fantasmas_Renombrar_YGrafo()
    {
        var tenantId = await SeedTenantAsync("Cerebro A");
        await using var ctx = _fixture.CreateContext(tenantId);
        var slug = await CreateProjectAsync(ctx, tenantId);
        var brain = new MockupBrainService(ctx, new AuditWriter(ctx), new BrainFiles());
        var ana = Actor(tenantId, "Ana", admin: false);

        var a = await brain.SaveNoteAsync(slug, null, "Menu principal", "Ver [[Radicacion]], [[inicio.html]] y [[T99999]] #ux", null, ana);
        Assert.True(a.IsOk, a.Error);
        var ghost = Assert.Single(a.Value!.Links, l => l.Kind == "note");
        Assert.False(ghost.Exists);

        var graph = (await brain.GetGraphAsync(slug))!;
        Assert.Contains(graph.Nodes, n => n is { Kind: "missing", Label: "Radicacion" });
        Assert.Contains(graph.Nodes, n => n is { Kind: "page", Key: "inicio.html" });
        Assert.Contains(graph.Nodes, n => n is { Kind: "task", Key: "T99999" });
        Assert.Contains(graph.Nodes, n => n is { Kind: "tag", Key: "ux" });
        Assert.Equal(4, graph.Edges.Count);

        // Crear la nota fantasma la resuelve: backlinks y el grafo ya no tienen el pendiente.
        var b = await brain.SaveNoteAsync(slug, null, "radicacion", "Vuelve a [[Menu principal]]", null, ana);
        Assert.True(b.IsOk, b.Error);
        Assert.Equal("Menu principal", Assert.Single(b.Value!.Backlinks).Title);
        Assert.True(Assert.Single(b.Value.Links).Exists);
        graph = (await brain.GetGraphAsync(slug))!;
        Assert.DoesNotContain(graph.Nodes, n => n.Kind == "missing");
        Assert.Equal(2, graph.Nodes.Single(n => n.NoteId == b.Value.Id).Degree);

        // Titulo repetido (sin mayusculas) e invalido.
        Assert.Equal(TaskCoreStatus.Conflict, (await brain.SaveNoteAsync(slug, null, "MENU  principal", "x", null, ana)).Status);
        Assert.Equal(TaskCoreStatus.Invalid, (await brain.SaveNoteAsync(slug, null, "T00001", "x", null, ana)).Status);

        // Renombrar: los [[radicacion]] de otras notas pasan al titulo nuevo.
        var renamed = await brain.SaveNoteAsync(slug, b.Value.Id, "Radicacion de PQRS", b.Value.Body, b.Value.Version, ana);
        Assert.True(renamed.IsOk, renamed.Error);
        var menu = (await brain.GetNoteAsync(slug, a.Value.Id))!;
        Assert.Contains("[[Radicacion de PQRS]]", menu.Body);
        Assert.True(menu.Links.Single(l => l.Kind == "note").Exists);
        Assert.Equal("Menu principal", Assert.Single(renamed.Value!.Backlinks).Title);

        // Concurrencia optimista: guardar con una version vieja es conflicto.
        var stale = await brain.SaveNoteAsync(slug, b.Value.Id, "Radicacion de PQRS", "pisar", b.Value.Version, ana);
        Assert.Equal(TaskCoreStatus.Conflict, stale.Status);

        // Lista y busqueda por contenido.
        var all = await brain.ListNotesAsync(slug);
        Assert.Equal(2, all.Count);
        Assert.Equal(1, all.Single(n => n.Title == "Menu principal").BackLinks);
        Assert.Single(await brain.ListNotesAsync(slug, "vuelve"));
    }

    [Fact]
    public async Task Archivar_SoloAutorOAdmin_LiberaElTitulo_YDejaFantasma()
    {
        var tenantId = await SeedTenantAsync("Cerebro Archivo");
        await using var ctx = _fixture.CreateContext(tenantId);
        var slug = await CreateProjectAsync(ctx, tenantId);
        var brain = new MockupBrainService(ctx, new AuditWriter(ctx), new BrainFiles());
        var ana = Actor(tenantId, "Ana", admin: false);
        var luis = Actor(tenantId, "Luis", admin: false);
        var owner = Actor(tenantId, "Owner", admin: true);

        var idea = (await brain.SaveNoteAsync(slug, null, "Idea", "contenido", null, ana)).Value!;
        var refNote = (await brain.SaveNoteAsync(slug, null, "Indice", "[[Idea]]", null, luis)).Value!;

        Assert.Equal(TaskCoreStatus.Forbidden, (await brain.ArchiveNoteAsync(slug, idea.Id, luis)).Status);
        Assert.True((await brain.ArchiveNoteAsync(slug, idea.Id, ana)).IsOk);

        Assert.DoesNotContain(await brain.ListNotesAsync(slug), n => n.Title == "Idea");
        Assert.Null(await brain.GetNoteAsync(slug, idea.Id));
        // El enlace entrante queda y su destino pasa a pendiente (fantasma).
        Assert.Contains((await brain.GetGraphAsync(slug))!.Nodes, n => n is { Kind: "missing", Label: "Idea" });
        Assert.False((await brain.GetNoteAsync(slug, refNote.Id))!.Links.Single().Exists);

        // Owner/Admin archiva notas ajenas.
        Assert.True((await brain.ArchiveNoteAsync(slug, refNote.Id, owner)).IsOk);
        Assert.Empty((await brain.GetGraphAsync(slug))!.Nodes);

        // El soft-delete se conserva en la BD (no hay DELETE fisico) y el titulo queda libre.
        var row = await ctx.MockupNotes.IgnoreQueryFilters().SingleAsync(n => n.Id == idea.Id);
        Assert.True(row.IsDeleted);
        Assert.NotNull(row.DeletedAt);
        var again = await brain.SaveNoteAsync(slug, null, "Idea", "segunda vida", null, luis);
        Assert.True(again.IsOk, again.Error);
    }

    [Fact]
    public async Task ImportarVault_CreaOmiteOSobrescribe_EnUnaTransaccion()
    {
        var tenantId = await SeedTenantAsync("Cerebro Vault");
        await using var ctx = _fixture.CreateContext(tenantId);
        var slug = await CreateProjectAsync(ctx, tenantId);
        var files = new BrainFiles();
        var brain = new MockupBrainService(ctx, new AuditWriter(ctx), files);
        var ana = Actor(tenantId, "Ana", admin: false);
        Assert.True((await brain.SaveNoteAsync(slug, null, "Inicio", "mia", null, ana)).IsOk);

        byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 7, 7, 7 };
        var zip = ZipEntries(
            ("vault/Inicio.md", Encoding.UTF8.GetBytes("del vault [[Ideas]] ![[a.png]]")),
            ("vault/Ideas.md", Encoding.UTF8.GetBytes("Ideas [[Inicio]] [[inicio.html]] #tema")),
            ("vault/Sub/Pendientes.md", Encoding.UTF8.GetBytes("- [ ] [[Ideas]]")),
            ("vault/.obsidian/app.json", Encoding.UTF8.GetBytes("{}")),
            ("vault/img/a.png", png),
            ("vault/img/b.svg", Encoding.UTF8.GetBytes("<svg/>")));

        var first = await brain.ImportVaultAsync(slug, zip, overwrite: false, ana);
        Assert.True(first.IsOk, first.Error);
        Assert.Equal(2, first.Value!.Created);
        Assert.Equal(1, first.Value.Skipped);
        Assert.Equal(1, first.Value.IgnoredFiles);
        Assert.Equal(1, first.Value.AssetsCreated);
        var asset = Assert.Single(await brain.ListAssetsAsync(slug));
        Assert.Equal("a.png", asset.Name);
        Assert.True(asset.IsImage);
        Assert.StartsWith("/archivo/", asset.Url);
        Assert.Equal("mia", (await brain.GetNoteAsync(slug, null, "inicio"))!.Body);
        var ideas = (await brain.GetNoteAsync(slug, null, "Ideas"))!;
        Assert.Equal("vault/Ideas.md", ideas.ImportedFrom);
        Assert.Equal(new[] { "Pendientes" }, ideas.Backlinks.Select(x => x.Title));

        var second = await brain.ImportVaultAsync(slug, zip, overwrite: true, ana);
        Assert.True(second.IsOk, second.Error);
        Assert.Equal(3, second.Value!.Updated);
        Assert.Equal(1, second.Value.AssetsCreated);
        Assert.Single(await brain.ListAssetsAsync(slug));
        var third = await brain.ImportVaultAsync(slug, zip, overwrite: false, ana);
        Assert.Equal(1, third.Value!.AssetsSkipped);
        Assert.Single(await brain.ListAssetsAsync(slug));
        Assert.Equal("del vault [[Ideas]] ![[a.png]]", (await brain.GetNoteAsync(slug, null, "Inicio"))!.Body);
        Assert.Equal(2, (await brain.GetNoteAsync(slug, null, "Ideas"))!.Backlinks.Count);
        Assert.Equal(3, (await brain.ListNotesAsync(slug)).Count);

        Assert.Equal(TaskCoreStatus.Invalid, (await brain.ImportVaultAsync(slug, new byte[] { 1, 2 }, false, ana)).Status);
    }

    [Fact]
    public async Task SubirAdjunto_ValidaFormatoYFirma_YNumeraRepetidos()
    {
        var tenantId = await SeedTenantAsync("Cerebro Adjuntos");
        await using var ctx = _fixture.CreateContext(tenantId);
        var slug = await CreateProjectAsync(ctx, tenantId);
        var brain = new MockupBrainService(ctx, new AuditWriter(ctx), new BrainFiles());
        var ana = Actor(tenantId, "Ana", admin: false);
        byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };

        var first = await brain.UploadAssetAsync(slug, "captura.png", png, ana);
        Assert.True(first.IsOk, first.Error);
        Assert.Equal("captura.png", first.Value!.Name);
        Assert.Equal("Captura (2).PNG", (await brain.UploadAssetAsync(slug, "Captura.PNG", png, ana)).Value!.Name);
        Assert.Equal(TaskCoreStatus.Invalid, (await brain.UploadAssetAsync(slug, "xss.png", Encoding.ASCII.GetBytes("<script>"), ana)).Status);
        Assert.Equal(TaskCoreStatus.Invalid, (await brain.UploadAssetAsync(slug, "logo.svg", Encoding.ASCII.GetBytes("<svg/>"), ana)).Status);
        Assert.Equal(TaskCoreStatus.NotFound, (await brain.UploadAssetAsync("no-existe", "a.png", png, ana)).Status);
        Assert.Equal(2, (await brain.ListAssetsAsync(slug)).Count);

        // Uso por notas y borrado (definitivo): solo Owner/Admin, solo adjuntos de ESTE proyecto.
        Assert.True((await brain.SaveNoteAsync(slug, null, "Boceto", "![[captura.png|200]] y ![x](img/captura.png)", null, ana)).IsOk);
        var listed = await brain.ListAssetsAsync(slug, withUsage: true);
        Assert.Equal(new[] { "Boceto" }, listed.Single(a => a.Name == "captura.png").UsedIn);
        Assert.Empty(listed.Single(a => a.Name == "Captura (2).PNG").UsedIn!);
        Assert.Null((await brain.ListAssetsAsync(slug)).First().UsedIn);

        var owner = Actor(tenantId, "Owner", admin: true);
        Assert.Equal(TaskCoreStatus.Forbidden, (await brain.DeleteAssetAsync(slug, first.Value.Id, ana)).Status);
        Assert.Equal(TaskCoreStatus.NotFound, (await brain.DeleteAssetAsync(slug, Guid.NewGuid(), owner)).Status);
        Assert.True((await brain.DeleteAssetAsync(slug, first.Value.Id, owner)).IsOk);
        Assert.Equal(new[] { "Captura (2).PNG" }, (await brain.ListAssetsAsync(slug)).Select(a => a.Name));
        Assert.Equal(TaskCoreStatus.NotFound, (await brain.DeleteAssetAsync(slug, first.Value.Id, owner)).Status);
    }

    [Fact]
    public async Task OtroTenant_NoVeNiEscribeEnElCerebro()
    {
        var a = await SeedTenantAsync("Cerebro TA");
        var b = await SeedTenantAsync("Cerebro TB");
        string slug;
        Guid noteId;
        await using (var ctxA = _fixture.CreateContext(a))
        {
            slug = await CreateProjectAsync(ctxA, a);
            var brainA = new MockupBrainService(ctxA, new AuditWriter(ctxA), new BrainFiles());
            noteId = (await brainA.SaveNoteAsync(slug, null, "Secreto", "solo A", null, Actor(a, "Ana", false))).Value!.Id;
        }

        await using (var ctxB = _fixture.CreateContext(b))
        {
            var brainB = new MockupBrainService(ctxB, new AuditWriter(ctxB), new BrainFiles());
            var intruso = Actor(b, "Intruso", admin: true);
            Assert.Empty(await brainB.ListNotesAsync(slug));
            Assert.Null(await brainB.GetNoteAsync(slug, noteId));
            Assert.Null(await brainB.GetGraphAsync(slug));
            Assert.Equal(TaskCoreStatus.NotFound, (await brainB.SaveNoteAsync(slug, null, "X", "y", null, intruso)).Status);
            Assert.Equal(TaskCoreStatus.NotFound, (await brainB.ArchiveNoteAsync(slug, noteId, intruso)).Status);
            Assert.Empty(await ctxB.MockupNotes.ToListAsync());
            Assert.Empty(await ctxB.MockupNoteLinks.ToListAsync());
        }
    }

    // ---- Helpers ----

    /// <summary>Gestor de archivos en memoria que SI filtra por modulo y Ref1 (los adjuntos del cerebro se listan asi).</summary>
    private sealed class BrainFiles : IFileStorageService
    {
        private readonly Dictionary<Guid, (FileStorageSaveRequest Req, DateTimeOffset At)> _files = new();

        public Task<StoredFileDto> SaveAsync(FileStorageSaveRequest request, CancellationToken ct = default)
        {
            var id = Guid.NewGuid();
            _files[id] = (request, DateTimeOffset.UtcNow);
            return Task.FromResult(Dto(id, request, _files[id].At));
        }

        public Task<IReadOnlyList<StoredFileDto>> ListAsync(string module, string? ref1 = null, string? ref2 = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StoredFileDto>>(_files
                .Where(f => f.Value.Req.Module == module && (ref1 is null || f.Value.Req.Ref1 == ref1) && (ref2 is null || f.Value.Req.Ref2 == ref2))
                .Select(f => Dto(f.Key, f.Value.Req, f.Value.At)).ToList());

        public Task<bool> DeleteAsync(Guid fileId, CancellationToken ct = default) => Task.FromResult(_files.Remove(fileId));
        public Task<StoredFileDto?> GetAsync(Guid fileId, CancellationToken ct = default) => Task.FromResult<StoredFileDto?>(null);
        public Task<FileStorageContent?> ReadAsync(Guid fileId, CancellationToken ct = default) => Task.FromResult<FileStorageContent?>(null);
        public Task<FileStorageContent?> ReadForTenantAsync(Guid fileId, Guid tenantId, CancellationToken ct = default) => Task.FromResult<FileStorageContent?>(null);
        public Task<StoredFileDto?> FindAsync(string module, string? ref1, string? ref2, string fileName, CancellationToken ct = default) => Task.FromResult<StoredFileDto?>(null);
        public Task<IReadOnlyCollection<string>> ListRefsWithFilesAsync(string module, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>());

        private static StoredFileDto Dto(Guid id, FileStorageSaveRequest r, DateTimeOffset at)
            => new(id, r.Module, r.Ref1, r.Ref2, r.FileName, r.ContentType, r.Content.Length, at, "memory");
    }

    private static McpActor Actor(Guid tenantId, string name, bool admin)
        => new(tenantId, Guid.CreateVersion7(), Guid.CreateVersion7(), name, admin, Guid.NewGuid());

    private static async Task<string> CreateProjectAsync(EcorexDbContext ctx, Guid tenantId)
    {
        var mockups = new MockupService(ctx, new TestTenantContext(tenantId, Guid.NewGuid()), new MockupProjectsTestsBase.MemoryFileStorage(), new AuditWriter(ctx));
        var created = await mockups.CreateProjectAsync("TRONOX SGDEA", "tronox-sgdea", null, isOwnerOrAdmin: true);
        Assert.True(created.IsOk, created.Error);
        return created.Value!.Slug;
    }

    private async Task<Guid> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId: null);
        ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
        await ctx.SaveChangesAsync();
        return tenantId;
    }

    private static byte[] ZipEntries(params (string Path, byte[] Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                using var stream = archive.CreateEntry(path).Open();
                stream.Write(content);
            }
        }
        return ms.ToArray();
    }

    private static byte[] Zip(params (string Path, string Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                using var w = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
                w.Write(content);
            }
        }
        return ms.ToArray();
    }

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class MockupBrainTests_Postgres : MockupBrainTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public MockupBrainTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture) { }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class MockupBrainTests_SqlServer : MockupBrainTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public MockupBrainTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture) { }
}
