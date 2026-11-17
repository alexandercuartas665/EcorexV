using Ecorex.Application.Common;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Gestor;

/// <summary>
/// Cifra/descifra el token del {enlace_baja} (ADR-0174). Implementado en Infrastructure con Data Protection
/// (proposito propio, distinto del de secretos). El token es opaco y lleva (tenant, correo): no hay que
/// guardar nada por contacto antes de enviar.
/// </summary>
public interface IUnsubscribeTokenProtector
{
    string Protect(Guid tenantId, string email);

    /// <summary>Devuelve (tenant, correo) o null si el token no es valido.</summary>
    (Guid TenantId, string Email)? Unprotect(string token);
}

/// <summary>Resultado de validar un enlace de baja (pagina publica).</summary>
public sealed record UnsubscribeLinkInfo(bool IsValid, Guid? TenantId, string? EmailMasked, string? CompanyName, bool AlreadyUnsubscribed);

/// <summary>Una baja registrada (lista en Plantillas de correo).</summary>
public sealed record EmailUnsubscribeDto(Guid Id, string Email, string Origen, string? Motivo, DateTimeOffset CreatedAt);

/// <summary>
/// Bajas de correo por tenant (ADR-0174). El motor de acciones consulta <see cref="GetUnsubscribedSetAsync"/>
/// y omite a esos contactos; la pagina publica /baja/{token} registra la baja.
/// </summary>
public interface IEmailUnsubscribeService
{
    /// <summary>Enlace absoluto de baja para (tenant, correo); null si no hay URL publica configurada.</summary>
    string? BuildLink(Guid tenantId, string email);

    /// <summary>Valida el token (UNICO punto cross-tenant, solo lectura) para pintar la pagina publica.</summary>
    Task<UnsubscribeLinkInfo> ValidateAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Registra la baja. El llamador ya fijo el tenant del token como ambient. Idempotente.</summary>
    Task<bool> UnsubscribeAsync(string token, string? motivo, CancellationToken cancellationToken = default);

    /// <summary>Correos dados de baja del tenant activo (normalizados).</summary>
    Task<HashSet<string>> GetUnsubscribedSetAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmailUnsubscribeDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Quita una baja (el contacto pidio volver a recibir correos). Borrado fisico permitido: no es
    /// un agregado de negocio sino una marca de exclusion; queda la auditoria en los logs de la app.</summary>
    Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class EmailUnsubscribeService : IEmailUnsubscribeService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IUnsubscribeTokenProtector _tokens;
    private readonly INotifyLinkBuilder _links;

    public EmailUnsubscribeService(IApplicationDbContext db, ITenantContext tenant, IUnsubscribeTokenProtector tokens, INotifyLinkBuilder links)
    {
        _db = db;
        _tenant = tenant;
        _tokens = tokens;
        _links = links;
    }

    public static string Normalize(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    public string? BuildLink(Guid tenantId, string email)
    {
        var norm = Normalize(email);
        if (string.IsNullOrWhiteSpace(norm)) { return null; }
        return _links.BuildUnsubscribeLink(_tokens.Protect(tenantId, norm));
    }

    public async Task<UnsubscribeLinkInfo> ValidateAsync(string token, CancellationToken cancellationToken = default)
    {
        var data = string.IsNullOrWhiteSpace(token) ? null : _tokens.Unprotect(token);
        if (data is not { } d) { return new UnsubscribeLinkInfo(false, null, null, null, false); }

        // Cross-tenant controlado: el tenant sale del token ya validado; solo lectura del nombre y del estado.
        var company = await _db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.Id == d.TenantId).Select(t => t.Name).FirstOrDefaultAsync(cancellationToken);
        if (company is null) { return new UnsubscribeLinkInfo(false, null, null, null, false); }
        var already = await _db.EmailUnsubscribes.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(u => u.TenantId == d.TenantId && u.Email == d.Email, cancellationToken);
        return new UnsubscribeLinkInfo(true, d.TenantId, Mask(d.Email), company, already);
    }

    public async Task<bool> UnsubscribeAsync(string token, string? motivo, CancellationToken cancellationToken = default)
    {
        var data = string.IsNullOrWhiteSpace(token) ? null : _tokens.Unprotect(token);
        if (data is not { } d) { return false; }
        // Defensa: el ambient debe ser el tenant del token (la pagina lo fija con AmbientTenantContext.Begin).
        if (_tenant.TenantId != d.TenantId) { return false; }

        var exists = await _db.EmailUnsubscribes.AnyAsync(u => u.Email == d.Email, cancellationToken);
        if (exists) { return true; }
        _db.EmailUnsubscribes.Add(new EmailUnsubscribe
        {
            TenantId = d.TenantId,
            Email = d.Email,
            Origen = "enlace",
            Motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim()[..Math.Min(motivo.Trim().Length, 500)],
        });
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Doble clic / dos pestanas: el indice unico (tenant, correo) ya tiene la baja. Idempotente.
        }
        return true;
    }

    public async Task<HashSet<string>> GetUnsubscribedSetAsync(CancellationToken cancellationToken = default)
    {
        var list = await _db.EmailUnsubscribes.AsNoTracking().Select(u => u.Email).ToListAsync(cancellationToken);
        return new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<EmailUnsubscribeDto>> ListAsync(CancellationToken cancellationToken = default)
        => await _db.EmailUnsubscribes.AsNoTracking()
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new EmailUnsubscribeDto(u.Id, u.Email, u.Origen, u.Motivo, u.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await _db.EmailUnsubscribes.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (row is null) { return false; }
        _db.EmailUnsubscribes.Remove(row);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // j***@acme.com: la pagina publica no expone el correo completo a quien tenga el enlace reenviado.
    internal static string Mask(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0) { return "***"; }
        return email[0] + "***" + email[at..];
    }
}
