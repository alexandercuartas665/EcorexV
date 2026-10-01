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
        Assert.Contains(toolset.Executed, x => x.Tool == "get_form");
        // Al cerrar (texto sin herramientas, sin pregunta) el SISTEMA corre verify_form obligatorio; salio limpio.
        Assert.Contains(toolset.Executed, x => x.Tool == "verify_form");
        Assert.Contains("Datos del cliente", r.AssistantText);
    }

    [Fact]
    public async Task Cierre_con_errores_el_sistema_fuerza_verify_y_reinyecta_para_corregir()
    {
        var ai = new FakeAi();
        // 1) el agente CIERRA en texto (sin pregunta, sin tools) creyendo que termino.
        ai.Enqueue(new AiCompletion(true, "El formulario esta listo y verificado.", null, 0, 0, Array.Empty<AiToolCall>()));
        // 2) tras el verify_form FORZADO por el sistema (que hallo 1 error) y la reinyeccion, propone la correccion.
        ai.Enqueue(new AiCompletion(true, "Corrijo el rollup: creo el campo gran_total.", null, 0, 0,
            new[] { new AiToolCall("f1", "add_question", "{\"field_code\":\"gran_total\"}") }));
        var toolset = new FakeToolset { VerifyErrors = 1 };
        var svc = NewService(ai, toolset, out _);

        var start = await svc.StartAsync(FormId, Guid.NewGuid());
        var r = await svc.SendAsync(start.ConversationId, "listo, ciérralo", null, Guid.NewGuid());

        // No dejo cerrar el formulario roto: forzo verify, reinyecto el error y el agente propuso la correccion.
        Assert.True(r.Ok);
        Assert.True(r.AwaitingConfirmation);
        Assert.Single(r.Proposals);
        Assert.Equal("add_question", r.Proposals[0].ToolName);
        Assert.Contains(toolset.Executed, x => x.Tool == "verify_form"); // lo corrio el SISTEMA, no el modelo
    }

    [Fact]
    public async Task Cierre_sin_errores_el_verify_forzado_pasa_y_cierra()
    {
        var ai = new FakeAi();
        ai.Enqueue(new AiCompletion(true, "El formulario esta listo.", null, 0, 0, Array.Empty<AiToolCall>()));
        var toolset = new FakeToolset { VerifyErrors = 0 };
        var svc = NewService(ai, toolset, out _);

        var start = await svc.StartAsync(FormId, Guid.NewGuid());
        var r = await svc.SendAsync(start.ConversationId, "listo", null, Guid.NewGuid());

        Assert.True(r.Ok);
        Assert.False(r.AwaitingConfirmation);            // verify obligatorio corrio y salio limpio -> cierra
        Assert.Empty(r.Proposals);
        Assert.Contains(toolset.Executed, x => x.Tool == "verify_form");
    }

    // ESCALA/ROBUSTEZ: una peticion absurda hace que la llamada al modelo tarde o no vuelva. La llamada esta
    // acotada (HttpClient 60s + CTS de 90s por llamada); cuando vence, el servicio NO cuelga ni corrompe: captura
    // la cancelacion (sin que el ct externo del circuito este cancelado) y devuelve un error AMABLE y accionable.
    [Fact]
    public async Task Si_la_llamada_al_modelo_expira_devuelve_error_amable_sin_ejecutar_nada()
    {
        var ai = new FakeAi();
        // Simula el timeout de la llamada al proveedor: la tarea se cancela (como el HttpClient a los 60s),
        // con el ct EXTERNO (el del circuito) SIN cancelar -> debe caer en el catch amable.
        ai.EnqueueThrow(new OperationCanceledException());
        var toolset = new FakeToolset();
        var svc = NewService(ai, toolset, out _);

        var start = await svc.StartAsync(FormId, Guid.NewGuid());
        var r = await svc.SendAsync(start.ConversationId, "crea 500 campos en 50 tablas ya", null, Guid.NewGuid());

        Assert.False(r.Ok);
        Assert.Contains("tardo demasiado", r.Error);   // mensaje amable, no una excepcion cruda
        Assert.Empty(toolset.Executed);                // GATE: no se ejecuto nada
    }

    // ESCALA/ROBUSTEZ: si el modelo se enreda pidiendo lecturas una y otra vez (sin cerrar ni proponer), el bucle
    // NO gira infinito: MaxRounds lo corta y devuelve un error claro pidiendo precisar la instruccion.
    [Fact]
    public async Task Demasiadas_lecturas_seguidas_cortan_el_bucle_con_error_claro()
    {
        var ai = new FakeAi();
        // 10 turnos que solo piden una lectura (get_form es read-only) y nunca cierran ni proponen: supera MaxRounds.
        for (var i = 0; i < 10; i++)
        {
            ai.Enqueue(new AiCompletion(true, null, null, 0, 0,
                new[] { new AiToolCall("r" + i, "get_form", "{}") }));
        }
        var toolset = new FakeToolset();
        var svc = NewService(ai, toolset, out _);

        var start = await svc.StartAsync(FormId, Guid.NewGuid());
        var r = await svc.SendAsync(start.ConversationId, "revisa y revisa", null, Guid.NewGuid());

        Assert.False(r.Ok);
        Assert.Contains("demasiadas consultas", r.Error);
        Assert.False(r.AwaitingConfirmation);          // no quedo nada esperando confirmacion
    }

    // ROBUSTEZ (hallado construyendo el Formulario 350 DIAN): cuando el modelo propone add_container + add_question
    // en el MISMO lote, el add_question referencia un container_id ADIVINADO (el id real se asigna al ejecutar) y
    // fallaba con "El contenedor no pertenece al formulario", perdiendo el campo. ConfirmAsync ahora autocura: tras
    // crear el contenedor en el lote, reintenta el add_question fallido apuntando al id real.
    [Fact]
    public async Task Lote_con_contenedor_y_campo_juntos_autocura_el_container_id_adivinado()
    {
        var ai = new FakeAi();
        const string guessed = "00000000-0000-0000-0000-0000000000aa"; // id inventado por el modelo (no existe)
        ai.Enqueue(new AiCompletion(true, "Creo la seccion y su primer campo.", null, 0, 0,
            new[]
            {
                new AiToolCall("c1", "add_container", "{\"container_type\":\"Section\",\"name\":\"Datos\"}"),
                new AiToolCall("q1", "add_question", "{\"label\":\"1. Año\",\"control_type\":\"Number\",\"container_id\":\"" + guessed + "\"}"),
            }));
        ai.Enqueue(new AiCompletion(true, "Listo, cree la seccion y el campo.", null, 0, 0, Array.Empty<AiToolCall>()));
        var toolset = new FakeToolset();
        var svc = NewService(ai, toolset, out _);

        var start = await svc.StartAsync(FormId, Guid.NewGuid());
        await svc.SendAsync(start.ConversationId, "crea la seccion con su campo", null, Guid.NewGuid());
        var r = await svc.ConfirmAsync(start.ConversationId, Guid.NewGuid());

        Assert.True(r.Ok);
        var addQ = toolset.Executed.Where(x => x.Tool == "add_question").ToList();
        Assert.Equal(2, addQ.Count);                                            // 1) fallo con id adivinado, 2) reintento
        Assert.Contains(guessed, addQ[0].Args);                                 // primer intento: id adivinado
        Assert.Contains(toolset.LastContainerId!.Value.ToString(), addQ[1].Args); // reintento: id REAL del contenedor
        Assert.DoesNotContain(guessed, addQ[1].Args);
    }

    [Fact]
    public async Task Escribir_con_propuesta_pendiente_la_descarta_y_atiende_el_mensaje()
    {
        var ai = new FakeAi();
        // Turno 1: el agente propone una mutacion (queda PENDIENTE, sin confirmar).
        ai.Enqueue(new AiCompletion(true, "Voy a crear la seccion.", null, 0, 0,
            new[] { new AiToolCall("c1", "add_container", "{\"type\":\"Section\"}") }));
        // Turno 2: el usuario ESCRIBE en vez de confirmar -> se descarta lo pendiente y el agente atiende el mensaje.
        ai.Enqueue(new AiCompletion(true, "De acuerdo, lo dejo asi.", null, 0, 0, Array.Empty<AiToolCall>()));
        var toolset = new FakeToolset();
        var svc = NewService(ai, toolset, out var store);

        var start = await svc.StartAsync(FormId, Guid.NewGuid());
        var r1 = await svc.SendAsync(start.ConversationId, "crea algo", null, Guid.NewGuid());
        Assert.True(r1.AwaitingConfirmation); // quedo una propuesta pendiente

        var r2 = await svc.SendAsync(start.ConversationId, "ya asi esta bien, no agregues mas", null, Guid.NewGuid());

        Assert.True(r2.Ok);
        Assert.False(r2.AwaitingConfirmation);
        var msgs = await store.GetMessagesAsync(start.ConversationId);
        // La propuesta pendiente quedo DESCARTADA (Rejected) y NO se ejecuto.
        Assert.Contains(msgs, m => m.Role == FormBuilderMessageRole.Proposal && m.ToolName == "add_container"
            && m.ProposalState == FormBuilderProposalState.Rejected);
        Assert.DoesNotContain(msgs, m => m.ProposalState == FormBuilderProposalState.Pending);
        Assert.DoesNotContain(toolset.Executed, x => x.Tool == "add_container");
    }

    [Fact]
    public async Task No_fuerza_verify_cuando_el_cierre_es_una_pregunta()
    {
        var ai = new FakeAi();
        // Texto-solo que TERMINA en pregunta: espera al usuario, no es un cierre -> no se fuerza verify.
        ai.Enqueue(new AiCompletion(true, "Ya agregue los campos. Quieres que agregue el boton de imprimir?", null, 0, 0, Array.Empty<AiToolCall>()));
        var toolset = new FakeToolset { VerifyErrors = 1 };
        var svc = NewService(ai, toolset, out _);

        var start = await svc.StartAsync(FormId, Guid.NewGuid());
        var r = await svc.SendAsync(start.ConversationId, "sigue", null, Guid.NewGuid());

        Assert.True(r.Ok);
        Assert.False(r.AwaitingConfirmation);
        Assert.DoesNotContain(toolset.Executed, x => x.Tool == "verify_form"); // era pregunta, no cierre
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

    [Fact]
    public async Task Narra_sin_emitir_tools_el_servicio_lo_empuja_a_ejecutar()
    {
        var ai = new FakeAi();
        // 1) El agente SOLO narra una accion futura, sin tool-calls (el bug: se quedaba aqui).
        ai.Enqueue(new AiCompletion(true, "Entendido. Voy a crear la seccion Datos del cliente. A continuacion agregare los campos.",
            null, 0, 0, Array.Empty<AiToolCall>()));
        // 2) Tras el empujon, ahora si emite la llamada.
        ai.Enqueue(new AiCompletion(true, "Creando la seccion.", null, 0, 0,
            new[] { new AiToolCall("c1", "add_container", "{\"type\":\"Section\"}") }));
        var toolset = new FakeToolset();
        var svc = NewService(ai, toolset, out _);

        var start = await svc.StartAsync(FormId, Guid.NewGuid());
        var r = await svc.SendAsync(start.ConversationId, "arma el formulario completo", null, Guid.NewGuid());

        // El empujon automatico hizo que el agente EMITA la propuesta en vez de terminar solo con la narracion.
        Assert.True(r.Ok);
        Assert.True(r.AwaitingConfirmation);
        Assert.Single(r.Proposals);
        Assert.Equal("add_container", r.Proposals[0].ToolName);
    }

    [Fact]
    public async Task No_empuja_cuando_el_agente_hace_una_pregunta()
    {
        var ai = new FakeAi();
        // Texto-solo que es una PREGUNTA -> debe esperar al usuario, NO auto-empujar.
        ai.Enqueue(new AiCompletion(true, "Antes de construir: la Prioridad es una lista fija o sale de una fuente?",
            null, 0, 0, Array.Empty<AiToolCall>()));
        var toolset = new FakeToolset();
        var svc = NewService(ai, toolset, out _);

        var start = await svc.StartAsync(FormId, Guid.NewGuid());
        var r = await svc.SendAsync(start.ConversationId, "arma el formulario", null, Guid.NewGuid());

        Assert.True(r.Ok);
        Assert.False(r.AwaitingConfirmation);
        // Si hubiera empujado, habria consumido la respuesta de reserva; que quede la PREGUNTA confirma que NO empujo.
        Assert.Contains("una lista fija o sale de una fuente", r.AssistantText);
        Assert.Empty(toolset.Executed);
    }

    private static IFormBuilderChatService NewService(FakeAi ai, FakeToolset toolset, out FakeStore store)
    {
        store = new FakeStore();
        return new FormBuilderChatService(new IdentitySecrets(), ai, toolset, store, new FakeSnapshots(), new FakeUsage());
    }

    // Telemetria de consumo: no-op en las pruebas.
    private sealed class FakeUsage : Ecorex.Application.Tenancy.IAiUsageService
    {
        public Task RecordAsync(Guid? agentId, AiProvider provider, string model, int inputTokens, int outputTokens, string source, bool success, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task<Ecorex.Application.Tenancy.AiUsageSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new Ecorex.Application.Tenancy.AiUsageSummaryDto(0, 0, 0, 0, 0m, System.Array.Empty<Ecorex.Application.Tenancy.AgentUsageDto>()));
        public Task<Ecorex.Application.Tenancy.AiQuotaDto> GetQuotaAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new Ecorex.Application.Tenancy.AiQuotaDto(0, 0, false));
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
        // Cada turno del modelo es una FUNCION: normalmente devuelve una AiCompletion, pero puede LANZAR
        // (para simular un timeout/cancelacion de la llamada al proveedor).
        private readonly Queue<Func<AiCompletion>> _queue = new();
        public void Enqueue(AiCompletion c) => _queue.Enqueue(() => c);
        public void EnqueueThrow(Exception ex) => _queue.Enqueue(() => throw ex);

        public Task<AiCompletion> CompleteWithToolsAsync(AiProvider provider, string apiKey, string? baseUrl, string model,
            string systemPrompt, IReadOnlyList<AiToolMessage> messages, IReadOnlyList<AiToolSpec> tools, CancellationToken cancellationToken = default)
        {
            if (_queue.Count == 0)
            {
                return Task.FromResult(new AiCompletion(true, "(sin mas respuestas)", null, 0, 0, Array.Empty<AiToolCall>()));
            }
            var next = _queue.Dequeue();
            try { return Task.FromResult(next()); }
            catch (Exception ex) { return Task.FromException<AiCompletion>(ex); }
        }

        public Task<AiChatResult> CompleteAsync(AiProvider provider, string apiKey, string? baseUrl, string model, string systemPrompt, IReadOnlyList<AiChatTurn> turns, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<AiChatResult> CompleteVisionAsync(AiProvider provider, string apiKey, string? baseUrl, string model, string systemPrompt, IReadOnlyList<AiVisionPart> content, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeToolset : IFormAuthoringToolset
    {
        public List<(string Tool, string Args)> Executed { get; } = new();
        // Cuantos errores devuelve verify_form (para probar el cierre forzado). 0 = formulario coherente.
        public int VerifyErrors { get; set; }
        public string GroupKey => "form-authoring";
        public string GroupLabel => "Autoria de formularios";
        public IReadOnlySet<string> ReadOnlyTools { get; } = new HashSet<string>(StringComparer.Ordinal) { "get_form", "verify_form", "describe_components", "list_data_containers" };
        public IReadOnlyList<AiToolSpec> GetSpecs() => new[]
        {
            new AiToolSpec("get_form", "lee", "{}"),
            new AiToolSpec("verify_form", "auto-verifica", "{}"),
            new AiToolSpec("add_container", "crea seccion", "{}"),
            new AiToolSpec("add_question", "crea campo", "{}"),
            new AiToolSpec("create_form", "crea formulario", "{}"),
        };
        // Id real del ultimo contenedor creado (para validar que los add_question apunten a el).
        public Guid? LastContainerId { get; private set; }
        public Task<AgentToolResult> ExecuteAsync(string toolName, string argumentsJson, Guid actorUserId, bool autonomous, CancellationToken cancellationToken = default)
        {
            Executed.Add((toolName, argumentsJson));
            string json;
            if (string.Equals(toolName, "create_form", StringComparison.Ordinal))
            {
                json = JsonSerializer.Serialize(new { id = Guid.NewGuid().ToString() });
            }
            else if (string.Equals(toolName, "add_container", StringComparison.Ordinal))
            {
                // Crea el contenedor y devuelve su id REAL (asignado al ejecutar, como en produccion).
                var id = Guid.NewGuid();
                LastContainerId = id;
                json = JsonSerializer.Serialize(new { ok = true, container = new { id = id.ToString() } });
            }
            else if (string.Equals(toolName, "add_question", StringComparison.Ordinal))
            {
                // Si trae container_id, DEBE coincidir con un contenedor real; si no, "no pertenece al formulario".
                string? cid = null;
                try
                {
                    using var d = JsonDocument.Parse(argumentsJson);
                    if (d.RootElement.ValueKind == JsonValueKind.Object
                        && d.RootElement.TryGetProperty("container_id", out var c) && c.ValueKind == JsonValueKind.String)
                    {
                        cid = c.GetString();
                    }
                }
                catch { /* args invalidos -> se trata como sin container */ }
                json = (string.IsNullOrWhiteSpace(cid) || cid == LastContainerId?.ToString())
                    ? JsonSerializer.Serialize(new { ok = true })
                    : JsonSerializer.Serialize(new { ok = false, status = "Invalid", error = "El contenedor no pertenece al formulario." });
            }
            else if (string.Equals(toolName, "verify_form", StringComparison.Ordinal))
            {
                json = VerifyErrors > 0
                    ? JsonSerializer.Serialize(new
                    {
                        ok = false,
                        errors = VerifyErrors,
                        warnings = 0,
                        issues = new[] { new { severity = "error", where = "tabla 'items', columna 'total_item'", problem = "su rollup apunta a 'gran_total', que no existe como campo", fix = "crea el campo destino o corrige el rollup" } }
                    })
                    : JsonSerializer.Serialize(new { ok = true, errors = 0, warnings = 0, issues = System.Array.Empty<object>() });
            }
            else
            {
                json = JsonSerializer.Serialize(new { ok = true });
            }
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

        // Ninguno marcado -> el servicio cae al proveedor por defecto (Gemini), que ResolveProviderAsync habilita.
        public Task<AiProvider?> GetFormBuilderProviderAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<AiProvider?>(null);

        public Task<string?> GetFormTitleAsync(Guid formDefinitionId, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>("Formulario de prueba");

        public Task<string> GetTenantNameAsync(Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult("AGRO TEST");
    }
}
