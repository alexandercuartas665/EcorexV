using System.Security.Cryptography;
using Ecorex.Application.Common;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ecorex.Application.Gestor;

/// <summary>
/// A quien pertenece una URL de webhook: <paramref name="Found"/> = el token existe;
/// <paramref name="TenantId"/> = el tenant duenno del SMTP propio, o null si es la URL del servidor GLOBAL
/// (entonces cada evento trae su tenant en ecx_tenant).
/// </summary>
public sealed record EmailWebhookScope(bool Found, Guid? TenantId)
{
    public static readonly EmailWebhookScope NotFound = new(false, null);
}

/// <summary>
/// URL del webhook para la UI: <paramref name="HasConfig"/> = hay servidor de correo guardado;
/// <paramref name="Url"/> = URL absoluta (null si aun no se genero o no hay URL publica);
/// <paramref name="HasToken"/> = ya hay token (aunque falte la URL publica).
/// </summary>
public sealed record EmailWebhookInfo(bool HasConfig, bool HasToken, string? Url);

/// <summary>Resumen de un lote aplicado (para el log del webhook).</summary>
public sealed record EmailDeliveryApplyResult(int Events, int RunsUpdated, int Suppressed, int Reactivated)
{
    public static readonly EmailDeliveryApplyResult Empty = new(0, 0, 0, 0);
}

/// <summary>
/// Eventos de entrega de correo del proveedor (SendGrid Event Webhook, ADR-0188): actualiza el estado real de
/// las ejecuciones del motor de acciones y alimenta la lista de bajas (rebotes, spam, bajas del proveedor) que
/// el motor ya respeta (ADR-0175). Tambien administra el token secreto de la URL del webhook.
/// </summary>
public interface IEmailDeliveryEventService
{
    /// <summary>Resuelve el token de la URL (UNICO punto cross-tenant: solo lectura del token).</summary>
    Task<EmailWebhookScope> ResolveScopeAsync(string? token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aplica un lote de eventos al tenant AMBIENTE (el llamador ya lo fijo con el scope del webhook).
    /// <paramref name="ownAccount"/> = la URL es del SMTP propio del tenant: sus rebotes/spam suprimen aunque el
    /// correo no fuera del motor. Por la URL global solo se aplican eventos que traen ecx_tenant.
    /// </summary>
    Task<EmailDeliveryApplyResult> ApplyAsync(IReadOnlyList<SendGridEvent> events, bool ownAccount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Estado de la URL del webhook del SMTP propio del tenant activo. <paramref name="create"/> = crear el token si
    /// falta; <paramref name="regenerate"/> = uno nuevo (revoca el anterior). Sin SMTP propio guardado = sin config.
    /// </summary>
    Task<EmailWebhookInfo> GetTenantWebhookAsync(bool create = false, bool regenerate = false, CancellationToken cancellationToken = default);

    /// <summary>Lo mismo para el servidor GLOBAL (solo plataforma).</summary>
    Task<EmailWebhookInfo> GetPlatformWebhookAsync(bool create = false, bool regenerate = false, CancellationToken cancellationToken = default);
}

public sealed class EmailDeliveryEventService : IEmailDeliveryEventService
{
    /// <summary>Ruta publica del webhook (el endpoint vive en SuperAdmin).</summary>
    public const string WebhookPath = "/webhooks/sendgrid/";

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly INotifyLinkBuilder _links;
    private readonly ILogger<EmailDeliveryEventService> _logger;

    public EmailDeliveryEventService(IApplicationDbContext db, ITenantContext tenant, INotifyLinkBuilder links,
        ILogger<EmailDeliveryEventService>? logger = null)
    {
        _db = db;
        _tenant = tenant;
        _links = links;
        _logger = logger ?? NullLogger<EmailDeliveryEventService>.Instance;
    }

    public async Task<EmailWebhookScope> ResolveScopeAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 64) { return EmailWebhookScope.NotFound; }

        // Cross-tenant controlado: el token es un secreto aleatorio; solo se lee a que tenant pertenece.
        var tenantId = await _db.TenantEmailConfigs.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.EventWebhookToken == token)
            .Select(c => (Guid?)c.TenantId)
            .FirstOrDefaultAsync(cancellationToken);
        if (tenantId is Guid tid) { return new EmailWebhookScope(true, tid); }

