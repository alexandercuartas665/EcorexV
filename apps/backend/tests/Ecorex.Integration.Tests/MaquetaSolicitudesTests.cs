using Ecorex.Application.Common;
using Ecorex.Application.Integraciones.Maqueta;
using Ecorex.Application.Mcp;
using Ecorex.Application.Organization;
using Ecorex.Application.Storage;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Integracion de la maqueta (ADR-0176) en matriz dual PostgreSQL / SQL Server:
/// - El mismo idLocal NO duplica: el reenvio actualiza la MISMA tarjeta (titulo, prioridad, etiquetas) y
///   una captura identica no se vuelve a adjuntar; una captura distinta si.
/// - Tenant nuevo (sin tablero ni tipos de actividad): se crea el tablero por nombre y un tipo de respaldo.
/// - El token de la maqueta solo sirve para la maqueta (no abre el MCP de tableros ni al reves).
/// </summary>
public abstract class MaquetaSolicitudesTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected MaquetaSolicitudesTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task MismoIdLocal_NoDuplica_ActualizaLaTarjetaYNoRepiteLaCaptura()
    {
        var seed = await SeedTenantAsync("Maqueta Idem", withActivityType: true);
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var files = new FakeFileStorage();
        var svc = Build(ctx, seed, files);
        var actor = Actor(seed);

        var png = Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 });
        var first = await svc.RecibirAsync(Request("an-test-1", "Boton deberia decir Nuevo registro", "Alta", png), actor);
        Assert.True(first.IsOk, first.Error);
        Assert.True(first.Value!.Created);
        Assert.True(first.Value.CaptureAttached);

        // Tablero creado por nombre, tarjeta en su PRIMERA columna, con etiquetas modulo+categoria y autor.
        var board = await ctx.TaskBoards.AsNoTracking().SingleAsync(b => b.Name == MaquetaSolicitudService.DefaultBoardName);
        var firstCol = await ctx.TaskBoardColumns.AsNoTracking().Where(c => c.BoardId == board.Id).OrderBy(c => c.SortOrder).FirstAsync();
        var task = await ctx.TaskItems.AsNoTracking().SingleAsync(t => t.Id == first.Value.TaskId);
        Assert.Equal(board.Id, task.BoardId);
        Assert.Equal(firstCol.Id, task.ColumnId);
        Assert.Equal(TaskPriority.High, task.Priority);
        Assert.Equal("Alex", task.RequesterName);
        var tags = await ctx.TaskItemTagAssignments.AsNoTracking().Where(a => a.TaskItemId == task.Id).Select(a => a.Tag!.Name).ToListAsync();
        Assert.Contains("RQ07", tags);
        Assert.Contains("Ajuste", tags);
        Assert.Single(files.Saved);

        // Reenvio del MISMO idLocal tras editar: misma tarjeta, sin duplicar, captura identica NO se re-adjunta.
        var second = await svc.RecibirAsync(Request("an-test-1", "Boton deberia decir Nuevo tercero", "Baja", png), actor);
        Assert.True(second.IsOk, second.Error);
        Assert.False(second.Value!.Created);
        Assert.False(second.Value.CaptureAttached);
        Assert.Equal(first.Value.TaskId, second.Value.TaskId);
        Assert.Equal(first.Value.Number, second.Value.Number);
        Assert.Equal(1, await ctx.TaskItems.AsNoTracking().CountAsync(t => t.BoardId == board.Id));
        var updated = await ctx.TaskItems.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.Equal("Boton deberia decir Nuevo tercero", updated.Title);
        Assert.Equal(TaskPriority.Low, updated.Priority);
        Assert.Equal(1, await ctx.TaskItemAttachments.AsNoTracking().CountAsync(a => a.TaskItemId == task.Id));
        var link = await ctx.ExternalTaskLinks.AsNoTracking().SingleAsync(l => l.ExternalKey == "an-test-1");
        Assert.Equal(2, link.ReceivedCount);
        Assert.Equal(task.Id, link.TaskItemId);
        Assert.Contains("Pages/Terceros.razor", link.MetadataJson);

        // Captura DISTINTA en el reenvio: si se adjunta (historial de capturas).
        var png2 = Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47, 9, 9, 9 });
        var third = await svc.RecibirAsync(Request("an-test-1", "Boton deberia decir Nuevo tercero", "Baja", png2), actor);
        Assert.True(third.Value!.CaptureAttached);
        Assert.Equal(2, await ctx.TaskItemAttachments.AsNoTracking().CountAsync(a => a.TaskItemId == task.Id));

        // Otro idLocal: otra tarjeta en el MISMO tablero (no crea un segundo tablero).
        var other = await svc.RecibirAsync(Request("an-test-2", "Otro ajuste", "Media", null), actor);
        Assert.True(other.Value!.Created);
        Assert.NotEqual(first.Value.TaskId, other.Value.TaskId);
        Assert.Equal(1, await ctx.TaskBoards.AsNoTracking().CountAsync(b => b.Name == MaquetaSolicitudService.DefaultBoardName));
        Assert.Equal(2, await ctx.TaskItems.AsNoTracking().CountAsync(t => t.BoardId == board.Id));
    }

    [Fact]
    public async Task TenantSinTiposDeActividad_CreaTipoDeRespaldo_YValidaEntrada()
    {
        var seed = await SeedTenantAsync("Maqueta Nuevo", withActivityType: false);
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var svc = Build(ctx, seed, new FakeFileStorage());
        var actor = Actor(seed);

        var bad = await svc.RecibirAsync(Request("an-x", "Titulo", "Media", "@@no-es-base64@@"), actor);
        Assert.Equal(TaskCoreStatus.Invalid, bad.Status);
        Assert.Equal(0, await ctx.TaskItems.AsNoTracking().CountAsync());

        var noKey = await svc.RecibirAsync(Request(" ", "Titulo", "Media", null), actor);
        Assert.Equal(TaskCoreStatus.Invalid, noKey.Status);

        var req = Request("an-y", "Ajuste en tenant nuevo", "Media", null);
        req.Tablero = "Tablero Maqueta Cliente";
        var ok = await svc.RecibirAsync(req, actor);
        Assert.True(ok.IsOk, ok.Error);
        var type = await ctx.ActivityTypes.AsNoTracking().SingleAsync();
        Assert.Equal("Solicitud de ajuste", type.Name);
        var task = await ctx.TaskItems.AsNoTracking().SingleAsync();
        Assert.Equal(type.Id, task.ActivityTypeId);
        Assert.Equal("Tablero Maqueta Cliente", (await ctx.TaskBoards.AsNoTracking().SingleAsync(b => b.Id == task.BoardId)).Name);
    }

    [Fact]
    public async Task TokenMaqueta_SoloSirveParaLaMaqueta()
    {
        var seed = await SeedTenantAsync("Maqueta Token", withActivityType: true);
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tokens = new PersonalAccessTokenService(ctx, new AuditWriter(ctx));

        var maqueta = await tokens.IssueAsync(seed.OwnerUserId, seed.OwnerPlatformId, "Maqueta TRONOX", TenantApiToken.ScopeMaqueta);
        Assert.Equal(TenantApiToken.ScopeMaqueta, maqueta.Token.Scope);
        var actor = await tokens.ResolveAsync(maqueta.PlainToken, TenantApiToken.ScopeMaqueta);
        Assert.NotNull(actor);
        Assert.Equal(seed.TenantId, actor!.TenantId);
        Assert.Null(await tokens.ResolveAsync(maqueta.PlainToken, TenantApiToken.ScopeMcpTableros));

        var mcp = await tokens.IssueAsync(seed.OwnerUserId, seed.OwnerPlatformId, "Claude");
        Assert.Null(await tokens.ResolveAsync(mcp.PlainToken, TenantApiToken.ScopeMaqueta));
    }

    [Fact]
    public async Task EditorDelProyecto_TableroPropio_ListaSusSolicitudes_YCuentaAbiertas()
    {
        var seed = await SeedTenantAsync("Maqueta Editor", withActivityType: true);
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var files = new FakeFileStorage();
        var svc = Build(ctx, seed, files);
        var mockups = new MockupService(ctx, new TestTenantContext(seed.TenantId, seed.OwnerPlatformId), files, new AuditWriter(ctx));
        var actor = await svc.ResolveSessionActorAsync(seed.OwnerPlatformId);
        Assert.NotNull(actor);

        // El proyecto nace con su tablero por defecto; solo Owner/Admin lo cambia.
        var project = (await mockups.CreateProjectAsync("TRONOX SGDEA", null, null, isOwnerOrAdmin: true)).Value!;
        Assert.Equal("Maqueta TRONOX SGDEA - Ajustes", project.BoardName);
        Assert.Equal(TaskCoreStatus.Forbidden, (await mockups.UpdateProjectAsync(project.Id, "X", null, "Otro", isOwnerOrAdmin: false)).Status);
        var updated = await mockups.UpdateProjectAsync(project.Id, "TRONOX SGDEA", "Maqueta", "Ajustes SGDEA", isOwnerOrAdmin: true);
        Assert.True(updated.IsOk, updated.Error);
        Assert.Equal("Ajustes SGDEA", updated.Value!.BoardName);
        var reset = await mockups.UpdateProjectAsync(project.Id, "TRONOX SGDEA", null, "  ", isOwnerOrAdmin: true);
        Assert.Equal("Maqueta TRONOX SGDEA - Ajustes", reset.Value!.BoardName);

        // Dos solicitudes desde el editor (origen maqueta-{slug}) y una de otra maqueta.
        var mine1 = Request("ed-1", "Cambiar el boton", "Alta", null);
        mine1.Origen = MockupService.SourceFor(project.Slug);
        mine1.Tablero = reset.Value.BoardName;
        var mine2 = Request("ed-2", "Falta el filtro", "Baja", null);
        mine2.Origen = MockupService.SourceFor(project.Slug);
        mine2.Tablero = reset.Value.BoardName;
        Assert.True((await svc.RecibirAsync(mine1, actor!)).IsOk);
        var second = await svc.RecibirAsync(mine2, actor!);
        Assert.True(second.IsOk, second.Error);
        Assert.True((await svc.RecibirAsync(Request("otro-1", "De otra maqueta", "Media", null), actor!)).IsOk);

        var list = await mockups.ListSolicitudesAsync(project.Slug);
        Assert.Equal(2, list.Count);
        Assert.All(list, s => Assert.True(s.IsOpen));
        var s1 = list.Single(s => s.Title == "Cambiar el boton");
        Assert.Equal("rq07-terceros.html", s1.Archivo);
        Assert.Equal("button.btn", s1.Selector);
        Assert.Equal("Catalogo de Terceros", s1.Pantalla);
        Assert.Equal(TaskPriority.High, s1.Priority);
        var board = await ctx.TaskBoards.AsNoTracking().SingleAsync(b => b.Id == second.Value!.BoardId);
        Assert.Equal("Maqueta TRONOX SGDEA - Ajustes", board.Name);

        // Al cerrar una, el conteo de abiertas de la galeria baja.
        var task = await ctx.TaskItems.SingleAsync(t => t.Id == second.Value!.TaskId);
        task.Status = TaskItemStatus.Closed;
        await ctx.SaveChangesAsync();
        var card = Assert.Single(await mockups.ListProjectsAsync());
        Assert.Equal(2, card.TotalRequests);
        Assert.Equal(1, card.OpenRequests);
        Assert.Single(await mockups.ListSolicitudesAsync(project.Slug), s => s.IsOpen);
        Assert.Empty(await mockups.ListSolicitudesAsync("no-existe"));
    }

    [Fact]
    public async Task SesionDelHub_ResuelveActorDelTenant_YAuditaCanalHub()
    {
        var seed = await SeedTenantAsync("Maqueta Sesion", withActivityType: true);
        var otro = await SeedTenantAsync("Maqueta Otro", withActivityType: true);
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var svc = Build(ctx, seed, new FakeFileStorage());

        // Usuario de la sesion del tenant: actor sin token (canal hub).
        var actor = await svc.ResolveSessionActorAsync(seed.OwnerPlatformId);
        Assert.NotNull(actor);
        Assert.Equal(seed.TenantId, actor!.TenantId);
        Assert.Equal(seed.OwnerUserId, actor.TenantUserId);
        Assert.Equal(Guid.Empty, actor.TokenId);
        Assert.True(actor.IsOwnerOrAdmin);

        // Un usuario de OTRA empresa no se resuelve dentro de este tenant (filtro global); tampoco el vacio.
        Assert.Null(await svc.ResolveSessionActorAsync(otro.OwnerPlatformId));
        Assert.Null(await svc.ResolveSessionActorAsync(Guid.Empty));

        // La tarjeta que llega por la sesion queda auditada con el motivo del hub.
        var res = await svc.RecibirAsync(Request("an-hub-1", "Desde el hub", "Media", null), actor);
        Assert.True(res.IsOk, res.Error);
        var audit = await ctx.SuperAdminAuditLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.EntityId == res.Value!.TaskId && a.ActionName == "maqueta.solicitud.create")
            .SingleAsync();
        Assert.Equal("integ-maqueta-hub", audit.Reason);

        // Usuario inactivo: sin actor.
        var member = await ctx.TenantUsers.SingleAsync(u => u.Id == seed.OwnerUserId);
        member.Status = PlatformUserStatus.Blocked;
        await ctx.SaveChangesAsync();
        Assert.Null(await svc.ResolveSessionActorAsync(seed.OwnerPlatformId));
    }

    // ---- Helpers ----

    private MaquetaSolicitudService Build(EcorexDbContext ctx, SeedData seed, IFileStorageService files)
    {
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var engine = new WorkflowEngine(ctx, tenantCtx, new NoOpWorkflowRuleHook(), new NoOpTaskBroadcaster());
        var tasks = new TaskItemService(ctx, tenantCtx, new SequenceService(ctx, tenantCtx), engine, new NoOpEmailSender(), new NodeAssigneeResolver(ctx));
        var boards = new ActivityBoardService(ctx, tenantCtx, new SequenceService(ctx, tenantCtx), tasks, new AuditWriter(ctx), new NodeAssigneeResolver(ctx),
            _fixture.CreateFactory(tenantCtx.TenantId));
        return new MaquetaSolicitudService(ctx, tenantCtx, boards, tasks, files, new AuditWriter(ctx));
    }

    private static McpActor Actor(SeedData s) => new(s.TenantId, s.OwnerUserId, s.OwnerPlatformId, "Owner Maqueta", true, Guid.NewGuid());

    private static MaquetaSolicitudRequest Request(string idLocal, string titulo, string prioridad, string? pngBase64) => new()
    {
        Origen = "maqueta-tronox",
        IdLocal = idLocal,
        Titulo = titulo,
        Descripcion = "Texto libre\n\n---\n**Origen en el app real:** `Pages/Terceros.razor`",
        Categoria = "Ajuste",
        Prioridad = prioridad,
        Autor = "Alex",
        Modulo = "RQ07",
        Pantalla = "Catalogo de Terceros",
        Ruta = "modulo/terceros",
        ArchivoMaqueta = "rq07-terceros.html",
        OrigenApp = "Pages/Terceros.razor",
        Handoff = "HANDOFF-rq07.md",
        Elemento = new MaquetaElemento { Tag = "button", Texto = "+ Nuevo Tercero", Clases = "btn", Selector = "button.btn", Rect = new MaquetaRect { X = 1, Y = 2, W = 10, H = 10 } },
        Tema = "classic-light",
        Viewport = "desktop",
        CreadoEn = "2026-11-17T00:00:00Z",
        Captura = pngBase64 is null ? null : new MaquetaCaptura { Nombre = $"rq07-terceros-{idLocal}.png", Mime = "image/png", Base64 = pngBase64 }
    };

    private async Task<SeedData> SeedTenantAsync(string name, bool withActivityType)
    {
        var tenantId = Guid.CreateVersion7();
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var ownerP = new PlatformUser { Email = $"owner-{tenantId:N}@maqueta.test", DisplayName = "Owner Maqueta", EmailVerified = true, Status = PlatformUserStatus.Active };
            ctx.PlatformUsers.Add(ownerP);
            var owner = new TenantUser { TenantId = tenantId, PlatformUserId = ownerP.Id, Email = ownerP.Email, TenantRole = TenantRole.Owner, Status = PlatformUserStatus.Active };
            ctx.TenantUsers.Add(owner);
            if (withActivityType)
            {
                ctx.ActivityTypes.Add(new ActivityType { TenantId = tenantId, Category = "General", Name = "Maqueta" });
            }
            await ctx.SaveChangesAsync();
            return new SeedData(tenantId, owner.Id, ownerP.Id);
        }
    }

    private sealed record SeedData(Guid TenantId, Guid OwnerUserId, Guid OwnerPlatformId);

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }

    /// <summary>Doble del gestor de archivos: guarda en memoria (el real usa Azure Blob/disco y vive en SuperAdmin).</summary>
    private sealed class FakeFileStorage : IFileStorageService
    {
        public List<FileStorageSaveRequest> Saved { get; } = new();

        public Task<StoredFileDto> SaveAsync(FileStorageSaveRequest request, CancellationToken ct = default)
        {
            Saved.Add(request);
            return Task.FromResult(new StoredFileDto(Guid.NewGuid(), request.Module, request.Ref1, request.Ref2, request.FileName,
                request.ContentType, request.Content.Length, DateTimeOffset.UtcNow, "memory"));
        }

        public Task<IReadOnlyList<StoredFileDto>> ListAsync(string module, string? ref1 = null, string? ref2 = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StoredFileDto>>(Array.Empty<StoredFileDto>());
        public Task<StoredFileDto?> GetAsync(Guid fileId, CancellationToken ct = default) => Task.FromResult<StoredFileDto?>(null);
        public Task<FileStorageContent?> ReadAsync(Guid fileId, CancellationToken ct = default) => Task.FromResult<FileStorageContent?>(null);
        public Task<FileStorageContent?> ReadForTenantAsync(Guid fileId, Guid tenantId, CancellationToken ct = default) => Task.FromResult<FileStorageContent?>(null);
        public Task<bool> DeleteAsync(Guid fileId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<StoredFileDto?> FindAsync(string module, string? ref1, string? ref2, string fileName, CancellationToken ct = default) => Task.FromResult<StoredFileDto?>(null);
        public Task<IReadOnlyCollection<string>> ListRefsWithFilesAsync(string module, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>());
    }
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class MaquetaSolicitudesTests_Postgres : MaquetaSolicitudesTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public MaquetaSolicitudesTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture) { }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class MaquetaSolicitudesTests_SqlServer : MaquetaSolicitudesTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public MaquetaSolicitudesTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture) { }
}
