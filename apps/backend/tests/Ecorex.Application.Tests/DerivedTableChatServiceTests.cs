using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Flujo completo del asistente de tablas derivadas (ADR-0130) con el motor generico de propuestas: el modelo
/// (fake) ve la frase del usuario, llama a proponer_derivacion (lectura: corre sola y devuelve la vista previa),
/// luego pide crear_tabla_derivada (mutante: queda como PROPUESTA, nada se crea), el usuario CONFIRMA y la tabla
/// nace por el servicio de derivadas (fake que registra el alta). Tambien: rechazar no crea, escribir con una
/// propuesta pendiente la descarta, y el contenedor_id se FUERZA aunque el modelo mande otro.
/// </summary>
public class DerivedTableChatServiceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid ModelId = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    private sealed class FakeTenant : ITenantContext { public Guid? TenantId => Tenant; public Guid? UserId => Actor; }

    private sealed class IdentitySecrets : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string ciphertext) => ciphertext;
    }

    private sealed class FakeUsage : IAiUsageService
    {
        public List<(string Source, int In, int Out)> Recorded { get; } = new();
        public Task RecordAsync(Guid? agentId, AiProvider provider, string model, int inputTokens, int outputTokens, string source, bool success, CancellationToken cancellationToken = default)
        {
            Recorded.Add((source, inputTokens, outputTokens));
            return Task.CompletedTask;
        }
        public Task<AiUsageSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AiUsageSummaryDto(0, 0, 0, 0, 0m, Array.Empty<AgentUsageDto>()));
        public Task<AiQuotaDto> GetQuotaAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AiQuotaDto(0, 0, false));
    }

    private sealed class FakeAi : IAiProviderClient
    {
        private readonly Queue<AiCompletion> _queue = new();
        public List<string> SystemPrompts { get; } = new();
        public List<IReadOnlyList<AiToolMessage>> Threads { get; } = new();
        public void Enqueue(AiCompletion c) => _queue.Enqueue(c);

        public Task<AiCompletion> CompleteWithToolsAsync(AiProvider provider, string apiKey, string? baseUrl, string model,
            string systemPrompt, IReadOnlyList<AiToolMessage> messages, IReadOnlyList<AiToolSpec> tools, CancellationToken cancellationToken = default)
        {
            SystemPrompts.Add(systemPrompt);
            Threads.Add(messages);
            return Task.FromResult(_queue.Count == 0
                ? new AiCompletion(true, "(sin mas respuestas)", null, 0, 0, Array.Empty<AiToolCall>())
                : _queue.Dequeue());
        }
        public Task<AiChatResult> CompleteAsync(AiProvider provider, string apiKey, string? baseUrl, string model, string systemPrompt, IReadOnlyList<AiChatTurn> turns, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<AiChatResult> CompleteVisionAsync(AiProvider provider, string apiKey, string? baseUrl, string model, string systemPrompt, IReadOnlyList<AiVisionPart> content, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeDerived : IDerivedTableService
    {
        public int Previews { get; private set; }
        public CreateDerivedTableRequest? LastCreate { get; private set; }
        public DerivedTableCreatedDto? Created { get; private set; }

        public Task<DerivationPreviewDto> PreviewAsync(DerivationSpec spec, int take = 20, CancellationToken ct = default)
        {
            Previews++;
            var cols = new[]
            {
                new DerivedColumnPlan("Categoria", DataContainerColumnType.Text, DerivedColumnRole.GroupBy),
                new DerivedColumnPlan("Clave", DataContainerColumnType.Text, DerivedColumnRole.Key),
                new DerivedColumnPlan("Cantidad", DataContainerColumnType.Number, DerivedColumnRole.Aggregate),
            };
            var rows = new List<IReadOnlyDictionary<string, string?>>
            {
                new Dictionary<string, string?> { ["Categoria"] = "Tornillos", ["Clave"] = "tornillos", ["Cantidad"] = "2" },
            };
            return Task.FromResult(new DerivationPreviewDto(cols, rows, 1, 2, 2));
        }

        public Task<DerivedTableCreatedDto> CreateAsync(CreateDerivedTableRequest req, Guid actorUserId, CancellationToken ct = default)
        {
            LastCreate = req;
            Created = new DerivedTableCreatedDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            return Task.FromResult(Created);
        }

        public Task<DerivedRefreshOutcome> RefreshAsync(Guid connectorId, ApiImportMode mode, string? keyColumnName, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed record Fixture(DerivedTableChatService Chat, FakeAi Ai, FakeDerived Derived, FakeUsage Usage, Guid ItemsId);

    private static Fixture NewFixture(bool providerEnabled = true)
    {
        var inner = DerivedChatTestDb.NewInner();
        inner.DataModels.Add(new DataModel { Id = ModelId, TenantId = Tenant, Name = "ALEGRA" });
        var items = new DataContainer { TenantId = Tenant, ModelId = ModelId, Name = "Items" };
        inner.DataContainers.Add(items);
        var cat = new DataContainerColumn { TenantId = Tenant, ContainerId = items.Id, Name = "Categoria", Type = DataContainerColumnType.Text, SortOrder = 0 };
        var precio = new DataContainerColumn { TenantId = Tenant, ContainerId = items.Id, Name = "Precio", Type = DataContainerColumnType.Decimal, SortOrder = 1 };
        inner.DataContainerColumns.AddRange(cat, precio);
        inner.AiProviderConfigs.Add(new AiProviderConfig
        {
            Provider = AiProvider.Gemini,
            IsEnabled = providerEnabled,
            ApiKeyEncrypted = providerEnabled ? "clave-de-prueba" : null,
            UseForFormBuilder = true,
            Model = "gemini-2.5-flash"
        });
        inner.SaveChanges();

        var db = new DerivedChatTestDb.FakeAppDb(inner);
        var tenant = new FakeTenant();
        var derived = new FakeDerived();
        var ai = new FakeAi();
        var usage = new FakeUsage();
        var toolset = new DerivedTableToolset(db, tenant, derived);
        var engine = new ProposalChatEngine(ai, usage);
        var chat = new DerivedTableChatService(db, tenant, new IdentitySecrets(), toolset, engine);
        return new Fixture(chat, ai, derived, usage, items.Id);
    }

    private static string Recipe(string? nombre = null) => JsonSerializer.Serialize(new
    {
        nombre,
        tabla_origen = "Items",
        agrupar_por = new[] { "Categoria" },
        agregados = new[] { new { funcion = "sum", columna = "Precio", nombre = "Total precio" } }
    });

    [Fact]
    public async Task Start_falla_claro_si_el_proveedor_no_esta_habilitado()
    {
        var f = NewFixture(providerEnabled: false);
        var r = await f.Chat.StartAsync(ModelId, null, Actor);
        Assert.False(r.Ok);
        Assert.Contains("Servidores de IA", r.Error);
    }

    [Fact]
    public async Task Start_saluda_y_el_arnes_lista_las_tablas_con_sus_columnas()
    {
        var f = NewFixture();
        f.Ai.Enqueue(new AiCompletion(true, "Que tabla quieres?", null, 10, 5, Array.Empty<AiToolCall>()));

        var start = await f.Chat.StartAsync(ModelId, f.ItemsId, Actor);
        Assert.True(start.Ok);
        Assert.Contains("Items", start.Greeting);

        var r = await f.Chat.SendAsync(start.ConversationId, "hola", Actor);
        Assert.True(r.Ok);
        Assert.Equal("Que tabla quieres?", r.AssistantText);
        var prompt = f.Ai.SystemPrompts.Single();
        Assert.Contains("Items [" + f.ItemsId.ToString("D") + "]: Categoria (texto), Precio (dec)", prompt);
        Assert.Contains("un solo nivel", prompt);
        Assert.Contains("abrio el asistente desde la tabla 'Items'", prompt);
        // Consumo registrado en AiUsageLog con la fuente del asistente.
        Assert.Single(f.Usage.Recorded);
        Assert.Equal(DerivedTableChatService.UsageSource, f.Usage.Recorded[0].Source);
        Assert.Equal(10, r.TurnInputTokens);
        Assert.Equal(5, r.TurnOutputTokens);
    }

    [Fact]
    public async Task Flujo_propuesta_confirmacion_tabla_creada()
    {
        var f = NewFixture();
        // Turno 1: el modelo valida con proponer_derivacion (lectura, corre sola) y resume con pregunta.
        f.Ai.Enqueue(new AiCompletion(true, null, null, 0, 0, new[] { new AiToolCall("p1", "proponer_derivacion", Recipe()) }));
        f.Ai.Enqueue(new AiCompletion(true, "Saldria 1 grupo con Cantidad y Total precio. La creo como 'Items por categoria'?", null, 0, 0, Array.Empty<AiToolCall>()));
        // Turno 2: el usuario dice que si; el modelo pide crear (mutante -> propuesta).
        f.Ai.Enqueue(new AiCompletion(true, "Creo la tabla.", null, 0, 0, new[] { new AiToolCall("c1", "crear_tabla_derivada", Recipe("Items por categoria")) }));
        // Turno 3 (tras confirmar): cierre.
        f.Ai.Enqueue(new AiCompletion(true, "Listo: 'Items por categoria' quedo creada.", null, 0, 0, Array.Empty<AiToolCall>()));

        var start = await f.Chat.StartAsync(ModelId, f.ItemsId, Actor);
        var r1 = await f.Chat.SendAsync(start.ConversationId, "items por categoria con total de precio", Actor);

        Assert.True(r1.Ok);
        Assert.False(r1.AwaitingConfirmation);
        Assert.Equal(1, f.Derived.Previews);                 // la lectura corrio sin gate
        Assert.NotNull(r1.Preview);                          // y la UI recibe la vista previa tipada
        Assert.Equal(1, r1.Preview!.TotalGroups);
        Assert.Equal("Tornillos", r1.Preview.Rows[0]["Categoria"]);
        Assert.Contains("La creo", r1.AssistantText);
        Assert.Null(r1.Created);

        var r2 = await f.Chat.SendAsync(start.ConversationId, "si, creala", Actor);
        Assert.True(r2.Ok);
        Assert.True(r2.AwaitingConfirmation);                // GATE humano
        Assert.Single(r2.Proposals);
        Assert.Equal("crear_tabla_derivada", r2.Proposals[0].ToolName);
        Assert.Null(f.Derived.LastCreate);                   // NO se creo nada todavia
        Assert.Null(r2.Created);

        var r3 = await f.Chat.ConfirmAsync(start.ConversationId, Actor);
        Assert.True(r3.Ok);
        Assert.False(r3.AwaitingConfirmation);
        Assert.NotNull(f.Derived.LastCreate);                // ahora si: alta por el servicio del wizard
        Assert.Equal("Items por categoria", f.Derived.LastCreate!.Name);
        Assert.Equal(ModelId, f.Derived.LastCreate.ModelId);
        Assert.Equal(f.ItemsId, f.Derived.LastCreate.Spec.SourceTableId);
        Assert.Equal(2, f.Derived.LastCreate.Spec.Aggregates.Count); // Count + Suma
        Assert.NotNull(r3.Created);                          // la UI dispara el primer refresco con esto
        Assert.Equal(f.Derived.Created!.ProcessId, r3.Created!.ProcessId);
        Assert.Equal("Items por categoria", r3.CreatedName);
        Assert.Contains("quedo creada", r3.AssistantText);

        // El hilo que viaja al proveedor tras confirmar incluye la llamada confirmada y su resultado.
        var last = f.Ai.Threads[^1];
        Assert.Contains(last, m => m.Role == "tool" && m.ToolName == "crear_tabla_derivada" && (m.Text ?? "").Contains("tabla_id"));
    }

    [Fact]
    public async Task Rechazar_no_crea_y_el_modelo_recibe_el_motivo()
    {
        var f = NewFixture();
        f.Ai.Enqueue(new AiCompletion(true, "Creo la tabla.", null, 0, 0, new[] { new AiToolCall("c1", "crear_tabla_derivada", Recipe("X")) }));
        f.Ai.Enqueue(new AiCompletion(true, "De acuerdo, que ajusto?", null, 0, 0, Array.Empty<AiToolCall>()));

        var start = await f.Chat.StartAsync(ModelId, null, Actor);
        var r1 = await f.Chat.SendAsync(start.ConversationId, "creala", Actor);
        Assert.True(r1.AwaitingConfirmation);

        var r2 = await f.Chat.RejectAsync(start.ConversationId, "quiero agrupar tambien por marca", Actor);
        Assert.True(r2.Ok);
        Assert.False(r2.AwaitingConfirmation);
        Assert.Null(f.Derived.LastCreate);
        Assert.Null(r2.Created);
        var last = f.Ai.Threads[^1];
        Assert.Contains(last, m => m.Role == "user" && (m.Text ?? "").Contains("quiero agrupar tambien por marca"));
        // La propuesta rechazada NO viaja como ejecutada.
        Assert.DoesNotContain(last, m => m.Role == "tool");
    }

    [Fact]
    public async Task Escribir_con_propuesta_pendiente_la_descarta_sin_crear()
    {
        var f = NewFixture();
        f.Ai.Enqueue(new AiCompletion(true, "Creo la tabla.", null, 0, 0, new[] { new AiToolCall("c1", "crear_tabla_derivada", Recipe("X")) }));
        f.Ai.Enqueue(new AiCompletion(true, "Entendido, la dejamos asi.", null, 0, 0, Array.Empty<AiToolCall>()));

        var start = await f.Chat.StartAsync(ModelId, null, Actor);
        await f.Chat.SendAsync(start.ConversationId, "creala", Actor);
        var r2 = await f.Chat.SendAsync(start.ConversationId, "mejor no, dejalo", Actor);

        Assert.True(r2.Ok);
        Assert.False(r2.AwaitingConfirmation);
        Assert.Null(f.Derived.LastCreate);
        var confirmar = await f.Chat.ConfirmAsync(start.ConversationId, Actor);
        Assert.False(confirmar.Ok);                          // ya no hay nada pendiente
    }

    [Fact]
    public async Task El_contenedor_id_se_fuerza_aunque_el_modelo_mande_otro()
    {
        var f = NewFixture();
        var otro = Guid.NewGuid();
        var args = JsonSerializer.Serialize(new { contenedor_id = otro, tabla = "Items" });
        f.Ai.Enqueue(new AiCompletion(true, null, null, 0, 0, new[] { new AiToolCall("d1", "describir_tabla", args) }));
        f.Ai.Enqueue(new AiCompletion(true, "Items tiene Categoria y Precio.", null, 0, 0, Array.Empty<AiToolCall>()));

        var start = await f.Chat.StartAsync(ModelId, null, Actor);
        var r = await f.Chat.SendAsync(start.ConversationId, "describe items", Actor);

        Assert.True(r.Ok);
        // La lectura se ejecuto contra el contenedor REAL (si hubiera usado el id falso, "no existe o no tiene tablas").
        var last = f.Ai.Threads[^1];
        var toolMsg = last.Single(m => m.Role == "tool");
        Assert.Contains("\"ok\":true", toolMsg.Text);
        Assert.Contains("Categoria", toolMsg.Text);
        var call = last.Single(m => m.Role == "assistant" && m.ToolCalls is { Count: > 0 }).ToolCalls![0];
        Assert.Contains(ModelId.ToString("D"), call.ArgumentsJson);
        Assert.DoesNotContain(otro.ToString("D"), call.ArgumentsJson);
    }

    // Hallado con gemini-2.5-pro (API compatible OpenAI): devuelve "ecorex.proponer_derivacion". Sin normalizar, la
    // lectura caia al gate como mutante y al confirmar fallaba por "herramienta desconocida".
    [Fact]
    public async Task Nombre_de_herramienta_con_prefijo_inventado_se_normaliza_y_la_lectura_corre_sola()
    {
        var f = NewFixture();
        f.Ai.Enqueue(new AiCompletion(true, null, null, 0, 0, new[] { new AiToolCall("p1", "ecorex.proponer_derivacion", Recipe()) }));
        f.Ai.Enqueue(new AiCompletion(true, "Saldria 1 grupo. La creo?", null, 0, 0, Array.Empty<AiToolCall>()));

        var start = await f.Chat.StartAsync(ModelId, f.ItemsId, Actor);
        var r = await f.Chat.SendAsync(start.ConversationId, "items por categoria", Actor);

        Assert.True(r.Ok);
        Assert.False(r.AwaitingConfirmation);   // lectura: sin gate
        Assert.Equal(1, f.Derived.Previews);
        Assert.NotNull(r.Preview);
        var call = f.Ai.Threads[^1].Single(m => m.Role == "assistant" && m.ToolCalls is { Count: > 0 }).ToolCalls![0];
        Assert.Equal("proponer_derivacion", call.Name); // al proveedor vuelve el nombre canonico
    }

    [Fact]
    public void ForceModelId_pisa_o_agrega_el_contenedor_id()
    {
        var id = Guid.NewGuid();
        var a = DerivedTableChatService.ForceModelId("{\"tabla\":\"Items\"}", id);
        Assert.Contains(id.ToString("D"), a);
        Assert.Contains("\"tabla\":\"Items\"", a);
        var b = DerivedTableChatService.ForceModelId("{\"contenedor_id\":\"x\"}", id);
        Assert.Contains(id.ToString("D"), b);
        Assert.DoesNotContain("\"x\"", b);
        var c = DerivedTableChatService.ForceModelId("no json", id);
        Assert.Contains(id.ToString("D"), c);
    }
}
