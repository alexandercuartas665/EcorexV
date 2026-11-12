using System.Net;
using System.Reflection;
using System.Text.Json;
using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Application.Mcp;
using Ecorex.Application.Roles;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Servidor MCP de WhatsApp por linea Evolution (ADR-0164) en matriz dual PostgreSQL / SQL Server, contra el
/// CONECTOR REAL (resolucion de servidor + instancia ecorex_tenant_linea) con un doble del cliente HTTP de Evolution:
/// - Catalogo consistente (36 herramientas) y toda herramienta se despacha.
/// - Enviar a un chat 1:1 pasa por la capa de chat: queda en Conversaciones a nombre del usuario "(via IA)" y SARA
///   cede el turno (ultimo saliente con autor humano).
/// - Primer contacto exige confirmacion; los envios directos (encuesta) se registran; los de grupo no.
/// - Permisos de conversaciones/lineas, aislamiento entre tenants, pausa del agente, limite de envios, media para la
///   IA, alcances de token separados y guarda anti-SSRF.
/// </summary>
public abstract class McpWhatsAppTestsBase
{
    private const string ClientePhone = "573001112233";
    private readonly TenantIsolationDbFixture _fixture;

    protected McpWhatsAppTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Catalog_IsConsistent_AndEveryToolDispatches()
    {
        var seed = await SeedAsync("Wa Catalogo");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var (toolset, _, _) = Build(ctx, seed);
        var me = Owner(seed);

        var specs = toolset.GetSpecs();
        var names = specs.Select(s => s.Name).ToList();
        Assert.Equal(36, specs.Count);
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        foreach (var s in specs)
        {
            Assert.True((s.Description ?? "").Length >= 25, $"{s.Name}: descripcion corta");
            using var schema = JsonDocument.Parse(s.ParametersJsonSchema);
            Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
            var props = schema.RootElement.TryGetProperty("properties", out var p) ? p : default;
            if (schema.RootElement.TryGetProperty("required", out var req))
            {
                foreach (var r in req.EnumerateArray())
                {
                    Assert.True(props.ValueKind == JsonValueKind.Object && props.TryGetProperty(r.GetString()!, out _), $"{s.Name}: required {r}");
                }
            }
        }
        Assert.All(toolset.ReadOnlyTools, n => Assert.Contains(n, names));
        Assert.All(toolset.DestructiveTools, n => Assert.Contains(n, names));
        // Nada de administracion de la instancia que rompa la integracion (webhook, logout, borrar, crear).
        Assert.DoesNotContain(names, n => n.Contains("webhook") || n.Contains("logout") || n.Contains("eliminar_linea"));

        foreach (var name in names)
        {
            var r = await toolset.ExecuteAsync(name, "{}", me);
            Assert.DoesNotContain("Herramienta desconocida", r.Json);
        }
    }

