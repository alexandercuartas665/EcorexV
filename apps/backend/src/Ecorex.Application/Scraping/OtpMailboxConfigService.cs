using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Scraping;

/// <summary>CRUD + prueba/lectura del buzon OTP. Aislamiento por filtro global de tenant; la app-password
/// se guarda cifrada (ISecretProtector) y solo se descifra en memoria justo antes de conectar por IMAP.</summary>
public sealed class OtpMailboxConfigService : IOtpMailboxConfigService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ISecretProtector _protector;
    private readonly IOtpMailboxReader _reader;

    public OtpMailboxConfigService(IApplicationDbContext db, ITenantContext tenant,
        ISecretProtector protector, IOtpMailboxReader reader)
    {
        _db = db;
        _tenant = tenant;
        _protector = protector;
        _reader = reader;
    }

    public async Task<IReadOnlyList<OtpMailboxDto>> ListAsync(CancellationToken ct = default) =>
        (await _db.OtpMailboxConfigs.AsNoTracking().OrderBy(x => x.Nombre).ToListAsync(ct))
        .Select(x => new OtpMailboxDto(x.Id, x.Nombre, x.Proveedor, x.Host, x.Puerto, x.UsarSsl, x.Usuario,
            x.Activo, !string.IsNullOrEmpty(x.PasswordCifrada), x.UltimaValidacion)).ToList();

    public async Task<(Guid? Id, string? Error)> SaveAsync(SaveOtpMailboxRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return (null, "No hay tenant activo."); }
        var nombre = (req.Nombre ?? string.Empty).Trim();
        if (nombre.Length is 0 or > 150) { return (null, "El nombre es obligatorio (max 150)."); }
        if (string.IsNullOrWhiteSpace(req.Host)) { return (null, "El host IMAP es obligatorio."); }
        if (string.IsNullOrWhiteSpace(req.Usuario)) { return (null, "El usuario es obligatorio."); }
        if (req.Puerto is < 1 or > 65535) { return (null, "Puerto invalido."); }

        OtpMailboxConfig entity;
        if (req.Id is { } id)
        {
            entity = await _db.OtpMailboxConfigs.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new InvalidOperationException("El buzon no existe.");
        }
        else
        {
            entity = new OtpMailboxConfig { TenantId = tenantId };
            _db.OtpMailboxConfigs.Add(entity);
        }

        entity.Nombre = nombre;
        entity.Proveedor = (req.Proveedor ?? "Microsoft").Trim();
        entity.Host = req.Host.Trim();
        entity.Puerto = req.Puerto;
        entity.UsarSsl = req.UsarSsl;
        entity.Usuario = req.Usuario.Trim();
        entity.Activo = req.Activo;
        if (!string.IsNullOrWhiteSpace(req.Password))
        {
            entity.PasswordCifrada = _protector.Protect(req.Password.Trim());
        }
        await _db.SaveChangesAsync(ct);
        return (entity.Id, null);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var e = await _db.OtpMailboxConfigs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        _db.OtpMailboxConfigs.Remove(e);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public Task<OtpReadResult> ProbarAsync(Guid configId, string? remitente, string? asunto, string regex,
        int timeoutSegundos, int ventanaMinutos, CancellationToken ct = default) =>
        LeerInternoAsync(configId, remitente, asunto, regex, timeoutSegundos,
            DateTimeOffset.UtcNow.AddMinutes(-Math.Clamp(ventanaMinutos, 1, 1440)), marcarValidacion: true, ct);

    public Task<OtpReadResult> LeerTokenAsync(Guid configId, string? remitente, string? asunto, string regex,
        int timeoutSegundos, DateTimeOffset sinceUtc, CancellationToken ct = default) =>
        LeerInternoAsync(configId, remitente, asunto, regex, timeoutSegundos, sinceUtc, marcarValidacion: false, ct);

    private async Task<OtpReadResult> LeerInternoAsync(Guid configId, string? remitente, string? asunto,
        string regex, int timeoutSegundos, DateTimeOffset sinceUtc, bool marcarValidacion, CancellationToken ct)
    {
        var cfg = await _db.OtpMailboxConfigs.FirstOrDefaultAsync(x => x.Id == configId, ct);
        if (cfg is null) { return new(false, null, "El buzon no existe."); }
        if (string.IsNullOrEmpty(cfg.PasswordCifrada)) { return new(false, null, "El buzon no tiene app-password configurada."); }
        if (string.IsNullOrWhiteSpace(regex)) { return new(false, null, "La regex de extraccion es obligatoria."); }
        string password;
        try { password = _protector.Unprotect(cfg.PasswordCifrada); }
        catch { return new(false, null, "No se pudo descifrar la clave del buzon; vuelve a guardarla."); }

        var req = new OtpReadRequest(cfg.Host, cfg.Puerto, cfg.UsarSsl, cfg.Usuario, password,
            string.IsNullOrWhiteSpace(remitente) ? null : remitente.Trim(),
            string.IsNullOrWhiteSpace(asunto) ? null : asunto.Trim(),
            regex.Trim(), sinceUtc, timeoutSegundos);
        var result = await _reader.ReadTokenAsync(req, ct);

        if (result.Ok && marcarValidacion)
        {
            cfg.UltimaValidacion = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return result;
    }
}
