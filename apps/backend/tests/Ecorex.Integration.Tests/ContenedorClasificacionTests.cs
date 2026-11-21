using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// ADR-0197 en matriz dual PostgreSQL / SQL Server:
/// - actualizar_filas_por_clave (agente): actualiza por clave SIN duplicar filas ni celdas, no toca otras columnas,
///   reporta las claves inexistentes (o las crea con crear_si_falta) y no ve la tabla homonima de otro tenant.
/// - Runner clasificar_lote: toma solo filas pendientes (marca vacia), en lotes, escribe solo las columnas de
///   salida + la marca, ignora claves ajenas al lote, deja sin marca lo que el agente no devolvio, registra el
///   consumo en AiUsageLog y exige el agente encendido.
/// </summary>
public abstract class ContenedorClasificacionTestsBase
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 21, 15, 30, 0, TimeSpan.Zero);

    private readonly TenantIsolationDbFixture _fixture;

    protected ContenedorClasificacionTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ActualizarFilasPorClave_ActualizaSinDuplicar_YEsTenantScoped()
    {
        var a = await SeedTenantAsync("Clasif Tenant A");
        var b = await SeedTenantAsync("Clasif Tenant B");
        var tablaA = await SeedTablaAsync(a, "P1", "P2", "P3");
        var tablaB = await SeedTablaAsync(b, "P1");

        await using (var ctx = _fixture.CreateContext(a))
        {
            var ts = new ContenedorDatosToolset(ctx, new TestTenantContext(a));

            var r1 = await RunAsync(ts, "actualizar_filas_por_clave", new
            {
                contenedor = "Tecnologia",
                columna_clave = "id_proceso",
                filas = new object[]
                {
                    new { id_proceso = "P1", sector = "software", columna_inexistente = "x" },
                    new { id_proceso = "P2", sector = "hardware" },
                    new { id_proceso = "PX", sector = "redes" },
                    new { sector = "sin clave" }
                }
            });
            Assert.True(r1.GetProperty("ok").GetBoolean(), r1.ToString());
            Assert.Equal(2, r1.GetProperty("actualizadas").GetInt32());
            Assert.Equal(0, r1.GetProperty("creadas").GetInt32());
            Assert.Equal(1, r1.GetProperty("sin_clave").GetInt32());
            Assert.Equal("PX", r1.GetProperty("no_encontradas")[0].GetString());

            // Re-escribir P1 cambia la celda existente: no crea otra celda ni otra fila.
            var r2 = await RunAsync(ts, "actualizar_filas_por_clave", new
            {
                contenedor = "SECOP Pruebas",
                tabla = "Tecnologia",
                columna_clave = "id_proceso",
                filas = new object[] { new { id_proceso = "P1", sector = "datos_bi" } }
            });
            Assert.Equal(1, r2.GetProperty("actualizadas").GetInt32());

            // crear_si_falta crea PX una sola vez aunque venga repetida en el lote.
            var r3 = await RunAsync(ts, "actualizar_filas_por_clave", new
            {
                contenedor = "Tecnologia",
                columna_clave = "id_proceso",
                crear_si_falta = true,
                filas = new object[] { new { id_proceso = "PX", sector = "redes" }, new { id_proceso = "PX", sector = "redes_telecom" } }
            });
            Assert.Equal(1, r3.GetProperty("creadas").GetInt32());
        }

        await using (var ctx = _fixture.CreateContext(a))
        {
            Assert.Equal(4, await ctx.DataContainerRows.CountAsync(r => r.ContainerId == tablaA.TableId));
            var filas = await LeerAsync(ctx, tablaA);
            Assert.Equal("datos_bi", filas["P1"]["sector"]);
            Assert.Equal("Proceso P1", filas["P1"]["nombre"]); // no tocada
            Assert.Equal("hardware", filas["P2"]["sector"]);
            Assert.False(filas["P3"].ContainsKey("sector"));
            Assert.Equal("redes_telecom", filas["PX"]["sector"]);
            // Una sola celda por (fila, columna).
            var dup = await ctx.DataContainerCells
                .Where(c => c.ColumnId == tablaA.Cols["sector"])
                .GroupBy(c => c.RowId).CountAsync(g => g.Count() > 1);
            Assert.Equal(0, dup);
        }

        // El tenant B no se toco (misma tabla, misma clave P1).
        await using (var ctx = _fixture.CreateContext(b))
        {
            var filas = await LeerAsync(ctx, tablaB);
            Assert.False(filas["P1"].ContainsKey("sector"));
            Assert.Equal(1, await ctx.DataContainerRows.CountAsync(r => r.ContainerId == tablaB.TableId));
        }
    }

    [Fact]
    public async Task ClasificarLote_MarcaPagina_EscribeSoloSalida_YRegistraConsumo()
    {
        var t = await SeedTenantAsync("Clasif Lote");
        var tabla = await SeedTablaAsync(t, "P1", "P2", "P3", "P4", "P5");
        await using (var ctx = _fixture.CreateContext(t))
        {
            // P0: ya clasificada (marca con valor): no es pendiente.
            var row = new DataContainerRow { TenantId = t, ContainerId = tabla.TableId };
            ctx.DataContainerRows.Add(row);
            ctx.DataContainerCells.Add(new DataContainerCell { TenantId = t, RowId = row.Id, ColumnId = tabla.Cols["id_proceso"], Value = "P0" });
            ctx.DataContainerCells.Add(new DataContainerCell { TenantId = t, RowId = row.Id, ColumnId = tabla.Cols["clasificado_en"], Value = "2026-01-01T00:00:00Z" });
            await ctx.SaveChangesAsync();
        }
        var agentId = await SeedAgentAsync(t, active: true);

        var fake = new ClasificadorFalso(omitir: "P3");
        await using (var ctx = _fixture.CreateContext(t))
        {
            var svc = NewService(ctx, t, fake);
            var req = new ClasificarLoteRequest(tabla.TableId, agentId.ToString(), "id_proceso", "clasificado_en",
                new[] { "sector" }, new[] { "nombre" }, 2);

            var r1 = await svc.ClasificarLoteAsync(req);
            Assert.True(r1.Ok, r1.Error);
            Assert.Equal(2, r1.Procesados);
            Assert.Equal(2, r1.Actualizados);
            Assert.Equal(3, r1.Restantes);
            Assert.Equal(1, r1.Ignorados); // la clave ajena al lote

            // El agente recibio solo nombre + clave (no la marca ni la salida) y solo filas pendientes.
            var enviados = fake.Lotes[0];
            Assert.Equal(new[] { "P1", "P2" }, enviados.Select(e => e["id_proceso"]).ToArray());
            Assert.All(enviados, e => Assert.Equal(new[] { "id_proceso", "nombre" }, e.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray()));

            var r2 = await svc.ClasificarLoteAsync(req with { Limite = 50 });
            Assert.Equal(3, r2.Procesados);
            Assert.Equal(2, r2.Actualizados);
            Assert.Equal(1, r2.SinRespuesta);
            Assert.Equal(1, r2.Restantes); // P3 queda pendiente
        }

        await using (var ctx = _fixture.CreateContext(t))
        {
            var filas = await LeerAsync(ctx, tabla);
            Assert.Equal("software", filas["P1"]["sector"]);
            Assert.Equal("Proceso P1", filas["P1"]["nombre"]); // el agente intento pisarla: se ignora
            Assert.Equal("2026-11-21T15:30:00Z", filas["P1"]["clasificado_en"]);
            Assert.False(filas["P3"].ContainsKey("clasificado_en"));
            Assert.Equal("2026-01-01T00:00:00Z", filas["P0"]["clasificado_en"]);
            Assert.False(filas.ContainsKey("P-AJENA"));
            Assert.Equal(6, await ctx.DataContainerRows.CountAsync(r => r.ContainerId == tabla.TableId));

            var usos = await ctx.AiUsageLogs.Where(l => l.Source == "clasificar-lote").ToListAsync();
            Assert.Equal(2, usos.Count);
            Assert.All(usos, u => Assert.Equal(agentId, u.AgentId));
        }
    }

    [Fact]
    public async Task ClasificarLote_AgenteApagado_NoLlamaAlProveedor()
    {
        var t = await SeedTenantAsync("Clasif Apagado");
        var tabla = await SeedTablaAsync(t, "P1");
        var agentId = await SeedAgentAsync(t, active: false);
        var fake = new ClasificadorFalso();
        await using var ctx = _fixture.CreateContext(t);
        var r = await NewService(ctx, t, fake).ClasificarLoteAsync(new ClasificarLoteRequest(
            tabla.TableId, "Clasificador de pruebas", "id_proceso", "clasificado_en", new[] { "sector" }, null, 10));
        Assert.False(r.Ok);
        Assert.Contains("apagado", r.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fake.Lotes);

        // Columna de salida inexistente: error claro, sin llamar.
        var r2 = await NewService(ctx, t, fake).ClasificarLoteAsync(new ClasificarLoteRequest(
            tabla.TableId, agentId.ToString(), "id_proceso", "clasificado_en", new[] { "no_existe" }, null, 10));
        Assert.False(r2.Ok);
        Assert.Contains("no_existe", r2.Error);
    }

    [Fact]
    public void ParseItems_ToleraBloquesYEnvoltorio()
    {
        Assert.Equal(2, ContenedorClasificacionService.ParseItems("```json\n[{\"a\":1},{\"a\":2}]\n```")!.Count);
        Assert.Single(ContenedorClasificacionService.ParseItems("Aqui va: {\"resultados\":[{\"a\":1}]} listo")!);
        Assert.Null(ContenedorClasificacionService.ParseItems("no hay json"));
    }

    // ---- Helpers ----

    private sealed record Tabla(Guid TableId, Dictionary<string, Guid> Cols);

    private ContenedorClasificacionService NewService(EcorexDbContext ctx, Guid tenantId, IAiProviderClient client)
    {
        var tenant = new TestTenantContext(tenantId);
        return new ContenedorClasificacionService(ctx, tenant, new PlainSecrets(), client, new AiUsageService(ctx, tenant), new FixedClock(Ahora));
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

    /// <summary>Contenedor "SECOP Pruebas" con la tabla "Tecnologia" (id_proceso, nombre, sector, clasificado_en)
    /// y una fila por clave con su nombre.</summary>
    private async Task<Tabla> SeedTablaAsync(Guid tenantId, params string[] keys)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        var model = new DataModel { TenantId = tenantId, Name = "SECOP Pruebas" };
        ctx.DataModels.Add(model);
        var table = new DataContainer { TenantId = tenantId, Name = "Tecnologia", ModelId = model.Id };
        ctx.DataContainers.Add(table);
        var cols = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var order = 0;
        foreach (var n in new[] { "id_proceso", "nombre", "sector", "clasificado_en" })
        {
            var c = new DataContainerColumn { TenantId = tenantId, ContainerId = table.Id, Name = n, Type = DataContainerColumnType.Text, SortOrder = order++ };
            ctx.DataContainerColumns.Add(c);
            cols[n] = c.Id;
        }
        await ctx.SaveChangesAsync();
        foreach (var k in keys)
        {
            var row = new DataContainerRow { TenantId = tenantId, ContainerId = table.Id };
            ctx.DataContainerRows.Add(row);
            ctx.DataContainerCells.Add(new DataContainerCell { TenantId = tenantId, RowId = row.Id, ColumnId = cols["id_proceso"], Value = k });
            ctx.DataContainerCells.Add(new DataContainerCell { TenantId = tenantId, RowId = row.Id, ColumnId = cols["nombre"], Value = $"Proceso {k}" });
            await ctx.SaveChangesAsync(); // una por una: CreatedAt/Id crecientes = orden del lote
        }
        return new Tabla(table.Id, cols);
    }

    private async Task<Guid> SeedAgentAsync(Guid tenantId, bool active)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        var agent = new AiAgent
        {
            TenantId = tenantId,
            Name = "Clasificador de pruebas",
            SystemPrompt = "Clasifica procesos por sector de TI.",
            Provider = AiProvider.DeepSeek,
            IsActive = active
        };
        ctx.AiAgents.Add(agent);
        await ctx.SaveChangesAsync();
        return agent.Id;
    }

    /// <summary>clave -> (columna -> valor) de la tabla.</summary>
    private static async Task<Dictionary<string, Dictionary<string, string?>>> LeerAsync(EcorexDbContext ctx, Tabla tabla)
    {
        var names = tabla.Cols.ToDictionary(kv => kv.Value, kv => kv.Key);
        var cells = await ctx.DataContainerCells.AsNoTracking()
            .Where(c => ctx.DataContainerRows.Any(r => r.Id == c.RowId && r.ContainerId == tabla.TableId))
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

    private static async Task<JsonElement> RunAsync(ContenedorDatosToolset ts, string tool, object args)
    {
        var r = await ts.ExecuteAsync(tool, JsonSerializer.Serialize(args), Guid.NewGuid(), autonomous: true);
        using var doc = JsonDocument.Parse(r.Json);
        return doc.RootElement.Clone();
    }

    /// <summary>Proveedor falso: clasifica todo como "software", intenta pisar 'nombre', agrega una clave AJENA al
    /// lote y omite la clave indicada. Guarda lo que recibio en cada lote.</summary>
    private sealed class ClasificadorFalso(string? omitir = null) : IAiProviderClient
    {
        public List<List<Dictionary<string, string?>>> Lotes { get; } = new();

        public Task<AiChatResult> CompleteAsync(AiProvider provider, string apiKey, string? baseUrl, string model, string systemPrompt,
            IReadOnlyList<AiChatTurn> turns, CancellationToken cancellationToken = default)
        {
            var recibidos = JsonSerializer.Deserialize<List<Dictionary<string, string?>>>(turns[^1].Text)!;
            Lotes.Add(recibidos);
            var salida = recibidos
                .Where(r => r["id_proceso"] != omitir)
                .Select(r => (object)new { id_proceso = r["id_proceso"], es_tecnologia = true, sector = "software", nombre = "PISADO" })
                .Append(new { id_proceso = "P-AJENA", es_tecnologia = true, sector = "x", nombre = "x" })
                .ToList();
            var text = "```json\n" + JsonSerializer.Serialize(salida) + "\n```";
            return Task.FromResult(new AiChatResult(true, text, null, 100, 20));
        }

        public Task<AiCompletion> CompleteWithToolsAsync(AiProvider provider, string apiKey, string? baseUrl, string model, string systemPrompt,
            IReadOnlyList<AiToolMessage> messages, IReadOnlyList<AiToolSpec> tools, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AiChatResult> CompleteVisionAsync(AiProvider provider, string apiKey, string? baseUrl, string model, string systemPrompt,
            IReadOnlyList<AiVisionPart> content, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
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
public sealed class ContenedorClasificacionTests_Postgres : ContenedorClasificacionTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public ContenedorClasificacionTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture) { }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class ContenedorClasificacionTests_SqlServer : ContenedorClasificacionTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public ContenedorClasificacionTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture) { }
}
