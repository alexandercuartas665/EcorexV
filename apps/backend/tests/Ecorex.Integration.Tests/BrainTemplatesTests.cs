using Ecorex.Application.Common;
using Ecorex.Application.Integraciones.Cerebro;
using Ecorex.Application.Integraciones.Maqueta;
using Ecorex.Application.Mcp;
using Ecorex.Application.Storage;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Cerebros plantilla de plataforma (ADR-0200, ola 3) en matriz dual PostgreSQL / SQL Server:
/// - Destinos de la siembra: empresas elegidas y por plan (con suscripcion vigente); la version sube en cada publicacion.
/// - Sembrado BLOQUEADO: solo lectura para el tenant (notas, adjuntos, editar, archivar); "Duplicar como propio".
/// - Re-publicar sincroniza el bloqueado (crea, actualiza y archiva notas, recalcula enlaces); la copia LIBRE no se toca.
/// - Aislamiento: aplicar en una empresa no deja nada en otra.
/// </summary>
public abstract class BrainTemplatesTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected BrainTemplatesTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Destinos_PorEmpresaYPorPlan_YVersion()
    {
        var a = await SeedTenantAsync("Plantilla destino A");
        var b = await SeedTenantAsync("Plantilla destino B");
        var platformUser = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId: null);
        var svc = new BrainTemplateService(ctx, new AuditWriter(ctx));

        Assert.Equal(TaskCoreStatus.Invalid, (await svc.SaveAsync(null, Input("X", BrainTemplateTargets.Tenants, Array.Empty<Guid>()), platformUser)).Status);
        var id = (await svc.SaveAsync(null, Input("Guia clinica", BrainTemplateTargets.Tenants, new[] { a }), platformUser)).Value;
        Assert.True((await svc.SaveNoteAsync(id, null, "Inicio", "Ver [[Triaje]] #guia", platformUser)).IsOk);
        Assert.Equal(TaskCoreStatus.Conflict, (await svc.SaveNoteAsync(id, null, "inicio", "x", platformUser)).Status);

        var plan = await svc.PreparePublishAsync(id, platformUser);
        Assert.True(plan.IsOk, plan.Error);
        Assert.Equal(1, plan.Value!.Snapshot.Version);
        Assert.Equal(a, Assert.Single(plan.Value.Tenants).Id);
        Assert.Equal(2, (await svc.PreparePublishAsync(id, platformUser)).Value!.Snapshot.Version);

        // Por plan: solo las empresas con suscripcion vigente a ese plan.
        var saas = new SaasPlan { Name = "Plan cerebros " + Guid.NewGuid().ToString("N")[..6] };
        ctx.SaasPlans.Add(saas);
        ctx.TenantSubscriptions.Add(new TenantSubscription
        {
            TenantId = b,
            PlanId = saas.Id,
            Status = SubscriptionStatus.Active,
            StartsAt = DateTimeOffset.UtcNow,
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1)
        });
        await ctx.SaveChangesAsync();
        Assert.True((await svc.SaveAsync(id, Input("Guia clinica", BrainTemplateTargets.Plans, new[] { saas.Id }), platformUser)).IsOk);
        Assert.Equal(b, Assert.Single((await svc.PreparePublishAsync(id, platformUser)).Value!.Tenants).Id);

        Assert.True((await svc.ArchiveAsync(id, platformUser)).IsOk);
        Assert.DoesNotContain(await svc.ListAsync(), t => t.Id == id);
        Assert.Equal(TaskCoreStatus.NotFound, (await svc.PreparePublishAsync(id, platformUser)).Status);
    }

    [Fact]
    public async Task Bloqueado_SoloLectura_Duplicar_YSincronizar()
    {
        var tenant = await SeedTenantAsync("Plantilla bloqueada");
        var otro = await SeedTenantAsync("Plantilla otra empresa");
        var platformUser = Guid.CreateVersion7();
        var v1 = Snapshot(Guid.CreateVersion7(), locked: true, version: 1,
            ("Inicio", "Ver [[Triaje]] y [[Escalas]] #guia"), ("Triaje", "Manchester"), ("Escalas", "Glasgow"));

        BrainTemplateApplyResult first;
        await using (var ctx = _fixture.CreateContext(tenant))
        {
            var brains = new BrainService(ctx, new AuditWriter(ctx));
            first = await brains.ApplyTemplateAsync(v1, tenant, platformUser);
            Assert.Equal(("created", 3), (first.Outcome, first.NotesCreated));
            var seeded = (await brains.GetAsync(first.BrainSlug!))!;
            Assert.True(seeded.IsLocked);
            Assert.Equal("Base clinica", seeded.CategoryName);
            Assert.True(Assert.Single(await brains.ListAsync()).IsLocked);

            // Solo lectura para el tenant, incluso para Owner/Admin.
            var owner = Actor(tenant, admin: true);
            var notes = new MockupBrainService(ctx, new AuditWriter(ctx), new NoFiles());
            Assert.Equal(TaskCoreStatus.Forbidden, (await notes.SaveNoteAsync(seeded.Slug, null, "Mia", "x", null, owner)).Status);
            var inicio = (await notes.GetNoteAsync(seeded.Slug, null, "Inicio"))!;
            Assert.All(inicio.Links.Where(l => l.Kind == "note"), l => Assert.True(l.Exists));
            Assert.Equal(TaskCoreStatus.Forbidden, (await notes.ArchiveNoteAsync(seeded.Slug, inicio.Id, owner)).Status);
            Assert.Equal(TaskCoreStatus.Forbidden, (await brains.UpdateAsync(seeded.Id, "Otro", null, null, seeded.Version, owner)).Status);
            Assert.Equal(TaskCoreStatus.Forbidden, (await brains.ArchiveAsync(seeded.Id, owner)).Status);

            // Duplicar como propio: copia editable con las mismas notas.
            Assert.Equal(TaskCoreStatus.Forbidden, (await brains.DuplicateAsync(seeded.Slug, null, Actor(tenant, admin: false))).Status);
            var copy = await brains.DuplicateAsync(seeded.Slug, null, owner);
            Assert.True(copy.IsOk, copy.Error);
            Assert.False(copy.Value!.IsLocked);
            Assert.Equal("Copia de Guia clinica", copy.Value.Name);
            Assert.Equal(3, (await notes.ListNotesAsync(copy.Value.Slug)).Count);
            Assert.True((await notes.SaveNoteAsync(copy.Value.Slug, null, "Mia", "editable", null, owner)).IsOk);
        }

        // v2: cambia Triaje, quita Escalas, agrega Codigo azul. El bloqueado se sincroniza.
        var v2 = v1 with
        {
            Version = 2,
            Name = "Guia clinica 2026",
            Notes = new[] { new BrainTemplateSnapshotNote("Inicio", "Ver [[Triaje]] #guia"), new BrainTemplateSnapshotNote("Triaje", "Manchester v2"),
                new BrainTemplateSnapshotNote("Codigo azul", "Paso a paso") }
        };
        await using (var ctx = _fixture.CreateContext(tenant))
        {
            var brains = new BrainService(ctx, new AuditWriter(ctx));
            var synced = await brains.ApplyTemplateAsync(v2, tenant, platformUser);
            Assert.Equal(("synced", 1, 2, 1), (synced.Outcome, synced.NotesCreated, synced.NotesUpdated, synced.NotesArchived));
            Assert.Equal(first.BrainSlug, synced.BrainSlug);
            var notes = new MockupBrainService(ctx, new AuditWriter(ctx), new NoFiles());
            var titles = (await notes.ListNotesAsync(first.BrainSlug!)).Select(n => n.Title).OrderBy(x => x).ToList();
            Assert.Equal(new[] { "Codigo azul", "Inicio", "Triaje" }, titles);
            Assert.Equal("Manchester v2", (await notes.GetNoteAsync(first.BrainSlug!, null, "Triaje"))!.Body);
            Assert.Equal("Guia clinica 2026", (await brains.GetAsync(first.BrainSlug!))!.Name);
            // La copia propia no cambio.
            Assert.Contains(await brains.ListAsync(), x => x.Name == "Copia de Guia clinica" && !x.IsLocked);
        }

        // Otra empresa no ve nada.
        await using (var ctxOtro = _fixture.CreateContext(otro))
        {
            Assert.Empty(await new BrainService(ctxOtro, new AuditWriter(ctxOtro)).ListAsync());
            Assert.Empty(await ctxOtro.MockupNotes.ToListAsync());
        }
    }

    [Fact]
    public async Task Libre_SeSiembraEditable_YNoSeVuelveATocar()
    {
        var tenant = await SeedTenantAsync("Plantilla libre");
        var platformUser = Guid.CreateVersion7();
        var v1 = Snapshot(Guid.CreateVersion7(), locked: false, version: 1, ("Bienvenida", "Hola"));
        await using var ctx = _fixture.CreateContext(tenant);
        var brains = new BrainService(ctx, new AuditWriter(ctx));
        var created = await brains.ApplyTemplateAsync(v1, tenant, platformUser);
        Assert.Equal("created", created.Outcome);
        var owner = Actor(tenant, admin: true);
        var notes = new MockupBrainService(ctx, new AuditWriter(ctx), new NoFiles());
        var hola = (await notes.GetNoteAsync(created.BrainSlug!, null, "Bienvenida"))!;
        Assert.True((await notes.SaveNoteAsync(created.BrainSlug!, hola.Id, "Bienvenida", "Hola, editado", hola.Version, owner)).IsOk);

        var skipped = await brains.ApplyTemplateAsync(v1 with { Version = 2, Notes = new[] { new BrainTemplateSnapshotNote("Bienvenida", "Pisado") } }, tenant, platformUser);
        Assert.Equal("skipped", skipped.Outcome);
        Assert.Equal("Hola, editado", (await notes.GetNoteAsync(created.BrainSlug!, null, "Bienvenida"))!.Body);
        Assert.Single(await brains.ListAsync());
    }

    // ---- Helpers ----

    private static BrainTemplateInput Input(string name, string mode, IReadOnlyList<Guid> ids)
        => new(name, "Guia", "Base clinica", "#0ea5e9", IsLocked: true, mode, ids);

    private static BrainTemplateSnapshot Snapshot(Guid id, bool locked, int version, params (string Title, string Body)[] notes)
        => new(id, "Guia clinica", "Protocolos", "Base clinica", "#0ea5e9", locked, version,
            notes.Select(n => new BrainTemplateSnapshotNote(n.Title, n.Body)).ToList());

    private static McpActor Actor(Guid tenantId, bool admin)
        => new(tenantId, Guid.CreateVersion7(), Guid.CreateVersion7(), admin ? "Owner" : "Usuario", admin, Guid.NewGuid());

    private async Task<Guid> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId: null);
        ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name, Status = TenantStatus.Active });
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
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class BrainTemplatesTests_Postgres : BrainTemplatesTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public BrainTemplatesTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture) { }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class BrainTemplatesTests_SqlServer : BrainTemplatesTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public BrainTemplatesTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture) { }
}
