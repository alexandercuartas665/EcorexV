using System.Globalization;
using System.Text;
using System.Text.Json;
using Ecorex.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <inheritdoc />
public sealed class AgentCierreService : IAgentCierreService
{
    private readonly IApplicationDbContext _db;
    private readonly IWhatsAppConnectorService _wa;
    private readonly IEmailSender _email;
    private readonly ITelegramClient _telegram;
    private readonly ISecretProtector _secretProtector;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _clock;

    public AgentCierreService(IApplicationDbContext db, IWhatsAppConnectorService wa, IEmailSender email, ITelegramClient telegram, ISecretProtector secretProtector, IAuditWriter audit, TimeProvider clock)
    {
        _db = db;
        _wa = wa;
        _email = email;
        _telegram = telegram;
        _secretProtector = secretProtector;
        _audit = audit;
        _clock = clock;
    }

    public async Task<AgentCierreConfig> GetConfigAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        var json = await _db.AiAgents.AsNoTracking()
            .Where(a => a.Id == agentId)
            .Select(a => a.CierreJson)
            .FirstOrDefaultAsync(cancellationToken);
        return AgentCierreConfig.Parse(json);
    }

    public async Task<bool> SaveConfigAsync(Guid agentId, AgentCierreConfig config, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var agent = await _db.AiAgents.FirstOrDefaultAsync(a => a.Id == agentId, cancellationToken);
        if (agent is null) { return false; }

        var previous = agent.CierreJson;
        // Config vacia => null (mantiene limpio el campo y la compatibilidad hacia atras).
        agent.CierreJson = config.IsNoop ? null : config.Serialize();

        _audit.Write(actorUserId, "ai-agent.cierre-config", nameof(Domain.Entities.AiAgent), agent.Id,
            previousValue: new { hadConfig = previous is not null },
            newValue: new { config.Olvidar, alertas = config.Alertas?.Count ?? 0 },
            tenantId: agent.TenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task HandleCloseAsync(Guid agentId, Guid conversationId, string? summary, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        try
        {
            var agent = await _db.AiAgents.AsNoTracking()
                .Where(a => a.Id == agentId)
                .Select(a => new { a.Name, a.CierreJson })
                .FirstOrDefaultAsync(cancellationToken);
            if (agent is null) { return; }

            var config = AgentCierreConfig.Parse(agent.CierreJson);
            if (config.IsNoop) { return; }

            var conv = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
            if (conv is null) { return; }

            // (a) Olvidar al cliente: reinicio NO destructivo del contexto (saluda desde cero la proxima vez).
            if (config.Olvidar)
            {
                conv.AgentContextResetAt = _clock.GetUtcNow();
            }

            // (b) Alertas: cada una se resuelve y envia de forma independiente (un fallo no frena las demas).
            if (config.Alertas is { Count: > 0 })
            {
                var tokenMap = BuildTokenMap(agent.Name, conv, summary);
                foreach (var alerta in config.Alertas)
                {
                    try { await DispatchAlertAsync(alerta, conv, tokenMap, summary, actorUserId, cancellationToken); }
                    catch { /* un envio fallido no debe romper el cierre ni las demas alertas */ }
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Best-effort: el cierre nunca debe propagar una excepcion a la respuesta del agente.
        }
    }

    private async Task DispatchAlertAsync(AgentCierreAlerta alerta, Domain.Entities.Conversation conv,
        IReadOnlyDictionary<string, string> tokenMap, string? summary, Guid actor, CancellationToken ct)
    {
        // Ola 2: grupo de Evolution -> texto plano al jid del grupo (no depende de un usuario destino).
        if (alerta.Canal == CierreCanal.WhatsAppGrupo)
        {
            var groupJid = alerta.GrupoJid?.Trim();
            var groupLine = alerta.LineaId ?? conv.WhatsAppLineId;
            if (string.IsNullOrWhiteSpace(groupJid) || groupLine is not Guid glid) { return; }
            // Evolution enruta al grupo cuando el jid "...@g.us" viaja en remoteJid (campo "number").
            await _wa.SendTestAsync(glid, groupJid!, BuildGroupText(tokenMap, summary), actor, remoteJid: groupJid, ct);
            return;
        }

        // Ola 3: Telegram -> mensaje al chat_id via el bot del tenant (token cifrado).
        if (alerta.Canal == CierreCanal.Telegram)
        {
            var chatId = alerta.ChatId?.Trim();
            if (string.IsNullOrWhiteSpace(chatId)) { return; }
            var cfg = await _db.TenantTelegramConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
            if (cfg is not { IsEnabled: true } || string.IsNullOrWhiteSpace(cfg.BotTokenEncrypted)) { return; }
            string token;
            try { token = _secretProtector.Unprotect(cfg.BotTokenEncrypted!); }
            catch { return; } // token cifrado con una version anterior: no rompemos el cierre
            await _telegram.SendMessageAsync(token, chatId!, BuildGroupText(tokenMap, summary), ct);
            return;
        }

        var userId = alerta.Destino == CierreDestino.Usuario
            ? alerta.UsuarioId
            : await ResolveAssignedUserIdAsync(conv, ct);
        if (userId is not Guid uid) { return; }

        var user = await _db.TenantUsers.AsNoTracking()
            .Where(u => u.Id == uid)
            .Select(u => new { u.Email, u.Phone })
            .FirstOrDefaultAsync(ct);
        if (user is null) { return; }

        switch (alerta.Canal)
        {
            case CierreCanal.Correo when !string.IsNullOrWhiteSpace(user.Email):
                var subject = string.IsNullOrWhiteSpace(alerta.Asunto)
                    ? $"Cierre de atencion - {tokenMap.GetValueOrDefault("cliente", conv.ContactPhone)}"
                    : alerta.Asunto!;
                await _email.SendAsync(user.Email!, subject, BuildEmailHtml(tokenMap, summary), ct);
                break;

            case CierreCanal.WhatsApp when !string.IsNullOrWhiteSpace(user.Phone) && !string.IsNullOrWhiteSpace(alerta.Plantilla):
                var fromLine = alerta.LineaId ?? conv.WhatsAppLineId;
                if (fromLine is not Guid lineId) { return; }
                var q = _db.WhatsAppTemplates.AsNoTracking().Where(t => t.Name == alerta.Plantilla && t.IsActive);
                if (!string.IsNullOrWhiteSpace(alerta.Idioma)) { q = q.Where(t => t.Language == alerta.Idioma); }
                var tpl = await q.FirstOrDefaultAsync(ct);
                if (tpl is null) { return; } // solo enviamos plantillas que existen (la UI las ofrece del catalogo)
                var lang = string.IsNullOrWhiteSpace(alerta.Idioma) ? tpl.Language : alerta.Idioma!;
                await _wa.SendTemplateAsync(lineId, user.Phone!, tpl.Name, lang, BuildTemplateParams(tpl.VariablesJson, tokenMap), actor, ct);
                break;
        }
    }

    private async Task<Guid?> ResolveAssignedUserIdAsync(Domain.Entities.Conversation conv, CancellationToken ct)
    {
        if (conv.WhatsAppLineId is not Guid lineId) { return null; }
        return await _db.WhatsAppLines.AsNoTracking()
            .Where(l => l.Id == lineId)
            .Select(l => l.AssignedToTenantUserId)
            .FirstOrDefaultAsync(ct);
    }

    // ---- Composicion del contenido ----

    private static Dictionary<string, string> BuildTokenMap(string agentName, Domain.Entities.Conversation conv, string? summary)
    {
        var cliente = string.IsNullOrWhiteSpace(conv.ContactName) ? conv.ContactPhone : conv.ContactName!;
        var resumen = string.IsNullOrWhiteSpace(summary) ? "Atencion cerrada." : summary!.Trim();
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["cliente"] = cliente,
            ["contacto"] = cliente,
            ["nombre"] = cliente,
            ["telefono"] = conv.ContactPhone,
            ["celular"] = conv.ContactPhone,
            ["whatsapp"] = conv.ContactPhone,
            ["agente"] = agentName,
            ["bot"] = agentName,
            ["resumen"] = resumen,
            ["detalle"] = resumen,
            ["mensaje"] = resumen,
            ["pedido"] = resumen,
            ["nota"] = resumen
        };
    }

    // Construye los parametros posicionales de la plantilla resolviendo cada variable {{token}} por su nombre.
    private static IReadOnlyList<string> BuildTemplateParams(string? variablesJson, IReadOnlyDictionary<string, string> tokenMap)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(variablesJson)) { return result; }
        try
        {
            using var doc = JsonDocument.Parse(variablesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) { return result; }
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var token = el.TryGetProperty("token", out var t) ? t.GetString() : null;
                var key = StripAccents((token ?? "").Trim().ToLowerInvariant());
                result.Add(tokenMap.TryGetValue(key, out var val) ? val : "");
            }
        }
        catch { /* variables mal formadas: sin parametros */ }
        return result;
    }

    // Texto plano para el grupo de WhatsApp (Evolution no usa plantilla HSM en grupos).
    private static string BuildGroupText(IReadOnlyDictionary<string, string> tokenMap, string? summary)
    {
        var cliente = tokenMap.GetValueOrDefault("cliente", "");
        var telefono = tokenMap.GetValueOrDefault("telefono", "");
        var agente = tokenMap.GetValueOrDefault("agente", "");
        var resumen = string.IsNullOrWhiteSpace(summary) ? "Atencion cerrada." : summary!.Trim();
        var sb = new StringBuilder();
        sb.Append("*Cierre de atencion*\n");
        sb.Append($"Cliente: {cliente} ({telefono})\n");
        sb.Append($"Agente: {agente}\n\n");
        sb.Append(resumen);
        return sb.ToString();
    }

    private static string BuildEmailHtml(IReadOnlyDictionary<string, string> tokenMap, string? summary)
    {
        var cliente = System.Net.WebUtility.HtmlEncode(tokenMap.GetValueOrDefault("cliente", ""));
        var telefono = System.Net.WebUtility.HtmlEncode(tokenMap.GetValueOrDefault("telefono", ""));
        var agente = System.Net.WebUtility.HtmlEncode(tokenMap.GetValueOrDefault("agente", ""));
        var resumen = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(summary) ? "Atencion cerrada." : summary!.Trim())
            .Replace("\n", "<br>");
        var sb = new StringBuilder();
        sb.Append("<div style=\"font-family:system-ui,Segoe UI,Arial,sans-serif;font-size:14px;color:#111;\">");
        sb.Append("<h2 style=\"margin:0 0 8px;font-size:16px;\">Cierre de atencion</h2>");
        sb.Append($"<p style=\"margin:2px 0;\"><b>Cliente:</b> {cliente} ({telefono})</p>");
        sb.Append($"<p style=\"margin:2px 0;\"><b>Agente:</b> {agente}</p>");
        sb.Append($"<div style=\"margin-top:10px;padding:10px 12px;background:#f6f7f9;border-radius:8px;\">{resumen}</div>");
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string StripAccents(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) { sb.Append(ch); }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
