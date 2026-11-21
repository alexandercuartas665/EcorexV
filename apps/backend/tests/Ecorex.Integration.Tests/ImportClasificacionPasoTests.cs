extern alias superadmin;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using superadmin::Ecorex.SuperAdmin.Agents;

namespace Ecorex.Integration.Tests;

/// <summary>
/// ADR-0199 en matriz dual PostgreSQL / SQL Server: la clasificacion con IA como PASO de la programacion de
/// importacion.
/// - Clasifica SOLO filas con la marca vacia, lote a lote hasta completar; repetir no llama a la IA (idempotente).
/// - Respeta el tope de lotes y el cupo del plan: corta y deja el resto pendiente.
/// - Reintenta una falla transitoria del proveedor; una falla de configuracion no se reintenta.
/// - limpiar_marca vacia marca (y salidas) por filtro o de toda la tabla, y esas filas se reclasifican.
/// - El cierre ok de una corrida pide la clasificacion; el barrido la toma, la corre y la anota en la bitacora.
/// </summary>
public abstract class ImportClasificacionPasoTestsBase
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 21, 18, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan[] SinEspera = { TimeSpan.Zero, TimeSpan.Zero };

    private readonly TenantIsolationDbFixture _fixture;

    protected ImportClasificacionPasoTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Paso_ClasificaSoloPendientes_HastaCompletar_EsIdempotente()
    {
        var t = await SeedTenantAsync("Paso Completo");
        var s = await SeedAsync(t, "P1", "P2", "P3", "P4");
        await MarcarAsync(t, s, "P0", "2026-01-01T00:00:00Z"); // ya clasificada: no es pendiente
        var agentId = await SeedAgentAsync(t, active: true);
        var ia = new IaFalsa();

        await using (var ctx = _fixture.CreateContext(t))
        {
            var (svc, _) = NewServices(ctx, t, ia);
            var conf = await svc.ConfigurarAsync(Req(s.ProcessId, agentId, limite: 2, maxLotes: 10));
            Assert.True(conf.Ok, conf.Error);
            Assert.Equal(s.TableId, conf.Paso!.TableId); // tabla por defecto = destino del conector

            var r = await svc.EjecutarAsync(s.ProcessId);
            Assert.True(r.Ok, r.Error);
            Assert.Equal(ImportClassifyRunResult.MotivoCompleto, r.Motivo);
            Assert.Equal(2, r.Lotes);
            Assert.Equal(4, r.Actualizados);
            Assert.Equal(0, r.Restantes);
            Assert.Equal(2, ia.Llamadas);
            Assert.All(ia.Recibidos, k => Assert.NotEqual("P0", k));

            // Idempotente: sin pendientes no se llama a la IA.
            var r2 = await svc.EjecutarAsync(s.ProcessId);
            Assert.True(r2.Ok);
            Assert.Equal(0, r2.Lotes);
            Assert.Equal(2, ia.Llamadas);
        }

        await using (var ctx = _fixture.CreateContext(t))
        {
            var filas = await LeerAsync(ctx, s);
            Assert.Equal("software", filas["P4"]["sector"]);
            Assert.Equal("2026-11-21T18:00:00Z", filas["P4"]["clasificado_en"]);
            Assert.Equal("2026-01-01T00:00:00Z", filas["P0"]["clasificado_en"]);
            Assert.False(filas["P0"].ContainsKey("sector"));
        }
    }

    [Fact]
    public async Task Paso_TopeDeLotesYCupo_CortanYDejanPendiente()
    {
        var t = await SeedTenantAsync("Paso Tope Cupo");
        var s = await SeedAsync(t, "P1", "P2", "P3", "P4", "P5", "P6");
        var agentId = await SeedAgentAsync(t, active: true);
        var ia = new IaFalsa();

        await using var ctx = _fixture.CreateContext(t);
        var (svc, usage) = NewServices(ctx, t, ia);

        Assert.True((await svc.ConfigurarAsync(Req(s.ProcessId, agentId, limite: 2, maxLotes: 1))).Ok);
        var tope = await svc.EjecutarAsync(s.ProcessId);
        Assert.True(tope.Ok);
        Assert.Equal(ImportClassifyRunResult.MotivoTope, tope.Motivo);
        Assert.Equal(1, tope.Lotes);
        Assert.Equal(4, tope.Restantes);

        // Cupo: se agota despues del primer lote de esta corrida -> corta, no es error, queda pendiente.
        Assert.True((await svc.ConfigurarAsync(Req(s.ProcessId, agentId, limite: 2, maxLotes: 10))).Ok);
        ia.DespuesDeLlamar = () => usage.Agotado = true;
        var cupo = await svc.EjecutarAsync(s.ProcessId);
        Assert.True(cupo.Ok);
        Assert.Equal(ImportClassifyRunResult.MotivoCupo, cupo.Motivo);
        Assert.Equal(1, cupo.Lotes);
        Assert.Equal(2, cupo.Restantes);
        Assert.Equal(2, ia.Llamadas);
    }

    [Fact]
    public async Task Paso_ReintentaFallaTransitoria_YNoReintentaConfiguracion()
    {
        var t = await SeedTenantAsync("Paso Reintento");
        var s = await SeedAsync(t, "P1", "P2");
        var agentId = await SeedAgentAsync(t, active: true);
        var ia = new IaFalsa { FallarPrimeras = 1 };

        await using (var ctx = _fixture.CreateContext(t))
        {
            var (svc, _) = NewServices(ctx, t, ia);
            Assert.True((await svc.ConfigurarAsync(Req(s.ProcessId, agentId, limite: 50, maxLotes: 5))).Ok);
            var r = await svc.EjecutarAsync(s.ProcessId);
            Assert.True(r.Ok, r.Error);
            Assert.Equal(ImportClassifyRunResult.MotivoCompleto, r.Motivo);
            Assert.Equal(1, r.Reintentos);
            Assert.Equal(2, r.Actualizados);
            Assert.Equal(2, ia.Llamadas);
        }

        // Errores consecutivos que agotan los reintentos: corta con error_proveedor y deja pendiente.
        var s2 = await SeedAsync(t, "Q1");
        var ia2 = new IaFalsa { FallarPrimeras = 10 };
        await using (var ctx = _fixture.CreateContext(t))
        {
            var (svc, _) = NewServices(ctx, t, ia2);
            Assert.True((await svc.ConfigurarAsync(Req(s2.ProcessId, agentId, limite: 50, maxLotes: 5))).Ok);
            var r = await svc.EjecutarAsync(s2.ProcessId);
            Assert.False(r.Ok);
            Assert.Equal(ImportClassifyRunResult.MotivoProveedor, r.Motivo);
            Assert.Equal(SinEspera.Length, r.Reintentos);
            Assert.Equal(1, r.Restantes);
            Assert.Equal(SinEspera.Length + 1, ia2.Llamadas);
        }

        // Agente apagado: falla de configuracion, sin reintentos ni llamadas.
        await using (var ctx = _fixture.CreateContext(t))
        {
            var agent = await ctx.AiAgents.SingleAsync(a => a.Id == agentId);
            agent.IsActive = false;
            await ctx.SaveChangesAsync();
            var ia3 = new IaFalsa();
            var (svc, _) = NewServices(ctx, t, ia3);
            var r = await svc.EjecutarAsync(s2.ProcessId);
            Assert.False(r.Ok);
            Assert.Equal(ImportClassifyRunResult.MotivoConfig, r.Motivo);
            Assert.Equal(0, r.Reintentos);
            Assert.Equal(0, ia3.Llamadas);
            Assert.Contains("apagado", r.Error, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task LimpiarMarca_PorFiltroYTotal_ReclasificaYEsTenantScoped()
    {
        var a = await SeedTenantAsync("Limpiar A");
        var b = await SeedTenantAsync("Limpiar B");
        var sa = await SeedAsync(a, "P1", "P2", "P3");
        var sb = await SeedAsync(b, "P1");
        await MarcarAsync(b, sb, "P1", "2026-01-01T00:00:00Z", existente: true);
        var agentId = await SeedAgentAsync(a, active: true);
        var ia = new IaFalsa();

        await using (var ctx = _fixture.CreateContext(a))
        {
            var (svc, _) = NewServices(ctx, a, ia);
            Assert.True((await svc.ConfigurarAsync(Req(sa.ProcessId, agentId, limite: 50, maxLotes: 5))).Ok);
            Assert.Equal(3, (await svc.EjecutarAsync(sa.ProcessId)).Actualizados);

            var clasif = NewClasificador(ctx, a, ia, out _);
            // Filtro por clave: solo P2, marca + sector.
            var l1 = await clasif.LimpiarMarcaAsync(new LimpiarMarcaRequest(sa.TableId, "clasificado_en", new[] { "sector" },
                new Dictionary<string, string> { ["id_proceso"] = "P2" }));
            Assert.True(l1.Ok, l1.Error);
            Assert.Equal(1, l1.Filas);
            Assert.Equal(2, l1.Celdas);
        }

        // Reclasifica en OTRO contexto (como en produccion: limpiar va por el scope del MCP y la clasificacion por el
        // del worker; el UPDATE masivo no refresca entidades ya cargadas en un contexto).
        await using (var ctx = _fixture.CreateContext(a))
        {
            var (svc, _) = NewServices(ctx, a, ia);
            var r = await svc.EjecutarAsync(sa.ProcessId);
            Assert.Equal(1, r.Actualizados);
            Assert.Equal(new[] { "P2" }, ia.Recibidos.Skip(3).ToArray());

            var clasif = NewClasificador(ctx, a, ia, out _);
            // Columna inexistente: error, nada cambia.
            var mal = await clasif.LimpiarMarcaAsync(new LimpiarMarcaRequest(sa.TableId, "no_existe", null, null));
            Assert.False(mal.Ok);
            Assert.Contains("no_existe", mal.Error);

            // Toda la tabla, solo la marca.
            var l2 = await clasif.LimpiarMarcaAsync(new LimpiarMarcaRequest(sa.TableId, "clasificado_en", null, null));
            Assert.Equal(3, l2.Filas);
            Assert.Equal(3, l2.Celdas);
        }

        await using (var ctx = _fixture.CreateContext(a))
        {
            var filas = await LeerAsync(ctx, sa);
            Assert.All(filas.Values, f => Assert.Null(f["clasificado_en"]));
            Assert.All(filas.Values, f => Assert.Equal("software", f["sector"])); // la salida no se pidio limpiar
        }

        // El tenant B no se toco.
        await using (var ctx = _fixture.CreateContext(b))
        {
            var filas = await LeerAsync(ctx, sb);
            Assert.Equal("2026-01-01T00:00:00Z", filas["P1"]["clasificado_en"]);
        }
    }

    [Fact]
    public async Task CierreOk_PideClasificacion_YElBarridoLaCorreYAnota()
    {
        var t = await SeedTenantAsync("Paso Barrido");
        var otro = await SeedTenantAsync("Paso Barrido Otro");
        var s = await SeedAsync(t, "P1", "P2");
        var sinPaso = await SeedAsync(t, "X1");
        var agentId = await SeedAgentAsync(t, active: true);
        var ia = new IaFalsa();

        await using (var ctx = _fixture.CreateContext(t))
        {
            var (svc, _) = NewServices(ctx, t, ia);
            Assert.True((await svc.ConfigurarAsync(Req(s.ProcessId, agentId, limite: 50, maxLotes: 5))).Ok);

            var log = new ImportRunLog(ctx, NullLogger<ImportRunLog>.Instance);
            // Corrida con error: no pide clasificacion.
            Assert.NotNull(await log.OpenAsync(s.ProcessId, ImportRunTrigger.Scheduled, Ahora.AddMinutes(-10), "corr-err"));
            await log.CloseAsync("corr-err", ok: false, 0, 0, 0, "fallo");
            Assert.Null((await ctx.ImportProcesses.AsNoTracking().SingleAsync(p => p.Id == s.ProcessId)).ClassifyRequestedAt);

            // Corrida ok: la pide. Una programacion sin paso no pide nada.
            Assert.NotNull(await log.OpenAsync(s.ProcessId, ImportRunTrigger.Scheduled, Ahora, "corr-ok"));
            await log.CloseAsync("corr-ok", ok: true, 2, 0, 0, null);
            Assert.NotNull(await log.OpenAsync(sinPaso.ProcessId, ImportRunTrigger.Scheduled, Ahora, "corr-sin"));
            await log.CloseAsync("corr-sin", ok: true, 1, 0, 0, null);
            Assert.NotNull((await ctx.ImportProcesses.AsNoTracking().SingleAsync(p => p.Id == s.ProcessId)).ClassifyRequestedAt);
            Assert.Null((await ctx.ImportProcesses.AsNoTracking().SingleAsync(p => p.Id == sinPaso.ProcessId)).ClassifyRequestedAt);
        }

        // Barrido de plataforma: solo ids, y solo el tenant con solicitud.
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            var (svc, _) = NewServices(ctx, null, ia);
            var tenants = await svc.FindTenantsWithRequestsAsync();
            Assert.Contains(t, tenants);
            Assert.DoesNotContain(otro, tenants);
        }

        // El tenant ajeno no ve ni corre nada.
        await using (var ctx = _fixture.CreateContext(otro))
        {
            var (svc, _) = NewServices(ctx, otro, ia);
            Assert.Equal(0, await svc.RunRequestedForTenantAsync());
        }

        await using (var ctx = _fixture.CreateContext(t))
        {
            var (svc, _) = NewServices(ctx, t, ia);
            Assert.Equal(1, await svc.RunRequestedForTenantAsync());
            Assert.Equal(0, await svc.RunRequestedForTenantAsync()); // ya se tomo
        }

        await using (var ctx = _fixture.CreateContext(t))
        {
            Assert.Null((await ctx.ImportProcesses.AsNoTracking().SingleAsync(p => p.Id == s.ProcessId)).ClassifyRequestedAt);
            var run = await ctx.ImportRuns.AsNoTracking().SingleAsync(r => r.CorrelationId == "corr-ok");
            Assert.Contains("Clasificacion IA: 1 lote(s), 2 clasificada(s), 0 pendiente(s) [completo]", run.Detail);
            var filas = await LeerAsync(ctx, s);
            Assert.All(filas.Values, f => Assert.Equal("software", f["sector"]));
            Assert.Equal(1, ia.Llamadas);
        }
    }

    // ---- Helpers ----

    private sealed record Seed(Guid ModelId, Guid TableId, Guid ProcessId, Dictionary<string, Guid> Cols);

    private static ConfigurarPasoClasificacionRequest Req(Guid processId, Guid agentId, int limite, int maxLotes)
        => new(processId, null, agentId.ToString(), "id_proceso", "clasificado_en", new[] { "sector" }, new[] { "nombre" }, limite, maxLotes);

    private (ImportClassificationService Svc, UsoFalso Usage) NewServices(EcorexDbContext ctx, Guid? tenantId, IAiProviderClient ia)
    {
        var clasif = NewClasificador(ctx, tenantId, ia, out var usage);
        return (new ImportClassificationService(ctx, new TestTenantContext(tenantId), clasif, new FixedClock(Ahora), SinEspera), usage);
    }

    private static ContenedorClasificacionService NewClasificador(EcorexDbContext ctx, Guid? tenantId, IAiProviderClient ia, out UsoFalso usage)
    {
        var tenant = new TestTenantContext(tenantId);
        usage = new UsoFalso(new AiUsageService(ctx, tenant));
        return new ContenedorClasificacionService(ctx, tenant, new PlainSecrets(), ia, usage, new FixedClock(Ahora));
    }

    private async Task<Guid> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId: null);
        ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
        if (!await ctx.AiProviderConfigs.AnyAsync(c => c.Provider == AiProvider.DeepSeek))
        {
            ctx.AiProviderConfigs.Add(new AiProviderConfig { Provider = AiProvider.DeepSeek, ApiKeyEncrypted = "key-de-prueba", IsEnabled = true, Model = "deepseek-chat" });
        }
        await ctx.SaveChangesAsync();
        return tenantId;
    }

    /// <summary>Contenedor con la tabla "Procesos" (id_proceso, nombre, sector, clasificado_en), un conector API que la
    /// alimenta y una programacion que lo dispara; una fila por clave.</summary>
    private async Task<Seed> SeedAsync(Guid tenantId, params string[] keys)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        var model = new DataModel { TenantId = tenantId, Name = "SECOP " + Guid.NewGuid().ToString("N")[..6] };
        ctx.DataModels.Add(model);
        var table = new DataContainer { TenantId = tenantId, Name = "Procesos", ModelId = model.Id };
        ctx.DataContainers.Add(table);
        var cols = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var order = 0;
        foreach (var n in new[] { "id_proceso", "nombre", "sector", "clasificado_en" })
        {
            var c = new DataContainerColumn { TenantId = tenantId, ContainerId = table.Id, Name = n, Type = DataContainerColumnType.Text, SortOrder = order++ };
            ctx.DataContainerColumns.Add(c);
            cols[n] = c.Id;
        }
        var connector = new DataConnector { TenantId = tenantId, ModelId = model.Id, Name = "Socrata", Kind = ConnectorKind.RestApi, ContainerId = table.Id };
        ctx.DataConnectors.Add(connector);
        var process = new ImportProcess { TenantId = tenantId, ModelId = model.Id, ConnectorId = connector.Id, Name = "Recientes diaria" };
        ctx.ImportProcesses.Add(process);
        await ctx.SaveChangesAsync();
        foreach (var k in keys)
        {
            var row = new DataContainerRow { TenantId = tenantId, ContainerId = table.Id };
            ctx.DataContainerRows.Add(row);
            ctx.DataContainerCells.Add(new DataContainerCell { TenantId = tenantId, RowId = row.Id, ColumnId = cols["id_proceso"], Value = k });
            ctx.DataContainerCells.Add(new DataContainerCell { TenantId = tenantId, RowId = row.Id, ColumnId = cols["nombre"], Value = $"Proceso {k}" });
            await ctx.SaveChangesAsync(); // una por una: orden estable del lote
        }
        return new Seed(model.Id, table.Id, process.Id, cols);
    }

    /// <summary>Pone la marca a una fila (la crea si no existe, salvo existente=true).</summary>
    private async Task MarcarAsync(Guid tenantId, Seed s, string key, string marca, bool existente = false)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        Guid rowId;
        if (existente)
        {
            rowId = await ctx.DataContainerCells.Where(c => c.ColumnId == s.Cols["id_proceso"] && c.Value == key).Select(c => c.RowId).SingleAsync();
        }
        else
        {
            var row = new DataContainerRow { TenantId = tenantId, ContainerId = s.TableId };
            ctx.DataContainerRows.Add(row);
            ctx.DataContainerCells.Add(new DataContainerCell { TenantId = tenantId, RowId = row.Id, ColumnId = s.Cols["id_proceso"], Value = key });
            rowId = row.Id;
        }
        ctx.DataContainerCells.Add(new DataContainerCell { TenantId = tenantId, RowId = rowId, ColumnId = s.Cols["clasificado_en"], Value = marca });
        await ctx.SaveChangesAsync();
    }

    private async Task<Guid> SeedAgentAsync(Guid tenantId, bool active)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        var agent = new AiAgent
        {
            TenantId = tenantId,
            Name = "Clasificador General",
            SystemPrompt = "Clasifica procesos por sector.",
            Provider = AiProvider.DeepSeek,
            IsActive = active
        };
        ctx.AiAgents.Add(agent);
        await ctx.SaveChangesAsync();
        return agent.Id;
    }

    /// <summary>clave -> (columna -> valor). Una celda vaciada aparece con valor null.</summary>
    private static async Task<Dictionary<string, Dictionary<string, string?>>> LeerAsync(EcorexDbContext ctx, Seed s)
    {
        var names = s.Cols.ToDictionary(kv => kv.Value, kv => kv.Key);
        var cells = await ctx.DataContainerCells.AsNoTracking()
            .Where(c => ctx.DataContainerRows.Any(r => r.Id == c.RowId && r.ContainerId == s.TableId))
            .Select(c => new { c.RowId, c.ColumnId, c.Value })
            .ToListAsync();
        var result = new Dictionary<string, Dictionary<string, string?>>(StringComparer.Ordinal);
        foreach (var g in cells.GroupBy(c => c.RowId))
        {
            var d = g.ToDictionary(c => names[c.ColumnId], c => c.Value, StringComparer.OrdinalIgnoreCase);
            result[d["id_proceso"]!] = d;
        }
        return result;
    }

    /// <summary>Proveedor falso: clasifica todo como "software". Puede lanzar en las primeras N llamadas (falla
    /// transitoria) y ejecutar algo despues de cada llamada ok.</summary>
    private sealed class IaFalsa : IAiProviderClient
    {
        public int Llamadas { get; private set; }
        public int FallarPrimeras { get; set; }
        public List<string> Recibidos { get; } = new();
        public Action? DespuesDeLlamar { get; set; }

        public Task<AiChatResult> CompleteAsync(AiProvider provider, string apiKey, string? baseUrl, string model, string systemPrompt,
            IReadOnlyList<AiChatTurn> turns, CancellationToken cancellationToken = default)
        {
            Llamadas++;
            if (Llamadas <= FallarPrimeras) { throw new HttpRequestException("timeout simulado"); }
            var lote = JsonSerializer.Deserialize<List<Dictionary<string, string?>>>(turns[^1].Text)!;
            Recibidos.AddRange(lote.Select(r => r["id_proceso"]!));
            var salida = lote.Select(r => new { id_proceso = r["id_proceso"], sector = "software" }).ToList();
            DespuesDeLlamar?.Invoke();
            return Task.FromResult(new AiChatResult(true, JsonSerializer.Serialize(salida), null, 50, 10));
        }

        public Task<AiCompletion> CompleteWithToolsAsync(AiProvider provider, string apiKey, string? baseUrl, string model, string systemPrompt,
            IReadOnlyList<AiToolMessage> messages, IReadOnlyList<AiToolSpec> tools, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AiChatResult> CompleteVisionAsync(AiProvider provider, string apiKey, string? baseUrl, string model, string systemPrompt,
            IReadOnlyList<AiVisionPart> content, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>Consumo real (AiUsageLog) con un interruptor para simular el cupo DURO agotado.</summary>
    private sealed class UsoFalso(IAiUsageService inner) : IAiUsageService
    {
        public bool Agotado { get; set; }

        public Task RecordAsync(Guid? agentId, AiProvider provider, string model, int inputTokens, int outputTokens, string source, bool success, CancellationToken cancellationToken = default)
            => inner.RecordAsync(agentId, provider, model, inputTokens, outputTokens, source, success, cancellationToken);

        public Task<AiUsageSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default) => inner.GetSummaryAsync(cancellationToken);

        public Task<AiQuotaDto> GetQuotaAsync(CancellationToken cancellationToken = default)
            => Agotado ? Task.FromResult(new AiQuotaDto(1000, 1000, true)) : inner.GetQuotaAsync(cancellationToken);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class PlainSecrets : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string ciphertext) => ciphertext;
    }

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class ImportClasificacionPasoTests_Postgres : ImportClasificacionPasoTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public ImportClasificacionPasoTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture) { }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class ImportClasificacionPasoTests_SqlServer : ImportClasificacionPasoTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public ImportClasificacionPasoTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture) { }
}