    [Fact]
    public async Task SendText_ToKnownChat_GoesThroughChat_AsUserViaIA_AndSaraYields()
    {
        var seed = await SeedAsync("Wa Envio");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var (toolset, evo, _) = Build(ctx, seed);
        var me = Owner(seed);

        var sent = Payload(await toolset.ExecuteAsync("enviar_texto", Args(new { para = ClientePhone, texto = "Hola Ana, ya revisamos tu pedido." }), me));
        Assert.True(sent.GetProperty("registrado_en_conversaciones").GetBoolean());
        Assert.Equal("WA-TXT-1", sent.GetProperty("mensaje_id").GetString());

        // El conector real resolvio el servidor del tenant y la instancia de ESTA linea.
        var call = Assert.Single(evo.Calls, c => c.Method == "sendText");
        Assert.Equal($"ecorex_{seed.TenantId:N}_{seed.LineId:N}", call.Instance);

        // Queda en Conversaciones a nombre del usuario "(via IA)" -> SARA cede el turno (ultimo saliente humano).
        var last = await ctx.Messages.AsNoTracking().Where(m => m.ConversationId == seed.ConversationId).OrderByDescending(m => m.SentAt).FirstAsync();
        Assert.Equal(MessageDirection.Outbound, last.Direction);
        Assert.Equal(seed.OwnerUserId, last.SentByTenantUserId);
        Assert.EndsWith("(via IA)", last.SentByName);
        Assert.Equal("WA-TXT-1", last.ExternalId);

        var read = Payload(await toolset.ExecuteAsync("leer_mensajes", Args(new { chat = ClientePhone }), me));
        Assert.Equal(2, read.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task FirstContact_RequiresConfirmation_ThenCreatesConversationOnTheLine()
    {
        var seed = await SeedAsync("Wa Primer Contacto");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var (toolset, evo, _) = Build(ctx, seed);
        var me = Owner(seed);
        const string nuevo = "573209998877";

        var cold = await toolset.ExecuteAsync("enviar_texto", Args(new { para = nuevo, texto = "Hola" }), me);
        Assert.True(cold.IsError);
        Assert.Contains("primer_contacto_confirmado", cold.Json);
        Assert.DoesNotContain(evo.Calls, c => c.Method == "sendText");
        Assert.False(await ctx.Conversations.AnyAsync(c => c.ContactPhone == nuevo));

        Assert.False((await toolset.ExecuteAsync("enviar_texto", Args(new { para = nuevo, texto = "Hola", primer_contacto_confirmado = true }), me)).IsError);
        var conv = await ctx.Conversations.AsNoTracking().SingleAsync(c => c.ContactPhone == nuevo);
        Assert.Equal(seed.LineId, conv.WhatsAppLineId);
        Assert.True(await ctx.Messages.AnyAsync(m => m.ConversationId == conv.Id && m.Direction == MessageDirection.Outbound));
    }

    [Fact]
    public async Task DirectSends_PollIsRecorded_GroupIsNot()
    {
        var seed = await SeedAsync("Wa Encuesta Grupo");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var (toolset, evo, _) = Build(ctx, seed);
        var me = Owner(seed);

        var poll = Payload(await toolset.ExecuteAsync("enviar_encuesta",
            Args(new { para = seed.ConversationId.ToString(), pregunta = "Horario de entrega?", opciones = new[] { "Manana", "Tarde" } }), me));
        Assert.True(poll.GetProperty("registrado_en_conversaciones").GetBoolean());
        var raw = Assert.Single(evo.Calls, c => c.Path.StartsWith("/message/sendPoll/", StringComparison.Ordinal));
        Assert.Contains("Horario de entrega?", raw.Body);
        Assert.True(await ctx.Messages.AnyAsync(m => m.ConversationId == seed.ConversationId && m.Body.StartsWith("[encuesta]")));

        var before = await ctx.Messages.CountAsync();
        var group = Payload(await toolset.ExecuteAsync("enviar_texto", Args(new { para = "120363000000000000@g.us", texto = "Buenos dias equipo" }), me));
        Assert.False(group.GetProperty("registrado_en_conversaciones").GetBoolean());
        Assert.Contains(evo.Calls, c => c.Path.StartsWith("/message/sendText/", StringComparison.Ordinal) && c.Body.Contains("@g.us"));
        Assert.Equal(before, await ctx.Messages.CountAsync());
    }

    [Fact]
    public async Task Permissions_AndTenantIsolation()
    {
        var seed = await SeedAsync("Wa Permisos");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var rol = new Rol { TenantId = seed.TenantId, Name = "Lector de chats" };
        ctx.Roles.Add(rol);
        ctx.RolPermisos.Add(new RolPermiso { TenantId = seed.TenantId, RolId = rol.Id, ModuleKey = "conversaciones", CanView = true });
        var member = await ctx.TenantUsers.SingleAsync(u => u.Id == seed.MemberUserId);
        member.RolId = rol.Id;
        await ctx.SaveChangesAsync();
        var (toolset, _, _) = Build(ctx, seed);
        var reader = new McpActor(seed.TenantId, seed.MemberUserId, seed.MemberPlatformId, "Lector", false, Guid.NewGuid());

        Assert.False((await toolset.ExecuteAsync("leer_mensajes", Args(new { chat = ClientePhone }), reader)).IsError);
        var send = await toolset.ExecuteAsync("enviar_texto", Args(new { para = ClientePhone, texto = "x" }), reader);
        Assert.True(send.IsError);
        Assert.Contains("permiso", send.Json);
        Assert.True((await toolset.ExecuteAsync("perfil_linea", Args(new { accion = "cambiar_nombre", valor = "Otra" }), reader)).IsError);

        // Otro tenant: la linea no existe para el (filtro global), ni por el toolset ni por el conector.
        var other = await SeedAsync("Wa Otro Tenant");
        await using var otherCtx = _fixture.CreateContext(other.TenantId);
        var (otherToolset, _, otherConnector) = Build(otherCtx, other);
        Assert.Null(await otherToolset.DescribeLineAsync(seed.LineId));
        var cross = await otherConnector.EvolutionCallAsync(seed.LineId, "GET", "/instance/connectionState/{instance}", null);
        Assert.False(cross.Ok);
        Assert.Contains("no existe", cross.Error);
        var bound = await otherToolset.ForLine(seed.LineId).ExecuteAsync("leer_mensajes", Args(new { chat = ClientePhone }), Owner(other));
        Assert.True(bound.IsError);
    }

    [Fact]
    public async Task PauseAndResumeAgent_UseTheBlacklist()
    {
        var seed = await SeedAsync("Wa Pausa");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var (toolset, _, _) = Build(ctx, seed);
        var me = Owner(seed);

        Assert.False((await toolset.ExecuteAsync("pausar_agente", Args(new { chat = ClientePhone, motivo = "Lo atiende ventas" }), me)).IsError);
        var row = await ctx.TenantBlockedNumbers.AsNoTracking().SingleAsync();
        Assert.Contains("via IA", row.Note);
        Assert.True(AgentControlCommands.IsBlocked(ClientePhone, new[] { row.Phone }));

        var resumed = Payload(await toolset.ExecuteAsync("reanudar_agente", Args(new { chat = ClientePhone }), me));
        Assert.Equal(1, resumed.GetProperty("entradas_quitadas").GetInt32());
        Assert.False(await ctx.TenantBlockedNumbers.AnyAsync());
    }

    [Fact]
    public async Task SendLimit_BlocksBursts_PerLine()
    {
        var seed = await SeedAsync("Wa Limite");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var (toolset, _, _) = Build(ctx, seed);
        var me = Owner(seed);
        WhatsAppMcpToolset.ResetSendLimit(seed.LineId);

        for (var i = 0; i < WhatsAppMcpToolset.SendsPerMinute; i++)
        {
            var ok = await toolset.ExecuteAsync("enviar_texto", Args(new { para = "120363000000000001@g.us", texto = $"m{i}" }), me);
            Assert.False(ok.IsError, ok.Json);
        }
        var blocked = await toolset.ExecuteAsync("enviar_texto", Args(new { para = "120363000000000001@g.us", texto = "uno mas" }), me);
        Assert.True(blocked.IsError);
        Assert.Contains("Limite", blocked.Json);
        WhatsAppMcpToolset.ResetSendLimit(seed.LineId);
    }

    [Fact]
    public async Task DownloadMedia_GivesTheImageToTheModel_AndUrlsAreGuarded()
    {
        var seed = await SeedAsync("Wa Media");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var (toolset, _, _) = Build(ctx, seed);
        var me = Owner(seed);

        var media = await toolset.ExecuteAsync("descargar_media", Args(new { mensaje_id = "IN-1" }), me);
        Assert.False(media.IsError, media.Json);
        Assert.NotNull(media.Media);
        Assert.Equal("image/png", media.Media!.MimeType);

        // Anti-SSRF: solo https publico; IPs privadas/metadata bloqueadas.
        var http = await toolset.ExecuteAsync("enviar_media", Args(new { para = ClientePhone, tipo = "imagen", url = "http://169.254.169.254/latest" }), me);
        Assert.True(http.IsError);
        Assert.Contains("https", http.Json);
        foreach (var ip in new[] { "127.0.0.1", "10.1.2.3", "192.168.0.10", "172.20.0.5", "169.254.169.254", "100.64.0.1", "::1", "fd00::1" })
        {
            Assert.True(WhatsAppMcpToolset.IsPrivateAddress(IPAddress.Parse(ip)), ip);
        }
        Assert.False(WhatsAppMcpToolset.IsPrivateAddress(IPAddress.Parse("8.8.8.8")));

        // Archivo pequeno por base64: va por la capa de chat (persiste como media).
        var png = Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 });
        Assert.False((await toolset.ExecuteAsync("enviar_media", Args(new { para = ClientePhone, tipo = "imagen", base64 = png, mime = "image/png", pie = "Factura" }), me)).IsError);
        Assert.True(await ctx.Messages.AnyAsync(m => m.ConversationId == seed.ConversationId && m.MediaType == MessageMediaType.Image && m.Body == "Factura"));
    }

