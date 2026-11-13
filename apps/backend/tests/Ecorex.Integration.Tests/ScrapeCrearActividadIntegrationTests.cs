extern alias superadmin;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Forms;
using Ecorex.Application.Notifications;
using Ecorex.Application.Organization;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using superadmin::Ecorex.SuperAdmin.Agents;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Paso "Crear actividad" del dron (2026-11-12) en matriz dual PostgreSQL / SQL Server: por el pedido que
/// el flujo tiene en el contexto crea UNA tarea del concepto, deja el formulario del concepto diligenciado
/// (cabecera + grilla de items) y anota el numero en la columna TAREA de la fila del Contenedor. Re-correrlo
/// NO duplica; la guarda "saltar si estado == Concluida" no crea nada (el historico solo llega al Contenedor).
/// </summary>
public abstract class ScrapeCrearActividadTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected ScrapeCrearActividadTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CreaUnaTareaConFormulario_YNoDuplica()
    {
        var seed = await SeedAsync("Dron Crear Actividad");
        var step = Step(seed);

        var first = await RunAsync(seed, step, Detalle("1501899999", "Pendiente"));
        Assert.True(first.Ok, first.Detail);
        Assert.True(first.Created, first.Detail);
        Assert.False(string.IsNullOrEmpty(first.TaskNumber));

        await using (var ctx = _fixture.CreateContext(seed.TenantId))
        {
            var task = await ctx.TaskItems.SingleAsync(t => t.SubcategoriaId == seed.SubcategoriaId);
            Assert.Equal(first.TaskNumber, task.Number);
            Assert.Equal("Pedido 1501899999", task.Title);

            var marca = await ctx.DataContainerCells.SingleAsync(c => c.RowId == seed.RowId && c.ColumnId == seed.TareaColId);
            Assert.Equal(task.Number, marca.Value);

            var response = await ctx.FormResponses.SingleAsync(r => r.DefinitionId == seed.FormId && r.Reference == task.Number);
            using var doc = JsonDocument.Parse(response.Data!);
            var root = doc.RootElement;
            Assert.Equal("1501899999", Field(root, "petof"));
            Assert.Equal("2026-10-13", Field(root, "fecha_cotizacion"));
            var items = JsonDocument.Parse(Field(root, "items")!).RootElement;
            Assert.Equal(2, items.GetArrayLength());
            Assert.Equal("Tornillo", items[0].GetProperty("material").GetString());
            Assert.Equal("12", items[1].GetProperty("cnt_sol").GetString());
        }

        // Segunda corrida del mismo pedido: no crea otra tarea.
        var second = await RunAsync(seed, step, Detalle("1501899999", "Pendiente"));
        Assert.True(second.Ok, second.Detail);
        Assert.False(second.Created);
        Assert.Equal(first.TaskNumber, second.TaskNumber);

        await using (var ctx = _fixture.CreateContext(seed.TenantId))
        {
            Assert.Equal(1, await ctx.TaskItems.CountAsync(t => t.SubcategoriaId == seed.SubcategoriaId));
        }
    }

    [Fact]
    public async Task Concluida_NoCreaTarea()
    {
        var seed = await SeedAsync("Dron Historico");

        var r = await RunAsync(seed, Step(seed), Detalle("1501899999", "Concluida"));

        Assert.True(r.Ok, r.Detail);
        Assert.False(r.Created);
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        Assert.Equal(0, await ctx.TaskItems.CountAsync(t => t.SubcategoriaId == seed.SubcategoriaId));
        Assert.False(await ctx.DataContainerCells.AnyAsync(c => c.RowId == seed.RowId && c.ColumnId == seed.TareaColId));
    }

    [Fact]
    public async Task SinFilaEnElContenedor_FallaSinCrear()
    {
        var seed = await SeedAsync("Dron Sin Fila");

        var r = await RunAsync(seed, Step(seed), Detalle("NO-EXISTE", "Pendiente"));

        Assert.False(r.Ok);
        Assert.Contains("No existe la fila", r.Detail);
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        Assert.Equal(0, await ctx.TaskItems.CountAsync(t => t.SubcategoriaId == seed.SubcategoriaId));
    }

    // ---- Helpers ----

    private static string? Field(JsonElement root, string code)
        => root.TryGetProperty(code, out var f) && f.TryGetProperty("value", out var v) ? v.GetString() : null;

    private static string Detalle(string petof, string estado) => JsonSerializer.Serialize(new
    {
        cabecera = new { petof, solicitud = "SOL-7", fechaCotizacion = "13/10/2026", estado },
        items = new[]
        {
            new { item = "10", codigo = "A1", material = "Tornillo", cntSol = "5" },
            new { item = "20", codigo = "B2", material = "Tuerca", cntSol = "12" },
        },
    });

    private static ScrapeStep Step(SeedData seed) => new()
    {
        Name = "Crear actividad",
        Kind = ScrapeStepKind.CrearActividad,
        MappingJson = ScrapeCrearActividad.Serialize(new ScrapeCrearActividad.Config
        {
            SubcategoriaId = seed.SubcategoriaId,
            Titulo = "Pedido {{DETALLE.cabecera.petof}}",
            Clave = "DETALLE.cabecera.petof",
            ContenedorId = seed.ContainerId,
            ColumnaClave = "PETOF",
            ColumnaTarea = "TAREA",
            Campos = new() { ["petof"] = "DETALLE.cabecera.petof", ["fecha_cotizacion"] = "DETALLE.cabecera.fechaCotizacion" },
            GrillaCampo = "items",
            ItemsRuta = "DETALLE.items",
            GrillaColumnas = new() { ["codigo"] = "codigo", ["material"] = "material", ["cnt_sol"] = "cntSol" },
            SaltarSiRuta = "DETALLE.cabecera.estado",
            SaltarSiValor = "Concluida",
        }),
    };

    private async Task<ScrapeCrearActividad.Result> RunAsync(SeedData seed, ScrapeStep step, string detalleJson)
    {
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenant = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        var engine = new WorkflowEngine(ctx, tenant, new NoOpWorkflowRuleHook(), new NoOpTaskBroadcaster());
        var tasks = new TaskItemService(ctx, tenant, new SequenceService(ctx, tenant), engine, new NoOpEmailSender(), new NodeAssigneeResolver(ctx));
        var forms = new FormResponseService(ctx, engine, new SequenceService(ctx, tenant), tenant,
            new NoOpFormRecordBroadcaster(),
            new Ecorex.Application.Forms.Lookups.FormLookupService(Array.Empty<Ecorex.Application.Forms.Lookups.IFormLookupSource>()),
            new Ecorex.Application.Rules.RulesEngine(ctx, tenant, new ServiceCollection().BuildServiceProvider()));
        var sp = new ServiceCollection()
            .AddSingleton<ITaskItemService>(tasks)
            .AddSingleton<IFormResponseService>(forms)
            .BuildServiceProvider();

        var run = new ScrapeRunContext();
        run.Set("DETALLE", detalleJson);
        return await ScrapeCrearActividad.RunAsync(sp, ctx, seed.TenantId, step, run, "Dron prueba", CancellationToken.None);
    }

    private async Task<SeedData> SeedAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var platformUser = new PlatformUser { Email = $"user-{tenantId:N}@dron.test", EmailVerified = true, Status = PlatformUserStatus.Active };
            ctx.PlatformUsers.Add(platformUser);
            var tenantUser = new TenantUser { TenantId = tenantId, PlatformUserId = platformUser.Id, Email = platformUser.Email };
            ctx.TenantUsers.Add(tenantUser);
            await ctx.SaveChangesAsync();

            // Formulario del concepto: cabecera + grilla de items, activo.
            var tenant = new TestTenantContext(tenantId, platformUser.Id);
            var definitions = new FormDefinitionService(ctx, tenant, new Ecorex.Application.MenuConfig.MenuConfigService(ctx, tenant), new SequenceService(ctx, tenant));
            var form = (await definitions.CreateAsync(new CreateFormDefinitionRequest("FRM-PED", "Pedido"))).Value!;
            Assert.True((await definitions.AddQuestionAsync(form.Id, new SaveFormQuestionRequest(null, "petof", "Peticion", FormControlType.Text))).IsOk);
            Assert.True((await definitions.AddQuestionAsync(form.Id, new SaveFormQuestionRequest(null, "fecha_cotizacion", "Fecha", FormControlType.Date))).IsOk);
            Assert.True((await definitions.AddQuestionAsync(form.Id, new SaveFormQuestionRequest(null, "items", "Items", FormControlType.GridDetail,
                OptionsJson: """[{"id":"codigo","label":"Codigo"},{"id":"material","label":"Material"},{"id":"cnt_sol","label":"Cant","type":"number"}]"""))).IsOk);
            Assert.True((await definitions.ActivateAsync(form.Id)).IsOk);

            var categoria = new ActividadCategoria { TenantId = tenantId, Codigo = "CAT-T", Nombre = "Comercial" };
            ctx.ActividadCategorias.Add(categoria);
            var concepto = new ActividadSubcategoria
            {
                TenantId = tenantId,
                CategoriaId = categoria.Id,
                Codigo = "CAT-T-01",
                Nombre = "Pedido dron",
                FormDefinitionId = form.Id,
            };
            ctx.ActividadSubcategorias.Add(concepto);

            // Contenedor con la fila del pedido (como lo deja la ingesta de la cabecera) y la columna TAREA.
            var container = new DataContainer { TenantId = tenantId, Name = "Cotizaciones" };
            ctx.DataContainers.Add(container);
            var petofCol = new DataContainerColumn { TenantId = tenantId, ContainerId = container.Id, Name = "PETOF", Type = DataContainerColumnType.Text, SortOrder = 0 };
            var tareaCol = new DataContainerColumn { TenantId = tenantId, ContainerId = container.Id, Name = "TAREA", Type = DataContainerColumnType.Text, SortOrder = 1 };
            ctx.DataContainerColumns.AddRange(petofCol, tareaCol);
            var row = new DataContainerRow { TenantId = tenantId, ContainerId = container.Id };
            ctx.DataContainerRows.Add(row);
            ctx.DataContainerCells.Add(new DataContainerCell { TenantId = tenantId, RowId = row.Id, ColumnId = petofCol.Id, Value = "1501899999" });
            await ctx.SaveChangesAsync();

            return new SeedData(tenantId, platformUser.Id, concepto.Id, form.Id, container.Id, row.Id, tareaCol.Id, tenantUser.Id, tenantUser.Email);
        }
    }

    private sealed record SeedData(Guid TenantId, Guid PlatformUserId, Guid SubcategoriaId, Guid FormId,
        Guid ContainerId, Guid RowId, Guid TareaColId, Guid TenantUserId, string TenantUserEmail);

    // ---- Paso Notificar (ADR-0167): corre "Crear actividad" y luego "Notificar" sobre el MISMO contexto ----

    [Fact]
    public async Task Notificar_Campana_AlResponsable_YNoRepiteAlRecorrer()
    {
        var seed = await SeedAsync("Dron Notificar Campana");
        var notificar = NotificarStep(new ScrapeNotificar.Config
        {
            Canal = ScrapeNotificar.Canal.Campana,
            Destino = ScrapeNotificar.Destino.Responsable,
            Asunto = "Pedido {form.petof}",
            Mensaje = "Tarea {tarea.numero} sol {{DETALLE.cabecera.solicitud}}",
        });

        var (crear1, aviso1, _) = await RunCrearYNotificarAsync(seed, notificar, responsable: true);
        Assert.True(crear1.Created, crear1.Detail);
        Assert.True(aviso1.Ok, aviso1.Detail);
        Assert.Equal(1, aviso1.Enviados);

        await using (var ctx = _fixture.CreateContext(seed.TenantId))
        {
            var n = await ctx.Notifications.SingleAsync(x => x.RecipientTenantUserId == seed.TenantUserId && x.Kind == NotificationKind.General);
            Assert.Equal("Pedido 1501899999", n.Title);
            Assert.Equal($"Tarea {crear1.TaskNumber} sol SOL-7", n.Body);
            var task = await ctx.TaskItems.SingleAsync(t => t.Number == crear1.TaskNumber);
            Assert.True(await ctx.TaskItemActivities.AnyAsync(a => a.TaskItemId == task.Id && a.Text.StartsWith("Aviso por campana")));
        }

        // Segunda corrida: la tarea ya existe -> TAREA_NUEVA=0 -> no se repite el aviso.
        var (crear2, aviso2, _) = await RunCrearYNotificarAsync(seed, notificar, responsable: true);
        Assert.False(crear2.Created);
        Assert.True(aviso2.Ok);
        Assert.Equal(0, aviso2.Enviados);
        await using (var ctx = _fixture.CreateContext(seed.TenantId))
        {
            Assert.Equal(1, await ctx.Notifications.CountAsync(x => x.RecipientTenantUserId == seed.TenantUserId && x.Kind == NotificationKind.General));
        }
    }

    [Fact]
    public async Task Notificar_Correo_AUsuariosElegidos()
    {
        var seed = await SeedAsync("Dron Notificar Correo");
        var notificar = NotificarStep(new ScrapeNotificar.Config
        {
            Canal = ScrapeNotificar.Canal.Correo,
            Destino = ScrapeNotificar.Destino.Usuarios,
            UsuarioIds = [seed.TenantUserId],
            Asunto = "Nueva tarea {tarea.numero}",
        });

        var (crear, aviso, sender) = await RunCrearYNotificarAsync(seed, notificar, responsable: false);

        Assert.True(aviso.Ok, aviso.Detail);
        Assert.Equal(1, aviso.Enviados);
        var mail = Assert.Single(sender.Emails);
        Assert.Equal(seed.TenantUserEmail, mail.To);
        Assert.Equal($"Nueva tarea {crear.TaskNumber}", mail.Subject);
    }

    [Fact]
    public async Task Notificar_WhatsApp_SinPlantilla_EsErrorDeConfig()
    {
        var seed = await SeedAsync("Dron Notificar WA");
        var notificar = NotificarStep(new ScrapeNotificar.Config { Canal = ScrapeNotificar.Canal.WhatsApp });

        var (_, aviso, sender) = await RunCrearYNotificarAsync(seed, notificar, responsable: true);

        Assert.False(aviso.Ok);
        Assert.Contains("plantilla", aviso.Detail);
        Assert.Empty(sender.Emails);
    }

    private static ScrapeStep NotificarStep(ScrapeNotificar.Config cfg) => new()
    {
        Name = "Notificar",
        Kind = ScrapeStepKind.Notificar,
        MappingJson = ScrapeNotificar.Serialize(cfg),
    };

    private async Task<(ScrapeCrearActividad.Result Crear, ScrapeNotificar.Result Aviso, FakeChannelSender Sender)> RunCrearYNotificarAsync(
        SeedData seed, ScrapeStep notificar, bool responsable)
    {
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenant = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        var engine = new WorkflowEngine(ctx, tenant, new NoOpWorkflowRuleHook(), new NoOpTaskBroadcaster());
        var tasks = new TaskItemService(ctx, tenant, new SequenceService(ctx, tenant), engine, new NoOpEmailSender(), new NodeAssigneeResolver(ctx));
        var forms = new FormResponseService(ctx, engine, new SequenceService(ctx, tenant), tenant,
            new NoOpFormRecordBroadcaster(),
            new Ecorex.Application.Forms.Lookups.FormLookupService(Array.Empty<Ecorex.Application.Forms.Lookups.IFormLookupSource>()),
            new Ecorex.Application.Rules.RulesEngine(ctx, tenant, new ServiceCollection().BuildServiceProvider()));
        var sender = new FakeChannelSender();
        var sp = new ServiceCollection()
            .AddSingleton<ITaskItemService>(tasks)
            .AddSingleton<IFormResponseService>(forms)
            .AddSingleton<INotifyTokenResolver>(new NotifyTokenResolver(ctx, TimeProvider.System))
            .AddSingleton<INotificationService>(new NotificationService(ctx))
            .AddSingleton<INotificationChannelSender>(sender)
            .BuildServiceProvider();

        var crearStep = Step(seed);
        if (responsable)
        {
            var cfg = ScrapeCrearActividad.Parse(crearStep.MappingJson)! with { ResponsableId = seed.TenantUserId };
            crearStep.MappingJson = ScrapeCrearActividad.Serialize(cfg);
        }

        var run = new ScrapeRunContext();
        run.Set("DETALLE", Detalle("1501899999", "Pendiente"));
        var crear = await ScrapeCrearActividad.RunAsync(sp, ctx, seed.TenantId, crearStep, run, "Dron prueba", CancellationToken.None);
        foreach (var (k, v) in ScrapeCrearActividad.OutputVars(crearStep, crear)) { run.Set(k, v); }
        var aviso = await ScrapeNotificar.RunAsync(sp, ctx, seed.TenantId, notificar, run, "Dron prueba", CancellationToken.None);
        return (crear, aviso, sender);
    }

    /// <summary>Doble del despachador de canales: registra los correos y no envia nada.</summary>
    private sealed class FakeChannelSender : INotificationChannelSender
    {
        public List<(string To, string Subject, string Html)> Emails { get; } = new();

        public Task<bool> SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
        {
            Emails.Add((toEmail, subject, htmlBody));
            return Task.FromResult(true);
        }

        public Task<WhatsAppSendOutcome> SendWhatsAppTemplateAsync(Guid lineId, string phone, string templateName, string? language,
            IReadOnlyDictionary<string, string> tokens, Guid actorUserId, string? attachmentBase64 = null, string? attachmentMime = null,
            string? attachmentFileName = null, string? headerMediaTypeOverride = null, string? headerMediaUrlOverride = null,
            string? headerMediaFileNameOverride = null, IReadOnlyDictionary<string, string>? decisionLinksByButtonLabel = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new WhatsAppSendOutcome(true, null));

        public Task<bool> SendWhatsAppGroupAsync(Guid lineId, string groupJid, string text, Guid actorUserId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> SendWhatsAppDocumentAsync(Guid lineId, string phone, string base64, string? mimeType, string? fileName,
            string? caption, Guid actorUserId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> SendTelegramAsync(string chatId, string text, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class ScrapeCrearActividadTests_Postgres
    : ScrapeCrearActividadTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public ScrapeCrearActividadTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class ScrapeCrearActividadTests_SqlServer
    : ScrapeCrearActividadTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public ScrapeCrearActividadTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture)
    {
    }
}
