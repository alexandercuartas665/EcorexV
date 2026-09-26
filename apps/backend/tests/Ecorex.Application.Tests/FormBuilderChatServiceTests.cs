using System.Text;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Forms;
using Ecorex.Application.Forms.Builder;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Pruebas deterministas del NUCLEO del asistente de creacion de formularios: el GATE humano (las
/// acciones mutantes se proponen y NO se ejecutan hasta confirmar), la auto-ejecucion de lecturas, la
/// continuacion tras confirmar y la ingesta de un adjunto tabular (CSV). Todo con fakes; sin BD ni IA real.
/// </summary>
public class FormBuilderChatServiceTests
{
    private static readonly Guid FormId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Turno_con_accion_mutante_se_propone_y_no_se_ejecuta()
    {
        var ai = new FakeAi();
        // El modelo pide crear una seccion (mutante).
        ai.Enqueue(new AiCompletion(true, "Voy a crear la seccion Datos del cliente.", null, 0, 0,
            new[] { new AiToolCall("c1", "add_container", "{\"type\":\"Section\"}") }));
        var toolset = new FakeToolset();
        var svc = NewService(ai, toolset, out _);

        var start = await svc.StartAsync(FormId, Guid.NewGuid());
        Assert.True(start.Ok);
        var r = await svc.SendAsync(start.ConversationId, "crea la seccion del cliente", null, Guid.NewGuid());

        Assert.True(r.Ok);
        Assert.True(r.AwaitingConfirmation);
        Assert.Single(r.Proposals);
        Assert.Equal("add_container", r.Proposals[0].ToolName);
        Assert.Empty(toolset.Executed); // GATE: no se ejecuto nada aun
    }

    [Fact]
    public async Task Confirmar_ejecuta_la_herramienta_y_continua_al_texto_final()
    {
        var ai = new FakeAi();
        ai.Enqueue(new AiCompletion(true, "Voy a crear la seccion.", null, 0, 0,
            new[] { new AiToolCall("c1", "add_container", "{\"type\":\"Section\"}") }));
        ai.Enqueue(new AiCompletion(true, "Listo, cree la seccion. Seguimos con los campos?", null, 0, 0,
            Array.Empty<AiToolCall>()));
        var toolset = new FakeToolset();
        var svc = NewService(ai, toolset, out _);

        var start = await svc.StartAsync(FormId, Guid.NewGuid());
        await svc.SendAsync(start.ConversationId, "crea la seccion", null, Guid.NewGuid());
        var r = await svc.ConfirmAsync(start.ConversationId, Guid.NewGuid());

        Assert.True(r.Ok);
        Assert.False(r.AwaitingConfirmation);
        Assert.Equal("Listo, cree la seccion. Seguimos con los campos?", r.AssistantText);
        Assert.Single(toolset.Executed);
        Assert.Equal("add_container", toolset.Executed[0].Tool);
    }

    [Fact]
    public async Task Turno_de_solo_lectura_se_ejecuta_sin_gate()
    {
        var ai = new FakeAi();
        // Lectura (get_form es read-only) y luego texto final: no debe pedir confirmacion.
        ai.Enqueue(new AiCompletion(true, null, null, 0, 0,
            new[] { new AiToolCall("r1", "get_form", "{}") }));
        ai.Enqueue(new AiCompletion(true, "El formulario esta vacio; propongo empezar por Datos del cliente.", null, 0, 0,
            Array.Empty<AiToolCall>()));
        var toolset = new FakeToolset();
        var svc = NewService(ai, toolset, out _);

        var start = await svc.StartAsync(FormId, Guid.NewGuid());
        var r = await svc.SendAsync(start.ConversationId, "revisa el formulario", null, Guid.NewGuid());

        Assert.True(r.Ok);
        Assert.False(r.AwaitingConfirmation);
        Assert.Single(toolset.Executed);
        Assert.Equal("get_form", toolset.Executed[0].Tool);
        Assert.Contains("Datos del cliente", r.AssistantText);
    }

    [Fact]
    public async Task Adjunto_CSV_se_ingiere_en_el_mensaje_del_usuario()
    {
        var ai = new FakeAi();
        ai.Enqueue(new AiCompletion(true, "Recibido. Veo columnas nombre, nit y telefono.", null, 0, 0,
            Array.Empty<AiToolCall>()));
        var toolset = new FakeToolset();
        var svc = NewService(ai, toolset, out var store);

        var csv = "nombre,nit,telefono\nJuan,900,3001112222\n";
        var att = new FormBuilderAttachment("clientes.csv", "text/csv", Convert.ToBase64String(Encoding.UTF8.GetBytes(csv)));

        var start = await svc.StartAsync(FormId, Guid.NewGuid());
        await svc.SendAsync(start.ConversationId, "arma un formulario con esto", new[] { att }, Guid.NewGuid());

        var msgs = await store.GetMessagesAsync(start.ConversationId);
        var userMsg = msgs.First(m => m.Role == FormBuilderMessageRole.User);
        Assert.Contains("clientes.csv", userMsg.Content);
        Assert.Contains("nombre", userMsg.Content);
        Assert.Contains("nit", userMsg.Content);
    }

    private static IFormBuilderChatService NewService(FakeAi ai, FakeToolset toolset, out FakeStore store)
    {
        store = new FakeStore();
        return new FormBuilderChatService(new IdentitySecrets(), ai, toolset, store, new FakeSnapshots());
    }

    // ===== Fakes =====

    private sealed class IdentitySecrets : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string ciphertext) => ciphertext;
    }

    // Auto-snapshot: no-op en las pruebas (no persiste; el confirm no debe depender de el).
    private sealed class FakeSnapshots : IFormSnapshotService
    {
        public Task<Guid?> SnapshotAsync(Guid formDefinitionId, string label, FormSnapshotTrigger trigger,
            Guid? conversationId, Guid? actorTenantUserId, CancellationToken cancellationToken = default)
            => Task.FromResult<Guid?>(null);
        public Task<IReadOnlyList<FormSnapshotItemDto>> ListAsync(Guid formDefinitionId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<FormSnapshotItemDto>>(System.Array.Empty<FormSnapshotItemDto>());
        public Task<FormResult<bool>> RestoreAsync(Guid snapshotId, Guid? actorTenantUserId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeAi : IAiProviderClient
    {
        private readonly Queue<AiCompletion> _queue = new();
        public void Enqueue(AiCompletion c) => _queue.Enqueue(c);

        public Task<AiCompletion> CompleteWithToolsAsync(AiProvider provider, string apiKey, string? baseUrl, string model,
            string systemPrompt, IReadOnlyList<AiToolMessage> messages, IReadOnlyList<AiToolSpec> tools, CancellationToken cancellationToken = default)
            => Task.FromResult(_queue.Count > 0 ? _queue.Dequeue()
                : new AiCompletion(true, "(sin mas respuestas)", null, 0, 0, Array.Empty<AiToolCall>()));

        public Task<AiChatResult> CompleteAsync(AiProvider provider, string apiKey, string? baseUrl, string model, string systemPrompt, IReadOnlyList<AiChatTurn> turns, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<AiChatResult> CompleteVisionAsync(AiProvider provider, string apiKey, string? baseUrl, string model, string systemPrompt, IReadOnlyList<AiVisionPart> content, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeToolset : IFormAuthoringToolset
    {
        public List<(string Tool, string Args)> Executed { get; } = new();
        public string GroupKey => "form-authoring";
        public string GroupLabel => "Autoria de formularios";
        public IReadOnlySet<string> ReadOnlyTools { get; } = new HashSet<string>(StringComparer.Ordinal) { "get_form", "describe_components", "list_data_containers" };
        public IReadOnlyList<AiToolSpec> GetSpecs() => new[]
        {
            new AiToolSpec("get_form", "lee", "{}"),
            new AiToolSpec("add_container", "crea seccion", "{}"),
            new AiToolSpec("create_form", "crea formulario", "{}"),
        };
        public Task<AgentToolResult> ExecuteAsync(string toolName, string argumentsJson, Guid actorUserId, bool autonomous, CancellationToken cancellationToken = default)
        {
            Executed.Add((toolName, argumentsJson));
            var json = string.Equals(toolName, "create_form", StringComparison.Ordinal)
                ? JsonSerializer.Serialize(new { id = Guid.NewGuid().ToString() })
                : JsonSerializer.Serialize(new { ok = true });
            return Task.FromResult(new AgentToolResult(json, false));
        }
    }

    private sealed class FakeStore : IFormBuilderChatStore
    {
        private readonly Dictionary<Guid, FormBuilderConversation> _convs = new();
        private readonly List<FormBuilderMessage> _msgs = new();

        public Task<FormBuilderConversation> CreateConversationAsync(Guid? formDefinitionId, AiProvider provider, string? model, string title, Guid? startedByTenantUserId, CancellationToken cancellationToken = default)
        {
            var c = new FormBuilderConversation
            {
                Id = Guid.NewGuid(),
                TenantId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                FormDefinitionId = formDefinitionId,
                Provider = provider,
                Model = model,
                Title = title,
                StartedByTenantUserId = startedByTenantUserId,
                Status = FormBuilderConversationStatus.Active,
            };
            _convs[c.Id] = c;
            return Task.FromResult(c);
        }

        public Task<FormBuilderConversation?> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken = default)
            => Task.FromResult(_convs.TryGetValue(conversationId, out var c) ? c : null);

        public Task SaveConversationAsync(FormBuilderConversation conversation, CancellationToken cancellationToken = default)
        {
            _convs[conversation.Id] = conversation;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FormBuilderMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<FormBuilderMessage>>(_msgs.Where(m => m.ConversationId == conversationId).OrderBy(m => m.Sequence).ToList());

        public Task<FormBuilderMessage> AddMessageAsync(FormBuilderMessage message, CancellationToken cancellationToken = default)
        {
            if (message.Id == Guid.Empty) { message.Id = Guid.NewGuid(); }
            _msgs.Add(message);
            return Task.FromResult(message);
        }

        public Task SaveMessageAsync(FormBuilderMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<FormBuilderProviderInfo?> ResolveProviderAsync(AiProvider provider, CancellationToken cancellationToken = default)
            => Task.FromResult<FormBuilderProviderInfo?>(new FormBuilderProviderInfo(true, "enc-key", "gemini-2.5-pro", null));

        public Task<string?> GetFormTitleAsync(Guid formDefinitionId, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>("Formulario de prueba");

        public Task<string> GetTenantNameAsync(Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult("AGRO TEST");
    }
}