    [Fact]
    public async Task TokenScopes_AreSeparated()
    {
        var seed = await SeedAsync("Wa Tokens");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var svc = new PersonalAccessTokenService(ctx, new AuditWriter(ctx));

        var wa = await svc.IssueAsync(seed.OwnerUserId, seed.OwnerPlatformId, "WhatsApp Ventas", TenantApiToken.ScopeMcpWhatsApp);
        Assert.NotNull(await svc.ResolveAsync(wa.PlainToken, TenantApiToken.ScopeMcpWhatsApp));
        Assert.Null(await svc.ResolveAsync(wa.PlainToken, TenantApiToken.ScopeMcpTableros));
        var tab = await svc.IssueAsync(seed.OwnerUserId, seed.OwnerPlatformId, "Tableros");
        Assert.Null(await svc.ResolveAsync(tab.PlainToken, TenantApiToken.ScopeMcpWhatsApp));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.IssueAsync(seed.OwnerUserId, seed.OwnerPlatformId, "x", TenantApiToken.ScopeAdmin));
    }

    // ---- Helpers ----

    private (WhatsAppMcpToolset Toolset, FakeEvolution Evo, WhatsAppConnectorService Connector) Build(EcorexDbContext ctx, Seed seed)
    {
        var tenant = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var evo = new FakeEvolution();
        var audit = new AuditWriter(ctx);
        var connector = new WhatsAppConnectorService(ctx, tenant, new PlainSecrets(), evo,
            Throwing<IWhatsAppCloudClient>.Create(), Throwing<IYCloudApiClient>.Create(), audit, TimeProvider.System,
            Throwing<IHttpClientFactory>.Create(), Throwing<Ecorex.Application.Notifications.ITemplateMediaStore>.Create());
        var chat = new ChatService(ctx, tenant, connector, new NoOpChatBroadcaster(), TimeProvider.System, audit);
        var toolset = new WhatsAppMcpToolset(ctx, connector, chat, new BlockedNumberService(ctx, tenant, audit), TimeProvider.System,
            new RolService(ctx, tenant, audit));
        toolset.ForLine(seed.LineId);
        return (toolset, evo, connector);
    }

    private static McpActor Owner(Seed s) => new(s.TenantId, s.OwnerUserId, s.OwnerPlatformId, "Owner Wa", true, Guid.NewGuid());

    private static string Args(object o) => JsonSerializer.Serialize(o);

    private static JsonElement Payload(McpToolResult r)
    {
        Assert.False(r.IsError, r.Json);
        using var doc = JsonDocument.Parse(r.Json);
        return doc.RootElement.Clone();
    }

    private async Task<Seed> SeedAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var ownerP = new PlatformUser { Email = $"owner-{tenantId:N}@wa.test", DisplayName = "Owner Wa", EmailVerified = true, Status = PlatformUserStatus.Active };
            var memberP = new PlatformUser { Email = $"member-{tenantId:N}@wa.test", DisplayName = "Miembro Wa", EmailVerified = true, Status = PlatformUserStatus.Active };
            ctx.PlatformUsers.AddRange(ownerP, memberP);
            var owner = new TenantUser { TenantId = tenantId, PlatformUserId = ownerP.Id, Email = ownerP.Email, TenantRole = TenantRole.Owner, Status = PlatformUserStatus.Active };
            var member = new TenantUser { TenantId = tenantId, PlatformUserId = memberP.Id, Email = memberP.Email, TenantRole = TenantRole.Advisor, Status = PlatformUserStatus.Active };
            ctx.TenantUsers.AddRange(owner, member);
            ctx.TenantEvolutionConfigs.Add(new TenantEvolutionConfig { TenantId = tenantId, UseMasterServer = false, BaseUrl = "https://evo.test", ApiTokenEncrypted = "llave-test" });
            var line = new WhatsAppLine { TenantId = tenantId, InstanceName = "Ventas", PhoneNumber = "573000000001", Status = WhatsAppLineStatus.Connected, Provider = WhatsAppProvider.Evolution };
            ctx.WhatsAppLines.Add(line);
            var now = DateTimeOffset.UtcNow;
            var conv = new Conversation { TenantId = tenantId, ContactPhone = ClientePhone, ContactName = "Ana", WhatsAppLineId = line.Id, LastMessageAt = now };
            ctx.Conversations.Add(conv);
            ctx.Messages.Add(new Message
            {
                TenantId = tenantId,
                ConversationId = conv.Id,
                Direction = MessageDirection.Inbound,
                Body = "Hola, como va mi pedido?",
                MessageType = "text",
                ExternalId = "IN-1",
                SentAt = now.AddMinutes(-5)
            });
            await ctx.SaveChangesAsync();
            return new Seed(tenantId, owner.Id, ownerP.Id, member.Id, memberP.Id, line.Id, conv.Id);
        }
    }

    private sealed record Seed(Guid TenantId, Guid OwnerUserId, Guid OwnerPlatformId, Guid MemberUserId, Guid MemberPlatformId, Guid LineId, Guid ConversationId);

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }

    private sealed class PlainSecrets : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string ciphertext) => ciphertext;
    }

    private sealed class NoOpChatBroadcaster : IChatBroadcaster
    {
        public Task MessageAddedAsync(Guid tenantId, Guid conversationId, MessageDto message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>Doble que explota si lo llaman: para dependencias que el camino de Evolution no debe tocar.</summary>
    public class Throwing<T> : DispatchProxy where T : class
    {
        public static T Create() => DispatchProxy.Create<T, Throwing<T>>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => throw new NotSupportedException($"{typeof(T).Name}.{targetMethod?.Name} no deberia llamarse en esta prueba.");
    }

    /// <summary>Doble del cliente HTTP de Evolution: registra cada llamada (metodo logico, ruta, instancia, cuerpo).</summary>
    private sealed class FakeEvolution : IEvolutionApiClient
    {
        public sealed record Call(string Method, string Path, string Instance, string Body);

        public List<Call> Calls { get; } = new();
        private int _n;

        private EvolutionSendResult Sent(string method, string instance, object body, string prefix)
        {
            Calls.Add(new Call(method, "/message/" + method + "/" + instance, instance, JsonSerializer.Serialize(body)));
            return new EvolutionSendResult(true, null, $"WA-{prefix}-{++_n}");
        }

        public Task<EvolutionSendResult> SendTextAsync(string baseUrl, string apiKey, string instanceName, string phone, string text, CancellationToken cancellationToken = default)
            => Task.FromResult(Sent("sendText", instanceName, new { phone, text }, "TXT"));

        public Task<EvolutionSendResult> SendMediaAsync(string baseUrl, string apiKey, string instanceName, string phone, string mediatype, string base64, string? mimeType, string? fileName, string? caption, CancellationToken cancellationToken = default)
            => Task.FromResult(Sent("sendMedia", instanceName, new { phone, mediatype, caption }, "MED"));

        public Task<EvolutionSendResult> SendAudioAsync(string baseUrl, string apiKey, string instanceName, string phone, string base64, CancellationToken cancellationToken = default)
            => Task.FromResult(Sent("sendWhatsAppAudio", instanceName, new { phone }, "AUD"));

        public Task<EvolutionSendResult> SendLocationAsync(string baseUrl, string apiKey, string instanceName, string phone, double latitude, double longitude, string? name, string? address, CancellationToken cancellationToken = default)
            => Task.FromResult(Sent("sendLocation", instanceName, new { phone, latitude, longitude }, "LOC"));

        public Task<EvolutionSendResult> SendReactionAsync(string baseUrl, string apiKey, string instanceName, string remoteJid, string messageId, string emoji, CancellationToken cancellationToken = default)
            => Task.FromResult(Sent("sendReaction", instanceName, new { remoteJid, messageId, emoji }, "REA"));

        public Task<EvolutionSendResult> DeleteMessageForEveryoneAsync(string baseUrl, string apiKey, string instanceName, string remoteJid, string messageId, bool fromMe, CancellationToken cancellationToken = default)
            => Task.FromResult(Sent("deleteMessageForEveryone", instanceName, new { remoteJid, messageId }, "DEL"));

        public Task<EvolutionMediaResult> GetBase64FromMediaMessageAsync(string baseUrl, string apiKey, string instanceName, string messageKeyId, CancellationToken cancellationToken = default)
            => Task.FromResult(new EvolutionMediaResult(true, "iVBORw0KGgo=", "image/png", null));

        public Task<EvolutionRawResult> CallAsync(string baseUrl, string apiKey, string method, string path, object? body, CancellationToken cancellationToken = default)
        {
            var instance = path.Split('/', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(2)?.Split('?')[0] ?? string.Empty;
            var logical = path.Split('/', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? path;
            Calls.Add(new Call(logical, path, Uri.UnescapeDataString(instance), body is null ? string.Empty : JsonSerializer.Serialize(body)));
            var json = path switch
            {
                _ when path.StartsWith("/instance/connectionState/", StringComparison.Ordinal) => """{"instance":{"state":"open"}}""",
                _ when path.StartsWith("/message/", StringComparison.Ordinal) => "{\"key\":{\"id\":\"WA-RAW-" + (++_n) + "\"}}",
                _ when path.StartsWith("/chat/getBase64FromMediaMessage/", StringComparison.Ordinal) => """{"base64":"iVBORw0KGgo=","mimetype":"image/png"}""",
                _ when path.StartsWith("/chat/findChats/", StringComparison.Ordinal) => """[{"remoteJid":"573001112233@s.whatsapp.net","pushName":"Ana"}]""",
                _ when path.StartsWith("/chat/findContacts/", StringComparison.Ordinal) => """[{"remoteJid":"573001112233@s.whatsapp.net","pushName":"Ana"}]""",
                _ when path.StartsWith("/group/fetchAllGroups/", StringComparison.Ordinal) => """[]""",
                _ => "{}"
            };
            string? id = null;
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("key", out var k)) { id = k.GetProperty("id").GetString(); }
            }
            return Task.FromResult(new EvolutionRawResult(true, 200, json, id, null));
        }

        public Task<EvolutionPingResult> CheckAsync(string baseUrl, string apiKey, CancellationToken cancellationToken = default)
            => Task.FromResult(new EvolutionPingResult(true, true, 200, null));

        public Task<EvolutionInstanceResult> CreateInstanceAsync(string baseUrl, string apiKey, string instanceName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EvolutionInstanceResult> ConnectAsync(string baseUrl, string apiKey, string instanceName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EvolutionInstanceResult> GetStateAsync(string baseUrl, string apiKey, string instanceName, CancellationToken cancellationToken = default)
            => Task.FromResult(new EvolutionInstanceResult(true, null, "open", null, null));

        public Task<bool> DeleteInstanceAsync(string baseUrl, string apiKey, string instanceName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("El MCP nunca debe borrar la instancia.");

        public Task<EvolutionSendResult> SetWebhookAsync(string baseUrl, string apiKey, string instanceName, string webhookUrl, string token, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("El MCP nunca debe tocar el webhook.");
    }
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class McpWhatsAppTests_Postgres : McpWhatsAppTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public McpWhatsAppTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture) { }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class McpWhatsAppTests_SqlServer : McpWhatsAppTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public McpWhatsAppTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture) { }
}
