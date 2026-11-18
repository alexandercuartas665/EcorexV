using System.IO.Compression;
using Ecorex.Application.Common;
using Ecorex.Application.Integraciones.Maqueta;
using Ecorex.Application.Storage;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Maquetas en la nube (ADR-0179) en matriz dual PostgreSQL / SQL Server:
/// - Solo Owner/Admin crea proyectos y sube versiones; el slug es unico por tenant.
/// - Cada subida es una version nueva correlativa y queda como actual; se puede volver a una anterior.
/// - La resolucion para servir respeta el tenant (otro tenant con el mismo slug no la ve) y el archivado.
/// </summary>
public abstract class MockupProjectsTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected MockupProjectsTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Proyecto_Versiones_VersionActual_YArchivado()
    {
        var tenantId = await SeedTenantAsync("Maquetas Nube");
        await using var ctx = _fixture.CreateContext(tenantId);
        var files = new MemoryFileStorage();
        var svc = Build(ctx, tenantId, files);

        // Permisos: un usuario normal no crea ni sube.
        Assert.False((await svc.CreateProjectAsync("TRONOX", null, null, isOwnerOrAdmin: false)).IsOk);

        var created = await svc.CreateProjectAsync("TRONOX SGDEA", null, "Maqueta del SGDEA", isOwnerOrAdmin: true);
        Assert.True(created.IsOk, created.Error);
        Assert.Equal("tronox-sgdea", created.Value!.Slug);
        Assert.Equal(TaskCoreStatus.Conflict, (await svc.CreateProjectAsync("Otro", "tronox-sgdea", null, true)).Status);
        Assert.False((await svc.UploadVersionAsync("tronox-sgdea", SiteZip("v1"), null, "Ana", isOwnerOrAdmin: false)).IsOk);

        // Version 1 y 2: correlativas, la ultima queda como actual.
        var v1 = await svc.UploadVersionAsync("tronox-sgdea", SiteZip("v1"), "primera", "Ana", true);
        Assert.True(v1.IsOk, v1.Error);
        Assert.Equal(1, v1.Value!.Number);
        Assert.True(v1.Value.HasAnotador);
        Assert.Equal(MockupService.AnotadorEntry, v1.Value.EntryPath);
        var v2 = await svc.UploadVersionAsync("tronox-sgdea", SiteZip("v2"), null, "Ana", true);
        Assert.Equal(2, v2.Value!.Number);
        Assert.Equal(2, files.Saved.Count);
        Assert.All(files.Saved.Values, f => Assert.Equal(MockupService.FileModule, f.Module));

        var current = await svc.ResolveForServingAsync("tronox-sgdea", null);
        Assert.Equal(v2.Value.Id, current!.VersionId);
        Assert.Equal("site/", current.RootPrefix);
        var old = await svc.ResolveForServingAsync("tronox-sgdea", 1);
        Assert.Equal(v1.Value.Id, old!.VersionId);
        Assert.Null(await svc.ResolveForServingAsync("tronox-sgdea", 9));

        // ZIP invalido: no crea version ni guarda archivo.
        var bad = await svc.UploadVersionAsync("tronox-sgdea", new byte[] { 1, 2, 3 }, null, "Ana", true);
        Assert.Equal(TaskCoreStatus.Invalid, bad.Status);
        Assert.Equal(2, files.Saved.Count);

        // Volver a la v1.
        Assert.True((await svc.SetCurrentVersionAsync(created.Value.Id, v1.Value.Id, true)).IsOk);
        Assert.Equal(v1.Value.Id, (await svc.ResolveForServingAsync("tronox-sgdea", null))!.VersionId);
        var listed = Assert.Single(await svc.ListProjectsAsync());
        Assert.Equal(1, listed.CurrentVersionNumber);
        Assert.Equal(2, listed.VersionCount);
        var versions = await svc.ListVersionsAsync(created.Value.Id);
        Assert.Equal(new[] { 2, 1 }, versions.Select(v => v.Number));
        Assert.True(versions.Single(v => v.Number == 1).IsCurrent);

        // Archivado: deja de listarse y de servirse.
        Assert.True((await svc.ArchiveProjectAsync(created.Value.Id, true)).IsOk);
        Assert.Empty(await svc.ListProjectsAsync());
        Assert.Null(await svc.ResolveForServingAsync("tronox-sgdea", null));
    }

    [Fact]
    public async Task OtroTenant_NoVeLaMaqueta_AunqueUseElMismoSlug()
    {
        var a = await SeedTenantAsync("Maquetas A");
        var b = await SeedTenantAsync("Maquetas B");

        await using (var ctxA = _fixture.CreateContext(a))
        {
            var svcA = Build(ctxA, a, new MemoryFileStorage());
            Assert.True((await svcA.CreateProjectAsync("Demo", "demo", null, true)).IsOk);
            Assert.True((await svcA.UploadVersionAsync("demo", SiteZip("A"), null, "A", true)).IsOk);
        }

        await using (var ctxB = _fixture.CreateContext(b))
        {
            var svcB = Build(ctxB, b, new MemoryFileStorage());
            Assert.Null(await svcB.ResolveForServingAsync("demo", null));
            Assert.Empty(await svcB.ListProjectsAsync());
            Assert.Equal(TaskCoreStatus.NotFound, (await svcB.UploadVersionAsync("demo", SiteZip("B"), null, "B", true)).Status);
            // El mismo slug es valido en otra empresa (unico POR tenant).
            Assert.True((await svcB.CreateProjectAsync("Demo B", "demo", null, true)).IsOk);
        }
    }

    // ---- Helpers ----

    private static MockupService Build(EcorexDbContext ctx, Guid tenantId, IFileStorageService files)
    {
        var tenantCtx = new TestTenantContext(tenantId, Guid.NewGuid());
        return new MockupService(ctx, tenantCtx, files, new AuditWriter(ctx));
    }

    private async Task<Guid> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId: null);
        ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
        await ctx.SaveChangesAsync();
        return tenantId;
    }

    /// <summary>ZIP "de la carpeta" (raiz site/) con index + anotador.</summary>
    internal static byte[] SiteZip(string marker)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in new[]
            {
                ("site/index.html", $"<html>{marker}</html>"),
                ("site/anotador/anotador.html", $"<html>anotador {marker}</html>"),
                ("site/.env", "SECRET=1")
            })
            {
                using var w = new StreamWriter(archive.CreateEntry(path).Open());
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

    /// <summary>Gestor de archivos en memoria (el real usa Azure Blob/disco y vive en SuperAdmin).</summary>
    internal sealed class MemoryFileStorage : IFileStorageService
    {
        public Dictionary<Guid, FileStorageSaveRequest> Saved { get; } = new();

        public Task<StoredFileDto> SaveAsync(FileStorageSaveRequest request, CancellationToken ct = default)
        {
            var id = Guid.NewGuid();
            Saved[id] = request;
            return Task.FromResult(new StoredFileDto(id, request.Module, request.Ref1, request.Ref2, request.FileName,
                request.ContentType, request.Content.Length, DateTimeOffset.UtcNow, "memory"));
        }

        public Task<FileStorageContent?> ReadForTenantAsync(Guid fileId, Guid tenantId, CancellationToken ct = default)
            => Task.FromResult(Saved.TryGetValue(fileId, out var f) ? new FileStorageContent(f.Content, f.ContentType ?? "", f.FileName) : null);

        public Task<FileStorageContent?> ReadAsync(Guid fileId, CancellationToken ct = default) => ReadForTenantAsync(fileId, Guid.Empty, ct);
        public Task<IReadOnlyList<StoredFileDto>> ListAsync(string module, string? ref1 = null, string? ref2 = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StoredFileDto>>(Array.Empty<StoredFileDto>());
        public Task<StoredFileDto?> GetAsync(Guid fileId, CancellationToken ct = default) => Task.FromResult<StoredFileDto?>(null);
        public Task<bool> DeleteAsync(Guid fileId, CancellationToken ct = default) => Task.FromResult(Saved.Remove(fileId));
        public Task<StoredFileDto?> FindAsync(string module, string? ref1, string? ref2, string fileName, CancellationToken ct = default) => Task.FromResult<StoredFileDto?>(null);
        public Task<IReadOnlyCollection<string>> ListRefsWithFilesAsync(string module, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>());
    }
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class MockupProjectsTests_Postgres : MockupProjectsTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public MockupProjectsTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture) { }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class MockupProjectsTests_SqlServer : MockupProjectsTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public MockupProjectsTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture) { }
}