        var global = await _db.EmailConfigs.AsNoTracking()
            .AnyAsync(c => c.EventWebhookToken == token, cancellationToken);
        return global ? new EmailWebhookScope(true, null) : EmailWebhookScope.NotFound;
    }

    public async Task<EmailDeliveryApplyResult> ApplyAsync(
        IReadOnlyList<SendGridEvent> events, bool ownAccount, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId || events.Count == 0) { return EmailDeliveryApplyResult.Empty; }
        // El tenant del evento (si lo trae) debe ser el ambiente: nunca se aplica un evento a otro tenant.
        var mine = events.Where(e => e.TenantId is null ? ownAccount : e.TenantId == tenantId)
            .OrderBy(e => e.AtUtc)
            .ToList();
        if (mine.Count == 0) { return EmailDeliveryApplyResult.Empty; }
        if (!await _db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId, cancellationToken))
        {
            return EmailDeliveryApplyResult.Empty;
        }

        // Ejecuciones del motor: por ecx_run y, si no vino, por el id que dio el SMTP (ExternalRef). El filtro
        // global por tenant acota ambas busquedas.
        var runIds = mine.Where(e => e.RunId is not null).Select(e => e.RunId!.Value).Distinct().ToList();
        var byId = runIds.Count == 0
            ? new Dictionary<Guid, ContactWorkflowRun>()
            : await _db.ContactWorkflowRuns.Where(r => runIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);
        var msgIds = mine.Where(e => e.RunId is null && e.MessageId is not null).Select(e => e.MessageId!).Distinct().ToList();
        var byMsg = new Dictionary<string, ContactWorkflowRun>(StringComparer.Ordinal);
        if (msgIds.Count > 0)
        {
            var found = await _db.ContactWorkflowRuns
                .Where(r => r.Channel == "email" && r.ExternalRef != null && msgIds.Contains(r.ExternalRef))
                .ToListAsync(cancellationToken);
            foreach (var r in found) { byMsg.TryAdd(r.ExternalRef!, r); }
        }

        var touched = new HashSet<Guid>();
        var suppressions = new Dictionary<string, EmailSuppression>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in mine)
        {
            ContactWorkflowRun? run = null;
            if (e.RunId is Guid rid) { byId.TryGetValue(rid, out run); }
            else if (e.MessageId is not null) { byMsg.TryGetValue(e.MessageId, out run); }
            if (run is not null && SendGridEvents.ApplyToRun(run, e)) { touched.Add(run.Id); }

            // El ultimo evento del correo manda (re-suscripcion despues de una baja, o al reves).
            if (SendGridEvents.SuppressionFor(e) is { } s) { suppressions[s.Email] = s; }
        }

        var (added, removed) = await ApplySuppressionsAsync(tenantId, suppressions.Values, cancellationToken);
        if (touched.Count > 0 || added > 0 || removed > 0)
        {
            // Un solo SaveChanges: ejecuciones y bajas del lote quedan o todas o ninguna.
            await _db.SaveChangesAsync(cancellationToken);
        }
        _logger.LogInformation(
            "Eventos de correo aplicados. tenant={Tenant} eventos={Events} ejecuciones={Runs} bajas={Added} reactivadas={Removed}",
            tenantId, mine.Count, touched.Count, added, removed);
        return new EmailDeliveryApplyResult(mine.Count, touched.Count, added, removed);
    }

    private async Task<(int Added, int Removed)> ApplySuppressionsAsync(
        Guid tenantId, IEnumerable<EmailSuppression> items, CancellationToken cancellationToken)
    {
        var list = items.ToList();
        if (list.Count == 0) { return (0, 0); }
        var emails = list.Select(s => s.Email).ToList();
        var existing = await _db.EmailUnsubscribes.Where(u => emails.Contains(u.Email))
            .ToDictionaryAsync(u => u.Email, StringComparer.OrdinalIgnoreCase, cancellationToken);

        int added = 0, removed = 0;
        foreach (var s in list)
        {
            existing.TryGetValue(s.Email, out var row);
            if (s.Remove)
            {
                // Solo se reactiva lo que dio de baja el proveedor; una baja por nuestro enlace, manual, rebote o
                // spam no la levanta un evento de SendGrid. Marca de exclusion, no agregado: borrado fisico (ADR-0175).
                if (row is not null && row.Origen == SendGridEvents.OrigenProveedor)
                {
                    _db.EmailUnsubscribes.Remove(row);
                    removed++;
                }
                continue;
            }
            if (row is not null) { continue; }
            _db.EmailUnsubscribes.Add(new EmailUnsubscribe
            {
                TenantId = tenantId,
                Email = s.Email,
                Origen = s.Origen,
                Motivo = s.Motivo,
            });
            added++;
        }
        return (added, removed);
    }

    public async Task<EmailWebhookInfo> GetTenantWebhookAsync(bool create = false, bool regenerate = false, CancellationToken cancellationToken = default)
    {
        var cfg = await _db.TenantEmailConfigs.FirstOrDefaultAsync(cancellationToken);
        if (cfg is null) { return new EmailWebhookInfo(false, false, null); }
        var token = await EnsureTokenAsync(cfg.EventWebhookToken, t => cfg.EventWebhookToken = t, create, regenerate, cancellationToken);
        return Info(token);
    }

    public async Task<EmailWebhookInfo> GetPlatformWebhookAsync(bool create = false, bool regenerate = false, CancellationToken cancellationToken = default)
    {
        var cfg = await _db.EmailConfigs.FirstOrDefaultAsync(cancellationToken);
        if (cfg is null) { return new EmailWebhookInfo(false, false, null); }
        var token = await EnsureTokenAsync(cfg.EventWebhookToken, t => cfg.EventWebhookToken = t, create, regenerate, cancellationToken);
        return Info(token);
    }

    private async Task<string?> EnsureTokenAsync(string? current, Action<string> set, bool create, bool regenerate, CancellationToken cancellationToken)
    {
        if (regenerate || (create && string.IsNullOrWhiteSpace(current)))
        {
            current = NewToken();
            set(current);
            await _db.SaveChangesAsync(cancellationToken);
        }
        return string.IsNullOrWhiteSpace(current) ? null : current;
    }

    private EmailWebhookInfo Info(string? token)
        => new(true, token is not null, token is null ? null : _links.ToAbsoluteUrl(WebhookPath + token));

    /// <summary>32 bytes aleatorios en base64url (43 caracteres): imposible de adivinar, seguro en una URL.</summary>
    public static string NewToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
