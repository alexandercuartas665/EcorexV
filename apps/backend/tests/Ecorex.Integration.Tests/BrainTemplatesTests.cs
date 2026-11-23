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

    /// <summary>ADR-0204: guia de los MCP. Una sola marcada; sin publicar = error; la empresa sembrada lee SU cerebro;
    /// la no sembrada lee el respaldo de la plantilla; ninguna lee el cerebro de otra.</summary>
    [Fact]
    public async Task GuiaMcp_UnaSola_SembradoYRespaldo_Aislado()
    {
        var sembrada = await SeedTenantAsync("Guia MCP sembrada");
        var nueva = await SeedTenantAsync("Guia MCP nueva");
        var platformUser = Guid.CreateVersion7();
        Guid guiaId, otraId;
        BrainTemplateSnapshot snapshot;
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            var svc = new BrainTemplateService(ctx, new AuditWriter(ctx));
            guiaId = (await svc.SaveAsync(null, Input("Guia de uso de ECOREX por IA", BrainTemplateTargets.All, Array.Empty<Guid>()), platformUser)).Value;
            Assert.True((await svc.SaveNoteAsync(guiaId, null, "00 - LEEME PRIMERO", "Empieza por [[R05 - Crear un contenedor de datos]].", platformUser)).IsOk);
            Assert.True((await svc.SaveNoteAsync(guiaId, null, "R05 - Crear un contenedor de datos", "Paso 1: crear_contenedor_datos. Si ves Tiempo de espera agotado, revisa el endpoint.", platformUser)).IsOk);
            Assert.True((await svc.SaveNoteAsync(guiaId, null, "T01 - Trampas", "No reenvies endpoints con ***.", platformUser)).IsOk);

            // Una sola guia: marcar otra desmarca la anterior (misma transaccion).
            otraId = (await svc.SaveAsync(null, Input("Otra plantilla", BrainTemplateTargets.All, Array.Empty<Guid>()), platformUser)).Value;
            Assert.True((await svc.SetMcpGuideAsync(guiaId, true, platformUser)).IsOk);
            Assert.True((await svc.SetMcpGuideAsync(otraId, true, platformUser)).IsOk);
            Assert.Equal(new[] { otraId }, (await svc.ListAsync()).Where(t => t.IsMcpGuide).Select(t => t.Id).ToArray());
            Assert.True((await svc.SetMcpGuideAsync(guiaId, true, platformUser)).IsOk);
            Assert.Equal(new[] { guiaId }, (await svc.ListAsync()).Where(t => t.IsMcpGuide).Select(t => t.Id).ToArray());
            Assert.Equal(TaskCoreStatus.NotFound, (await svc.SetMcpGuideAsync(Guid.CreateVersion7(), true, platformUser)).Status);
        }

        // Marcada pero SIN publicar: error claro.
        await using (var ctx = _fixture.CreateContext(sembrada))
        {
            var (index, error) = await new McpGuideService(ctx).IndexAsync();
            Assert.Null(index);
            Assert.Equal(McpGuideService.NotPublishedMessage, error);
        }

        // Publicar y sembrar SOLO en "sembrada"; despues la plantilla cambia sin re-publicar.
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            var svc = new BrainTemplateService(ctx, new AuditWriter(ctx));
            snapshot = (await svc.PreparePublishAsync(guiaId, platformUser)).Value!.Snapshot;
        }
        await using (var ctx = _fixture.CreateContext(sembrada))
        {
            Assert.Equal("created", (await new BrainService(ctx, new AuditWriter(ctx)).ApplyTemplateAsync(snapshot, sembrada, platformUser)).Outcome);
        }
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            var svc = new BrainTemplateService(ctx, new AuditWriter(ctx));
            var r05 = (await svc.GetAsync(guiaId))!.Notes.Single(n => n.Title.StartsWith("R05", StringComparison.Ordinal));
            Assert.True((await svc.SaveNoteAsync(guiaId, r05.Id, r05.Title, "BORRADOR sin publicar", platformUser)).IsOk);
        }

        // Empresa sembrada: lee SU cerebro (lo publicado), no el borrador de la plantilla.
        await using (var ctx = _fixture.CreateContext(sembrada))
        {
            var guide = new McpGuideService(ctx);
            var (index, error) = await guide.IndexAsync();
            Assert.Null(error);
            Assert.Equal("cerebro", index!.Source);
            Assert.Equal("00 - LEEME PRIMERO", index.Entry!.Title);
            Assert.Contains("R05 - Crear un contenedor de datos", index.Entry.Links);
            Assert.Equal(3, index.Titles.Count);

            var (note, noteErr) = await guide.NoteAsync("r05 - crear un contenedor de datos");
            Assert.Null(noteErr);
            Assert.Contains("crear_contenedor_datos", note!.Body);
            Assert.DoesNotContain("BORRADOR", note.Body);
            Assert.NotNull((await guide.NoteAsync("T01")).Note); // titulo parcial unico
            Assert.Null((await guide.NoteAsync("No existe")).Note);

            var (hits, searchErr) = await guide.SearchAsync("tiempo de espera agotado");
            Assert.Null(searchErr);
            Assert.Equal("R05 - Crear un contenedor de datos", Assert.Single(hits).Title);
            Assert.Contains("Tiempo de espera agotado", hits[0].Snippet);
        }

        // Empresa SIN siembra (creada despues de publicar): respaldo con la plantilla; no ve el cerebro de "sembrada".
        await using (var ctx = _fixture.CreateContext(nueva))
        {
            Assert.Empty(await ctx.Brains.ToListAsync());
            var (index, error) = await new McpGuideService(ctx).IndexAsync();
            Assert.Null(error);
            Assert.Equal("plantilla", index!.Source);
            Assert.Contains("BORRADOR", (await new McpGuideService(ctx).NoteAsync("R05 - Crear un contenedor de datos")).Note!.Body);
        }

        // Archivar la guia la apaga.
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            Assert.True((await new BrainTemplateService(ctx, new AuditWriter(ctx)).ArchiveAsync(guiaId, platformUser)).IsOk);
        }
        await using (var ctx = _fixture.CreateContext(nueva))
        {
            Assert.Equal(McpGuideService.NotPublishedMessage, (await new McpGuideService(ctx).IndexAsync()).Error);
        }
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
